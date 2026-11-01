using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

public sealed record WorkflowStoredResult(Guid InstanceId, long Version, bool Duplicate);

/// <summary>Owner persistence only. Caller supplies trusted scope and current admission; never commits the root.</summary>
public sealed class WorkflowStore(WorkflowDbContext context)
{
    public async Task<WorkflowStoredResult> StageCreationAsync(WorkflowSpecification specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        RequireRoot();
        var receipt = await context.Set<WorkflowRequestReceiptRecord>().SingleOrDefaultAsync(x =>
            x.InstallationId == specification.InstallationId && x.TenantId == specification.TenantId &&
            x.RequestId == specification.RequestId, cancellationToken);
        if (receipt is not null)
        {
            if (receipt.ContentHash != specification.SpecificationHash || receipt.InstanceId != specification.InstanceId)
                throw new InvalidOperationException("Workflow request identity/hash conflict.");
            return new(receipt.InstanceId, 1, true);
        }
        context.Add(new WorkflowInstanceRecord {
            InstallationId = specification.InstallationId, TenantId = specification.TenantId,
            InstanceId = specification.InstanceId, RequestId = specification.RequestId,
            SpecificationHash = specification.SpecificationHash, Specification = specification.WriteCanonical(),
            State = (int)WorkflowState.Pending, Version = 1, CreatedAtUtc = specification.CreatedAtUtc, DeadlineUtc = specification.DeadlineUtc
        });
        for (var ordinal = 0; ordinal < 4; ordinal++)
            context.Add(new WorkflowStepRecord {
                InstallationId = specification.InstallationId, TenantId = specification.TenantId,
                InstanceId = specification.InstanceId, StepId = Guid.NewGuid(), Ordinal = ordinal,
                State = (int)WorkflowStepState.Pending, Version = 1
            });
        context.Add(new WorkflowRequestReceiptRecord {
            InstallationId = specification.InstallationId, TenantId = specification.TenantId,
            InstanceId = specification.InstanceId, RequestId = specification.RequestId,
            ContentHash = specification.SpecificationHash,
            ResultJson = JsonSerializer.Serialize(new { instanceId = specification.InstanceId, version = 1 }),
            RecordedAtUtc = specification.CreatedAtUtc
        });
        // Concurrent uniqueness conflicts require root rollback and independent receipt reconciliation, never a hidden retry.
        await context.SaveChangesAsync(cancellationToken);
        return new(specification.InstanceId, 1, false);
    }

    public async Task<WorkflowStoredResult> StageTransitionAsync(WorkflowIdentity identity, Guid stepId,
        Guid operationId, string specificationHash, long expectedVersion, WorkflowState nextState,
        DateTimeOffset recordedAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (stepId == Guid.Empty || operationId == Guid.Empty || recordedAtUtc == default ||
            specificationHash is null || specificationHash.Length != 64 ||
            specificationHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Invalid workflow transition identity.");
        RequireRoot();
        var prior = await context.Set<WorkflowTransitionRecord>().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId &&
            x.InstanceId == identity.InstanceId && x.OperationId == operationId, cancellationToken);
        if (prior is not null)
        {
            if (prior.SpecificationHash != specificationHash || prior.StepId != stepId ||
                prior.ResultingState != (int)nextState || expectedVersion < 1 ||
                expectedVersion == long.MaxValue || prior.ResultingVersion != expectedVersion + 1)
                throw new InvalidOperationException("Workflow operation identity/hash conflict.");
            return new(identity.InstanceId, prior.ResultingVersion, true);
        }
        var instance = await context.Set<WorkflowInstanceRecord>().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId &&
            x.InstanceId == identity.InstanceId, cancellationToken)
            ?? throw new InvalidOperationException("Workflow unavailable in the trusted scope.");
        if (instance.SpecificationHash != specificationHash ||
            !WorkflowStateRules.CanChangeState((WorkflowState)instance.State, nextState))
            throw new InvalidOperationException("Workflow binding/state conflict.");
        instance.Version = WorkflowStateRules.NextVersion(instance.Version, expectedVersion);
        instance.State = (int)nextState;
        // Establish the CAS before history insertion; both saves remain uncommitted in the same caller root.
        // A losing root must report the stale version, not hit the unique resulting-version index first.
        await context.SaveChangesAsync(cancellationToken);
        context.Add(new WorkflowTransitionRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId,
            StepId = stepId, OperationId = operationId, SpecificationHash = specificationHash,
            ResultingVersion = instance.Version, ResultingState = (int)nextState, RecordedAtUtc = recordedAtUtc.ToUniversalTime()
        });
        await context.SaveChangesAsync(cancellationToken);
        return new(identity.InstanceId, instance.Version, false);
    }

    /// <summary>Initial claim only. Successor takeover/reconciliation is deliberately not inferred from a stale heartbeat.</summary>
    public async Task<WorkflowCustodyRecord> StageInitialCustodyAsync(WorkflowIdentity identity, Guid stepId,
        Guid operationId, string specificationHash, Guid workerId, Guid workerBootId, long expectedStepVersion,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (stepId == Guid.Empty || operationId == Guid.Empty || workerId == Guid.Empty || workerBootId == Guid.Empty || now == default)
            throw new ArgumentException("Invalid initial workflow custody binding.");
        RequireRoot();
        var instance = await context.Set<WorkflowInstanceRecord>().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId &&
            x.InstanceId == identity.InstanceId, cancellationToken);
        if (instance is null || instance.SpecificationHash != specificationHash || instance.State != (int)WorkflowState.Running)
            throw new InvalidOperationException("Workflow custody binding/state conflict.");
        var previous = await context.Set<WorkflowCustodyRecord>().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId &&
            x.InstanceId == identity.InstanceId && x.StepId == stepId && x.OperationId == operationId, cancellationToken);
        if (previous is not null)
        {
            if (previous.WorkerId != workerId || previous.WorkerBootId != workerBootId || previous.Epoch != 1 ||
                previous.SpecificationHash != specificationHash)
                throw new InvalidOperationException("Existing custody cannot be taken over by an initial claim.");
            return previous;
        }
        var step = await context.Set<WorkflowStepRecord>().SingleOrDefaultAsync(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId &&
            x.InstanceId == identity.InstanceId && x.StepId == stepId, cancellationToken)
            ?? throw new InvalidOperationException("Workflow step unavailable in trusted scope.");
        if (step.State != (int)WorkflowStepState.Pending)
            throw new InvalidOperationException("Initial custody requires a pending step.");
        step.Version = WorkflowStateRules.NextVersion(step.Version, expectedStepVersion);
        step.State = (int)WorkflowStepState.Running;
        await context.SaveChangesAsync(cancellationToken);
        var custody = new WorkflowCustodyRecord {
            InstallationId = identity.InstallationId, TenantId = identity.TenantId, InstanceId = identity.InstanceId,
            StepId = stepId, OperationId = operationId, SpecificationHash = specificationHash,
            WorkerId = workerId, WorkerBootId = workerBootId, Epoch = 1, Version = 1,
            StartedAtUtc = now.ToUniversalTime(), HeartbeatAtUtc = now.ToUniversalTime(), Phase = 1
        };
        context.Add(custody);
        await context.SaveChangesAsync(cancellationToken);
        return custody;
    }

    private void RequireRoot()
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Workflow staging requires the caller-owned root transaction.");
    }
}
