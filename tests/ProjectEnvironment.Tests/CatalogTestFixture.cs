using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Contracts;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Kordra.Catalog.Tests;

public abstract class CatalogTestFixture : IAsyncLifetime
{
    protected PooledDbContextFactory<ProjectDbContext> Projects = null!;
    protected PooledDbContextFactory<InventoryDbContext> Inventory = null!;
    protected PooledDbContextFactory<TenantDbContext> Tenants = null!;
    protected PooledDbContextFactory<KordraIdentityDbContext> Identity = null!;
    protected RoleDefinitionStore Roles = null!;
    protected TenantMembershipService TenantService = null!;
    protected Guid Tenant, Principal, Member;
    protected FairAsyncReaderWriterAdmissionGate Gate = new();
    protected readonly Policies Policy = new();
    protected readonly Subject CurrentSubject = new();
    protected string Locator = "";
    private readonly OfflineLease _offline = new();
    private readonly HashSet<Guid> _definitions = [];
    private readonly HashSet<string> _keys = [];
    private readonly HashSet<Guid> _tenantIds = [];
    private readonly HashSet<Guid> _principalIds = [];

    public async Task InitializeAsync()
    {
        Locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Private isolated kordra_test locator required.");
        Projects = new(ProjectDbContextFactory.CreateTestOptions(Locator));
        Inventory = new(InventoryDbContextFactory.CreateTestOptions(Locator));
        Tenants = new(TenantDbContextFactory.CreateTestOptions(Locator));
        Identity = new(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(Locator,
            p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
        await using (var aud = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(Locator))) await aud.Database.MigrateAsync();
        await using (var iam = await Identity.CreateDbContextAsync()) await iam.Database.MigrateAsync();
        await using (var ten = await Tenants.CreateDbContextAsync()) await ten.Database.MigrateAsync();
        await using (var prj = await Projects.CreateDbContextAsync()) await prj.Database.MigrateAsync();
        await using (var inv = await Inventory.CreateDbContextAsync()) await inv.Database.MigrateAsync();
        Roles = new(Identity, Gate, new IdentityAccessAuditTransactionAdapter());
        TenantService = new(Tenants, Roles, CurrentSubject, new ScopeOwner(Projects, Policy), new TenantAuditTransactionAdapter(), Gate, new Clock());
        Principal = await NewPrincipal(); CurrentSubject.Principal = Principal;
        Tenant = await NewTenant(Principal);
        await using var db = await Tenants.CreateDbContextAsync();
        Member = (await db.Memberships.SingleAsync(x => x.TenantId == Tenant && x.PrincipalId == Principal)).Id;
    }
    protected CatalogMutationCoordinator Coordinator(ICatalogAuditTransactionFactory? audit = null) =>
        new(Projects, new Authority(TenantService, CurrentSubject), Policy, Gate, new Clock(), audit ?? new CatalogAuditTransactionFactory());
    protected async Task<CatalogMutationResult> Catalog(CatalogCommand command, ICatalogAuditTransactionFactory? audit = null)
    { _keys.Add(Hash(command.Key)); return await Coordinator(audit).ExecuteAsync(command); }
    protected async Task<TargetMutationResult> Target(TargetCommand command, ICatalogAuditTransactionFactory? audit = null)
    { _keys.Add(Hash(command.Key)); return await Coordinator(audit).ExecuteAsync(command); }
    protected static string Key() => Guid.NewGuid().ToString("N");
    protected async Task<CatalogFact> Project()
    {
        var created = await Catalog(new(Key(), "create", "project", Tenant, Name: "Synthetic project"));
        Assert.Equal("success", created.Status);
        await Grant(new("project", created.Resource!.Id), [new("project.manage", "project"), new("project.read", "project"), new("target.manage", "project"), new("target.read", "project")]);
        return created.Resource;
    }
    protected async Task<CatalogFact> CreateEnvironment(Guid project, string classification = "Production", long policy = 1)
    {
        Policy.Valid.Add((Tenant, project, classification, policy));
        var created = await Catalog(new(Key(), "create", "environment", Tenant, project, Name: "Synthetic environment", Classification: classification, PolicyRevision: policy));
        Assert.Equal("success", created.Status);
        await Grant(new("environment", created.Resource!.Id), [new("target.manage", "environment"), new("target.read", "environment")]);
        return created.Resource;
    }
    protected async Task Grant(TenantScope scope, IReadOnlyList<PermissionRule> rules)
    {
        var role = await Roles.PublishOfflineAsync(_offline, Guid.NewGuid(), Guid.Empty, 0, "Explicit synthetic grant", rules);
        Assert.Equal("success", role.Status); _definitions.Add(role.DefinitionId);
        await using var ten = await Tenants.CreateDbContextAsync();
        var member = await ten.Memberships.SingleAsync(x => x.TenantId == Tenant && x.Id == Member);
        var granted = await TenantService.GrantOfflineAsync(_offline, Guid.NewGuid(), Tenant, Member, member.Revision, role.DefinitionId, role.Revision, scope, null);
        Assert.Equal("success", granted.Status);
    }
    protected async Task<TargetMetadata?> ReadTarget(Guid tenant, Guid id)
    {
        await using var admission = await Gate.AcquireReadAsync();
        await using var prj = await Projects.CreateDbContextAsync(); await using var inv = await Inventory.CreateDbContextAsync();
        return await new TargetRegistryStore(inv, new ProjectCatalogStore(prj, Policy)).ReadCurrentAsync(tenant, id);
    }
    protected async Task<Guid> NewPrincipal()
    {
        var id = Guid.NewGuid(); var name = "t05-" + id.ToString("N"); _principalIds.Add(id);
        await using var db = await Identity.CreateDbContextAsync();
        db.Principals.Add(new() { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), PasswordHash = "synthetic-not-a-login-hash", SecurityStamp = Key() });
        await db.SaveChangesAsync(); return id;
    }
    protected async Task<Guid> NewTenant(Guid principal)
    {
        var role = await Roles.PublishOfflineAsync(_offline, Guid.NewGuid(), Guid.Empty, 0, "Explicit catalog founder", [new("project.manage", "organization"), new("org.manage", "organization"), new("membership.assign-role", "organization")]);
        Assert.Equal("success", role.Status); _definitions.Add(role.DefinitionId);
        var created = await TenantService.InitializeOfflineAsync(_offline, Guid.NewGuid(), principal, "Synthetic tenant", role.DefinitionId, 1);
        Assert.Equal("success", created.Status); _tenantIds.Add(created.TenantId); return created.TenantId;
    }
    public async Task DisposeAsync()
    {
        await using var inv = await Inventory.CreateDbContextAsync();
        await inv.Commands.Where(x => _keys.Contains(x.KeyHash)).ExecuteDeleteAsync(); await inv.Targets.Where(x => _tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync();
        await using var prj = await Projects.CreateDbContextAsync();
        await prj.Set<PolicyBindingHistoryRecord>().Where(x => _tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync();
        await prj.Set<PolicyBindingRecord>().Where(x => _tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync();
        await prj.Commands.Where(x => _keys.Contains(x.KeyHash)).ExecuteDeleteAsync(); await prj.Associations.Where(x => _tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync();
        await prj.Environments.Where(x => _tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync(); await prj.Applications.Where(x => _tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync();
        await prj.Projects.Where(x => _tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync();
        await using var ten = await Tenants.CreateDbContextAsync();
        await ten.Commands.Where(x => _tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync(); await ten.Assignments.Where(x => _tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync();
        await ten.Memberships.Where(x => _tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync(); await ten.Organizations.Where(x => _tenantIds.Contains(x.Id)).ExecuteDeleteAsync();
        await using var iam = await Identity.CreateDbContextAsync();
        var policyIds = await iam.Set<ResourcePolicyIdentity>().Where(x => _tenantIds.Contains(x.TenantId)).Select(x => x.PolicyId).ToArrayAsync();
        await iam.Set<ResourcePolicyHead>().Where(x => policyIds.Contains(x.PolicyId)).ExecuteDeleteAsync();
        await iam.Set<ResourcePolicyRule>().Where(x => policyIds.Contains(x.PolicyId)).ExecuteDeleteAsync();
        await iam.Set<ResourcePolicyRevision>().Where(x => policyIds.Contains(x.PolicyId)).ExecuteDeleteAsync();
        await iam.Set<ResourcePolicyIdentity>().Where(x => policyIds.Contains(x.PolicyId)).ExecuteDeleteAsync();
        await iam.RoleDefinitions.Where(x => _definitions.Contains(x.DefinitionId)).ExecuteDeleteAsync(); await iam.Principals.Where(x => _principalIds.Contains(x.Id)).ExecuteDeleteAsync();
        _offline.Dispose(); // immutable audit evidence is retained
    }
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    protected sealed class Subject : ITenantSubjectReader
    {
        public Guid? Principal;
        public Task<Guid?> CurrentPrincipalAsync(CancellationToken cancellationToken = default) => Task.FromResult(Principal);
    }
    protected sealed class Policies : IEnvironmentPolicyReader
    {
        public HashSet<(Guid, Guid, string, long)> Valid { get; } = [];
        public Task<bool> IsApplicableAsync(Guid tenantId, Guid projectId, string classification, long policyRevision, CancellationToken cancellationToken = default) => Task.FromResult(Valid.Contains((tenantId, projectId, classification, policyRevision)));
    }
    private sealed class ScopeOwner(PooledDbContextFactory<ProjectDbContext> contexts, Policies policies) : ITenantScopeOwner
    {
        public async Task<bool> BelongsToTenantAsync(Guid tenantId, TenantScope scope, CancellationToken cancellationToken = default)
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            if (scope.Kind == "project") return await new ProjectCatalogStore(db, policies).ResolveOwnershipAsync(tenantId, scope.Id, null, cancellationToken) is not null;
            var env = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == scope.Id, cancellationToken);
            return env is not null && await new ProjectCatalogStore(db, policies).ResolveOwnershipAsync(tenantId, env.ProjectId, scope.Id, cancellationToken) is not null;
        }
    }
    private sealed class Authority(TenantMembershipService tenants, Subject subjects) : ICatalogAuthority
    {
        public async Task<Guid?> ResolvePrincipalAsync(Guid tenantId, Guid projectId, Guid? environmentId, string permission, CancellationToken cancellationToken = default)
        {
            var subject = subjects.Principal; if (subject is null) return null;
            var scope = environmentId is not null ? new TenantScope("environment", environmentId.Value) : projectId == Guid.Empty ? new("organization", tenantId) : new("project", projectId);
            return await tenants.ReadEffectiveUnderAdmissionAsync(tenantId, subject.Value, permission, scope, cancellationToken) is null ? null : subject;
        }
    }
    private sealed class Clock : IAuthorityClock { public DateTimeOffset GetTrustedUtcNow() => DateTimeOffset.UtcNow; }
    protected sealed class OfflineLease : IOfflineProvisioningLease, IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "kordra-t05-" + Guid.NewGuid().ToString("N"));
        private readonly FileStream _file; private bool _disposed;
        public OfflineLease() => _file = new(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        public void RequireHeld() { if (_disposed) throw new InvalidOperationException("Test offline lease released."); }
        public void Dispose() { _disposed = true; _file.Dispose(); File.Delete(_path); }
    }
    protected sealed class FaultAudit(Action? beforeSave = null, bool unknownAfterCommit = false, bool failSetup = false, bool unknownWithoutCommit = false) : ICatalogAuditTransactionFactory
    {
        public async ValueTask<ICatalogAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default)
        {
            if (failSetup) throw new TimeoutException("Synthetic pre-commit setup timeout.");
            return new Enlisted(await new CatalogAuditTransactionFactory().EnlistAsync(connection, transaction, ct), beforeSave, unknownAfterCommit, unknownWithoutCommit);
        }
        private sealed class Enlisted(ICatalogAuditTransaction inner, Action? before, bool unknownAfter, bool unknownWithout) : ICatalogAuditTransaction
        {
            public async Task<AuditAppendOutcome> CommitAsync(AuditIntent intent, Func<CancellationToken, Task<bool>> saveOwners, CancellationToken ct = default)
            {
                if (unknownWithout) return AuditAppendOutcome.Unknown();
                var result = await inner.CommitAsync(intent, async token =>
                {
                    before?.Invoke();
                    return await saveOwners(token);
                }, ct);
                return unknownAfter && result.Status == AuditAppendStatus.Durable ? AuditAppendOutcome.Unknown() : result;
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
