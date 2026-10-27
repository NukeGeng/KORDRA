using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kordra.Api;

/// <summary>Private reviewed selection, never an HTTP command or a source of runtime business authority.</summary>
public sealed record ArtifactRecoveryPlan(int SchemaVersion, Guid PlanId, Guid InstallationId, Guid RegistrationId,
    Guid BootId, Guid InvocationId, int OldProcessId, string? ReceiptHash, string? AuditHash)
{
    public byte[] CanonicalBytes() => JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public string Hash() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("artifact-recovery-plan-v1\n").Concat(CanonicalBytes()).ToArray()));
    public static ArtifactRecoveryPlan? Parse(ReadOnlyMemory<byte> json)
    {
        if (json.Length is <= 0 or > 16384) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 3 });
            var value = doc.RootElement;
            string[] expected = ["schemaVersion", "planId", "installationId", "registrationId", "bootId", "invocationId", "oldProcessId", "receiptHash", "auditHash"];
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != expected.Length ||
                value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != expected.Length ||
                value.EnumerateObject().Any(p => !expected.Contains(p.Name, StringComparer.Ordinal))) return null;
            Guid Id(string name) => ArtifactRequestParser.TryGuid(value.GetProperty(name).GetString(), out var id) ? id : throw new FormatException();
            string? HashValue(string name)
            {
                var v = value.GetProperty(name); if (v.ValueKind == JsonValueKind.Null) return null;
                var text = v.GetString(); return ValidHash(text) ? text : throw new FormatException();
            }
            if (!value.GetProperty("schemaVersion").TryGetInt32(out var schema) || schema != 1 ||
                !value.GetProperty("oldProcessId").TryGetInt32(out var pid) || pid <= 0) return null;
            return new(1, Id("planId"), Id("installationId"), Id("registrationId"), Id("bootId"), Id("invocationId"), pid, HashValue("receiptHash"), HashValue("auditHash"));
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or OverflowException) { return null; }
    }
    internal static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
}

public interface IArtifactProcessQuiescence
{
    bool IsQuiescent(int oldProcessId);
}

/// <summary>Conservative local proof: exact old PID gone, and no other designated Kordra.Api process/helper exists.</summary>
public sealed class ArtifactProcessQuiescence : IArtifactProcessQuiescence
{
    public bool IsQuiescent(int oldProcessId)
    {
        if (oldProcessId <= 0 || oldProcessId == Environment.ProcessId) return false;
        var processes = System.Diagnostics.Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                if (process.Id == oldProcessId) return false; // PID reuse is conservative denial, never guessed exit.
                if (process.Id != Environment.ProcessId && process.ProcessName == "Kordra.Api") return false;
            }
            return true;
        }
        catch { return false; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
