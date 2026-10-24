using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kordra.Modules.ConnectionManagement.Keychain;

internal sealed record KeychainRequest(int SchemaVersion, Guid RequestId, Guid InstallationId, Guid ReferenceId, string KeychainPath)
{
    public bool IsValid => SchemaVersion == 1 && RequestId != Guid.Empty && InstallationId != Guid.Empty && ReferenceId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(KeychainPath) && Path.IsPathFullyQualified(KeychainPath) && KeychainPath.Length <= 2048 &&
        !KeychainPath.Any(char.IsControl) && Path.GetFullPath(KeychainPath) == KeychainPath;
    public override string ToString() => "KeychainRequest [redacted]";
}
internal sealed class SecretLookup(byte[]? bytes, bool quiescent = true) : IDisposable
{
    public byte[]? Bytes { get; } = bytes;
    public bool Quiescent { get; } = quiescent;
    public void Dispose() { if (Bytes is not null) CryptographicOperations.ZeroMemory(Bytes); }
    public override string ToString() => "SecretLookup [redacted]";
}
internal static class KeychainProtocol
{
    internal static async Task WriteRequestAsync(Stream stream, KeychainRequest request, CancellationToken ct)
    {
        if (!request.IsValid) throw new InvalidDataException("Invalid credential request.");
        using var memory = new MemoryStream();
        using (var w = new Utf8JsonWriter(memory))
        {
            w.WriteStartObject(); w.WriteNumber("schemaVersion", 1); w.WriteString("requestId", request.RequestId.ToString("D"));
            w.WriteString("installationId", request.InstallationId.ToString("D")); w.WriteString("referenceId", request.ReferenceId.ToString("D"));
            w.WriteString("keychainPath", request.KeychainPath); w.WriteEndObject();
        }
        var json = memory.ToArray(); if (json.Length > 4096) throw new InvalidDataException("Invalid credential request.");
        var header = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)json.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(json, ct); await stream.FlushAsync(ct);
    }
    internal static async Task<KeychainRequest?> ReadRequestAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            var header = new byte[4]; await stream.ReadExactlyAsync(header, ct); var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length is 0 or > 4096) return null;
            var bytes = new byte[(int)length]; await stream.ReadExactlyAsync(bytes, ct);
            if (await stream.ReadAsync(new byte[1], ct) != 0) return null;
            using var doc = JsonDocument.Parse(bytes, new() { MaxDepth = 2 }); var p = doc.RootElement;
            var names = p.EnumerateObject().Select(x => x.Name).ToArray();
            string[] allowed = ["schemaVersion", "requestId", "installationId", "referenceId", "keychainPath"];
            if (names.Length != 5 || names.Distinct().Count() != 5 || names.Except(allowed).Any()) return null;
            Guid Id(string n) { var s = p.GetProperty(n).GetString(); if (!Guid.TryParseExact(s, "D", out var id) || id.ToString("D") != s) throw new FormatException(); return id; }
            var r = new KeychainRequest(p.GetProperty("schemaVersion").GetInt32(), Id("requestId"), Id("installationId"), Id("referenceId"), p.GetProperty("keychainPath").GetString()!);
            return r.IsValid ? r : null;
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or IOException or ArgumentException) { return null; }
    }
    internal static async Task WriteResultAsync(Stream stream, Guid requestId, byte[]? bytes, CancellationToken ct)
    {
        var valid = bytes is { Length: > 0 and <= 16384 };
        var header = new byte[26]; "KSEC"u8.CopyTo(header); header[4] = 1; header[5] = valid ? (byte)0 : (byte)1;
        requestId.TryWriteBytes(header.AsSpan(6, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(22), valid ? (uint)bytes!.Length : 0);
        await stream.WriteAsync(header, ct); if (valid) await stream.WriteAsync(bytes!, ct); await stream.FlushAsync(ct);
    }
    internal static async Task<SecretLookup> ReadResultAsync(Stream stream, Guid requestId, CancellationToken ct)
    {
        byte[]? bytes = null;
        try
        {
            var header = new byte[26]; await stream.ReadExactlyAsync(header, ct);
            if (!header.AsSpan(0, 4).SequenceEqual("KSEC"u8) || header[4] != 1 || header[5] > 2 || new Guid(header.AsSpan(6, 16), bigEndian: true) != requestId)
                throw new InvalidDataException("Invalid credential response.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(22));
            if (header[5] == 0 ? length is 0 or > 16384 : length != 0) throw new InvalidDataException("Invalid credential response.");
            if (length > 0) { bytes = new byte[(int)length]; await stream.ReadExactlyAsync(bytes, ct); }
            if (await stream.ReadAsync(new byte[1], ct) != 0) throw new InvalidDataException("Invalid credential response.");
            return new(bytes);
        }
        catch { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); throw; }
    }
}
