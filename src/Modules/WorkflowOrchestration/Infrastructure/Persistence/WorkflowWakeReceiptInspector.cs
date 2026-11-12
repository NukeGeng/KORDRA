using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Data;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>
/// Independent read-only WFL observation. Matching is not complete AUD/MSG reconciliation,
/// terminal-backend proof, authority, takeover or permission to publish another generation.
/// </summary>
public sealed class WorkflowWakeReceiptInspector : IWorkflowWakeReceiptInspector, IAsyncDisposable
{
    private readonly WorkflowDbContext _context;
    private readonly Guid _caseId;
    private WorkflowWakeReceiptInspector(WorkflowDbContext context, Guid caseId)
    { _context = context; _caseId = caseId; }
    public static WorkflowWakeReceiptInspector CreateIsolatedTestInspector(NpgsqlConnection independent, Guid caseId)
        => new(WorkflowDbContext.CreateWakeFixture(independent, caseId), caseId);

    public static WorkflowWakeReceiptInspector CreateIsolatedCancellationTestInspector(NpgsqlConnection independent, Guid caseId)
        => new(WorkflowDbContext.CreateCancellationFixture(independent, caseId), caseId);

    public static WorkflowWakeReceiptInspector CreateDisposableTestInspector(NpgsqlConnection independent, Guid caseId,
        bool includesCancellation, DisposableTestBinding profile)
        => new(WorkflowDbContext.CreateDisposableTestFixture(independent, caseId, includesCancellation ? 3 : 2, profile), caseId);

    /// <summary>
    /// Observes the exact published waiting state with no WFL completion. A positive read is only
    /// a WFL fact. Callers still need terminal transaction, custody, MSG, AUD and delivery evidence.
    /// </summary>
    public async Task<bool> ObserveUncompletedWakeAsync(WorkflowSpecification specification, WorkflowWakeOperation wake,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(wake);
        ct.ThrowIfCancellationRequested();
        if (!_context.IncludesCancellationIntents || specification.InstallationId != _caseId ||
            specification.TenantId != _caseId || _context.Database.GetDbConnection().State != ConnectionState.Open ||
            _context.Database.CurrentTransaction is not null)
            return false;

        try
        {
            var descriptor = wake.WriteCanonical();
            _ = WorkflowWakeOperation.ReadCanonical(descriptor, specification);
            var descriptorHash = wake.DescriptorHash;
            var instanceId = wake.InstanceId;
            var steps = _context.Set<WorkflowStepRecord>().Where(x =>
                x.InstallationId == _caseId && x.TenantId == _caseId && x.InstanceId == instanceId);
            var custody = _context.Set<WorkflowCustodyRecord>().Where(x =>
                x.InstallationId == _caseId && x.TenantId == _caseId && x.InstanceId == instanceId);
            var transitions = _context.Set<WorkflowTransitionRecord>().Where(x =>
                x.InstallationId == _caseId && x.TenantId == _caseId && x.InstanceId == instanceId);
            var observed = await (
                from instance in _context.Set<WorkflowInstanceRecord>()
                join retained in _context.Set<WorkflowWakeOperationRecord>()
                    on new { instance.InstallationId, instance.TenantId, instance.InstanceId }
                    equals new { retained.InstallationId, retained.TenantId, retained.InstanceId }
                where instance.InstallationId == _caseId && instance.TenantId == _caseId && instance.InstanceId == instanceId &&
                    instance.RequestId == wake.RequestId && instance.SpecificationHash == wake.SpecificationHash &&
                    instance.Version == wake.ExpectedWorkflowVersion && instance.State == (int)WorkflowState.WaitingTimer &&
                    instance.CreatedAtUtc == specification.CreatedAtUtc && instance.DeadlineUtc == specification.DeadlineUtc &&
                    !instance.DeadlineExceeded &&
                    retained.StepId == wake.StepId && retained.TimerId == wake.TimerId && retained.Generation == wake.Generation &&
                    retained.DescriptorHash == descriptorHash && retained.SchedulerOperationId == wake.SchedulerOperationId &&
                    retained.WakeOperationId == wake.WakeOperationId && retained.MessageId == wake.MessageId &&
                    retained.SchedulerAttempt == wake.SchedulerAttempt && retained.CustodyEpoch == wake.CustodyEpoch &&
                    steps.Any(x => x.StepId == wake.StepId && x.Ordinal == 1 && x.Version == wake.StepVersion &&
                        x.State == (int)WorkflowStepState.WaitingTimer) &&
                    !steps.Any(x => x.Ordinal < 1 && x.State != (int)WorkflowStepState.Succeeded) &&
                    _context.Set<WorkflowTimerRecord>().Any(x => x.InstallationId == _caseId && x.TenantId == _caseId &&
                        x.InstanceId == instanceId && x.StepId == wake.StepId && x.TimerId == wake.TimerId &&
                        x.Generation == wake.Generation && x.Status == 2 && x.SchedulerAttempts == wake.SchedulerAttempt &&
                        x.DueAtUtc == specification.DueAtUtc) &&
                    transitions.Any(x => x.OperationId == wake.SchedulerOperationId && x.StepId == wake.StepId &&
                        x.SpecificationHash == wake.SpecificationHash && x.ResultingVersion == wake.ExpectedWorkflowVersion &&
                        x.ResultingState == (int)WorkflowState.WaitingTimer) &&
                    !transitions.Any(x => x.OperationId == wake.WakeOperationId) &&
                    custody.Any(x => x.OperationId == wake.SchedulerOperationId && x.StepId == wake.StepId &&
                        x.SpecificationHash == wake.SpecificationHash && x.Epoch == wake.CustodyEpoch &&
                        x.Phase == 3 && x.LastCommitReceiptId == wake.SchedulerOperationId) &&
                    !custody.Any(x => x.Phase != 3 || x.OperationId == wake.WakeOperationId) &&
                    !_context.Set<WorkflowCancellationIntentRecord>().Any(x =>
                        x.InstallationId == _caseId && x.TenantId == _caseId && x.InstanceId == instanceId)
                select new { instance.Specification, retained.Descriptor }).SingleOrDefaultAsync(ct);
            return observed is not null && observed.Specification.AsSpan().SequenceEqual(specification.WriteCanonical()) &&
                observed.Descriptor.AsSpan().SequenceEqual(descriptor);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.Data.Common.DbException)
        { return false; }
    }

    public async Task<WorkflowReceiptInspectionStatus> InspectAsync(WorkflowWakeReceiptExpectation expected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expected); ct.ThrowIfCancellationRequested();
        if (_context.Database.GetDbConnection().State != ConnectionState.Open || _context.Database.CurrentTransaction is not null)
            return WorkflowReceiptInspectionStatus.Unavailable;
        try
        {
            var scheduler = expected.Scheduler; var identity = scheduler.Identity;
            var instance = await _context.Set<WorkflowInstanceRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId, ct);
            if (instance is null) return WorkflowReceiptInspectionStatus.StillUnknown;
            var spec = WorkflowSpecification.ReadCanonical(instance.Specification);
            if (spec.InstallationId != identity.InstallationId || spec.TenantId != identity.TenantId || spec.InstanceId != identity.InstanceId ||
                spec.RequestId != scheduler.RequestId || instance.RequestId != scheduler.RequestId ||
                spec.SpecificationHash != scheduler.SpecificationHash || instance.SpecificationHash != scheduler.SpecificationHash)
                return WorkflowReceiptInspectionStatus.Conflict;
            var row = await _context.Set<WorkflowWakeOperationRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
                x.StepId == scheduler.StepId && x.TimerId == expected.TimerId && x.Generation == expected.Generation, ct);
            if (row is null) return WorkflowReceiptInspectionStatus.StillUnknown;
            // An older parent alone is not a conflicting operation. A committed descriptor may
            // become visible between READ COMMITTED statements; refresh the parent before judging it.
            if (instance.Version < scheduler.ResultingVersion)
            {
                var version = await _context.Set<WorkflowInstanceRecord>().AsNoTracking().Where(x =>
                    x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId)
                    .Select(x => (long?)x.Version).SingleOrDefaultAsync(ct);
                if (version is null || version < scheduler.ResultingVersion)
                    return WorkflowReceiptInspectionStatus.Conflict;
            }
            var wake = WorkflowWakeOperation.ReadCanonical(row.Descriptor, spec);
            if (row.DescriptorHash != expected.DescriptorHash || wake.DescriptorHash != expected.DescriptorHash ||
                wake.InstallationId != row.InstallationId || wake.TenantId != row.TenantId || wake.InstanceId != row.InstanceId ||
                wake.StepId != row.StepId || wake.TimerId != row.TimerId || wake.Generation != row.Generation ||
                wake.SchedulerOperationId != scheduler.OperationId || row.SchedulerOperationId != wake.SchedulerOperationId ||
                wake.ExpectedWorkflowVersion != scheduler.ResultingVersion || wake.SchedulerAttempt != row.SchedulerAttempt ||
                wake.CustodyEpoch != row.CustodyEpoch || wake.WakeOperationId != row.WakeOperationId || wake.MessageId != row.MessageId)
                return WorkflowReceiptInspectionStatus.Conflict;
            var transition = await _context.Set<WorkflowTransitionRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
                x.OperationId == scheduler.OperationId, ct);
            if (transition is null) return WorkflowReceiptInspectionStatus.StillUnknown;
            if (transition.StepId != scheduler.StepId || transition.ResultingVersion != scheduler.ResultingVersion ||
                transition.SpecificationHash != scheduler.SpecificationHash || transition.ResultingState != (int)WorkflowState.WaitingTimer)
                return WorkflowReceiptInspectionStatus.Conflict;
            var settled = await _context.Set<WorkflowCustodyRecord>().AsNoTracking().AnyAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
                x.StepId == scheduler.StepId && x.OperationId == scheduler.OperationId && x.SpecificationHash == scheduler.SpecificationHash &&
                x.Epoch == wake.CustodyEpoch && x.Phase == 3 && x.LastCommitReceiptId == scheduler.OperationId, ct);
            return settled ? WorkflowReceiptInspectionStatus.MatchingObserved : WorkflowReceiptInspectionStatus.StillUnknown;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Data.Common.DbException)
        { return WorkflowReceiptInspectionStatus.Unavailable; }
    }

    public ValueTask DisposeAsync() => _context.DisposeAsync();
}
