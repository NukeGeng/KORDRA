using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.TenantManagement;

public sealed class TenantOrganizationAuthority(IDbContextFactory<TenantDbContext> contexts) : IPolicyTenantAuthority
{
    public async Task<bool> IsActiveTenantUnderAdmissionAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return tenantId != Guid.Empty && await db.Organizations.AsNoTracking().AnyAsync(x => x.Id == tenantId && x.Status == "active", ct);
    }
}

/// <summary>Owner-controlled lookup using a caller-enlisted connection under stopped custody.</summary>
public sealed class EnlistedTenantOrganizationAuthority(TenantDbContext db) : IPolicyTenantAuthority
{
    public Task<bool> IsActiveTenantUnderAdmissionAsync(Guid tenantId, CancellationToken ct = default) =>
        tenantId == Guid.Empty ? Task.FromResult(false) :
        db.Organizations.AsNoTracking().AnyAsync(x => x.Id == tenantId && x.Status == "active", ct);
}
