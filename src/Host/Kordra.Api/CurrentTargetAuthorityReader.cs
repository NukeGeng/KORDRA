using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public sealed class CurrentProjectOwnershipReader(IDbContextFactory<ProjectDbContext> contexts, IEnvironmentPolicyReader legacyPolicy) : IProjectOwnershipReader
{
    public async Task<ProjectOwnership?> ResolveOwnershipAsync(Guid tenantId, Guid projectId, Guid? environmentId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var store = new PolicyBindingStore(db);
        var owner = await store.ReadOwnershipUnderAdmissionAsync(tenantId, projectId, environmentId, cancellationToken);
        if (owner is null) return null;
        var binding = await store.ReadCurrentUnderAdmissionAsync(tenantId, projectId, environmentId, cancellationToken);
        return binding is null ? null : owner with { PolicyBinding = binding };
    }
    public async Task<TargetAssociation?> ReadAssociationAsync(Guid tenantId, Guid targetId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new ProjectCatalogStore(db, legacyPolicy).ReadAssociationAsync(tenantId, targetId, cancellationToken);
    }
}

/// <summary>INV/PRJ facts through owner handlers only; caller already holds shared admission.</summary>
public sealed class CurrentTargetAuthorityReader(IDbContextFactory<InventoryDbContext> contexts, IProjectOwnershipReader projects) : ITargetAuthorityReader
{
    public async Task<TargetAuthority?> ReadTargetUnderAdmissionAsync(Guid target, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var row = await new TargetRegistryStore(db, projects).ReadSelectionUnderAdmissionAsync(target, ct);
        if (row is null) return null;
        var owner = await projects.ResolveOwnershipAsync(row.TenantId, row.ProjectId, row.EnvironmentId, ct);
        return owner is null ? null : new(row.TargetId, row.TenantId, row.ProjectId, row.Revision, owner.ProjectRevision,
            row.AssociationRevision, row.EnvironmentId, owner.EnvironmentRevision, owner.Classification);
    }
}
