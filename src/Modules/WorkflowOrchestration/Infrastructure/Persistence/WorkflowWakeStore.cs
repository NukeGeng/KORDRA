using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

public sealed record WorkflowWakeStoredResult(WorkflowWakeOperation Operation, long WorkflowVersion, bool Duplicate);

/// <summary>Initial scheduler publication only. No independent commit, authority or successor takeover.</summary>
public sealed class WorkflowWakeStore(WorkflowDbContext context)
{
    /// <summary>Completes the pure WaitUntil step from the exact retained wake; never dispatches provider work.</summary>
    public async Task<WorkflowWakeStoredResult> StageTimerDueCompletionAsync(WorkflowWakeDeliveryExpectation delivery,
        Guid workerId, Guid workerBootId, DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (!context.IncludesWakeOperations || context.Database.CurrentTransaction is null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Timer completion requires the expanded clean owner context on its caller root.");
        if (workerId == Guid.Empty || workerBootId == Guid.Empty || effectiveUtc == default)
            throw new ArgumentException("Current worker binding and effective instant required.");
        ct.ThrowIfCancellationRequested(); effectiveUtc = effectiveUtc.ToUniversalTime();
        var identity = delivery.Identity; var request = delivery.Request;
        var locked = await new WorkflowStore(context).ReadLockedInstanceAsync(identity, ct);
        if (locked is null || locked.Value.Hash != request.SpecificationHash)
            throw new InvalidOperationException("Workflow unavailable in trusted timer-delivery scope.");
        var instance = await context.Set<WorkflowInstanceRecord>().SingleAsync(x => x.InstallationId == identity.InstallationId &&
            x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId, ct);
        await context.Entry(instance).ReloadAsync(ct);
        var spec = WorkflowSpecification.ReadCanonical(instance.Specification);
        var row = await context.Set<WorkflowWakeOperationRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.StepId == request.StepId && x.TimerId == request.TimerId && x.Generation == request.Generation, ct)
            ?? throw new InvalidOperationException("Retained wake missing; reconciliation required.");
        var wake = WorkflowWakeOperation.ReadCanonical(row.Descriptor, spec);
        if (spec.InstallationId != identity.InstallationId || spec.TenantId != identity.TenantId || spec.InstanceId != identity.InstanceId ||
            row.DescriptorHash != wake.DescriptorHash || row.SchedulerOperationId != wake.SchedulerOperationId ||
            row.WakeOperationId != wake.WakeOperationId || row.MessageId != wake.MessageId || row.SchedulerAttempt != wake.SchedulerAttempt ||
            row.CustodyEpoch != wake.CustodyEpoch || request.OperationId != wake.WakeOperationId || request.StepId != wake.StepId ||
            request.TimerId != wake.TimerId || request.Generation != wake.Generation ||
            request.ExpectedVersion != wake.ExpectedWorkflowVersion || delivery.StepVersion != wake.StepVersion ||
            request.CustodyEpoch != wake.CustodyEpoch || request.SpecificationHash != wake.SpecificationHash ||
            request.TemplateHash != wake.TemplateHash || delivery.MessageId != wake.MessageId ||
            delivery.CorrelationId != wake.RequestId || delivery.CausationId != wake.SchedulerOperationId ||
            delivery.OccurredAtUtc != wake.OccurredAtUtc || delivery.DeadlineUtc != wake.DeadlineUtc ||
            spec.RequestId != instance.RequestId || !await context.Set<WorkflowInstanceRecord>().AsNoTracking().AnyAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
                x.CreatedAtUtc == spec.CreatedAtUtc && x.DeadlineUtc == spec.DeadlineUtc, ct))
            throw new InvalidOperationException("Timer delivery differs from the retained immutable wake.");
        var previous = await context.Set<WorkflowTransitionRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.OperationId == wake.WakeOperationId, ct);
        if (previous is not null)
        {
            var settled = await context.Set<WorkflowCustodyRecord>().AsNoTracking().AnyAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
                x.StepId == wake.StepId && x.OperationId == wake.WakeOperationId && x.Epoch == wake.CustodyEpoch && x.Phase == 3 &&
                x.LastCommitReceiptId == wake.WakeOperationId && x.SpecificationHash == wake.SpecificationHash, ct);
            if (wake.ExpectedWorkflowVersion == long.MaxValue || previous.ResultingVersion != wake.ExpectedWorkflowVersion + 1 ||
                previous.StepId != wake.StepId || previous.SpecificationHash != wake.SpecificationHash ||
                previous.ResultingState != (int)WorkflowState.Running || !settled)
                throw new InvalidOperationException("Existing timer completion conflicts; reconciliation required.");
            return new(wake, previous.ResultingVersion, true);
        }
        if (await WorkflowCancellationStore.HasIntentAsync(context, identity, ct))
            throw new InvalidOperationException("Cancellation intent forbids a fresh timer effect.");
        if (instance.State != (int)WorkflowState.WaitingTimer || instance.Version != wake.ExpectedWorkflowVersion || instance.DeadlineExceeded ||
            effectiveUtc < wake.OccurredAtUtc || effectiveUtc >= wake.DeadlineUtc || effectiveUtc < spec.DueAtUtc || effectiveUtc >= spec.DeadlineUtc)
            throw new InvalidOperationException("Timer completion is not current, due or within its original window.");
        var step = await context.Set<WorkflowStepRecord>().SingleAsync(x => x.InstallationId == identity.InstallationId &&
            x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.StepId == wake.StepId, ct);
        await context.Entry(step).ReloadAsync(ct);
        var schedulerSettled = await context.Set<WorkflowCustodyRecord>().AsNoTracking().AnyAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.StepId == wake.StepId && x.OperationId == wake.SchedulerOperationId && x.Phase == 3 && x.Epoch == wake.CustodyEpoch &&
            x.LastCommitReceiptId == wake.SchedulerOperationId && x.SpecificationHash == wake.SpecificationHash, ct);
        var schedulerRecorded = await context.Set<WorkflowTransitionRecord>().AsNoTracking().AnyAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.StepId == wake.StepId && x.OperationId == wake.SchedulerOperationId && x.ResultingVersion == wake.ExpectedWorkflowVersion &&
            x.SpecificationHash == wake.SpecificationHash && x.ResultingState == (int)WorkflowState.WaitingTimer, ct);
        if (step.Ordinal != 1 || step.State != (int)WorkflowStepState.WaitingTimer || step.Version != wake.StepVersion || !schedulerSettled ||
            !schedulerRecorded ||
            await context.Set<WorkflowCustodyRecord>().AsNoTracking().AnyAsync(x => x.InstallationId == identity.InstallationId &&
                x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.Phase != 3, ct) ||
            await context.Set<WorkflowStepRecord>().AsNoTracking().AnyAsync(x => x.InstallationId == identity.InstallationId &&
                x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.Ordinal < step.Ordinal &&
                x.State != (int)WorkflowStepState.Succeeded, ct))
            throw new InvalidOperationException("Timer completion predecessor state/custody conflict.");
        var timer = await context.Set<WorkflowTimerRecord>().SingleAsync(x => x.InstallationId == identity.InstallationId &&
            x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.StepId == wake.StepId &&
            x.TimerId == wake.TimerId && x.Generation == wake.Generation, ct);
        await context.Entry(timer).ReloadAsync(ct);
        if (timer.Status != 2 || timer.SchedulerAttempts != wake.SchedulerAttempt ||
            !await context.Set<WorkflowTimerRecord>().AsNoTracking().AnyAsync(x => x.InstallationId == identity.InstallationId &&
                x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.StepId == wake.StepId &&
                x.TimerId == wake.TimerId && x.Generation == wake.Generation && x.DueAtUtc == spec.DueAtUtc, ct))
            throw new InvalidOperationException("Timer generation is not the exact published wake.");
        instance.Version = WorkflowStateRules.NextVersion(instance.Version, wake.ExpectedWorkflowVersion);
        instance.State = (int)WorkflowState.Running;
        step.Version = WorkflowStateRules.NextVersion(step.Version, wake.StepVersion); step.State = (int)WorkflowStepState.Running;
        timer.Version = WorkflowStateRules.NextVersion(timer.Version, timer.Version); timer.Status = 3;
        await context.SaveChangesAsync(ct);
        // WaitUntil has no external effect: close the admitted pure step inside the same uncommitted root.
        step.Version = WorkflowStateRules.NextVersion(step.Version, step.Version); step.State = (int)WorkflowStepState.Succeeded;
        await context.SaveChangesAsync(ct);
        context.Add(new WorkflowCustodyRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId, StepId = wake.StepId,
            OperationId = wake.WakeOperationId, SpecificationHash = wake.SpecificationHash, WorkerId = workerId, WorkerBootId = workerBootId,
            Epoch = wake.CustodyEpoch, Version = 1, Phase = 3, LastCommitReceiptId = wake.WakeOperationId,
            StartedAtUtc = effectiveUtc, HeartbeatAtUtc = effectiveUtc
        });
        context.Add(new WorkflowTransitionRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId, StepId = wake.StepId,
            OperationId = wake.WakeOperationId, SpecificationHash = wake.SpecificationHash, ResultingState = (int)WorkflowState.Running,
            ResultingVersion = instance.Version, RecordedAtUtc = effectiveUtc
        });
        await context.SaveChangesAsync(ct);
        return new(wake, instance.Version, false);
    }

    public async Task<WorkflowWakeStoredResult> StageInitialDispatchAsync(WorkflowWakeOperation operation,
        long expectedWorkflowVersion, long expectedTimerVersion, Guid workerId, Guid workerBootId,
        DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!context.IncludesWakeOperations || context.Database.CurrentTransaction is null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Wake dispatch requires the expanded clean owner context on its caller root.");
        if (expectedWorkflowVersion is < 1 or long.MaxValue || expectedTimerVersion is < 1 or long.MaxValue ||
            operation.ExpectedWorkflowVersion != expectedWorkflowVersion + 1 || workerId == Guid.Empty || workerBootId == Guid.Empty ||
            operation.Generation != 1 || operation.SchedulerAttempt != 1 || operation.CustodyEpoch != 1 || effectiveUtc == default)
            throw new ArgumentException("Initial wake requires exact bounded versions and initial generation/attempt/epoch.");
        ct.ThrowIfCancellationRequested(); effectiveUtc = effectiveUtc.ToUniversalTime();
        var identity = new WorkflowIdentity(operation.InstallationId, operation.TenantId, operation.InstanceId);
        var locked = await new WorkflowStore(context).ReadLockedInstanceAsync(identity, ct);
        if (locked is null || locked.Value.Hash != operation.SpecificationHash)
            throw new InvalidOperationException("Workflow unavailable in trusted wake scope.");
        var instance = await context.Set<WorkflowInstanceRecord>().SingleAsync(x => x.InstallationId == identity.InstallationId &&
            x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId, ct);
        await context.Entry(instance).ReloadAsync(ct);
        var spec = WorkflowSpecification.ReadCanonical(instance.Specification);
        var wake = WorkflowWakeOperation.ReadCanonical(operation.WriteCanonical(), spec);
        if (spec.RequestId != instance.RequestId || !await context.Set<WorkflowInstanceRecord>().AsNoTracking().AnyAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.CreatedAtUtc == spec.CreatedAtUtc && x.DeadlineUtc == spec.DeadlineUtc, ct))
            throw new InvalidOperationException("Workflow canonical scalar binding conflict.");
        var existing = await context.Set<WorkflowWakeOperationRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.StepId == wake.StepId && x.TimerId == wake.TimerId && x.Generation == wake.Generation, ct);
        if (existing is not null)
        {
            var transition = await context.Set<WorkflowTransitionRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
                x.OperationId == wake.SchedulerOperationId, ct);
            var custodyMatches = await context.Set<WorkflowCustodyRecord>().AsNoTracking().AnyAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
                x.StepId == wake.StepId && x.OperationId == wake.SchedulerOperationId && x.WorkerId == workerId &&
                x.WorkerBootId == workerBootId && x.Epoch == wake.CustodyEpoch && x.Phase == 3 &&
                x.SpecificationHash == wake.SpecificationHash && x.LastCommitReceiptId == wake.SchedulerOperationId, ct);
            if (existing.DescriptorHash != wake.DescriptorHash || existing.SchedulerAttempt != wake.SchedulerAttempt ||
                existing.CustodyEpoch != wake.CustodyEpoch || existing.SchedulerOperationId != wake.SchedulerOperationId ||
                existing.WakeOperationId != wake.WakeOperationId || existing.MessageId != wake.MessageId ||
                !existing.Descriptor.AsSpan().SequenceEqual(wake.WriteCanonical()) ||
                transition is null || transition.StepId != wake.StepId || transition.ResultingVersion != wake.ExpectedWorkflowVersion ||
                transition.SpecificationHash != wake.SpecificationHash || transition.ResultingState != (int)WorkflowState.WaitingTimer || !custodyMatches)
                throw new InvalidOperationException("Existing scheduler operation binding conflict; reconciliation required.");
            return new(WorkflowWakeOperation.ReadCanonical(existing.Descriptor, spec), transition.ResultingVersion, true);
        }
        if (await WorkflowCancellationStore.HasIntentAsync(context, identity, ct))
            throw new InvalidOperationException("Cancellation intent forbids a fresh timer effect.");
        if (instance.State != (int)WorkflowState.WaitingTimer || instance.Version != expectedWorkflowVersion || instance.DeadlineExceeded ||
            effectiveUtc < wake.OccurredAtUtc || effectiveUtc >= wake.DeadlineUtc || effectiveUtc < spec.DueAtUtc || effectiveUtc >= spec.DeadlineUtc)
            throw new InvalidOperationException("Wake is not due in the current executable owner version/window.");
        var step = await context.Set<WorkflowStepRecord>().SingleOrDefaultAsync(x => x.InstallationId == identity.InstallationId &&
            x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.StepId == wake.StepId, ct)
            ?? throw new InvalidOperationException("Wake step unavailable.");
        await context.Entry(step).ReloadAsync(ct);
        if (step.Ordinal != 1 || step.State != (int)WorkflowStepState.WaitingTimer || step.Version != wake.StepVersion ||
            await context.Set<WorkflowStepRecord>().AsNoTracking().AnyAsync(x => x.InstallationId == identity.InstallationId &&
                x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.Ordinal < step.Ordinal &&
                x.State != (int)WorkflowStepState.Succeeded, ct) ||
            await context.Set<WorkflowCustodyRecord>().AsNoTracking().AnyAsync(x => x.InstallationId == identity.InstallationId &&
                x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.Phase != 3, ct))
            throw new InvalidOperationException("Wake predecessor step/custody has not settled.");
        var predecessor = await context.Set<WorkflowTransitionRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.StepId == wake.StepId && x.ResultingVersion == expectedWorkflowVersion &&
            x.ResultingState == (int)WorkflowState.WaitingTimer && x.SpecificationHash == wake.SpecificationHash, ct);
        if (predecessor is null || !await context.Set<WorkflowCustodyRecord>().AsNoTracking().AnyAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.StepId == wake.StepId && x.OperationId == predecessor.OperationId && x.Phase == 3 &&
            x.SpecificationHash == wake.SpecificationHash && x.LastCommitReceiptId == predecessor.OperationId, ct))
            throw new InvalidOperationException("Exact settled waiting-timer predecessor receipt required.");
        var timer = await context.Set<WorkflowTimerRecord>().SingleOrDefaultAsync(x => x.InstallationId == identity.InstallationId &&
            x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.StepId == wake.StepId &&
            x.TimerId == wake.TimerId && x.Generation == wake.Generation, ct)
            ?? throw new InvalidOperationException("Exact timer generation unavailable.");
        await context.Entry(timer).ReloadAsync(ct);
        if (timer.Status != 1 || timer.Version != expectedTimerVersion || timer.SchedulerAttempts != 0 ||
            !await context.Set<WorkflowTimerRecord>().AsNoTracking().AnyAsync(x => x.InstallationId == identity.InstallationId &&
                x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.StepId == wake.StepId &&
                x.TimerId == wake.TimerId && x.Generation == wake.Generation && x.DueAtUtc == spec.DueAtUtc, ct))
            throw new InvalidOperationException("Timer already published or immutable due/version conflict.");
        instance.Version = WorkflowStateRules.NextVersion(instance.Version, expectedWorkflowVersion);
        timer.Version = WorkflowStateRules.NextVersion(timer.Version, expectedTimerVersion);
        timer.Status = 2; timer.SchedulerAttempts = WorkflowTimingRules.NextSchedulerAttempt(timer.SchedulerAttempts);
        await context.SaveChangesAsync(ct);
        context.Add(new WorkflowWakeOperationRecord {
            InstallationId = wake.InstallationId, TenantId = wake.TenantId, InstanceId = wake.InstanceId, StepId = wake.StepId,
            TimerId = wake.TimerId, Generation = wake.Generation, SchedulerAttempt = wake.SchedulerAttempt, CustodyEpoch = wake.CustodyEpoch,
            SchedulerOperationId = wake.SchedulerOperationId, WakeOperationId = wake.WakeOperationId, MessageId = wake.MessageId,
            DescriptorHash = wake.DescriptorHash, Descriptor = wake.WriteCanonical()
        });
        context.Add(new WorkflowCustodyRecord {
            InstallationId = wake.InstallationId, TenantId = wake.TenantId, InstanceId = wake.InstanceId, StepId = wake.StepId,
            OperationId = wake.SchedulerOperationId, SpecificationHash = wake.SpecificationHash, WorkerId = workerId,
            WorkerBootId = workerBootId, Epoch = 1, Version = 1, Phase = 3, StartedAtUtc = effectiveUtc,
            HeartbeatAtUtc = effectiveUtc, LastCommitReceiptId = wake.SchedulerOperationId
        });
        context.Add(new WorkflowTransitionRecord {
            InstallationId = wake.InstallationId, TenantId = wake.TenantId, InstanceId = wake.InstanceId, StepId = wake.StepId,
            OperationId = wake.SchedulerOperationId, SpecificationHash = wake.SpecificationHash,
            ResultingState = (int)WorkflowState.WaitingTimer, ResultingVersion = instance.Version, RecordedAtUtc = effectiveUtc
        });
        await context.SaveChangesAsync(ct);
        return new(wake, instance.Version, false);
    }
}
