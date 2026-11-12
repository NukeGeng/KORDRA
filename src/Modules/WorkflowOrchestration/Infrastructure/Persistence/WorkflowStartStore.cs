using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>Stages an admitted start on the caller root. Authority and disclosure remain with the caller.</summary>
public sealed class WorkflowStartStore(WorkflowDbContext context)
{
    public Task<WorkflowStoredResult> StageAsync(WorkflowStartCommand command,
        Func<WorkflowSpecification> createSpecification, DateTimeOffset effectiveUtc, CancellationToken ct = default) =>
        StageCoreAsync(command, createSpecification, effectiveUtc, null, ct);

    /// <summary>Checks trusted owner facts for both a fresh specification and the canonical original on retry.</summary>
    public Task<WorkflowStoredResult> StageAsync(WorkflowStartCommand command,
        Func<WorkflowSpecification> createSpecification, DateTimeOffset effectiveUtc,
        Func<WorkflowSpecification, bool> matchesOwner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(matchesOwner);
        return StageCoreAsync(command, createSpecification, effectiveUtc, matchesOwner, ct);
    }

    private async Task<WorkflowStoredResult> StageCoreAsync(WorkflowStartCommand command,
        Func<WorkflowSpecification> createSpecification, DateTimeOffset effectiveUtc,
        Func<WorkflowSpecification, bool>? matchesOwner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(createSpecification);
        ct.ThrowIfCancellationRequested();
        if (!context.IncludesStartReceipts || context.Database.CurrentTransaction is null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Workflow start requires a clean revision 4 caller root.");

        var prior = await context.Set<WorkflowStartReceiptRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstallationId == command.InstallationId && x.TenantId == command.TenantId &&
            x.ActorId == command.ActorId && x.ScopedKeyHash == command.ScopedKeyHash, ct);
        if (prior is not null)
        {
            if (prior.BusinessHash != command.BusinessHash || prior.RequestId != command.RequestId)
                throw new InvalidOperationException("Workflow start key conflicts with the original request.");
            var original = await context.Set<WorkflowRequestReceiptRecord>().AsNoTracking().SingleAsync(x =>
                x.InstallationId == command.InstallationId && x.TenantId == command.TenantId && x.RequestId == prior.RequestId, ct);
            var owner = await context.Set<WorkflowInstanceRecord>().AsNoTracking().SingleAsync(x =>
                x.InstallationId == command.InstallationId && x.TenantId == command.TenantId &&
                x.InstanceId == original.InstanceId && x.RequestId == prior.RequestId, ct);
            var frozen = WorkflowSpecification.ReadCanonical(owner.Specification);
            if (frozen.InstanceId != original.InstanceId || !command.Matches(frozen) ||
                frozen.SpecificationHash != original.ContentHash || original.ContentHash != owner.SpecificationHash ||
                matchesOwner is not null && !matchesOwner(frozen))
                throw new InvalidOperationException("Workflow start receipt differs from the original owner binding.");
            return new(original.InstanceId, 1, true);
        }

        var specification = createSpecification();
        if (specification is null || !command.Matches(specification) || matchesOwner is not null && !matchesOwner(specification))
            throw new InvalidOperationException("Workflow specification differs from its start command.");
        var result = await new WorkflowStore(context).StageCreationAsync(specification, effectiveUtc, ct);
        if (result.Duplicate)
            throw new InvalidOperationException("Workflow request ID is already bound without this exact start key.");

        context.Add(new WorkflowStartReceiptRecord
        {
            InstallationId = command.InstallationId, TenantId = command.TenantId, ActorId = command.ActorId,
            RequestId = command.RequestId, ScopedKeyHash = command.ScopedKeyHash, BusinessHash = command.BusinessHash
        });
        // Unique conflicts abort the caller root. The next explicit retry looks up the committed original.
        await context.SaveChangesAsync(ct);
        return result;
    }
}
