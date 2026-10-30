using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.ProjectEnvironment.Contracts;

namespace Kordra.Api;

public sealed class CurrentReleaseProjectAuthorityReader(IProjectIdentityReader projects) : IReleaseProjectAuthorityReader
{
    public async Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await projects.ReadCurrentProjectUnderAdmissionAsync(projectId, ct);
        return project is null ? null : new(project.TenantId, project.ProjectId, project.Revision);
    }
}
