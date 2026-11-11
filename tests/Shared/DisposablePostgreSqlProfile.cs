using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace Kordra.Testing;

/// <summary>Test-only exact selection. Does not grant DB operation rights, change global state, or open a connection.</summary>
internal sealed class DisposablePostgreSqlProfile
{
    private static readonly string[] Fields = ["schemaVersion", "kind", "runId", "host", "port", "database", "role", "schema",
        "sourceSha256", "operationManifestSha256", "expiresAtUtc"];
    private static readonly string[] LocatorKeys = ["Host", "Port", "Database", "Username", "Password", "Pooling", "Multiplexing",
        "Include Error Detail", "Timeout", "Command Timeout", "Enlist", "Application Name", "Persist Security Info"];
    internal Guid RunId { get; }
    internal string Database { get; }
    internal string SourceSha256 { get; }
    internal string OperationManifestSha256 { get; }
    internal DateTimeOffset ExpiresAtUtc { get; }
    internal (Guid RunId, string SourceSha256, string OperationManifestSha256, DateTimeOffset ExpiresAtUtc) OwnerBinding =>
        (RunId, SourceSha256, OperationManifestSha256, ExpiresAtUtc);

    private DisposablePostgreSqlProfile(Guid runId, string database, string source, string manifest, DateTimeOffset expires) =>
        (RunId, Database, SourceSha256, OperationManifestSha256, ExpiresAtUtc) = (runId, database, source, manifest, expires);

    internal static DisposablePostgreSqlProfile Read(ReadOnlyMemory<byte> bytes, Guid expectedRunId,
        string expectedSourceSha256, string expectedOperationManifestSha256, DateTimeOffset now)
    {
        try
        {
            if (bytes.Length is < 1 or > 16384 || expectedRunId == Guid.Empty || !Hash(expectedSourceSha256) ||
                !Hash(expectedOperationManifestSha256) || !Utc(now)) throw Invalid();
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 2 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!Fields.Contains(property.Name, StringComparer.Ordinal) || !names.Add(property.Name)) throw Invalid();
            if (names.Count != Fields.Length || root.GetProperty("schemaVersion").GetRawText() != "1" ||
                Text(root, "kind") != "disposable-postgresql-regression" ||
                Text(root, "runId") != expectedRunId.ToString("D") || Text(root, "host") != "127.0.0.1" ||
                root.GetProperty("port").GetRawText() != "5432" || Text(root, "role") != "kordra_test" ||
                Text(root, "schema") != "app" || Text(root, "database") != "kordra_test_" + expectedRunId.ToString("N") ||
                !SameHash(Text(root, "sourceSha256"), expectedSourceSha256) ||
                !SameHash(Text(root, "operationManifestSha256"), expectedOperationManifestSha256) ||
                !DateTimeOffset.TryParseExact(Text(root, "expiresAtUtc"), "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var until) || !Utc(until) || until <= now)
                throw Invalid();
            return new(expectedRunId, Text(root, "database"), expectedSourceSha256, expectedOperationManifestSha256, until);
        }
        catch (JsonException) { throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
        catch (FormatException) { throw Invalid(); }
    }

    internal void ValidateLocator(string locator, DateTimeOffset now)
    {
        try
        {
            if (!Utc(now) || now >= ExpiresAtUtc || string.IsNullOrWhiteSpace(locator)) throw Invalid();
            var parsed = new NpgsqlConnectionStringBuilder(locator);
            if (parsed.Host != "127.0.0.1" || parsed.Port != 5432 || parsed.Database != Database ||
                parsed.Username != "kordra_test" || parsed.Pooling || parsed.Multiplexing || parsed.IncludeErrorDetail || parsed.PersistSecurityInfo ||
                parsed.Keys.Cast<string>().Any(key => !LocatorKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) ||
                !string.IsNullOrEmpty(parsed.SearchPath) || !string.IsNullOrEmpty(parsed.Options)) throw Invalid();
        }
        catch (ArgumentException) { throw Invalid(); }
    }

    /// <summary>Separate, exact transport-role selection. Does not grant CREATE ROLE, HBA or operation rights.</summary>
    internal void ValidateRestrictedFixtureLocator(string locator, Guid caseId, DateTimeOffset now)
    {
        try
        {
            if (!Utc(now) || now >= ExpiresAtUtc || caseId == Guid.Empty || string.IsNullOrWhiteSpace(locator)) throw Invalid();
            var parsed = new NpgsqlConnectionStringBuilder(locator);
            if (parsed.Host != "127.0.0.1" || parsed.Port != 5432 || parsed.Database != Database ||
                parsed.Username != "kordra_t12_" + caseId.ToString("N") || parsed.Pooling || parsed.Multiplexing ||
                parsed.IncludeErrorDetail || parsed.PersistSecurityInfo ||
                parsed.Keys.Cast<string>().Any(key => !LocatorKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) ||
                !string.IsNullOrEmpty(parsed.SearchPath) || !string.IsNullOrEmpty(parsed.Options)) throw Invalid();
        }
        catch (ArgumentException) { throw Invalid(); }
    }

    private static string Text(JsonElement root, string name) =>
        root.GetProperty(name).ValueKind == JsonValueKind.String ? root.GetProperty(name).GetString()! : throw Invalid();
    private static bool Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero && value != default && value != DateTimeOffset.MaxValue;
    private static bool Hash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool SameHash(string value, string expected) => Hash(value) &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(value), Convert.FromHexString(expected));
    private static InvalidOperationException Invalid() => new("Disposable regression target selection is invalid; locator and profile values omitted.");
    public override string ToString() => "DisposablePostgreSqlProfile [test selection only, not operation authority]";
}
