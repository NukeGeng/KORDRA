using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>Owner staging only. Caller supplies trusted scope/current authority and commits WFL/AUD/MSG together.</summary>
public sealed class WorkflowTimerStore(WorkflowDbContext context)
{
    private const int Scheduled = 1;
    private const int ActiveCustody = 1;
    private const int SettledCustody = 3;

    public async Task<WorkflowTimerScheduleResult> StageWaitingAsync(WorkflowTimerScheduleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request); ct.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Waiting timer requires a clean owner context on the caller root.");
        var identity = request.Identity;
        var locked = await new WorkflowStore(context).ReadLockedInstanceAsync(identity, ct);
        if (locked is null || locked.Value.Hash != request.SpecificationHash)
            throw new InvalidOperationException("Workflow unavailable in trusted timer scope.");
        var instance = await context.Set<WorkflowInstanceRecord>().SingleAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId, ct);
        await context.Entry(instance).ReloadAsync(ct);
        var specification = WorkflowSpecification.ReadCanonical(instance.Specification);
        if (specification.SpecificationHash != request.SpecificationHash || specification.InstallationId != identity.InstallationId ||
            specification.TenantId != identity.TenantId || specification.InstanceId != identity.InstanceId ||
            specification.RequestId != instance.RequestId)
            throw new InvalidOperationException("Timer canonical binding conflict.");
        // Compare through PostgreSQL parameters, using exactly the provider precision used at insertion.
        // Canonical bytes/hash retain all seven fractional digits; no tolerance or canonical truncation.
        var datesMatch = await context.Set<WorkflowInstanceRecord>().AsNoTracking().AnyAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.CreatedAtUtc == specification.CreatedAtUtc && x.DeadlineUtc == specification.DeadlineUtc, ct);
        if (!datesMatch) throw new InvalidOperationException("Timer canonical binding conflict.");
        var step = await context.Set<WorkflowStepRecord>().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId &&
            x.InstanceId == identity.InstanceId && x.StepId == request.StepId, ct)
            ?? throw new InvalidOperationException("Timer step unavailable in trusted scope.");
        await context.Entry(step).ReloadAsync(ct);
        if (step.Ordinal != 1) throw new InvalidOperationException("Canonical template step is not WaitUntil.");
        var custody = await context.Set<WorkflowCustodyRecord>().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId &&
            x.InstanceId == identity.InstanceId && x.StepId == request.StepId && x.OperationId == request.OperationId, ct)
            ?? throw new InvalidOperationException("Exact active timer operation custody required.");
        await context.Entry(custody).ReloadAsync(ct);
        if (custody.SpecificationHash != request.SpecificationHash || custody.WorkerId != request.Custody.WorkerId ||
            custody.WorkerBootId != request.Custody.WorkerBootId || custody.Epoch != request.Custody.Epoch)
            throw new InvalidOperationException("Timer custody binding conflict.");
        var previous = await context.Set<WorkflowTransitionRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId &&
            x.InstanceId == identity.InstanceId && x.OperationId == request.OperationId, ct);
        if (previous is not null)
        {
            var timer = await context.Set<WorkflowTimerRecord>().AsNoTracking().Where(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId &&
                x.InstanceId == identity.InstanceId && x.StepId == request.StepId && x.Generation == 1)
                .Select(x => new { x.TimerId, x.Generation, MatchesCanonicalDue = x.DueAtUtc == specification.DueAtUtc })
                .SingleOrDefaultAsync(ct);
            if (custody.Phase != SettledCustody || custody.LastCommitReceiptId != request.OperationId ||
                previous.StepId != request.StepId || previous.SpecificationHash != request.SpecificationHash ||
                previous.ResultingState != (int)WorkflowState.WaitingTimer ||
                previous.ResultingVersion != request.ExpectedWorkflowVersion + 1 || timer is null || !timer.MatchesCanonicalDue)
                throw new InvalidOperationException("Existing waiting-timer operation conflicts.");
            return new(timer.TimerId, timer.Generation, previous.ResultingVersion, true);
        }
        if (await WorkflowCancellationStore.HasIntentAsync(context, identity, ct))
            throw new InvalidOperationException("Cancellation intent forbids a fresh waiting timer.");
        if (locked.Value.State != WorkflowState.Running || instance.State != (int)WorkflowState.Running ||
            step.State != (int)WorkflowStepState.Running || custody.Phase != ActiveCustody ||
            custody.Version != request.Custody.Version || request.RecordedAtUtc < specification.CreatedAtUtc ||
            request.RecordedAtUtc >= specification.DeadlineUtc)
            throw new InvalidOperationException("Waiting timer state, clock or custody version conflict.");
        if (await context.Set<WorkflowStepRecord>().AsNoTracking().AnyAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.Ordinal < step.Ordinal && x.State != (int)WorkflowStepState.Succeeded, ct))
            throw new InvalidOperationException("Earlier canonical workflow steps are incomplete.");
        if (await context.Set<WorkflowTimerRecord>().AnyAsync(x => x.InstallationId == identity.InstallationId &&
            x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.StepId == request.StepId, ct))
            throw new InvalidOperationException("Initial waiting timer already exists.");
        instance.Version = WorkflowStateRules.NextVersion(instance.Version, request.ExpectedWorkflowVersion);
        step.Version = WorkflowStateRules.NextVersion(step.Version, request.ExpectedStepVersion);
        custody.Version = WorkflowStateRules.NextVersion(custody.Version, request.Custody.Version);
        instance.State = (int)WorkflowState.WaitingTimer; step.State = (int)WorkflowStepState.WaitingTimer;
        custody.Phase = SettledCustody; custody.LastCommitReceiptId = request.OperationId;
        // Establish all owner CAS changes before immutable history. Still no independent commit.
        await context.SaveChangesAsync(ct);
        var scheduled = new WorkflowTimerRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId,
            StepId = request.StepId, TimerId = Guid.NewGuid(), Generation = 1, Version = 1,
            Status = Scheduled, DueAtUtc = specification.DueAtUtc, SchedulerAttempts = 0
        };
        context.Add(scheduled);
        context.Add(new WorkflowTransitionRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId,
            StepId = request.StepId, OperationId = request.OperationId, SpecificationHash = request.SpecificationHash,
            ResultingState = (int)WorkflowState.WaitingTimer, ResultingVersion = instance.Version, RecordedAtUtc = request.RecordedAtUtc
        });
        await context.SaveChangesAsync(ct);
        return new(scheduled.TimerId, scheduled.Generation, instance.Version, false);
    }

    public async Task<IReadOnlyList<WorkflowDueTimerSnapshot>> DiscoverDueAsync(Guid installationId, Guid tenantId,
        DateTimeOffset effectiveNow, int limit = 100, CancellationToken ct = default)
    {
        if (installationId == Guid.Empty || tenantId == Guid.Empty || effectiveNow == default || limit is < 1 or > 100)
            throw new ArgumentException("Exact timer scope, effective clock and bounded page required.");
        ct.ThrowIfCancellationRequested(); effectiveNow = effectiveNow.ToUniversalTime();
        var rows = await context.Set<WorkflowTimerRecord>().AsNoTracking()
            .Where(x => x.InstallationId == installationId && x.TenantId == tenantId && x.Status == Scheduled && x.DueAtUtc <= effectiveNow)
            .OrderBy(x => x.DueAtUtc).ThenBy(x => x.TimerId).Take(limit).ToArrayAsync(ct);
        return rows.Select(x => new WorkflowDueTimerSnapshot(new(x.InstallationId, x.TenantId, x.InstanceId), x.StepId,
            x.TimerId, x.Generation, x.Version, x.DueAtUtc, x.SchedulerAttempts)).ToArray();
    }
}
