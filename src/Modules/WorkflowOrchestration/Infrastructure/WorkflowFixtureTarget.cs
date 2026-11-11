using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using Npgsql;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure;

/// <summary>Closed isolated-test target metadata. Never opens/provisions a database or grants operation approval.</summary>
public sealed class WorkflowFixtureTarget
{
    public Guid CaseId { get; }
    public string SchemaName => "t12_" + CaseId.ToString("N");
    private WorkflowFixtureTarget(Guid caseId) => CaseId = caseId;

    public static WorkflowFixtureTarget Validate(NpgsqlConnection connection, Guid caseId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (caseId == Guid.Empty) throw new InvalidOperationException("Explicit isolated WFL case required.");
        NpgsqlConnectionStringBuilder target;
        try { target = new(connection.ConnectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("Invalid WFL fixture target; locator omitted."); }
        if (target.Host != "127.0.0.1" || target.Port != 5432 || target.Database != "kordra_test" ||
            target.Username != "kordra_test" || !string.IsNullOrEmpty(target.SearchPath) ||
            !string.IsNullOrEmpty(target.Options) || target.Multiplexing || target.IncludeErrorDetail)
            throw new InvalidOperationException("Exact isolated WFL test target required; locator omitted.");
        return new(caseId);
    }
    /// <summary>Explicit ADR-0025 database selection; original fixture/default guard remains unchanged.</summary>
    public static WorkflowFixtureTarget ValidateDisposableTest(NpgsqlConnection connection, Guid caseId, DisposableTestBinding profile)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return ValidateDisposableTestLocator(connection.ConnectionString, caseId, profile);
    }

    public static WorkflowFixtureTarget ValidateDisposableTestLocator(string locator, Guid caseId, DisposableTestBinding profile)
    {
        var now = DateTimeOffset.UtcNow;
        if (profile.RunId == Guid.Empty || caseId == Guid.Empty || !Hash(profile.SourceSha256) || !Hash(profile.OperationManifestSha256) ||
            profile.ExpiresAtUtc.Offset != TimeSpan.Zero || profile.ExpiresAtUtc == DateTimeOffset.MaxValue || profile.ExpiresAtUtc <= now ||
            string.IsNullOrWhiteSpace(locator)) throw InvalidDisposableTarget();
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new(locator); }
        catch (ArgumentException) { throw InvalidDisposableTarget(); }
        string[] keys = ["Host", "Port", "Database", "Username", "Password", "Pooling", "Multiplexing", "Include Error Detail",
            "Timeout", "Command Timeout", "Enlist", "Application Name", "Persist Security Info"];
        if (parsed.Host != "127.0.0.1" || parsed.Port != 5432 || parsed.Username != "kordra_test" ||
            parsed.Database != "kordra_test_" + profile.RunId.ToString("N") || parsed.Pooling || parsed.Multiplexing ||
            parsed.IncludeErrorDetail || parsed.PersistSecurityInfo || !string.IsNullOrEmpty(parsed.SearchPath) || !string.IsNullOrEmpty(parsed.Options) ||
            parsed.Keys.Cast<string>().Any(key => !keys.Contains(key, StringComparer.OrdinalIgnoreCase))) throw InvalidDisposableTarget();
        static bool Hash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        return new(caseId);
    }
    private static InvalidOperationException InvalidDisposableTarget() => new("Disposable WFL fixture target/profile invalid; values omitted.");

    public override string ToString() => "WorkflowFixtureTarget [private]";
}
