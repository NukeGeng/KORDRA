using Npgsql;

namespace Kordra.Messaging.Durability.Tests;

/// <summary>Test-only target guard; no authority over Product runtime, identities or existing schemas.</summary>
internal sealed class FixtureDatabaseScope
{
    private readonly string _locator;
    public Guid CaseId { get; }
    public string Schema => "t12_" + CaseId.ToString("N");
    public FixtureDatabaseScope(string locator, Guid caseId) : this(locator, caseId, false) { }
    internal static FixtureDatabaseScope RestrictedRuntime(string locator, Guid caseId) => new(locator, caseId, true);
    private FixtureDatabaseScope(string locator, Guid caseId, bool restricted)
    {
        if (caseId == Guid.Empty) throw new InvalidOperationException("A fresh case identity is required.");
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new(locator); }
        catch { throw new InvalidOperationException("Invalid private fixture locator."); }
        if (parsed.Host != "127.0.0.1" || parsed.Port != 5432 || parsed.Database != "kordra_test" ||
            parsed.Username != (restricted ? "kordra_t12_" + caseId.ToString("N") : "kordra_test") ||
            !string.IsNullOrEmpty(parsed.SearchPath) || !string.IsNullOrEmpty(parsed.Options))
            throw new InvalidOperationException("Only the exact isolated test target without session overrides is allowed.");
        CaseId = caseId; _locator = parsed.ConnectionString;
    }
    public NpgsqlConnection CreateConnection() => new(_locator);
    public override string ToString() => "FixtureDatabaseScope [private]";
}
