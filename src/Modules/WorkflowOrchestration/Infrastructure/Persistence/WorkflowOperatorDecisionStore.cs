using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>Stages decision metadata only. Caller must retain custody/current authority and pair AUD/floor on the same root.</summary>
public sealed class WorkflowOperatorDecisionStore(WorkflowDbContext context)
{
    public async Task<WorkflowStoredResult> StageAsync(WorkflowReconciliationCommand command, Guid projectId, Guid environmentId,
        Func<CancellationToken, Task<bool>> verifyEvidence, DateTimeOffset recordedAtUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(verifyEvidence);
        ct.ThrowIfCancellationRequested();
        if (!context.IncludesOperatorReconciliation || context.Database.CurrentTransaction is null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Operator decision requires a clean revision 5 caller root.");
        if (projectId == Guid.Empty || environmentId == Guid.Empty || recordedAtUtc == default ||
            recordedAtUtc == DateTimeOffset.MaxValue || recordedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Trusted owner scope and time required.");

        var identity = new WorkflowIdentity(command.InstallationId, command.TenantId, command.InstanceId);
        var locked = await new WorkflowStore(context).ReadLockedInstanceAsync(identity, ct);
        if (locked is null || locked.Value.Hash != command.SpecificationHash)
            throw new InvalidOperationException("Operator decision owner unavailable.");
        var instance = await context.Set<WorkflowInstanceRecord>().SingleAsync(x =>
            x.InstallationId == command.InstallationId && x.TenantId == command.TenantId && x.InstanceId == command.InstanceId, ct);
        await context.Entry(instance).ReloadAsync(ct);
        var spec = WorkflowSpecification.ReadCanonical(instance.Specification);
        if (spec.InstallationId != command.InstallationId || spec.TenantId != command.TenantId ||
            spec.InstanceId != command.InstanceId || spec.RequestId != instance.RequestId ||
            spec.ProjectId != projectId || spec.EnvironmentId != environmentId ||
            spec.SpecificationHash != command.SpecificationHash || recordedAtUtc < spec.CreatedAtUtc)
            throw new InvalidOperationException("Operator decision canonical scope conflict.");

        var prior = await context.Set<WorkflowOperatorDecisionRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstallationId == command.InstallationId && x.TenantId == command.TenantId && x.RequestId == command.RequestId, ct);
        if (prior is not null)
        {
            if (prior.InstanceId != command.InstanceId || prior.ActorId != command.ActorId || prior.CommandHash != command.CommandHash ||
                prior.OperationId != command.OperationId || prior.Kind != (int)command.Kind ||
                prior.EvidenceHash != command.EvidenceHash || prior.SpecificationHash != command.SpecificationHash ||
                prior.ExpectedVersion != command.ExpectedVersion || prior.ResultingVersion != command.ExpectedVersion + 1)
                throw new InvalidOperationException("Operator decision request/hash conflict.");
            return new(command.InstanceId, prior.ResultingVersion, true);
        }
        if (WorkflowStateRules.IsTerminal((WorkflowState)instance.State) ||
            !Enum.IsDefined((WorkflowState)instance.State) || !await verifyEvidence(ct))
            throw new InvalidOperationException("Operator decision requires verified current evidence and nonterminal owner.");

        instance.Version = WorkflowStateRules.NextVersion(instance.Version, command.ExpectedVersion);
        await context.SaveChangesAsync(ct);
        context.Add(new WorkflowOperatorDecisionRecord
        {
            InstallationId = command.InstallationId, TenantId = command.TenantId, InstanceId = command.InstanceId,
            RequestId = command.RequestId, ActorId = command.ActorId, OperationId = command.OperationId,
            Kind = (int)command.Kind, ExpectedVersion = command.ExpectedVersion, ResultingVersion = instance.Version,
            CommandHash = command.CommandHash, EvidenceHash = command.EvidenceHash,
            SpecificationHash = command.SpecificationHash, RecordedAtUtc = recordedAtUtc
        });
        await context.SaveChangesAsync(ct);
        return new(command.InstanceId, instance.Version, false);
    }
}
