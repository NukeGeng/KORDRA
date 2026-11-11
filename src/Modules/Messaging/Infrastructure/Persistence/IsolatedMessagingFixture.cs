using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using Npgsql;
using System.Data.Common;

namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

/// <summary>Testability guard only; no connection opening, schema creation or operational permission.</summary>
internal static class IsolatedMessagingFixture
{
    internal static string SchemaFor(DbConnection connection, Guid caseId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var target = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (caseId == Guid.Empty || target.Host != "127.0.0.1" || target.Port != 5432 ||
            target.Database != "kordra_test" || target.Username != "kordra_test" || target.Multiplexing ||
            !string.IsNullOrEmpty(target.SearchPath) || !string.IsNullOrEmpty(target.Options))
            throw new InvalidOperationException("Exact local UUID messaging fixture target required.");
        return "t12_" + caseId.ToString("N");
    }
    internal static string SchemaForDisposableTest(DbConnection connection, Guid caseId, DisposableTestBinding profile)
    {
        var now = DateTimeOffset.UtcNow;
        if (profile.RunId == Guid.Empty || caseId == Guid.Empty || !Hash(profile.SourceSha256) || !Hash(profile.OperationManifestSha256) ||
            profile.ExpiresAtUtc.Offset != TimeSpan.Zero || profile.ExpiresAtUtc == DateTimeOffset.MaxValue || profile.ExpiresAtUtc <= now ||
            connection is not NpgsqlConnection) throw InvalidDisposableTarget();
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new(connection.ConnectionString); }
        catch (ArgumentException) { throw InvalidDisposableTarget(); }
        string[] keys = ["Host", "Port", "Database", "Username", "Password", "Pooling", "Multiplexing", "Include Error Detail",
            "Timeout", "Command Timeout", "Enlist", "Application Name", "Persist Security Info"];
        if (parsed.Host != "127.0.0.1" || parsed.Port != 5432 || parsed.Username != "kordra_test" ||
            parsed.Database != "kordra_test_" + profile.RunId.ToString("N") || parsed.Pooling || parsed.Multiplexing ||
            parsed.IncludeErrorDetail || parsed.PersistSecurityInfo || !string.IsNullOrEmpty(parsed.SearchPath) || !string.IsNullOrEmpty(parsed.Options) ||
            parsed.Keys.Cast<string>().Any(key => !keys.Contains(key, StringComparer.OrdinalIgnoreCase))) throw InvalidDisposableTarget();
        static bool Hash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        return "t12_" + caseId.ToString("N");
    }
    private static InvalidOperationException InvalidDisposableTarget() => new("Disposable MSG fixture target/profile invalid; values omitted.");

}
