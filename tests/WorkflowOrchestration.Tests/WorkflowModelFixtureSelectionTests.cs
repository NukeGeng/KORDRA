using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Kordra.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowModelFixtureSelectionTests
{
    [Fact]
    public void RunnerSelectionKeepsAllFiveRevisionsOnExactUnopenedConnection()
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        var locator = $"Host=127.0.0.1;Database={profile?.Database ?? "kordra_test"};Username=kordra_test;Pooling=false";
        using var connection = WorkflowModelFixture.Connection(locator, Guid.NewGuid()); var caseId = Guid.NewGuid();
        for (var revision = 1; revision <= 5; revision++)
        {
            using var context = WorkflowModelFixture.Create(connection, caseId, revision);
            Assert.Same(connection, context.Database.GetDbConnection());
            Assert.Equal("t12_" + caseId.ToString("N"), context.Model.GetDefaultSchema());
            Assert.Equal(revision >= 2, context.IncludesWakeOperations);
            Assert.Equal(revision >= 3, context.IncludesCancellationIntents);
            Assert.Equal(revision >= 4, context.IncludesStartReceipts);
            Assert.Equal(revision >= 5, context.IncludesOperatorReconciliation);
        }
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        var forbidden = new NpgsqlConnectionStringBuilder(locator) { Database = profile is null ? "kordra" : "kordra_test" };
        Assert.Throws<InvalidOperationException>(() => WorkflowModelFixture.Connection(forbidden.ConnectionString, caseId));
        Assert.Throws<InvalidOperationException>(() => WorkflowModelFixture.Create(connection, caseId, 6));
        Assert.Equal(4, WorkflowStartPostgreSqlTests.Scripts(connection).Count);
        Assert.Equal(4, WorkflowOperatorDecisionDatabaseTests.Scripts(connection).Count);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        if (profile is not null) Assert.Throws<InvalidOperationException>(() => WorkflowDbContext.CreateFixture(connection, caseId));
    }
}
