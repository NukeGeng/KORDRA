using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Testing;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Npgsql;

namespace Kordra.WorkflowOrchestration.Tests;

internal static class WorkflowModelFixture
{
    internal static void Validate(NpgsqlConnection connection, Guid caseId)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        if (profile is { } p) _ = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, p.OwnerBinding);
        else _ = WorkflowFixtureTarget.Validate(connection, caseId);
    }
    internal static NpgsqlConnection Connection(string locator, Guid caseId)
    {
        var connection = new NpgsqlConnection(locator);
        try { Validate(connection, caseId); return connection; }
        catch { connection.Dispose(); throw; }
    }
    internal static WorkflowDbContext Create(NpgsqlConnection connection, Guid caseId, int revision)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        if (profile is { } p) return WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, revision, p.OwnerBinding);
        return revision switch {
            1 => WorkflowDbContext.CreateFixture(connection, caseId),
            2 => WorkflowDbContext.CreateWakeFixture(connection, caseId),
            3 => WorkflowDbContext.CreateCancellationFixture(connection, caseId),
            4 => WorkflowDbContext.CreateStartFixture(connection, caseId),
            5 => WorkflowDbContext.CreateReconciliationFixture(connection, caseId),
            6 => WorkflowDbContext.CreateAuthorizationFixture(connection, caseId),
            _ => throw new InvalidOperationException("Unsupported fixture schema revision.") };
    }
    internal static WorkflowDbContext CreateFixture(NpgsqlConnection connection, Guid caseId) => Create(connection, caseId, 1);
    internal static WorkflowDbContext CreateWakeFixture(NpgsqlConnection connection, Guid caseId) => Create(connection, caseId, 2);
    internal static WorkflowDbContext CreateCancellationFixture(NpgsqlConnection connection, Guid caseId) => Create(connection, caseId, 3);
    internal static WorkflowDbContext CreateStartFixture(NpgsqlConnection connection, Guid caseId) => Create(connection, caseId, 4);
    internal static WorkflowDbContext CreateReconciliationFixture(NpgsqlConnection connection, Guid caseId) => Create(connection, caseId, 5);
}
