using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Kordra.Testing;
using Npgsql;

namespace Kordra.WorkflowOrchestration.Tests;

internal static class WorkflowModelFixture
{
    internal static void Validate(NpgsqlConnection connection, Guid caseId)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        if (profile is { } selected) _ = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, selected.OwnerBinding);
        else _ = WorkflowFixtureTarget.Validate(connection, caseId);
    }

    internal static WorkflowDbContext CreateFixture(NpgsqlConnection connection, Guid caseId)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is { } selected
            ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, selected.OwnerBinding)
            : WorkflowDbContext.CreateFixture(connection, caseId);
    }
}
