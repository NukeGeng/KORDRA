using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ProjectEnvironment;

/// <summary>Neutral owner facts; caller must hold admission and verify authority and INV agreement.</summary>
public sealed class ReleaseCatalogReader(ProjectDbContext db) : IReleaseCatalogReader
{
    public async Task<ReleaseCatalogSelection?> ReadUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid applicationId,
        Guid environmentId, Guid targetId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || applicationId == Guid.Empty ||
            environmentId == Guid.Empty || targetId == Guid.Empty) return null;
        var project = await new ProjectIdentityReader(db).ReadCurrentProjectUnderAdmissionAsync(projectId, cancellationToken);
        if (project is null || project.TenantId != tenantId) return null;
        var applications = await db.Applications.AsNoTracking().Where(x => x.Id == applicationId).Take(2).ToArrayAsync(cancellationToken);
        if (applications.Length != 1) return null;
        var app = applications[0];
        if (app.TenantId != tenantId || app.ProjectId != projectId || app.Status != "active" || app.Revision <= 0) return null;
        var environment = await new ReleasePolicyEnvironmentReader(db).ReadCurrentUnderAdmissionAsync(tenantId, projectId, environmentId, cancellationToken);
        if (environment is null || environment.ProjectRevision != project.Revision) return null;
        var association = await db.Associations.AsNoTracking().SingleOrDefaultAsync(x => x.TargetId == targetId, cancellationToken);
        if (association is null || association.TenantId != tenantId || association.ProjectId != projectId ||
            association.EnvironmentId != environmentId || association.Revision <= 0) return null;
        return new(tenantId, projectId, project.Revision, applicationId, app.Revision, environmentId,
            environment.EnvironmentRevision, environment.Classification, targetId, association.Revision);
    }
}
