using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>Stage only. Caller must bind trusted actor/scope/current workflow.cancel and commit owner/AUD/floor atomically.</summary>
public sealed class WorkflowCancellationStore(WorkflowDbContext context)
{
    public Task<WorkflowStoredResult> StageIntentAsync(WorkflowCancellationIntent intent,
        DateTimeOffset effectiveUtc, CancellationToken ct = default) =>
        StageCoreAsync(intent, null, effectiveUtc, ct);

    public Task<WorkflowStoredResult> StageScopedIntentAsync(WorkflowCancellationIntent intent,
        Guid projectId, Guid environmentId, DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        if (projectId == Guid.Empty || environmentId == Guid.Empty)
            throw new ArgumentException("Exact trusted project and environment required.");
        return StageCoreAsync(intent, (projectId, environmentId), effectiveUtc, ct);
    }

    private async Task<WorkflowStoredResult> StageCoreAsync(WorkflowCancellationIntent intent,
        (Guid Project, Guid Environment)? scope, DateTimeOffset effectiveUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(intent); ct.ThrowIfCancellationRequested();
        if (!context.IncludesCancellationIntents || context.Database.CurrentTransaction is null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Cancellation requires the expanded clean owner context on the caller root.");
        if (effectiveUtc == default) throw new ArgumentException("Trusted effective clock required.");
        var identity = new WorkflowIdentity(intent.InstallationId, intent.TenantId, intent.InstanceId);
        var locked = await new WorkflowStore(context).ReadLockedInstanceAsync(identity, ct);
        if (locked is null || locked.Value.Hash != intent.SpecificationHash)
            throw new InvalidOperationException("Workflow unavailable in trusted cancellation scope.");
        var instance = await context.Set<WorkflowInstanceRecord>().SingleAsync(x => x.InstallationId == identity.InstallationId &&
            x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId, ct);
        await context.Entry(instance).ReloadAsync(ct);
        var specification = WorkflowSpecification.ReadCanonical(instance.Specification);
        if (specification.InstallationId != identity.InstallationId || specification.TenantId != identity.TenantId ||
            specification.InstanceId != identity.InstanceId || specification.RequestId != instance.RequestId ||
            specification.SpecificationHash != intent.SpecificationHash ||
            !await context.Set<WorkflowInstanceRecord>().AsNoTracking().AnyAsync(x => x.InstallationId == identity.InstallationId &&
                x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.CreatedAtUtc == specification.CreatedAtUtc &&
                x.DeadlineUtc == specification.DeadlineUtc, ct))
            throw new InvalidOperationException("Cancellation canonical binding conflict.");
        if (scope is { } expected && (specification.ProjectId != expected.Project ||
            specification.EnvironmentId != expected.Environment))
            throw new InvalidOperationException("Workflow unavailable in trusted cancellation scope.");
        var prior = await context.Set<WorkflowCancellationIntentRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstallationId == intent.InstallationId && x.TenantId == intent.TenantId && x.RequestId == intent.RequestId, ct);
        if (prior is not null)
        {
            if (prior.InstanceId != intent.InstanceId || prior.ActorId != intent.ActorId || prior.CommandHash != intent.CommandHash ||
                prior.SpecificationHash != intent.SpecificationHash || prior.ExpectedVersion != intent.ExpectedVersion ||
                prior.ResultingVersion != intent.ExpectedVersion + 1)
                throw new InvalidOperationException("Cancellation request identity/hash conflict.");
            return new(intent.InstanceId, prior.ResultingVersion, true);
        }
        if (WorkflowStateRules.IsTerminal((WorkflowState)instance.State) || !Enum.IsDefined((WorkflowState)instance.State) ||
            effectiveUtc.ToUniversalTime() < specification.CreatedAtUtc || await HasIntentAsync(context, identity, ct))
            throw new InvalidOperationException("Cancellation cannot resurrect terminal state or replace a recorded intent.");
        instance.Version = WorkflowStateRules.NextVersion(instance.Version, intent.ExpectedVersion);
        // Pending/Reconciling retain their accepted graph state. The append-only intent blocks new work;
        // it never hides Unknown or claims that confirmed effects were undone.
        if (WorkflowStateRules.CanChangeState((WorkflowState)instance.State, WorkflowState.CancelRequested))
            instance.State = (int)WorkflowState.CancelRequested;
        await context.SaveChangesAsync(ct);
        context.Add(new WorkflowCancellationIntentRecord {
            InstallationId = intent.InstallationId, TenantId = intent.TenantId, InstanceId = intent.InstanceId,
            RequestId = intent.RequestId, ActorId = intent.ActorId, SpecificationHash = intent.SpecificationHash,
            CommandHash = intent.CommandHash, ExpectedVersion = intent.ExpectedVersion, ResultingVersion = instance.Version,
            RecordedAtUtc = effectiveUtc.ToUniversalTime()
        });
        await context.SaveChangesAsync(ct);
        return new(intent.InstanceId, instance.Version, false);
    }

    internal static Task<bool> HasIntentAsync(WorkflowDbContext context, WorkflowIdentity identity, CancellationToken ct)
        => context.IncludesCancellationIntents ? context.Set<WorkflowCancellationIntentRecord>().AsNoTracking().AnyAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId, ct)
            : Task.FromResult(false);
}
