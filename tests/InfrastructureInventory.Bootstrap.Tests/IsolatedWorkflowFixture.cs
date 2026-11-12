using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using System.Data.Common;
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

/// <summary>Test composition only: selects actual WFL/AUD/MSG implementations, with no lifecycle, effect or outcome logic.
/// Offline SQL exports and default guard tests deliberately call their original factories directly.</summary>
internal static class IsolatedWorkflowFixture
{
    private static DisposablePostgreSqlProfile? Profile(DbConnection connection)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        profile?.ValidateLocator(connection.ConnectionString, DateTimeOffset.UtcNow);
        return profile;
    }
    internal static string ExpectedDatabase => RegressionPostgreSqlSelection.LoadRunnerProfile()?.Database ?? "kordra_test";
    internal static string BoundedOwnerLocator(string locator, string? applicationName = null)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        profile?.ValidateLocator(locator, DateTimeOffset.UtcNow);
        var settings = new NpgsqlConnectionStringBuilder(locator) { Timeout = 3, CommandTimeout = 3 };
        // Preserve legacy pool limits; the approved disposable profile explicitly uses unpooled owner connections.
        if (profile is null) settings.MaxPoolSize = 4;
        if (applicationName is not null) settings.ApplicationName = applicationName;
        return settings.ConnectionString;
    }
    internal static NpgsqlConnection Connection(string locator, Guid caseId)
    {
        var connection = new NpgsqlConnection(locator);
        try { _ = Validate(connection, caseId); return connection; }
        catch { connection.Dispose(); throw; }
    }
    internal static WorkflowFixtureTarget Validate(NpgsqlConnection connection, Guid caseId) => Profile(connection) is { } p
        ? WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, p.OwnerBinding)
        : WorkflowFixtureTarget.Validate(connection, caseId);
    internal static WorkflowDbContext CreateFixture(NpgsqlConnection connection, Guid caseId) => Profile(connection) is { } p
        ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, 1, p.OwnerBinding)
        : WorkflowDbContext.CreateFixture(connection, caseId);
    internal static WorkflowDbContext CreateWakeFixture(NpgsqlConnection connection, Guid caseId) => Profile(connection) is { } p
        ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, 2, p.OwnerBinding)
        : WorkflowDbContext.CreateWakeFixture(connection, caseId);
    internal static WorkflowDbContext CreateCancellationFixture(NpgsqlConnection connection, Guid caseId) => Profile(connection) is { } p
        ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, 3, p.OwnerBinding)
        : WorkflowDbContext.CreateCancellationFixture(connection, caseId);
    internal static WorkflowDbContext CreateStartFixture(NpgsqlConnection connection, Guid caseId) => Profile(connection) is { } p
        ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, 4, p.OwnerBinding)
        : WorkflowDbContext.CreateStartFixture(connection, caseId);
    internal static WorkflowDbContext CreateReconciliationFixture(NpgsqlConnection connection, Guid caseId) => Profile(connection) is { } p
        ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, 5, p.OwnerBinding)
        : WorkflowDbContext.CreateReconciliationFixture(connection, caseId);
    internal static AuditDbContext AuditContext(DbConnection connection, Guid caseId) => Profile(connection) is { } p
        ? AuditDbContextFactory.CreateDisposableIsolatedTestContext(connection, caseId, p.OwnerBinding)
        : AuditDbContextFactory.CreateIsolatedTestContext(connection, caseId);
    internal static Task<AuditTransactionContext> AuditTransaction(DbConnection connection, DbTransaction root, Guid caseId,
        CancellationToken ct = default) => Profile(connection) is { } p
        ? AuditTransactionContext.CreateDisposableIsolatedTestAsync(connection, root, caseId, p.OwnerBinding, ct)
        : AuditTransactionContext.CreateIsolatedTestAsync(connection, root, caseId, ct);
    internal static Task<WorkflowRootTransaction> CreateIsolatedTestAsync(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId,
        MessageContext messages, FairAsyncReaderWriterAdmissionGate admission, CancellationToken ct = default) => Profile(connection) is { } p
        ? WorkflowRootTransaction.CreateDisposableTestAsync(connection, root, caseId, messages, admission, p.OwnerBinding, ct)
        : WorkflowRootTransaction.CreateIsolatedTestAsync(connection, root, caseId, messages, admission, ct);
    internal static Task<WorkflowRootTransaction> CreateIsolatedWakeTestAsync(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId,
        MessageContext messages, FairAsyncReaderWriterAdmissionGate admission, CancellationToken ct = default) => Profile(connection) is { } p
        ? WorkflowRootTransaction.CreateDisposableWakeTestAsync(connection, root, caseId, messages, admission, p.OwnerBinding, ct)
        : WorkflowRootTransaction.CreateIsolatedWakeTestAsync(connection, root, caseId, messages, admission, ct);
    internal static Task<WorkflowRootTransaction> CreateIsolatedCancellationTestAsync(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId,
        MessageContext messages, FairAsyncReaderWriterAdmissionGate admission, CancellationToken ct = default) => Profile(connection) is { } p
        ? WorkflowRootTransaction.CreateDisposableCancellationTestAsync(connection, root, caseId, messages, admission, p.OwnerBinding, ct)
        : WorkflowRootTransaction.CreateIsolatedCancellationTestAsync(connection, root, caseId, messages, admission, ct);
    internal static Task<WorkflowRootTransaction> CreateIsolatedStartTestAsync(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId,
        MessageContext messages, FairAsyncReaderWriterAdmissionGate admission, CancellationToken ct = default) => Profile(connection) is { } p
        ? WorkflowRootTransaction.CreateDisposableStartTestAsync(connection, root, caseId, messages, admission, p.OwnerBinding, ct)
        : WorkflowRootTransaction.CreateIsolatedStartTestAsync(connection, root, caseId, messages, admission, ct);
    internal static CallerOwnedReceiptStore ReceiptStore(NpgsqlConnection connection, DbTransaction root, Guid caseId) => Profile(connection) is { } p
        ? CallerOwnedReceiptStore.CreateDisposableTestStore(connection, root, caseId, p.OwnerBinding)
        : CallerOwnedReceiptStore.CreateIsolatedTestStore(connection, root, caseId);
    internal static CallerOwnedMessageJournal Journal(NpgsqlConnection connection, DbTransaction root, Guid caseId) => Profile(connection) is { } p
        ? CallerOwnedMessageJournal.CreateDisposableTestJournal(connection, root, caseId, p.OwnerBinding)
        : CallerOwnedMessageJournal.CreateIsolatedTestJournal(connection, root, caseId);
    internal static PublicationCapacityGuard Capacity(NpgsqlConnection connection, DbTransaction root, Guid caseId) => Profile(connection) is { } p
        ? PublicationCapacityGuard.CreateDisposableTestGuard(connection, root, caseId, p.OwnerBinding)
        : PublicationCapacityGuard.CreateIsolatedTestGuard(connection, root, caseId);
    internal static MessageReceiptInspector MessageReceipt(DbConnection connection, Guid caseId) => Profile(connection) is { } p
        ? MessageReceiptInspector.CreateDisposableTestInspector(connection, caseId, p.OwnerBinding)
        : MessageReceiptInspector.CreateIsolatedTestInspector(connection, caseId);
    internal static WorkflowReceiptInspector WorkflowReceipt(NpgsqlConnection connection, Guid caseId) => Profile(connection) is { } p
        ? WorkflowReceiptInspector.CreateDisposableTestInspector(connection, caseId, p.OwnerBinding)
        : WorkflowReceiptInspector.CreateIsolatedTestInspector(connection, caseId);
    internal static WorkflowWakeReceiptInspector WakeReceipt(NpgsqlConnection connection, Guid caseId, bool cancellation = false) => Profile(connection) is { } p
        ? WorkflowWakeReceiptInspector.CreateDisposableTestInspector(connection, caseId, cancellation, p.OwnerBinding)
        : cancellation ? WorkflowWakeReceiptInspector.CreateIsolatedCancellationTestInspector(connection, caseId)
            : WorkflowWakeReceiptInspector.CreateIsolatedTestInspector(connection, caseId);
    internal static WorkflowWakeRecoveryReader Recovery(NpgsqlConnection connection, Guid caseId, bool cancellation = false) => Profile(connection) is { } p
        ? WorkflowWakeRecoveryReader.CreateDisposableTestReader(connection, caseId, cancellation, p.OwnerBinding)
        : cancellation ? WorkflowWakeRecoveryReader.CreateIsolatedCancellationTestReader(connection, caseId)
            : WorkflowWakeRecoveryReader.CreateIsolatedTestReader(connection, caseId);
    internal static Task<WorkflowTransactionObservation> Probe(WorkflowTransactionOrigin origin, NpgsqlConnection independent, CancellationToken ct = default)
        => Profile(independent) is { } p ? WorkflowTransactionProbe.InspectDisposableTestAsync(origin, independent, p.OwnerBinding, ct)
            : WorkflowTransactionProbe.InspectIsolatedAsync(origin, independent, ct);
    internal static Task<WorkflowFixtureCustodyLease?> Custody(string locator, Guid caseId, long manifestKey, CancellationToken ct = default)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is { } p ? WorkflowFixtureCustodyLease.TryAcquireDisposableTestAsync(locator, caseId, manifestKey, p.OwnerBinding, ct)
            : WorkflowFixtureCustodyLease.TryAcquireIsolatedAsync(locator, caseId, manifestKey, ct);
    }
    internal static WorkflowWakeRecoveryReader CancellationRecovery(NpgsqlConnection connection, Guid caseId) => Recovery(connection, caseId, true);
    internal static WorkflowWakeReceiptInspector CancellationWakeReceipt(NpgsqlConnection connection, Guid caseId) => WakeReceipt(connection, caseId, true);
    internal static Task<WorkflowCancellationTransactionResult> Cancel(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowCancellationIntent intent, Guid projectId, Guid environmentId,
        AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission,
        Func<CancellationToken, Task<bool>> stillCurrent, CancellationToken ct = default) => Profile(connection) is { } p
        ? WorkflowCancellationTransaction.ExecuteDisposableTestAsync(connection, root, caseId, intent, projectId, environmentId, mutation, admission, stillCurrent, p.OwnerBinding, ct)
        : WorkflowCancellationTransaction.ExecuteIsolatedAsync(connection, root, caseId, intent, projectId, environmentId, mutation, admission, stillCurrent, ct);
    internal static Task<WorkflowCancellationTransactionResult> CancelAdmitted(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowCancellationIntent intent, Guid projectId, Guid environmentId,
        SessionSelection session, WorkflowCommandAdmissionReader authority,
        FairAsyncReaderWriterAdmissionGate admission, CancellationToken ct = default) => Profile(connection) is { } p
        ? WorkflowCancellationTransaction.ExecuteAdmittedDisposableTestAsync(connection, root, caseId, intent, projectId, environmentId, session, authority, admission, p.OwnerBinding, ct)
        : WorkflowCancellationTransaction.ExecuteAdmittedIsolatedAsync(connection, root, caseId, intent, projectId, environmentId, session, authority, admission, ct);
    internal static Task<WorkflowReconciliationTransactionResult> Reconcile(
        NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId, WorkflowReconciliationCommand command,
        Guid projectId, Guid environmentId, AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission,
        WorkflowFixtureCustodyLease custody, Func<CancellationToken, Task<bool>> verifyEvidence,
        Func<CancellationToken, Task<bool>> stillCurrent, CancellationToken ct = default) => Profile(connection) is { } p
        ? WorkflowReconciliationTransaction.ExecuteDisposableTestAsync(connection, root, caseId, command, projectId, environmentId, mutation, admission, custody, verifyEvidence, stillCurrent, p.OwnerBinding, ct)
        : WorkflowReconciliationTransaction.ExecuteIsolatedAsync(connection, root, caseId, command, projectId, environmentId, mutation, admission, custody, verifyEvidence, stillCurrent, ct);
    internal static Task<WorkflowReconciliationTransactionResult> ReconcileAdmitted(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId, WorkflowReconciliationSelection selection,
        WorkflowReadScope scope, SessionSelection session, WorkflowCommandAdmissionReader authority,
        WorkflowFixtureCustodyLease custody, WorkflowWakeRecoveryReader evidenceReader,
        WorkflowReconciliationSource source, FairAsyncReaderWriterAdmissionGate admission, CancellationToken ct = default) => Profile(connection) is { } p
        ? WorkflowReconciliationTransaction.ExecuteAdmittedDisposableTestAsync(connection, root, caseId, selection, scope, session, authority, custody, evidenceReader, source, admission, p.OwnerBinding, ct)
        : WorkflowReconciliationTransaction.ExecuteAdmittedIsolatedAsync(connection, root, caseId, selection, scope, session, authority, custody, evidenceReader, source, admission, ct);
}
