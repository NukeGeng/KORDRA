using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kordra.Modules.IdentityAccess.Authentication;

/// <summary>Versioned, locally supplied password denylist and accepted pilot length policy.</summary>
public sealed class LocalPasswordPolicy
{
    public const int MaximumArtifactBytes = 64 * 1024 * 1024;
    private readonly HashSet<string> _deniedPasswords;

    private LocalPasswordPolicy(string version, string provenance, string sha256, HashSet<string> deniedPasswords)
    {
        Version = version;
        Provenance = provenance;
        Sha256 = sha256;
        _deniedPasswords = deniedPasswords;
    }

    public string Version { get; }
    public string Provenance { get; }
    public string Sha256 { get; }

    public static LocalPasswordPolicy Load(
        string artifactPath,
        string version,
        string provenance,
        string expectedSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        if (expectedSha256.Length != 64 || expectedSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("Password denylist hash configuration is invalid.");
        }

        var path = Path.GetFullPath(artifactPath);
        if (File.Exists(path) is false || new FileInfo(path).LinkTarget is not null)
        {
            throw new InvalidOperationException("A regular, non-symlink password denylist artifact is required.");
        }

        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumArtifactBytes)
        {
            throw new InvalidOperationException("Password denylist artifact size is invalid.");
        }

        var bytes = File.ReadAllBytes(path);
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash), Convert.FromHexString(expectedSha256)))
        {
            throw new InvalidOperationException("Password denylist artifact hash does not match its pinned value.");
        }

        string[] entries;
        try
        {
            entries = JsonSerializer.Deserialize<string[]>(bytes)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Password denylist artifact format is invalid.");
        }

        if (entries.Length == 0 || entries.Any(string.IsNullOrEmpty))
        {
            throw new InvalidOperationException("Password denylist artifact must contain nonempty entries.");
        }

        return new LocalPasswordPolicy(
            version.Trim(),
            provenance.Trim(),
            actualHash,
            new HashSet<string>(entries, StringComparer.Ordinal));
    }

    public bool IsAcceptable(string? password)
    {
        if (password is null || Encoding.UTF8.GetByteCount(password) > 512)
        {
            return false;
        }

        var scalarCount = 0;
        var remaining = password.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out _, out var consumed);
            if (status != OperationStatus.Done)
            {
                return false;
            }

            scalarCount++;
            remaining = remaining[consumed..];
        }

        return scalarCount is >= 15 and <= 128 && !_deniedPasswords.Contains(password);
    }
}
