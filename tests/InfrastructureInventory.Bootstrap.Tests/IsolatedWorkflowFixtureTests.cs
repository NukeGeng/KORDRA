using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Kordra.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class IsolatedWorkflowFixtureTests
{
    [Fact]
    public async Task RunnerSelectionKeepsModelsNativeHandlerAndInspectorsOnOneExactUnopenedTarget()
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        var database = profile?.Database ?? "kordra_test";
        var locator = $"Host=127.0.0.1;Database={database};Username=kordra_test;Pooling=false";
        Assert.Equal(database, IsolatedWorkflowFixture.ExpectedDatabase);
        var bounded = new NpgsqlConnectionStringBuilder(IsolatedWorkflowFixture.BoundedOwnerLocator(locator, "synthetic-native-fixture"));
        Assert.Equal(database, bounded.Database); Assert.Equal(3, bounded.Timeout); Assert.Equal(3, bounded.CommandTimeout);
        Assert.Equal("synthetic-native-fixture", bounded.ApplicationName);
        if (profile is not null)
        {
            Assert.False(bounded.Pooling);
            // Npgsql ContainsKey reports supported keywords, not whether the locator explicitly sets them.
            Assert.DoesNotContain(bounded.Keys.Cast<string>(), key => key.Equals("Maximum Pool Size", StringComparison.OrdinalIgnoreCase));
        }
        else Assert.Equal(4, bounded.MaxPoolSize);
        var caseId = Guid.NewGuid();
        using var connection = new NpgsqlConnection(locator);
        Assert.Equal(caseId, IsolatedWorkflowFixture.Validate(connection, caseId).CaseId);
        using var workflow = IsolatedWorkflowFixture.CreateCancellationFixture(connection, caseId);
        using var audit = IsolatedWorkflowFixture.AuditContext(connection, caseId);
        Assert.Equal("t12_" + caseId.ToString("N"), workflow.Model.GetDefaultSchema());
        Assert.Equal(workflow.Model.GetDefaultSchema(), audit.Model.GetDefaultSchema());
        Assert.Same(connection, workflow.Database.GetDbConnection()); Assert.Same(connection, audit.Database.GetDbConnection());
        using var other = IsolatedWorkflowFixture.CreateStartFixture(connection, Guid.NewGuid());
        Assert.NotEqual(workflow.Model.GetDefaultSchema(), other.Model.GetDefaultSchema());
        await using var recovery = IsolatedWorkflowFixture.CancellationRecovery(connection, caseId);
        await using var owner = IsolatedWorkflowFixture.WorkflowReceipt(connection, caseId);
        Assert.NotNull(IsolatedWorkflowFixture.MessageReceipt(connection, caseId));
        var state = new WorkflowWakeFixtureState(caseId, locator);
        using var nativeOwned = state.NewOwnedConnection();
        Assert.Equal(database, nativeOwned.Database); Assert.Equal(System.Data.ConnectionState.Closed, nativeOwned.State);
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var missingRoot = await Assert.ThrowsAsync<ArgumentNullException>(() => IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(
            connection, null!, caseId, null!, gate));
        Assert.Equal("root", missingRoot.ParamName);
        Assert.False(gate.IsClosed); Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        var wrong = new NpgsqlConnectionStringBuilder(locator) { Database = profile is null ? "kordra" : "kordra_test" };
        using var forbidden = new NpgsqlConnection(wrong.ConnectionString);
        Assert.Throws<InvalidOperationException>(() => IsolatedWorkflowFixture.Validate(forbidden, caseId));
        Assert.Equal(System.Data.ConnectionState.Closed, forbidden.State);
        Assert.Throws<InvalidOperationException>(() => IsolatedWorkflowFixture.Connection(wrong.ConnectionString, caseId));
        // Live-script metadata selection must also work before any physical connection is opened.
        var scripts = WorkflowReconciliationDatabaseTests.Scripts(connection);
        Assert.Equal(4, scripts.Count); Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        if (profile is not null)
            Assert.Throws<InvalidOperationException>(() => WorkflowDbContext.CreateCancellationFixture(connection, caseId));
    }
}
