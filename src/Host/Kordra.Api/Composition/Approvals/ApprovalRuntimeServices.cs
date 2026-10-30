using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.Modules.TenantManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kordra.Api;

/// <summary>Uses the already configured local release/APR owner factories and shared admission. No provisioning.</summary>
public static class ApprovalRuntimeServices
{
    public static void AddApprovalOwnerServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IDurablePrincipalReader, DurablePrincipalReader>();
        services.TryAddSingleton<IDurableGrantEvidenceReader>(s => s.GetRequiredService<TenantMembershipService>());
        services.AddSingleton<ApprovalEnvironmentAuthorityReader>();
        services.AddSingleton<IApprovalEnvironmentAuthorityReader>(s => s.GetRequiredService<ApprovalEnvironmentAuthorityReader>());
        services.AddSingleton<IApprovalEnvironmentIdentityReader>(s => s.GetRequiredService<ApprovalEnvironmentAuthorityReader>());
        services.AddSingleton<ApprovalAuthorityCapture>(); services.AddSingleton<DurableApprovalAuthorityReader>();
        services.AddSingleton<StoredApprovalAuthorityReader>(); services.AddSingleton<ApprovalSubjectReader>(); services.AddSingleton<ApprovalEligibilityReader>();
        services.AddSingleton<IApprovalSchemaReadiness>(s => new ApprovalSchemaReadiness(Installation(s),
            s.GetRequiredService<IDbContextFactory<ApprovalDbContext>>(), s.GetRequiredService<IReleaseSchemaReadiness>()));
        services.AddSingleton(s => new ApprovalUseAdmission(Installation(s), s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<ApprovalAuthorityCapture>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(),
            s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>()));
        services.AddSingleton(s => new ApprovalMutationTransaction(Installation(s), s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(), s.GetRequiredService<ApprovalAuthorityCapture>(), s.GetRequiredService<StoredApprovalAuthorityReader>(),
            s.GetRequiredService<ApprovalSubjectReader>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(), s.GetRequiredService<IAuditMutationEvidenceReader>(),
            s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>()));
        services.AddSingleton(s => new ApprovalMutationDisclosure(Installation(s), s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(), s.GetRequiredService<IDbContextFactory<ApprovalDbContext>>(),
            s.GetRequiredService<ApprovalAuthorityCapture>(), s.GetRequiredService<ApprovalEligibilityReader>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(),
            s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>()));
        services.AddSingleton<ApprovalMutationExecutionCoordinator>();
        services.AddSingleton(s => new ApprovalReadCoordinator(Installation(s), s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(), s.GetRequiredService<IDbContextFactory<ApprovalDbContext>>(),
            s.GetRequiredService<ApprovalAuthorityCapture>(), s.GetRequiredService<ApprovalEligibilityReader>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(),
            s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), s.GetRequiredService<IApprovalSchemaReadiness>(),
            s.GetRequiredService<IApprovalEnvironmentIdentityReader>()));
    }
    private static Guid Installation(IServiceProvider services) => services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;
}
