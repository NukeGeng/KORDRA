using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

/// <summary>Adapts PRJ-owned environment facts without granting authority or accessing another owner's tables.</summary>
public sealed class ApprovalEnvironmentAuthorityReader(IDbContextFactory<ProjectDbContext> contexts) : IApprovalEnvironmentAuthorityReader, IApprovalEnvironmentIdentityReader
{
    public async Task<ReleasePolicyEnvironment?> ReadCurrentUnderAdmissionAsync(Guid environmentId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new ReleasePolicyEnvironmentReader(db).ReadCurrentUnderAdmissionAsync(environmentId, cancellationToken);
    }

    public async Task<ApprovalEnvironmentAuthority?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId,
        Guid environmentId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var owner = await new ReleasePolicyEnvironmentReader(db).ReadCurrentUnderAdmissionAsync(tenantId, projectId, environmentId, ct);
        return owner is null ? null : new(owner.TenantId, owner.ProjectId, owner.EnvironmentId, owner.EnvironmentRevision);
    }
}
