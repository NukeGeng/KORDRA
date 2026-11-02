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
    public override string ToString() => "WorkflowFixtureTarget [private]";
}
