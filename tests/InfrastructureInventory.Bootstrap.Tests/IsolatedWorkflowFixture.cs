using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Kordra.Testing;
using Npgsql;
using Wolverine.Runtime;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

internal static class IsolatedWorkflowFixture
{
    internal static void Validate(NpgsqlConnection connection, Guid caseId)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        if (profile is { } selected) _ = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, selected.OwnerBinding);
        else _ = WorkflowFixtureTarget.Validate(connection, caseId);
    }
    internal static Task<WorkflowRootTransaction> CreateRoot(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId,
        MessageContext messages, FairAsyncReaderWriterAdmissionGate admission, CancellationToken ct)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is { } selected
            ? WorkflowRootTransaction.CreateDisposableTestAsync(connection, root, caseId, messages, admission, selected.OwnerBinding, ct)
            : WorkflowRootTransaction.CreateIsolatedTestAsync(connection, root, caseId, messages, admission, ct);
    }
    internal static CallerOwnedReceiptStore Receipt(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is { } selected ? CallerOwnedReceiptStore.CreateDisposableTestStore(connection, root, caseId, selected.OwnerBinding)
            : CallerOwnedReceiptStore.CreateIsolatedTestStore(connection, root, caseId);
    }
    internal static Task<AuditTransactionContext> Audit(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId, CancellationToken ct)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is { } selected ? AuditTransactionContext.CreateDisposableIsolatedTestAsync(connection, root, caseId, selected.OwnerBinding, ct)
            : AuditTransactionContext.CreateIsolatedTestAsync(connection, root, caseId, ct);
    }
    internal static MessageReceiptInspector Inspector(NpgsqlConnection connection, Guid caseId)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is { } selected ? MessageReceiptInspector.CreateDisposableTestInspector(connection, caseId, selected.OwnerBinding)
            : MessageReceiptInspector.CreateIsolatedTestInspector(connection, caseId);
    }
    internal static WorkflowReceiptInspector WorkflowInspector(NpgsqlConnection connection, Guid caseId)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is { } selected ? WorkflowReceiptInspector.CreateDisposableTestInspector(connection, caseId, selected.OwnerBinding)
            : WorkflowReceiptInspector.CreateIsolatedTestInspector(connection, caseId);
    }
    internal static AuditDbContext AuditContext(NpgsqlConnection connection, Guid caseId)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is { } selected ? AuditDbContextFactory.CreateDisposableIsolatedTestContext(connection, caseId, selected.OwnerBinding)
            : AuditDbContextFactory.CreateIsolatedTestContext(connection, caseId);
    }
}
