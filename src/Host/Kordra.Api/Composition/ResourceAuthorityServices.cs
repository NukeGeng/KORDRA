using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Contracts;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public static class ResourceAuthorityServices
{
    public static void AddLocalResourceAuthority(this IServiceCollection services, string runtimeConnectionString)
    {
        ProjectDbContextFactory.ValidateRuntimeTarget(runtimeConnectionString);
        InventoryDbContextFactory.ValidateRuntimeTarget(runtimeConnectionString);
        TenantDbContextFactory.ValidateRuntimeTarget(runtimeConnectionString);
        services.AddDbContextFactory<ProjectDbContext>(b => b.UseNpgsql(runtimeConnectionString, p => p.MigrationsHistoryTable(ProjectDbContext.HistoryTable, "app").CommandTimeout(2)));
        services.AddDbContextFactory<InventoryDbContext>(b => b.UseNpgsql(runtimeConnectionString, p => p.MigrationsHistoryTable(InventoryDbContext.HistoryTable, "app").CommandTimeout(2)));
        services.AddDbContextFactory<TenantDbContext>(b => b.UseNpgsql(runtimeConnectionString, p => p.MigrationsHistoryTable(TenantDbContext.HistoryTable, "app").CommandTimeout(2)));
        services.AddResourceAuthorityOwnerServices();
    }

    /// <summary>Owner composition only; callers must provide the already guarded owner factories and shared auth/gate.</summary>
    public static void AddResourceAuthorityOwnerServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorityClock>(s => s.GetRequiredService<LocalAuthenticationService>());
        services.AddSingleton<ICurrentSubjectReader>(s => s.GetRequiredService<LocalAuthenticationService>());
        services.AddSingleton<IAuthorityCommitter>(s => s.GetRequiredService<LocalAuthenticationService>());
        services.AddSingleton<IRoleDefinitionReader, RoleDefinitionStore>();
        services.AddSingleton<ITenantAuditTransactionFactory, TenantAuditTransactionAdapter>();
        services.AddSingleton<ITenantSubjectReader, CurrentHttpSubject>();
        services.AddSingleton<ITenantScopeOwner, CurrentTenantScopeOwner>();
        services.AddSingleton<TenantMembershipService>();
        services.AddSingleton<IScopedGrantReader>(s => s.GetRequiredService<TenantMembershipService>());
        services.AddSingleton<CurrentResourcePolicyReader>();
        services.AddSingleton<IResourcePolicyReader>(s => s.GetRequiredService<CurrentResourcePolicyReader>());
        services.AddSingleton<IEnvironmentPolicyReader>(s => s.GetRequiredService<CurrentResourcePolicyReader>());
        services.AddSingleton<IExactEnvironmentPolicyReader>(s => s.GetRequiredService<CurrentResourcePolicyReader>());
        services.AddSingleton<IProjectOwnershipReader, CurrentProjectOwnershipReader>();
        services.AddSingleton<ITargetAuthorityReader, CurrentTargetAuthorityReader>();
        services.AddSingleton<ProtectedReadAdmission>(); services.AddSingleton<TargetMetadataRead>();
        services.AddSingleton<ICatalogAuthority, CurrentCatalogAuthority>();
        services.AddSingleton<ICatalogAuditTransactionFactory, CatalogAuditTransactionFactory>();
        services.AddSingleton<ICatalogClockFloorWriter, CatalogClockFloorWriter>();
        services.AddSingleton<CatalogMutationCoordinator>();
    }
    private sealed class CurrentHttpSubject(IHttpContextAccessor accessor, LocalAuthenticationService authentication) : ITenantSubjectReader
    {
        public async Task<Guid?> CurrentPrincipalAsync(CancellationToken cancellationToken = default)
        {
            var selected = accessor.HttpContext is { } context ? LocalAuthenticationEndpoints.CurrentSessionSelection(context) : null;
            return selected is null ? null : (await authentication.ReadCurrentUnderAdmissionAsync(selected, cancellationToken))?.PrincipalId;
        }
    }
    private sealed class CurrentTenantScopeOwner(IDbContextFactory<ProjectDbContext> contexts) : ITenantScopeOwner
    {
        public async Task<bool> BelongsToTenantAsync(Guid tenantId, TenantScope scope, CancellationToken cancellationToken = default)
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            return await new PolicyBindingStore(db).ScopeBelongsToTenantUnderAdmissionAsync(tenantId, scope.Kind, scope.Id, cancellationToken);
        }
    }

    private sealed class CurrentCatalogAuthority(ITenantSubjectReader subjects, TenantMembershipService tenants) : ICatalogAuthority, IAuthenticatedCatalogSubjectReader
    {
        public Task<Guid?> CurrentPrincipalUnderAdmissionAsync(CancellationToken ct = default) => subjects.CurrentPrincipalAsync(ct);
        public async Task<Guid?> ResolvePrincipalAsync(Guid tenantId, Guid projectId, Guid? environmentId, string permission, CancellationToken cancellationToken = default)
        {
            var principal = await subjects.CurrentPrincipalAsync(cancellationToken);
            if (principal is null) return null;
            var scope = environmentId is not null ? new TenantScope("environment", environmentId.Value) :
                projectId == Guid.Empty ? new("organization", tenantId) : new("project", projectId);
            return await tenants.ReadEffectiveUnderAdmissionAsync(tenantId, principal.Value, permission, scope, cancellationToken) is null ? null : principal;
        }
    }
}
