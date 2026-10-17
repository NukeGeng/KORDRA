using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Kordra.ArtifactManagement.Domain;

/// <summary>
/// Immutable SHA-256/SHA-512 OCI content digest. Parsing establishes syntax,
/// never registry existence, provenance, authorization or deployment approval.
/// </summary>
public sealed class OCIImageDigest : IEquatable<OCIImageDigest>
{
    private const int PrefixLength = 7;

    private OCIImageDigest(string value) => Value = value;

    public string Value { get; }
    public string Algorithm => Value[..(PrefixLength - 1)];
    public string EncodedHash => Value[PrefixLength..];

    public static OCIImageDigest Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!TryParse(value, out var digest))
        {
            throw new FormatException("Unsupported or malformed OCI digest.");
        }

        return digest;
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out OCIImageDigest? digest)
    {
        digest = null;
        if (value is null)
        {
            return false;
        }

        var supported = (value.Length == PrefixLength + 64 &&
                         value.StartsWith("sha256:", StringComparison.Ordinal)) ||
                        (value.Length == PrefixLength + 128 &&
                         value.StartsWith("sha512:", StringComparison.Ordinal));
        if (!supported)
        {
            return false;
        }

        foreach (var character in value.AsSpan(PrefixLength))
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        digest = new OCIImageDigest(value);
        return true;
    }

    /// <summary>Checks the exact descriptor byte size before hashing raw content.</summary>
    public bool MatchesContent(ReadOnlySpan<byte> content, long expectedSize)
    {
        if (expectedSize < 0 || expectedSize != content.Length)
        {
            return false;
        }

        Span<byte> computed = stackalloc byte[64];
        var bytesWritten = Algorithm == "sha256"
            ? SHA256.HashData(content, computed)
            : SHA512.HashData(content, computed);
        var expected = Convert.FromHexString(EncodedHash);
        return CryptographicOperations.FixedTimeEquals(computed[..bytesWritten], expected);
    }

    public bool Equals(OCIImageDigest? other) =>
        other is not null && StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is OCIImageDigest other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;

    public static bool operator ==(OCIImageDigest? left, OCIImageDigest? right) =>
        ReferenceEquals(left, right) || (left?.Equals(right) ?? false);

    public static bool operator !=(OCIImageDigest? left, OCIImageDigest? right) => !(left == right);
}
