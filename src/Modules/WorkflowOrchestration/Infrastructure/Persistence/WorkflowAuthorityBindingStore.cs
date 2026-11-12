using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>
/// 12.2f owner staging only. The caller binds trusted actor/scope, current workflow.start + release.read, the IAM
/// delegation reference and commits WFL/AUD/floor atomically. Never commits, never leaves BlockedAuthority.
/// </summary>
public sealed class WorkflowAuthorityBindingStore(WorkflowDbContext context)
{
    /// <summary>
    /// The original result for an already recorded request, or null. Lets the caller avoid reissuing delegation for a retry.
    /// A changed command for the same request identity conflicts.
    /// </summary>
    public Task<WorkflowAuthorityBindingResult?> ReadRecordedAsync(WorkflowReauthorizationCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!context.IncludesAuthorizationReceipts)
            throw new InvalidOperationException("Reauthorization receipts require the revision 6 owner context.");
        return ReadRecordedCoreAsync(command, ct);
    }

    private async Task<WorkflowAuthorityBindingResult?> ReadRecordedCoreAsync(WorkflowReauthorizationCommand command, CancellationToken ct)
    {
        var prior = await context.Set<WorkflowAuthorityBindingRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstallationId == command.InstallationId && x.TenantId == command.TenantId && x.InstanceId == command.InstanceId &&
            x.RequestId == command.RequestId, ct);
        if (prior is null) return null;
        if (prior.ActorId != command.ActorId || prior.CommandHash != command.CommandHash ||
            prior.SpecificationHash != command.SpecificationHash || prior.ExpectedVersion != command.ExpectedVersion ||
            prior.ResultingVersion != command.ExpectedVersion + 1)
            throw new InvalidOperationException("Reauthorization request identity/hash conflict.");
        return new(prior.BindingId, prior.Generation, prior.ResultingVersion, true);
    }

    public async Task<WorkflowAuthorityBindingResult> StageReauthorizationAsync(WorkflowReauthorizationCommand command,
        WorkflowDelegationReference delegation, DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(delegation);
        ct.ThrowIfCancellationRequested();
        if (!context.IncludesAuthorizationReceipts || context.Database.CurrentTransaction is null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Reauthorization requires the revision 6 clean owner context on the caller root.");
        if (effectiveUtc == default || delegation.DelegationId == Guid.Empty || delegation.ValidUntilUtc == default)
            throw new ArgumentException("Trusted clock and exact delegation reference required.");
        effectiveUtc = effectiveUtc.ToUniversalTime();
        var identity = new WorkflowIdentity(command.InstallationId, command.TenantId, command.InstanceId);
        var locked = await new WorkflowStore(context).ReadLockedInstanceAsync(identity, ct);
        if (locked is null || locked.Value.Hash != command.SpecificationHash)
            throw new InvalidOperationException("Workflow unavailable in trusted reauthorization scope.");
        var instance = await context.Set<WorkflowInstanceRecord>().SingleAsync(x => x.InstallationId == identity.InstallationId &&
            x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId, ct);
        await context.Entry(instance).ReloadAsync(ct);
        var specification = WorkflowSpecification.ReadCanonical(instance.Specification);
        // Persisted timestamps compare through PostgreSQL parameters at insertion precision (PR29-WFL-TIMESTAMP-002).
        if (specification.InstallationId != identity.InstallationId || specification.TenantId != identity.TenantId ||
            specification.InstanceId != identity.InstanceId || specification.RequestId != instance.RequestId ||
            specification.SpecificationHash != command.SpecificationHash ||
            !await context.Set<WorkflowInstanceRecord>().AsNoTracking().AnyAsync(x => x.InstallationId == identity.InstallationId &&
                x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
                x.CreatedAtUtc == specification.CreatedAtUtc && x.DeadlineUtc == specification.DeadlineUtc, ct))
            throw new InvalidOperationException("Reauthorization canonical binding conflict.");

        var bindings = context.Set<WorkflowAuthorityBindingRecord>().AsNoTracking().Where(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId);
        if (await ReadRecordedCoreAsync(command, ct) is { } recorded) return recorded;

        var current = await bindings.OrderByDescending(x => x.Generation).Select(x => new { x.Generation, x.ActorId }).FirstOrDefaultAsync(ct);
        var scope = context.Set<WorkflowStepRecord>().AsNoTracking().Where(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId);
        var snapshot = new WorkflowReauthorizationSnapshot((WorkflowState)instance.State, specification.CreatedAtUtc, specification.DeadlineUtc,
            await WorkflowCancellationStore.HasIntentAsync(context, identity, ct),
            await context.Set<WorkflowCustodyRecord>().AsNoTracking().AnyAsync(x => x.InstallationId == identity.InstallationId &&
                x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.Phase != 3, ct),
            await scope.AnyAsync(x => x.State == (int)WorkflowStepState.Unknown, ct),
            await context.Set<WorkflowTimerRecord>().AsNoTracking().AnyAsync(x => x.InstallationId == identity.InstallationId &&
                x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId && x.Status == 2, ct),
            current?.Generation ?? 0, current?.ActorId ?? Guid.Empty);
        var generation = WorkflowReauthorizationRules.RequireEligible(command, snapshot, effectiveUtc);
        if (instance.Version != command.ExpectedVersion)
            throw new DbUpdateConcurrencyException("Workflow version changed before reauthorization.");

        // D3: record a new generation only. State stays BlockedAuthority; Running needs a later worker admission (12.3e).
        instance.Version = WorkflowStateRules.NextVersion(instance.Version, command.ExpectedVersion);
        await context.SaveChangesAsync(ct);
        var binding = new WorkflowAuthorityBindingRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId,
            BindingId = Guid.NewGuid(), DelegationId = delegation.DelegationId, ActorId = command.ActorId, Generation = generation,
            RecordedAtUtc = effectiveUtc, RequestId = command.RequestId, CommandHash = command.CommandHash,
            SpecificationHash = command.SpecificationHash, ExpectedVersion = command.ExpectedVersion, ResultingVersion = instance.Version
        };
        context.Add(binding);
        await context.SaveChangesAsync(ct);
        return new(binding.BindingId, binding.Generation, binding.ResultingVersion, false);
    }
}
