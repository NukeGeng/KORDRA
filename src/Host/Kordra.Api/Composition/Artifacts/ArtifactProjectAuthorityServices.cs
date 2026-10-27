using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public static class ArtifactProjectAuthorityServices
{
    /// <summary>Current owner adapters only; creates no grant, route or positive registry/SEC default.</summary>
    public static void AddArtifactProjectAuthorityOwnerServices(this IServiceCollection services)
    {
        services.AddSingleton<IProjectIdentityReader, CurrentProjectIdentityReader>();
        services.AddSingleton<IArtifactProjectAuthorityReader, CurrentArtifactProjectAuthorityReader>();
        services.AddSingleton<ArtifactProjectAuthorityReader>();
    }
}

public sealed class CurrentProjectIdentityReader(IDbContextFactory<ProjectDbContext> contexts) : IProjectIdentityReader
{
    public async Task<ProjectIdentity?> ReadCurrentProjectUnderAdmissionAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await new ProjectIdentityReader(db).ReadCurrentProjectUnderAdmissionAsync(projectId, ct);
    }
}

public sealed class CurrentArtifactProjectAuthorityReader(IProjectIdentityReader projects) : IArtifactProjectAuthorityReader
{
    public async Task<ArtifactProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await projects.ReadCurrentProjectUnderAdmissionAsync(projectId, ct);
        return project is null ? null : new(project.TenantId, project.ProjectId, project.Revision);
    }
}
