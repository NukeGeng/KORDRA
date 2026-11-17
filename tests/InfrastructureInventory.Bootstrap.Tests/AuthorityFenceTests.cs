using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.TenantManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>12.3c-1/2 offline: fence port modes, enforced-fence guards and registry coverage (no database access).</summary>
public sealed class AuthorityFenceTests
{
    [Fact]
    public async Task InactiveFenceNeverTouchesTheDatabaseAndValidatesReasonCodes()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Timeout=1");
        Assert.Equal(AuthorityFenceMode.Inactive, AuthorityFence.Inactive.Mode);
        await Assert.ThrowsAsync<ArgumentNullException>(() => AuthorityFence.Inactive.AcquireAsync(connection, null!));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.True(AuthorityFence.IsReasonCode("iam.logout")); Assert.True(AuthorityFence.IsReasonCode("iam.logout-all"));
        Assert.False(AuthorityFence.IsReasonCode("IAM.logout")); Assert.False(AuthorityFence.IsReasonCode("1abc"));
        Assert.False(AuthorityFence.IsReasonCode("")); Assert.False(AuthorityFence.IsReasonCode(new string('a', 65)));
    }

    [Fact]
    public async Task EnforcedFenceRequiresAnInstallationAndThePostgreSqlRootWithoutOpeningAConnection()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Timeout=1");
        Assert.Equal(AuthorityFenceMode.Enforced, new PostgresAuthorityFence(Guid.NewGuid()).Mode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresAuthorityFence(Guid.Empty).AcquireAsync(connection, null!));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresAuthorityFence(Guid.NewGuid()).AcquireAsync(connection, null!));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void IamWritersDefaultToTheExplicitInactiveFence()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var configuration = new LocalAuthenticationConfiguration(Guid.NewGuid(), Guid.NewGuid());
        var auth = new LocalAuthenticationService(new NoContexts(), new NoAudit(), new PasswordHasher<KordraIdentityUser>(), configuration, gate);
        Assert.Equal(AuthorityFenceMode.Inactive, auth.FenceMode);
        Assert.Equal(AuthorityFenceMode.Inactive, new RoleDefinitionStore(new NoContexts(), gate, new NoAudit()).FenceMode);
        Assert.Equal(AuthorityFenceMode.Enforced, new LocalAuthenticationService(new NoContexts(), new NoAudit(),
            new PasswordHasher<KordraIdentityUser>(), configuration, gate, null, null, new PostgresAuthorityFence(configuration.InstallationId)).FenceMode);
    }

    [Fact]
    public void TenantCatalogAndPolicyWritersDefaultToTheExplicitInactiveFence()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();
        Assert.Equal(AuthorityFenceMode.Inactive, new TenantMembershipService(null!, null!, null!, null!, null!, gate, null!).FenceMode);
        Assert.Equal(AuthorityFenceMode.Inactive, new CatalogMutationCoordinator(null!, null!, null!, gate, null!, null!).FenceMode);
        Assert.Equal(AuthorityFenceMode.Inactive, new OfflineResourcePolicyCoordinator(Guid.NewGuid(), null!, null!, null!, gate, null!).FenceMode);
        var fence = new PostgresAuthorityFence(Guid.NewGuid());
        Assert.Equal(AuthorityFenceMode.Enforced, new TenantMembershipService(null!, null!, null!, null!, null!, gate, null!, fence).FenceMode);
        Assert.Equal(AuthorityFenceMode.Enforced, new CatalogMutationCoordinator(null!, null!, null!, gate, null!, null!, null, fence).FenceMode);
        Assert.Equal(AuthorityFenceMode.Enforced, new OfflineResourcePolicyCoordinator(Guid.NewGuid(), null!, null!, null!, gate, null!, null, fence).FenceMode);
    }

    [Fact]
    public void RegistryCoverageMatchesTheRaceScenariosAndBlocksActivationWhileIncomplete()
    {
        AuthorityWriterRegistry.Validate();
        var covered = AuthorityWriterRegistry.Entries.Where(x => x.FenceRaceScenario is not null).ToArray();
        // Every declared scenario is proven by a race test class: 12.3c-1 the nine IAM writers, 12.3c-2 TEN/catalog/policy.
        var proven = AuthorityFenceIamWriterDatabaseTests.Scenarios.Concat(AuthorityFenceTenantCatalogWriterDatabaseTests.Scenarios).ToArray();
        Assert.Equal(proven.Length, proven.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(proven.Order(StringComparer.Ordinal), covered.Select(x => x.FenceRaceScenario!).Distinct().Order(StringComparer.Ordinal));
        Assert.All(covered, x => Assert.Equal(x.Id, x.FenceRaceScenario));
        // Offline SEC/approval-policy and REL/APR writers are 12.3c-3: activation must still refuse, naming only those.
        var error = Assert.Throws<InvalidOperationException>(AuthorityWriterRegistry.RequireEnforcedCoverage);
        Assert.Contains("sec.binding", error.Message);
        foreach (var prefix in new[] { "iam.", "ten.", "catalog.", "policy.publish-bind" }) Assert.DoesNotContain(prefix, error.Message);
    }

    private sealed class NoContexts : IDbContextFactory<KordraIdentityDbContext>
    {
        public KordraIdentityDbContext CreateDbContext() => throw new InvalidOperationException("No database in offline tests.");
    }

    private sealed class NoAudit : IIdentityAuditTransactionFactory
    {
        public ValueTask<IIdentityAuditTransaction> EnlistAsync(System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No audit in offline tests.");
    }
}
