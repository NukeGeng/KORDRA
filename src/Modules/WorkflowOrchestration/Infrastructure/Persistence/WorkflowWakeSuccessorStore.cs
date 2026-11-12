using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>
/// Inactive owner staging port. The admitted coordinator must establish exact transaction,
/// delivery settlement and custody evidence before calling. An evidence hash is a reference,
/// not proof or authorization. All changes remain provisional on the caller root.
/// </summary>
public sealed class WorkflowWakeSuccessorStore(WorkflowDbContext context)
{
    public async Task<bool> LockOwnerAsync(WorkflowIdentity identity, string specificationHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!context.IncludesCancellationIntents || context.Database.CurrentTransaction is null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Successor locking requires a clean revision 3 owner context on its caller root.");
        var owner = await new WorkflowStore(context).ReadLockedInstanceAsync(identity, ct);
        return owner is not null && owner.Value.Hash == specificationHash;
    }

    public async Task<WorkflowWakeStoredResult> StageAsync(WorkflowWakeOperation previous, WorkflowWakeOperation successor,
        long expectedTimerVersion, Guid workerId, Guid workerBootId, Guid decisionId, string evidenceHash,
        DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(successor);
        ct.ThrowIfCancellationRequested();
        if (!context.IncludesCancellationIntents || context.Database.CurrentTransaction is null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Successor staging requires a clean revision 3 owner context on its caller root.");
        if (expectedTimerVersion is < 1 or long.MaxValue || workerId == Guid.Empty || workerBootId == Guid.Empty ||
            decisionId == Guid.Empty || evidenceHash is not { Length: 64 } || !evidenceHash.All(char.IsAsciiHexDigitLower) ||
            effectiveUtc == default)
            throw new ArgumentException("Exact successor owner versions and immutable evidence reference required.");
        var identity = new WorkflowIdentity(previous.InstallationId, previous.TenantId, previous.InstanceId);
        if (!await LockOwnerAsync(identity, previous.SpecificationHash, ct))
            throw new InvalidOperationException("Workflow unavailable in trusted successor scope.");
        var instance = await context.Set<WorkflowInstanceRecord>().SingleAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId, ct);
        await context.Entry(instance).ReloadAsync(ct);
        var specification = WorkflowSpecification.ReadCanonical(instance.Specification);
        WorkflowTimingRules.RequireSuccessorBinding(specification, previous, successor, successor.OccurredAtUtc);
        var wakes = context.Set<WorkflowWakeOperationRecord>().Where(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId);
        var transitions = context.Set<WorkflowTransitionRecord>().Where(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId);
        var custody = context.Set<WorkflowCustodyRecord>().Where(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId);
        var decisions = context.Set<WorkflowRecoveryDecisionRecord>().Where(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId);
        var recorded = await wakes.AsNoTracking().SingleOrDefaultAsync(x =>
            x.StepId == successor.StepId && x.TimerId == successor.TimerId && x.Generation == successor.Generation, ct);
        if (recorded is not null)
        {
            if (recorded.DescriptorHash != successor.DescriptorHash ||
                !recorded.Descriptor.AsSpan().SequenceEqual(successor.WriteCanonical()) ||
                recorded.SchedulerOperationId != successor.SchedulerOperationId || recorded.WakeOperationId != successor.WakeOperationId ||
                recorded.MessageId != successor.MessageId || recorded.CustodyEpoch != successor.CustodyEpoch ||
                recorded.SchedulerAttempt != successor.SchedulerAttempt ||
                !await decisions.AnyAsync(x => x.DecisionId == decisionId && x.OperationId == previous.SchedulerOperationId &&
                    x.StepId == previous.StepId && x.SpecificationHash == previous.SpecificationHash &&
                    x.EvidenceHash == evidenceHash && x.Kind == 1 && x.ResultingVersion == successor.ExpectedWorkflowVersion, ct) ||
                !await transitions.AnyAsync(x => x.OperationId == successor.SchedulerOperationId && x.StepId == successor.StepId &&
                    x.SpecificationHash == successor.SpecificationHash && x.ResultingVersion == successor.ExpectedWorkflowVersion &&
                    x.ResultingState == (int)WorkflowState.WaitingTimer, ct) ||
                !await custody.AnyAsync(x => x.OperationId == successor.SchedulerOperationId && x.StepId == successor.StepId &&
                    x.SpecificationHash == successor.SpecificationHash && x.Epoch == successor.CustodyEpoch &&
                    x.WorkerId == workerId && x.WorkerBootId == workerBootId && x.Phase == 3 &&
                    x.LastCommitReceiptId == successor.SchedulerOperationId, ct))
                throw new InvalidOperationException("Recorded successor or reconciliation evidence conflicts.");
            return new(successor, successor.ExpectedWorkflowVersion, true);
        }
        if (instance.RequestId != specification.RequestId || instance.SpecificationHash != previous.SpecificationHash ||
            instance.CreatedAtUtc != specification.CreatedAtUtc || instance.DeadlineUtc != specification.DeadlineUtc ||
            instance.State != (int)WorkflowState.WaitingTimer || instance.Version != previous.ExpectedWorkflowVersion ||
            instance.DeadlineExceeded || effectiveUtc.ToUniversalTime() != successor.OccurredAtUtc ||
            effectiveUtc >= successor.DeadlineUtc || effectiveUtc >= specification.DeadlineUtc ||
            await WorkflowCancellationStore.HasIntentAsync(context, identity, ct))
            throw new InvalidOperationException("Current owner state, original window or cancellation forbids a successor.");
        var retained = await wakes.AsNoTracking().SingleOrDefaultAsync(x =>
            x.StepId == previous.StepId && x.TimerId == previous.TimerId && x.Generation == previous.Generation, ct);
        if (retained is null || retained.DescriptorHash != previous.DescriptorHash ||
            !retained.Descriptor.AsSpan().SequenceEqual(previous.WriteCanonical()) ||
            retained.SchedulerOperationId != previous.SchedulerOperationId || retained.WakeOperationId != previous.WakeOperationId ||
            retained.MessageId != previous.MessageId || retained.CustodyEpoch != previous.CustodyEpoch ||
            retained.SchedulerAttempt != previous.SchedulerAttempt ||
            await wakes.AnyAsync(x => x.TimerId == previous.TimerId && x.Generation > previous.Generation, ct) ||
            await decisions.AnyAsync(x => x.DecisionId == decisionId, ct) ||
            await transitions.AnyAsync(x => x.OperationId == previous.WakeOperationId, ct) ||
            await custody.AnyAsync(x => x.Phase != 3 || x.OperationId == previous.WakeOperationId, ct) ||
            !await transitions.AnyAsync(x => x.OperationId == previous.SchedulerOperationId && x.StepId == previous.StepId &&
                x.ResultingVersion == previous.ExpectedWorkflowVersion && x.SpecificationHash == previous.SpecificationHash &&
                x.ResultingState == (int)WorkflowState.WaitingTimer, ct) ||
            !await custody.AnyAsync(x => x.OperationId == previous.SchedulerOperationId && x.StepId == previous.StepId &&
                x.Epoch == previous.CustodyEpoch && x.SpecificationHash == previous.SpecificationHash &&
                x.Phase == 3 && x.LastCommitReceiptId == previous.SchedulerOperationId, ct))
            throw new InvalidOperationException("Original wake history or custody no longer matches the reconciled predecessor.");
        var steps = context.Set<WorkflowStepRecord>().Where(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId);
        if (!await steps.AnyAsync(x => x.StepId == previous.StepId && x.Ordinal == 1 &&
                x.Version == previous.StepVersion && x.State == (int)WorkflowStepState.WaitingTimer, ct) ||
            await steps.AnyAsync(x => x.Ordinal < 1 && x.State != (int)WorkflowStepState.Succeeded, ct))
            throw new InvalidOperationException("Original waiting step no longer matches.");
        var timer = await context.Set<WorkflowTimerRecord>().SingleAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.StepId == previous.StepId && x.TimerId == previous.TimerId && x.Generation == previous.Generation, ct);
        await context.Entry(timer).ReloadAsync(ct);
        if (timer.Status != 2 || timer.Version != expectedTimerVersion || timer.SchedulerAttempts != previous.SchedulerAttempt ||
            timer.DueAtUtc != specification.DueAtUtc)
            throw new InvalidOperationException("Original timer changed since reconciliation.");
        timer.Status = 3;
        timer.Version = WorkflowStateRules.NextVersion(timer.Version, expectedTimerVersion);
        instance.Version = WorkflowStateRules.NextVersion(instance.Version, previous.ExpectedWorkflowVersion);
        context.Add(new WorkflowTimerRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId,
            StepId = successor.StepId, TimerId = successor.TimerId, Generation = successor.Generation,
            Version = 1, Status = 2, DueAtUtc = specification.DueAtUtc, SchedulerAttempts = successor.SchedulerAttempt
        });
        context.Add(new WorkflowWakeOperationRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId,
            StepId = successor.StepId, TimerId = successor.TimerId, Generation = successor.Generation,
            SchedulerAttempt = successor.SchedulerAttempt, CustodyEpoch = successor.CustodyEpoch,
            SchedulerOperationId = successor.SchedulerOperationId, WakeOperationId = successor.WakeOperationId,
            MessageId = successor.MessageId, DescriptorHash = successor.DescriptorHash, Descriptor = successor.WriteCanonical()
        });
        context.Add(new WorkflowRecoveryDecisionRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId,
            DecisionId = decisionId, OperationId = previous.SchedulerOperationId, StepId = previous.StepId, Kind = 1,
            EvidenceHash = evidenceHash, SpecificationHash = previous.SpecificationHash,
            ResultingVersion = instance.Version, RecordedAtUtc = effectiveUtc.ToUniversalTime()
        });
        context.Add(new WorkflowCustodyRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId,
            StepId = successor.StepId, OperationId = successor.SchedulerOperationId, SpecificationHash = successor.SpecificationHash,
            WorkerId = workerId, WorkerBootId = workerBootId, Epoch = successor.CustodyEpoch, Version = 1, Phase = 3,
            LastCommitReceiptId = successor.SchedulerOperationId, StartedAtUtc = effectiveUtc.ToUniversalTime(),
            HeartbeatAtUtc = effectiveUtc.ToUniversalTime()
        });
        context.Add(new WorkflowTransitionRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId,
            OperationId = successor.SchedulerOperationId, StepId = successor.StepId, SpecificationHash = successor.SpecificationHash,
            ResultingVersion = instance.Version, ResultingState = (int)WorkflowState.WaitingTimer,
            RecordedAtUtc = effectiveUtc.ToUniversalTime()
        });
        await context.SaveChangesAsync(ct);
        return new(successor, instance.Version, false);
    }
}
