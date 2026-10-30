using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.InfrastructureInventory;

/// <summary>Read registered INV identity under the caller's admission; no provider or credential access.</summary>
public sealed class ReleaseTargetReader(InventoryDbContext db) : IReleaseTargetReader
{
    public async Task<ReleaseTargetIdentity?> ReadUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid environmentId,
        Guid targetId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || environmentId == Guid.Empty || targetId == Guid.Empty) return null;
        var target = await db.Targets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == targetId, cancellationToken);
        if (target is null || target.TenantId != tenantId || target.ProjectId != projectId || target.EnvironmentId != environmentId ||
            target.Status != "active" || target.Revision <= 0 || target.AssociationRevision <= 0 || string.IsNullOrWhiteSpace(target.Kind)) return null;
        return new(tenantId, projectId, environmentId, targetId, target.Kind, target.Revision, target.AssociationRevision);
    }
}
