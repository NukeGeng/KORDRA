using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Contracts;
using Kordra.Modules.TenantManagement.Infrastructure;
using Kordra.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>
/// 12.3c-2 (D4) on actual PostgreSQL (operation-gated; ADR-0025 disposable database + run-scoped roles): the TEN, catalog and
/// offline resource-policy writers, each called through its REAL entry point with the enforced fence, (a) wait while the
/// installation fence is held, (b) advance the epoch by exactly one with every committed change, after which a permit issued
/// before them is rejected (KD005), and (c) leave the epoch unchanged when nothing commits (exact replay, denial).
/// </summary>
public sealed class AuthorityFenceTenantCatalogWriterDatabaseTests
{
    /// <summary>Registry scenarios this class proves; the completeness test pins it to <see cref="AuthorityWriterRegistry"/>.</summary>
    internal static readonly string[] Scenarios = ["ten.initialize", "ten.member", "ten.grant", "ten.mutate", "catalog.mutate",
        "catalog.environment-v2", "policy.publish-bind"];
    private const string ManifestVariable = "KORDRA_FENCE_TENANT_MANIFEST";
    private const string LocatorVariable = "KORDRA_FENCE_TENANT_CONNECTION_STRING";

    [Fact]
    public void ExportFenceSqlWithoutDatabaseAccess()
    {
        var sql = AuthorityFenceIamWriterDatabaseTests.Script("0123456789ab");
        Assert.Contains("CREATE SCHEMA iam_workflow AUTHORIZATION kordra_iam_owner_r0123456789ab", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_FENCE_TENANT_EXPORT") is not { Length: > 0 } directory) return;
        var suffix = Environment.GetEnvironmentVariable("KORDRA_FENCE_TENANT_SUFFIX") ?? throw new InvalidOperationException("Run suffix required.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "iam-workflow-authority-v2.sql"), AuthorityFenceIamWriterDatabaseTests.Script(suffix));
    }

    [AuthorityFenceTenantDatabaseFact]
    public async Task TenantCatalogAndPolicyWritersHoldTheFenceAndAdvanceTheEpochOnEveryCommit()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(240));
        var ct = budget.Token;
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable(ManifestVariable)!));
        Assert.Equal("approved_for_execution", approval.RootElement.GetProperty("status").GetString());
        var suffix = approval.RootElement.GetProperty("suffix").GetString()!;
        var script = AuthorityFenceIamWriterDatabaseTests.Script(suffix);
        Assert.Equal(approval.RootElement.GetProperty("sha256").GetProperty("iam-workflow-authority-v2.sql").GetString(), OwnerFunctionPoc.Sha256(script));
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile() ?? throw new InvalidOperationException("Disposable runner profile required.");
        var locator = Environment.GetEnvironmentVariable(LocatorVariable) ?? throw new InvalidOperationException("Approved fixture locator required.");
        var selection = new ProductPostgreSqlFixtureSelection(locator, profile);
        var identity = new PooledDbContextFactory<KordraIdentityDbContext>(selection.Identity());
        var tenants = new PooledDbContextFactory<TenantDbContext>(selection.Tenant());
        var projects = new PooledDbContextFactory<ProjectDbContext>(selection.Project());
        var inventory = new PooledDbContextFactory<InventoryDbContext>(selection.Inventory());

        // Owner schemas through their own migrations, then the reviewed IAM v2 artifact (fence, services, permits).
        await using (var db = await identity.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using (var db = new AuditDbContext(selection.Audit())) await db.Database.MigrateAsync(ct);
        await using (var db = await tenants.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using (var db = await projects.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using (var db = await inventory.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using var race = await AuthorityFenceRaceHarness.StartAsync(locator, suffix, script, ct);

        // Real writers with the enforced fence; role definitions are setup only (inactive IAM store, not under test here).
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var clock = new UtcClock();
        var fence = new PostgresAuthorityFence(race.InstallationId);
        var lease = new HeldLease();
        var subject = new FixedSubject();
        var roles = new RoleDefinitionStore(identity, gate, selection.IdentityAudit());
        var ten = new TenantMembershipService(tenants, roles, subject, new ScopeOwner(projects), selection.TenantAudit(), gate, clock, fence);
        var policyReader = new CurrentResourcePolicyReader(identity, projects, clock);
        var floor = CatalogClockFloorWriter.CreateDisposableTestWriter(new LocalAuthenticationConfiguration(race.InstallationId, Guid.NewGuid()),
            clock, profile.OwnerBinding);
        var catalog = CatalogMutationCoordinator.CreateDisposableTestCoordinator(projects, new CatalogAuthority(subject, ten), policyReader,
            gate, clock, selection.CatalogAudit(), profile.OwnerBinding, floor, fence);
        var policies = OfflineResourcePolicyCoordinator.CreateDisposableTestCoordinator(race.InstallationId, identity, ten,
            selection.AuditStore(new PooledDbContextFactory<AuditDbContext>(selection.Audit())), gate, clock, null, profile.OwnerBinding, fence);
        Assert.Equal(AuthorityFenceMode.Enforced, ten.FenceMode);
        Assert.Equal(AuthorityFenceMode.Enforced, catalog.FenceMode);
        Assert.Equal(AuthorityFenceMode.Enforced, policies.FenceMode);

        var actor = await NewPrincipal("fence-ten-actor"); var member = await NewPrincipal("fence-ten-member");
        subject.Principal = actor;
        var founder = await Define("Fence founder", new("project.manage", "organization"), new("org.manage", "organization"),
            new("membership.assign-role", "organization"));
        var memberRole = await Define("Fence member", new PermissionRule("project.manage", "organization"));

        // TEN: initialize, member and founding grant (offline), then an online revocation and its exact replay.
        TenantCommandResult created = null!, added = null!, granted = null!;
        await race.RaceAsync("ten.initialize", true, async () =>
        {
            created = await ten.InitializeOfflineAsync(lease, Guid.NewGuid(), actor, "Fence tenant", founder.DefinitionId, founder.Revision, ct);
            return created.Status == "success";
        });
        var tenant = created.TenantId;
        await race.RaceAsync("ten.member", true, async () =>
        {
            added = await ten.AddMemberOfflineAsync(lease, Guid.NewGuid(), tenant, member, 1, ct);
            return added.Status == "success";
        });
        await race.RaceAsync("ten.grant", true, async () =>
        {
            granted = await ten.GrantOfflineAsync(lease, Guid.NewGuid(), tenant, added.ResourceId, await MembershipRevision(added.ResourceId),
                memberRole.DefinitionId, memberRole.Revision, new("organization", tenant), null, ct);
            return granted.Status == "success";
        });
        var revoke = new TenantCommand(Key("fence-revoke"), "revoke-assignment", tenant, added.ResourceId, granted.Revision, AssignmentId: granted.ResourceId);
        await race.RaceAsync("ten.mutate", true, async () => (await ten.ExecuteAsync(revoke, ct)).Status == "success");
        await race.RaceAsync("ten.mutate", false, async () => (await ten.ExecuteAsync(revoke, ct)).Status == "success");

        // Catalog: project creation and its exact replay; a subject without authority is denied only after the fence.
        var createProject = new CatalogCommand(Key("fence-project"), "create", "project", tenant, Name: "Fence project");
        CatalogMutationResult project = null!;
        await race.RaceAsync("catalog.mutate", true, async () =>
        {
            project = await catalog.ExecuteAsync(createProject, ct);
            return project.Status == "success";
        });
        await race.RaceAsync("catalog.mutate", false, async () => (await catalog.ExecuteAsync(createProject, ct)).Status == "success");
        subject.Principal = member;
        await race.RaceAsync("catalog.mutate", false, async () =>
            (await catalog.ExecuteAsync(new CatalogCommand(Key("fence-denied"), "create", "project", tenant, Name: "Denied project"), ct)).Status == "denied");
        subject.Principal = actor;
        var projectId = project.Resource!.Id;
        var projectRole = await Define("Fence project", new("project.manage", "project"), new("project.read", "project"),
            new("target.manage", "project"), new("target.read", "project"));
        var actorMembership = await MembershipOf(tenant, actor);
        Assert.Equal("success", (await ten.GrantOfflineAsync(lease, Guid.NewGuid(), tenant, actorMembership.Id, actorMembership.Revision,
            projectRole.DefinitionId, projectRole.Revision, new("project", projectId), null, ct)).Status);
        await race.RaceAsync("catalog.mutate", true, async () => (await catalog.ExecuteAsync(new TargetCommand(Key("fence-target"), "register",
            tenant, projectId, Name: "Fence target", Kind: "metadata"), ct)).Status == "success");

        // Offline resource policy publication (environment scope), its exact receipt replay (no fence, no commit), then the
        // environment v2 creation that binds it.
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var manifest = new ResourcePolicyManifest(1, Guid.NewGuid(), race.InstallationId, new(selection.Database, "kordra_test"), tenant, projectId,
            "create", null, 0, new(1, "target.read", "environment", "active", now.AddMinutes(-1), null,
                [new("Production", "allow"), new("Staging", "allow")]), Array.Empty<PolicyBindingChange>());
        var json = Encoding.UTF8.GetString(selection.PolicyBytes(manifest)); var hash = selection.PolicyHash(manifest);
        ResourcePolicyOperationResult published = null!;
        await race.RaceAsync("policy.publish-bind", true, async () =>
        {
            published = await policies.ExecuteAsync(lease, json, hash, ct);
            return published.Status == "success";
        });
        var beforeReplay = await race.EpochAsync();
        var replayed = await policies.ExecuteAsync(lease, json, hash, ct);
        Assert.Equal(("success", published.PolicyId, published.Revision), (replayed.Status, replayed.PolicyId, replayed.Revision));
        Assert.Equal(beforeReplay, await race.EpochAsync());
        await race.RaceAsync("catalog.environment-v2", true, async () => (await catalog.ExecuteEnvironmentV2Async(new EnvironmentCreateV2Command(
            Key("fence-environment"), tenant, projectId, "Fence staging", "Staging", new(published.PolicyId, published.Revision), Guid.NewGuid()), ct)).Status == "success");
        Assert.Equal(Scenarios.Order(StringComparer.Ordinal), race.Proven.Order(StringComparer.Ordinal));

        async Task<Guid> NewPrincipal(string prefix)
        {
            var id = Guid.NewGuid(); var name = prefix + "-" + id.ToString("N")[..12];
            await using var db = await identity.CreateDbContextAsync(ct);
            db.Principals.Add(new() { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(),
                PasswordHash = "synthetic-not-a-login-hash", SecurityStamp = Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync(ct);
            return id;
        }

        async Task<DefinitionWriteResult> Define(string name, params PermissionRule[] rules)
        {
            var defined = await roles.PublishOfflineAsync(lease, Guid.NewGuid(), Guid.Empty, 0, name, rules, false, ct);
            Assert.Equal("success", defined.Status);
            return defined;
        }

        async Task<long> MembershipRevision(Guid membership)
        {
            await using var db = await tenants.CreateDbContextAsync(ct);
            return (await db.Memberships.AsNoTracking().SingleAsync(x => x.Id == membership, ct)).Revision;
        }

        async Task<TenantMembership> MembershipOf(Guid tenantId, Guid principal)
        {
            await using var db = await tenants.CreateDbContextAsync(ct);
            return await db.Memberships.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.PrincipalId == principal, ct);
        }
    }

    private static string Key(string prefix) => prefix + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    private sealed class UtcClock : IAuthorityClock { public DateTimeOffset GetTrustedUtcNow() => DateTimeOffset.UtcNow; }

    private sealed class HeldLease : IOfflineProvisioningLease { public void RequireHeld() { } }

    private sealed class FixedSubject : ITenantSubjectReader
    {
        public Guid? Principal;
        public Task<Guid?> CurrentPrincipalAsync(CancellationToken cancellationToken = default) => Task.FromResult(Principal);
    }

    // Mirrors the Host's private CurrentTenantScopeOwner and CurrentCatalogAuthority adapters (trusted composition, not writers).
    private sealed class ScopeOwner(IDbContextFactory<ProjectDbContext> contexts) : ITenantScopeOwner
    {
        public async Task<bool> BelongsToTenantAsync(Guid tenantId, TenantScope scope, CancellationToken cancellationToken = default)
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            return await new PolicyBindingStore(db).ScopeBelongsToTenantUnderAdmissionAsync(tenantId, scope.Kind, scope.Id, cancellationToken);
        }
    }

    private sealed class CatalogAuthority(ITenantSubjectReader subjects, TenantMembershipService tenants) : ICatalogAuthority, IAuthenticatedCatalogSubjectReader
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

public sealed class AuthorityFenceTenantDatabaseFactAttribute : FactAttribute
{
    public AuthorityFenceTenantDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_FENCE_TENANT_MANIFEST")))
            Skip = "Requires the exact approved 12.3c-2 fence fixture manifest, disposable database and run roles.";
    }
}
