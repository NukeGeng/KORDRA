using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.ReleaseManagement;
using Kordra.Modules.ApprovalGovernance;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Api;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.TenantManagement.Infrastructure;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Binding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);

namespace Kordra.Testing;

/// <summary>Captured test composition only. Uses actual owner factories/adapters, never Product startup or global routing.</summary>
internal sealed class ProductPostgreSqlFixtureSelection
{
    private readonly string locator;
    internal DisposablePostgreSqlProfile? Profile { get; }
    internal string Database => Profile?.Database ?? "kordra_test";
    internal ProductPostgreSqlFixtureSelection(string locator) : this(locator, RegressionPostgreSqlSelection.LoadRunnerProfile()) { }
    internal ProductPostgreSqlFixtureSelection(string locator, DisposablePostgreSqlProfile? selected)
    {
        this.locator = locator;
        Profile = selected;
        if (Profile is { } profile) profile.ValidateLocator(locator, DateTimeOffset.UtcNow);
        else
        {
            var parsed = new Npgsql.NpgsqlConnectionStringBuilder(locator);
            if (parsed.Host != "127.0.0.1" || parsed.Port != 5432 || parsed.Database != "kordra_test" || parsed.Username != "kordra_test")
                throw new InvalidOperationException("Exact legacy fixture target required; values omitted.");
        }
    }
    private T Select<T>(Func<string, T> legacy, Func<string, Binding, T> selected) =>
        Profile is { } profile ? selected(locator, profile.OwnerBinding) : legacy(locator);
    internal DbContextOptions<KordraIdentityDbContext> Identity() => Select(
        s => new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(s,
            p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app").CommandTimeout(2)).Options,
        IdentityAccessDbContextFactory.CreateDisposableTestOptions);
    internal DbContextOptions<AuditDbContext> Audit() => Select(AuditDbContextFactory.CreateTestOptions, AuditDbContextFactory.CreateDisposableTestOptions);
    internal DbContextOptions<TenantDbContext> Tenant() => Select(TenantDbContextFactory.CreateTestOptions, TenantDbContextFactory.CreateDisposableTestOptions);
    internal DbContextOptions<ProjectDbContext> Project() => Select(ProjectDbContextFactory.CreateTestOptions, ProjectDbContextFactory.CreateDisposableTestOptions);
    internal DbContextOptions<InventoryDbContext> Inventory() => Select(InventoryDbContextFactory.CreateTestOptions, InventoryDbContextFactory.CreateDisposableTestOptions);
    internal DbContextOptions<CredentialDbContext> Credential() => Select(CredentialDbContextFactory.CreateTestOptions, CredentialDbContextFactory.CreateDisposableTestOptions);
    internal DbContextOptions<ArtifactDbContext> Artifact() => Select(ArtifactDbContextFactory.CreateTestOptions, ArtifactDbContextFactory.CreateDisposableTestOptions);
    internal DbContextOptions<ReleaseDbContext> Release() => Select(ReleaseDbContextFactory.CreateTestOptions, ReleaseDbContextFactory.CreateDisposableTestOptions);
    internal DbContextOptions<ApprovalDbContext> Approval() => Select(ApprovalDbContextFactory.CreateTestOptions, ApprovalDbContextFactory.CreateDisposableTestOptions);
    internal AuditAppendStore AuditStore(IDbContextFactory<AuditDbContext> contexts) => Profile is { } p
        ? AuditAppendStore.CreateDisposableTestStore(contexts, p.OwnerBinding)
        : new(contexts);
    internal IdentityAccessAuditTransactionAdapter IdentityAudit() => Profile is { } p
        ? IdentityAccessAuditTransactionAdapter.CreateDisposableTestAdapter(p.OwnerBinding) : new();
    internal TenantAuditTransactionAdapter TenantAudit(ICatalogClockFloorWriter? floor = null) => Profile is { } p
        ? TenantAuditTransactionAdapter.CreateDisposableTestAdapter(p.OwnerBinding, floor) : new(floor);
    internal CatalogAuditTransactionFactory CatalogAudit() => Profile is { } p
        ? CatalogAuditTransactionFactory.CreateDisposableTestFactory(p.OwnerBinding) : new();
    internal ArtifactAuditTransactionFactory ArtifactAudit() => Profile is { } p
        ? ArtifactAuditTransactionFactory.CreateDisposableTestFactory(p.OwnerBinding) : new();
    internal OfflinePolicyAuditTransactionFactory PolicyAudit() => Profile is { } p
        ? OfflinePolicyAuditTransactionFactory.CreateDisposableTestFactory(p.OwnerBinding) : new();
    internal OfflineResourcePolicyCoordinator PolicyCoordinator(Guid installation, IDbContextFactory<KordraIdentityDbContext> contexts,
        IPolicyTenantAuthority tenants, IAuditAppendPort audit, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock,
        IOfflinePolicyAuditTransactionFactory? auditTransactions = null) => Profile is { } p
        ? OfflineResourcePolicyCoordinator.CreateDisposableTestCoordinator(installation, contexts, tenants, audit, gate, clock, auditTransactions, p.OwnerBinding)
        : new(installation, contexts, tenants, audit, gate, clock, auditTransactions);
    internal ArtifactRegistrationStore ArtifactStore(ArtifactDbContext context) => Profile is { } p
        ? ArtifactRegistrationStore.CreateDisposableTestStore(context, p.OwnerBinding) : new(context);
    internal ArtifactWriteReadiness ArtifactReadiness(IDbContextFactory<ArtifactDbContext> artifacts,
        IDbContextFactory<CredentialDbContext> credentials) => Profile is { } p
        ? ArtifactWriteReadiness.CreateDisposableTestReadiness(artifacts, credentials, p.OwnerBinding) : new(artifacts, credentials);
    internal ArtifactOwnerTransaction ArtifactTransaction(Guid installation, IDbContextFactory<KordraIdentityDbContext> identity,
        IArtifactAuditTransactionFactory audit, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock) => Profile is { } p
        ? ArtifactOwnerTransaction.CreateDisposableTestTransaction(installation, identity, audit, gate, clock, p.OwnerBinding)
        : new(installation, identity, audit, gate, clock);
    internal OfflineCredentialCoordinator CredentialCoordinator(Guid installation, IDbContextFactory<KordraIdentityDbContext> identity,
        IPolicyTenantAuthority tenants, ITargetAuthorityReader targets, IAuditAppendPort audit, FairAsyncReaderWriterAdmissionGate gate,
        IAuthorityClock clock, IOfflinePolicyAuditTransactionFactory? transactions = null) => Profile is { } p
        ? OfflineCredentialCoordinator.CreateDisposableTestCoordinator(installation, identity, tenants, targets, audit, gate, clock, transactions, p.OwnerBinding)
        : new(installation, identity, tenants, targets, audit, gate, clock, transactions);
    internal OfflineRegistryCredentialCoordinator RegistryCredentialCoordinator(Guid installation, IDbContextFactory<KordraIdentityDbContext> identity,
        IPolicyTenantAuthority tenants, IProjectIdentityReader projects, IAuditAppendPort audit, FairAsyncReaderWriterAdmissionGate gate,
        IAuthorityClock clock, IOfflinePolicyAuditTransactionFactory? transactions = null) => Profile is { } p
        ? OfflineRegistryCredentialCoordinator.CreateDisposableTestCoordinator(installation, identity, tenants, projects, audit, gate, clock, transactions, p.OwnerBinding)
        : new(installation, identity, tenants, projects, audit, gate, clock, transactions);
    internal OfflineArtifactRecoveryCoordinator ArtifactRecovery(Guid installation, IDbContextFactory<KordraIdentityDbContext> identity,
        FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock, IArtifactProcessQuiescence processes,
        IArtifactAuditTransactionFactory? transactions = null) => Profile is { } p
        ? OfflineArtifactRecoveryCoordinator.CreateDisposableTestCoordinator(installation, identity, gate, clock, processes, transactions, p.OwnerBinding)
        : new(installation, identity, gate, clock, processes, transactions);
    internal global::Kordra.Api.ReleaseUseAdmission NewReleaseUseAdmission(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    ReleaseProjectAuthorityReader authority, IArtifactAuditTransactionFactory audits,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock) => Profile is { } p
        ? global::Kordra.Api.ReleaseUseAdmission.CreateDisposableTestComposition(installationId, identity, authority, audits, gate, clock, p.OwnerBinding)
        : new(installationId, identity, authority, audits, gate, clock);
    internal global::Kordra.Api.ReleaseMutationTransaction NewReleaseMutationTransaction(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    ReleaseProjectAuthorityReader authority, ReleaseSelectionReader selections, IArtifactAuditTransactionFactory audits,
    IAuditMutationEvidenceReader auditEvidence,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock) => Profile is { } p
        ? global::Kordra.Api.ReleaseMutationTransaction.CreateDisposableTestTransaction(installationId, identity, authority, selections, audits, auditEvidence, gate, clock, p.OwnerBinding)
        : new(installationId, identity, authority, selections, audits, auditEvidence, gate, clock);
    internal global::Kordra.Api.ReleaseMutationDisclosure NewReleaseMutationDisclosure(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ReleaseDbContext> releases, ReleaseProjectAuthorityReader authority, ReleaseSelectionReader selections,
    IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock) => Profile is { } p
        ? global::Kordra.Api.ReleaseMutationDisclosure.CreateDisposableTestComposition(installationId, identity, releases, authority, selections, audits, gate, clock, p.OwnerBinding)
        : new(installationId, identity, releases, authority, selections, audits, gate, clock);
    internal global::Kordra.Api.ReleaseReadCoordinator NewReleaseReadCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ReleaseDbContext> releases, ReleaseProjectAuthorityReader authority, ReleaseSelectionReader selections,
    IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock, IReleaseSchemaReadiness readiness) => Profile is { } p
        ? global::Kordra.Api.ReleaseReadCoordinator.CreateDisposableTestComposition(installationId, identity, releases, authority, selections, audits, gate, clock, readiness, p.OwnerBinding)
        : new(installationId, identity, releases, authority, selections, audits, gate, clock, readiness);
    internal global::Kordra.Api.ApprovalUseAdmission NewApprovalUseAdmission(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    ApprovalAuthorityCapture authority, IArtifactAuditTransactionFactory audits,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock) => Profile is { } p
        ? global::Kordra.Api.ApprovalUseAdmission.CreateDisposableTestComposition(installationId, identity, authority, audits, gate, clock, p.OwnerBinding)
        : new(installationId, identity, authority, audits, gate, clock);
    internal global::Kordra.Api.ApprovalMutationTransaction NewApprovalMutationTransaction(Guid installation, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ReleaseDbContext> releases, ApprovalAuthorityCapture authority, StoredApprovalAuthorityReader durableAuthority,
    ApprovalSubjectReader subjects, IArtifactAuditTransactionFactory audits, IAuditMutationEvidenceReader auditEvidence,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock) => Profile is { } p
        ? global::Kordra.Api.ApprovalMutationTransaction.CreateDisposableTestTransaction(installation, identity, releases, authority, durableAuthority, subjects, audits, auditEvidence, gate, clock, p.OwnerBinding)
        : new(installation, identity, releases, authority, durableAuthority, subjects, audits, auditEvidence, gate, clock);
    internal global::Kordra.Api.ApprovalMutationDisclosure NewApprovalMutationDisclosure(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ReleaseDbContext> releases, IDbContextFactory<Kordra.Modules.ApprovalGovernance.Infrastructure.ApprovalDbContext> approvals,
    ApprovalAuthorityCapture authority, ApprovalEligibilityReader eligibility,
    IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock) => Profile is { } p
        ? global::Kordra.Api.ApprovalMutationDisclosure.CreateDisposableTestComposition(installationId, identity, releases, approvals, authority, eligibility, audits, gate, clock, p.OwnerBinding)
        : new(installationId, identity, releases, approvals, authority, eligibility, audits, gate, clock);
    internal global::Kordra.Api.ApprovalReadCoordinator NewApprovalReadCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ReleaseDbContext> releases, IDbContextFactory<Kordra.Modules.ApprovalGovernance.Infrastructure.ApprovalDbContext> approvals,
    ApprovalAuthorityCapture authority, ApprovalEligibilityReader eligibility,
    IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock, IApprovalSchemaReadiness readiness, IApprovalEnvironmentIdentityReader? environments) => Profile is { } p
        ? global::Kordra.Api.ApprovalReadCoordinator.CreateDisposableTestComposition(installationId, identity, releases, approvals, authority, eligibility, audits, gate, clock, readiness, environments, p.OwnerBinding)
        : new(installationId, identity, releases, approvals, authority, eligibility, audits, gate, clock, readiness, environments);
    internal void BindRegisteredOwnerTransactions(IServiceCollection services)
    {
        if (Profile is not { } profile) return;
        if (services.LastOrDefault(d => d.ServiceType == typeof(IArtifactAuditTransactionFactory))?.ImplementationType == typeof(ArtifactAuditTransactionFactory))
            services.AddSingleton<IArtifactAuditTransactionFactory>(ArtifactAudit());
        if (services.Any(d => d.ServiceType == typeof(ArtifactOwnerTransaction)))
            services.AddSingleton(s => ArtifactOwnerTransaction.CreateDisposableTestTransaction(s.GetRequiredService<Kordra.Modules.IdentityAccess.Authentication.LocalAuthenticationConfiguration>().InstallationId, s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IArtifactAuditTransactionFactory>(), s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), profile.OwnerBinding));
        if (services.Any(d => d.ServiceType == typeof(ReleaseUseAdmission)))
            services.AddSingleton(s => ReleaseUseAdmission.CreateDisposableTestComposition(s.GetRequiredService<Kordra.Modules.IdentityAccess.Authentication.LocalAuthenticationConfiguration>().InstallationId, s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<ReleaseProjectAuthorityReader>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(), s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), profile.OwnerBinding));
        if (services.Any(d => d.ServiceType == typeof(ReleaseMutationTransaction)))
            services.AddSingleton(s => ReleaseMutationTransaction.CreateDisposableTestTransaction(s.GetRequiredService<Kordra.Modules.IdentityAccess.Authentication.LocalAuthenticationConfiguration>().InstallationId, s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<ReleaseProjectAuthorityReader>(), s.GetRequiredService<ReleaseSelectionReader>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(),
            s.GetRequiredService<IAuditMutationEvidenceReader>(), s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), profile.OwnerBinding));
        if (services.Any(d => d.ServiceType == typeof(ReleaseMutationDisclosure)))
            services.AddSingleton(s => ReleaseMutationDisclosure.CreateDisposableTestComposition(s.GetRequiredService<Kordra.Modules.IdentityAccess.Authentication.LocalAuthenticationConfiguration>().InstallationId, s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(), s.GetRequiredService<ReleaseProjectAuthorityReader>(), s.GetRequiredService<ReleaseSelectionReader>(),
            s.GetRequiredService<IArtifactAuditTransactionFactory>(), s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), profile.OwnerBinding));
        if (services.Any(d => d.ServiceType == typeof(ReleaseReadCoordinator)))
            services.AddSingleton(s => ReleaseReadCoordinator.CreateDisposableTestComposition(s.GetRequiredService<Kordra.Modules.IdentityAccess.Authentication.LocalAuthenticationConfiguration>().InstallationId, s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(), s.GetRequiredService<ReleaseProjectAuthorityReader>(), s.GetRequiredService<ReleaseSelectionReader>(),
            s.GetRequiredService<IArtifactAuditTransactionFactory>(), s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), s.GetRequiredService<IReleaseSchemaReadiness>(), profile.OwnerBinding));
        if (services.Any(d => d.ServiceType == typeof(ApprovalUseAdmission)))
            services.AddSingleton(s => ApprovalUseAdmission.CreateDisposableTestComposition(s.GetRequiredService<Kordra.Modules.IdentityAccess.Authentication.LocalAuthenticationConfiguration>().InstallationId, s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<ApprovalAuthorityCapture>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(),
            s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), profile.OwnerBinding));
        if (services.Any(d => d.ServiceType == typeof(ApprovalMutationTransaction)))
            services.AddSingleton(s => ApprovalMutationTransaction.CreateDisposableTestTransaction(s.GetRequiredService<Kordra.Modules.IdentityAccess.Authentication.LocalAuthenticationConfiguration>().InstallationId, s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(), s.GetRequiredService<ApprovalAuthorityCapture>(), s.GetRequiredService<StoredApprovalAuthorityReader>(),
            s.GetRequiredService<ApprovalSubjectReader>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(), s.GetRequiredService<IAuditMutationEvidenceReader>(),
            s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), profile.OwnerBinding));
        if (services.Any(d => d.ServiceType == typeof(ApprovalMutationDisclosure)))
            services.AddSingleton(s => ApprovalMutationDisclosure.CreateDisposableTestComposition(s.GetRequiredService<Kordra.Modules.IdentityAccess.Authentication.LocalAuthenticationConfiguration>().InstallationId, s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(), s.GetRequiredService<IDbContextFactory<ApprovalDbContext>>(),
            s.GetRequiredService<ApprovalAuthorityCapture>(), s.GetRequiredService<ApprovalEligibilityReader>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(),
            s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), profile.OwnerBinding));
        if (services.Any(d => d.ServiceType == typeof(ApprovalReadCoordinator)))
            services.AddSingleton(s => ApprovalReadCoordinator.CreateDisposableTestComposition(s.GetRequiredService<Kordra.Modules.IdentityAccess.Authentication.LocalAuthenticationConfiguration>().InstallationId, s.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(),
            s.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(), s.GetRequiredService<IDbContextFactory<ApprovalDbContext>>(),
            s.GetRequiredService<ApprovalAuthorityCapture>(), s.GetRequiredService<ApprovalEligibilityReader>(), s.GetRequiredService<IArtifactAuditTransactionFactory>(),
            s.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), s.GetRequiredService<IAuthorityClock>(), s.GetRequiredService<IApprovalSchemaReadiness>(),
            s.GetRequiredService<IApprovalEnvironmentIdentityReader>(), profile.OwnerBinding));
    }
    internal ResourcePolicyManifest? ParsePolicy(string json) => Profile is { } p
        ? ResourcePolicyManifest.ParseDisposableTest(json, p.OwnerBinding) : ResourcePolicyManifest.Parse(json);
    internal byte[] PolicyBytes(ResourcePolicyManifest manifest) => Profile is { } p
        ? manifest.CanonicalDisposableTestBytes(p.OwnerBinding) : manifest.CanonicalBytes();
    internal string PolicyHash(ResourcePolicyManifest manifest) => Profile is { } p
        ? manifest.HashDisposableTest(p.OwnerBinding) : manifest.Hash();
}
