using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.InfrastructureInventory;

/// <summary>Current local registry observation only; no network, credential or provider fallback.</summary>
public sealed class CurrentTargetMetadataReader(IDbContextFactory<InventoryDbContext> contexts,
    IProjectOwnershipReader projects) : ITargetMetadataReader
{
    public async Task<TargetMetadata?> ReadAsync(TargetMetadataReadSelection selection, CancellationToken cancellationToken = default)
    {
        if (selection is null || selection.InvocationId == Guid.Empty || selection.TenantId == Guid.Empty ||
            selection.TargetId == Guid.Empty || selection.ScopeId == Guid.Empty ||
            selection.ScopeKind is not ("project" or "environment")) return null;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new TargetRegistryStore(db, projects).ReadCurrentAsync(selection.TenantId, selection.TargetId, cancellationToken);
    }
}
