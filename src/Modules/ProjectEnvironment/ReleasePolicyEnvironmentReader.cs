using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ProjectEnvironment;

/// <summary>Neutral project/environment facts. The caller owns authorization and admission.</summary>
public sealed class ReleasePolicyEnvironmentReader(ProjectDbContext db) : IReleasePolicyEnvironmentReader
{
    public async Task<ReleasePolicyEnvironment?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId,
        Guid environmentId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || environmentId == Guid.Empty) return null;
        // Reject collisions even when the other owner's record is inactive.
        var projects = await db.Projects.AsNoTracking().Where(x => x.Id == projectId).Take(2).ToArrayAsync(cancellationToken);
        if (projects.Length != 1 || projects[0].TenantId != tenantId || projects[0].Status != "active" || projects[0].Revision <= 0)
            return null;
        var environments = await db.Environments.AsNoTracking().Where(x => x.Id == environmentId).Take(2).ToArrayAsync(cancellationToken);
        if (environments.Length != 1) return null;
        var env = environments[0];
        if (env.TenantId != tenantId || env.ProjectId != projectId || env.Status != "active" || env.Revision <= 0 ||
            env.Classification is not ("Development" or "Test" or "Staging" or "Production")) return null;
        return new(tenantId, projectId, projects[0].Revision, env.Id, env.Revision, env.Classification);
    }
}
