using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ApprovalGovernance;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Contracts;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kordra.Api;

/// <summary>Opt-in local release composition; only supplied validated owner factories and shared admission are used.</summary>
public static class ReleaseRuntimeServices
{
    public static void AddLocalReleases(this IServiceCollection services, string releaseLocator, string approvalLocator, string artifactLocator, string auditLocator)
    {
        services.AddSingleton<IDbContextFactory<ReleaseDbContext>>(new PooledDbContextFactory<ReleaseDbContext>(ReleaseDbContextFactory.CreateRuntimeOptions(releaseLocator)));
        services.AddSingleton<IDbContextFactory<ApprovalDbContext>>(new PooledDbContextFactory<ApprovalDbContext>(ApprovalDbContextFactory.CreateRuntimeOptions(approvalLocator)));
        services.TryAddSingleton<IDbContextFactory<ArtifactDbContext>>(new PooledDbContextFactory<ArtifactDbContext>(ArtifactDbContextFactory.CreateRuntimeOptions(artifactLocator)));
        services.AddSingleton<IDbContextFactory<AuditDbContext>>(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateRuntimeOptions(auditLocator)));
        services.AddReleaseOwnerServices();
        services.AddApprovalOwnerServices();
    }
    public static void AddReleaseOwnerServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IProjectIdentityReader, CurrentProjectIdentityReader>();
        services.TryAddSingleton<IArtifactAuditTransactionFactory, ArtifactAuditTransactionFactory>();
        services.AddSingleton<IReleaseProjectAuthorityReader, CurrentReleaseProjectAuthorityReader>();
        services.AddSingleton<ReleaseProjectAuthorityReader>();
        services.AddSingleton<IReleaseCatalogReader, CatalogReader>(); services.AddSingleton<IReleaseTargetReader, TargetReader>();
        services.AddSingleton<IReleaseArtifactReader, ArtifactReader>(); services.AddSingleton<IReleaseApprovalPolicyReader, PolicyReader>();
        services.AddSingleton<IAuditMutationEvidenceReader, AuditMutationEvidenceReader>(); services.AddSingleton<ReleaseSelectionReader>();
        services.AddSingleton<IReleaseSchemaReadiness>(s => new ReleaseSchemaReadiness(Installation(s), s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>()));
        services.AddSingleton(s => new ReleaseUseAdmission(Installation(s), s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<ReleaseProjectAuthorityReader>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(), s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>()));
        services.AddSingleton(s => new ReleaseMutationTransaction(Installation(s), s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<ReleaseProjectAuthorityReader>(), s.GetRequiredService<ReleaseSelectionReader>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(),
            s.GetRequiredService<IAuditMutationEvidenceReader>(), s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>()));
        services.AddSingleton(s => new ReleaseMutationDisclosure(Installation(s), s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(), s.GetRequiredService<ReleaseProjectAuthorityReader>(), s.GetRequiredService<ReleaseSelectionReader>(),
            s.GetRequiredService<IArtifactAuditTransactionFactory>(), s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>()));
        services.AddSingleton<ReleaseMutationExecutionCoordinator>();
        services.AddSingleton(s => new ReleaseReadCoordinator(Installation(s), s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(), s.GetRequiredService<ReleaseProjectAuthorityReader>(), s.GetRequiredService<ReleaseSelectionReader>(),
            s.GetRequiredService<IArtifactAuditTransactionFactory>(), s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), s.GetRequiredService<IReleaseSchemaReadiness>()));
    }
    private static Guid Installation(IServiceProvider services) => services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;
    private sealed class CatalogReader(IDbContextFactory<ProjectDbContext> contexts) : IReleaseCatalogReader
    {
        public async Task<ReleaseCatalogSelection?> ReadUnderAdmissionAsync(Guid t, Guid p, Guid a, Guid e, Guid target, CancellationToken ct = default)
        { await using var db = await contexts.CreateDbContextAsync(ct); return await new ReleaseCatalogReader(db).ReadUnderAdmissionAsync(t, p, a, e, target, ct); }
    }
    private sealed class TargetReader(IDbContextFactory<InventoryDbContext> contexts) : IReleaseTargetReader
    {
        public async Task<ReleaseTargetIdentity?> ReadUnderAdmissionAsync(Guid t, Guid p, Guid e, Guid target, CancellationToken ct = default)
        { await using var db = await contexts.CreateDbContextAsync(ct); return await new ReleaseTargetReader(db).ReadUnderAdmissionAsync(t, p, e, target, ct); }
    }
    private sealed class ArtifactReader(IDbContextFactory<ArtifactDbContext> contexts) : IReleaseArtifactReader
    {
        public async Task<ArtifactV1?> ReadReleaseArtifactUnderAdmissionAsync(Guid t, Guid p, Guid a, CancellationToken ct = default)
        { await using var db = await contexts.CreateDbContextAsync(ct); return await new ArtifactQueryStore(db).ReadReleaseArtifactUnderAdmissionAsync(t, p, a, ct); }
    }
    private sealed class PolicyReader(IDbContextFactory<ApprovalDbContext> contexts) : IReleaseApprovalPolicyReader
    {
        public async Task<ReleaseApprovalPolicyBinding?> ReadCurrentUnderAdmissionAsync(Guid i, Guid t, Guid p, Guid e, CancellationToken ct = default)
        { await using var db = await contexts.CreateDbContextAsync(ct); return await new ApprovalPolicyStore(db).ReadCurrentUnderAdmissionAsync(i, t, p, e, ct); }
    }
}
