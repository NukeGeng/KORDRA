using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.ArtifactManagement.Infrastructure.Registry;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public static class ArtifactRuntimeServices
{
    /// <summary>Opt-in owner composition. No startup schema probe, migration, credential lookup or registry request.</summary>
    public static void AddLocalArtifacts(this IServiceCollection services, string locator, RegistryTrustConfiguration? configuration)
    {
        var options = ArtifactDbContextFactory.CreateRuntimeOptions(locator);
        services.AddSingleton<IDbContextFactory<ArtifactDbContext>>(
            new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<ArtifactDbContext>(options));
        services.AddArtifactRuntimeOwnerServices(configuration);
    }

    /// <summary>Caller supplies guarded owner factories and the same installation/session/admission services.</summary>
    public static void AddArtifactRuntimeOwnerServices(this IServiceCollection services, RegistryTrustConfiguration? configuration)
    {
        services.AddArtifactProjectAuthorityOwnerServices();
        services.AddSingleton<IArtifactAuditTransactionFactory, ArtifactAuditTransactionFactory>();
        services.AddSingleton<ArtifactWriteReadiness>();
        services.AddSingleton(s => new ArtifactAuthorityResolver(Installation(s), s.GetRequiredService<ArtifactProjectAuthorityReader>(),
            s.GetRequiredService<IDbContextFactory<CredentialDbContext>>(), s.GetRequiredService<IAuthorityClock>(), configuration));
        services.AddSingleton(s => new ArtifactOwnerTransaction(Installation(s), s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IArtifactAuditTransactionFactory>(), s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>()));
        services.AddSingleton(s => new ArtifactReadCoordinator(Installation(s), s.GetRequiredService<ArtifactAuthorityResolver>(),
            s.GetRequiredService<IDbContextFactory<ArtifactDbContext>>(), s.GetRequiredService<ArtifactOwnerTransaction>(),
            s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>()));
        services.AddSingleton(s => new ArtifactRegistrationCoordinator(Installation(s), s.GetRequiredService<ArtifactAuthorityResolver>(),
            s.GetRequiredService<IDbContextFactory<ArtifactDbContext>>(), s.GetRequiredService<ArtifactWriteReadiness>(),
            s.GetRequiredService<ArtifactOwnerTransaction>(), s.GetRequiredService<ArtifactReadCoordinator>(),
            s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), s.GetService<CredentialBroker>()));
    }
    private static Guid Installation(IServiceProvider services) => services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;
}
