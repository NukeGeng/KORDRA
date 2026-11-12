using System.Data;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>Owner projection. The caller must authorize the environment and revalidate before disclosure.</summary>
public sealed class WorkflowMetadataReader(WorkflowDbContext context)
{
    private const int HistoryLimit = 50;
    private const int StepLimit = 32;

    public async Task<WorkflowMetadataPage?> ReadAsync(WorkflowIdentity identity, Guid projectId, Guid environmentId,
        long? beforeVersion = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (projectId == Guid.Empty || environmentId == Guid.Empty || beforeVersion is < 1)
            throw new ArgumentException("An owner-resolved project and environment and positive cursor are required.");
        ct.ThrowIfCancellationRequested();
        if (context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException("Workflow metadata requires the caller's open connection.");

        var instances = context.Set<WorkflowInstanceRecord>().AsNoTracking().Where(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId);
        var instance = await instances.SingleOrDefaultAsync(ct);
        if (instance is null) return null;
        var specification = WorkflowSpecification.ReadCanonical(instance.Specification);
        if (specification.InstallationId != identity.InstallationId || specification.TenantId != identity.TenantId ||
            specification.InstanceId != identity.InstanceId || specification.SpecificationHash != instance.SpecificationHash ||
            specification.RequestId != instance.RequestId || specification.DeadlineUtc != instance.DeadlineUtc ||
            instance.Version < 1 || !Enum.IsDefined((WorkflowState)instance.State))
            throw new InvalidOperationException("Workflow metadata binding is inconsistent.");
        if (specification.ProjectId != projectId || specification.EnvironmentId != environmentId) return null;

        var timers = context.Set<WorkflowTimerRecord>().AsNoTracking().Where(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            (x.Status == 1 || x.Status == 2));
        var steps = await context.Set<WorkflowStepRecord>().AsNoTracking().Where(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId)
            .OrderBy(x => x.Ordinal).Take(StepLimit + 1)
            .Select(x => new WorkflowStepMetadata(x.StepId, x.Ordinal, x.Version, x.State,
                timers.Where(t => t.StepId == x.StepId).OrderByDescending(t => t.Generation)
                    .Select(t => (DateTimeOffset?)t.DueAtUtc).FirstOrDefault()))
            .ToListAsync(ct);
        if (steps.Count > StepLimit || steps.Any(x => !Enum.IsDefined((WorkflowStepState)x.State)))
            throw new InvalidOperationException("Workflow step metadata is inconsistent.");

        var historyQuery = context.Set<WorkflowTransitionRecord>().AsNoTracking().Where(x =>
            x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
            x.ResultingVersion <= instance.Version);
        if (beforeVersion is { } cursor)
            historyQuery = historyQuery.Where(x => x.ResultingVersion < cursor);
        var history = await historyQuery.OrderByDescending(x => x.ResultingVersion).Take(HistoryLimit + 1)
            .Select(x => new WorkflowTransitionMetadata(x.ResultingVersion, x.StepId, x.ResultingState, x.RecordedAtUtc))
            .ToListAsync(ct);
        if (history.Any(x => !Enum.IsDefined((WorkflowState)x.State)) ||
            await instances.Select(x => (long?)x.Version).SingleOrDefaultAsync(ct) != instance.Version)
            throw new InvalidOperationException("Workflow metadata changed during the read or is inconsistent.");
        long? next = null;
        if (history.Count > HistoryLimit)
        {
            history.RemoveAt(HistoryLimit);
            next = history[^1].Version;
        }
        return new(instance.InstanceId, instance.Version, instance.State, instance.DeadlineExceeded,
            instance.DeadlineUtc, steps.AsReadOnly(), history.AsReadOnly(), next);
    }
}
