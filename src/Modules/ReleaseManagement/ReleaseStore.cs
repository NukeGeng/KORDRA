using Kordra.Modules.ReleaseManagement.Contracts;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ReleaseManagement;

/// <summary>REL-owned reads; caller holds admission and independently authorizes use or disclosure.</summary>
public sealed class ReleaseStore(ReleaseDbContext db) : IFrozenReleaseReader
{
    public async Task<FrozenReleaseReference?> ReadFrozenUnderAdmissionAsync(Guid installationId, Guid tenantId, Guid projectId,
        Guid revisionId, CancellationToken cancellationToken = default)
    {
        if (installationId == Guid.Empty || tenantId == Guid.Empty || projectId == Guid.Empty || revisionId == Guid.Empty) return null;
        var revision = await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId && x.TenantId == tenantId &&
            x.ProjectId == projectId && x.Id == revisionId, cancellationToken);
        if (revision is null || revision.State != "Frozen") return null;
        var intent = ReleaseIntegrity.ReadFrozenOrRetired(revision);
        var hash = intent.SpecHash;
        return new(installationId, tenantId, projectId, revision.ReleaseId, revision.Id, hash, intent.GetCanonicalBytes());
    }
}
