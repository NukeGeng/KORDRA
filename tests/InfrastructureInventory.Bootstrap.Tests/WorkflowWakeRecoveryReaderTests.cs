using Kordra.Api;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowWakeRecoveryReaderTests
{
    [Theory]
    [InlineData("tenant")][InlineData("resource")][InlineData("scope")][InlineData("correlation")][InlineData("stage")]
    public async Task WrongAuditBindingIsConflictBeforeOpeningAnyConnection(string changed)
    {
        var spec = Spec(); var wake = Wake(spec); var audit = Audit(spec);
        audit = changed switch {
            "tenant" => audit with { TenantRef = Guid.NewGuid() }, "resource" => audit with { ResourceRef = Guid.NewGuid() },
            "scope" => audit with { ScopeId = Guid.NewGuid() }, "correlation" => audit with { CorrelationId = Guid.NewGuid() },
            _ => audit with { Stage = AuditIntentStage.Use }
        };
        await using var connection = Connection();
        await using var reader = WorkflowWakeRecoveryReader.CreateIsolatedTestReader(connection, spec.InstallationId);
        var request = WorkflowWakePublication.Prepare(spec, wake, WorkflowWakePublication.CreateRegistry(new RefuseRequest()), wake.OccurredAtUtc);
        Assert.Equal(WorkflowReceiptInspectionStatus.Conflict, await reader.InspectSchedulerAsync(spec, wake, request, audit));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task MissingReaderCannotProveCommitRollbackOrRenewOriginalDeadline()
    {
        var spec = Spec(); var wake = Wake(spec);
        var request = WorkflowWakePublication.Prepare(spec, wake, WorkflowWakePublication.CreateRegistry(new RefuseRequest()), wake.OccurredAtUtc);
        var result = WorkflowTransitionResultPublication.PrepareTimerDue(wake, 6, Guid.NewGuid(),
            WorkflowTransitionResultPublication.CreateRegistry(new RefuseResult()), wake.OccurredAtUtc);
        await using var connection = Connection();
        await using var reader = WorkflowWakeRecoveryReader.CreateIsolatedTestReader(connection, spec.InstallationId);
        Assert.Equal(WorkflowReceiptInspectionStatus.Unavailable, await reader.InspectSchedulerAsync(spec, wake, request, Audit(spec)));
        Assert.Equal(WorkflowReceiptInspectionStatus.Unavailable, await reader.InspectCompletionAsync(spec, wake, request, Audit(spec), result, Audit(spec)));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.Equal(wake.DeadlineUtc, result.Binding.DeadlineUtc);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.InspectSchedulerAsync(spec, wake, request, Audit(spec), cancelled.Token));
    }

    [Fact]
    public async Task OneAuditIntentCannotStandForBothSchedulerAndCompletion()
    {
        var spec = Spec(); var wake = Wake(spec); var audit = Audit(spec);
        var request = WorkflowWakePublication.Prepare(spec, wake, WorkflowWakePublication.CreateRegistry(new RefuseRequest()), wake.OccurredAtUtc);
        var result = WorkflowTransitionResultPublication.PrepareTimerDue(wake, 6, Guid.NewGuid(),
            WorkflowTransitionResultPublication.CreateRegistry(new RefuseResult()), wake.OccurredAtUtc);
        await using var connection = Connection();
        await using var reader = WorkflowWakeRecoveryReader.CreateIsolatedTestReader(connection, spec.InstallationId);
        Assert.Equal(WorkflowReceiptInspectionStatus.Conflict, await reader.InspectCompletionAsync(spec, wake, request, audit, result, audit));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData(WorkflowTransactionProbeStatus.Committed, WorkflowReceiptInspectionStatus.MatchingObserved, WorkflowReceiptInspectionStatus.MatchingObserved)]
    [InlineData(WorkflowTransactionProbeStatus.Committed, WorkflowReceiptInspectionStatus.StillUnknown, WorkflowReceiptInspectionStatus.StillUnknown)]
    [InlineData(WorkflowTransactionProbeStatus.Aborted, WorkflowReceiptInspectionStatus.StillUnknown, WorkflowReceiptInspectionStatus.StillUnknown)]
    [InlineData(WorkflowTransactionProbeStatus.Aborted, WorkflowReceiptInspectionStatus.MatchingObserved, WorkflowReceiptInspectionStatus.Conflict)]
    [InlineData(WorkflowTransactionProbeStatus.Unresolved, WorkflowReceiptInspectionStatus.MatchingObserved, WorkflowReceiptInspectionStatus.StillUnknown)]
    [InlineData(WorkflowTransactionProbeStatus.Unavailable, WorkflowReceiptInspectionStatus.MatchingObserved, WorkflowReceiptInspectionStatus.Unavailable)]
    public void OnlyCommittedRootWithMatchingReceiptsCanConfirmOriginalCommit(
        WorkflowTransactionProbeStatus root, WorkflowReceiptInspectionStatus receipts, WorkflowReceiptInspectionStatus expected)
        => Assert.Equal(expected, WorkflowWakeRecoveryReader.ClassifyTerminalRoot(root, receipts));

    private static NpgsqlConnection Connection() => new("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
    private static WorkflowSpecification Spec()
    {
        var scope = Guid.NewGuid(); var now = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        return new(scope, scope, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            1, new string('a', 64), WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
    }
    private static WorkflowWakeOperation Wake(WorkflowSpecification spec) => new(spec, Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 3, spec.DueAtUtc, spec.DueAtUtc.AddSeconds(120));
    private static AuditIntent Audit(WorkflowSpecification spec) => new(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation,
        Guid.NewGuid(), spec.TenantId, spec.InstanceId, "workflow.start", "environment", spec.EnvironmentId, 1, 1, "synthetic_fixture",
        spec.DueAtUtc, spec.RequestId);
    private sealed class RefuseRequest : IMessageConsumer<WorkflowTransitionRequestedV1>
    {
        public ValueTask HandleAsync(WorkflowTransitionRequestedV1 value, CancellationToken ct) => throw new InvalidOperationException("No effect.");
    }
    private sealed class RefuseResult : IMessageConsumer<WorkflowTransitionRecordedV1>
    {
        public ValueTask HandleAsync(WorkflowTransitionRecordedV1 value, CancellationToken ct) => throw new InvalidOperationException("No effect.");
    }
}
