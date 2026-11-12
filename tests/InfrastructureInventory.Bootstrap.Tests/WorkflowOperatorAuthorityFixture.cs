using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Contracts;
using Kordra.Modules.TenantManagement.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>Fixture-owned persisted authority only; never registered in Product. No password login or startup.</summary>
internal sealed class WorkflowOperatorAuthorityFixture<TCase> where TCase : IWorkflowOperatorAuthorityCase
{
    internal static readonly Guid CaseId = TCase.CaseId;
    internal static string Schema => "t12_" + CaseId.ToString("N");
    internal SessionSelection Session { get; }
    internal WorkflowCommandAdmissionReader Commands { get; }
    private Guid SessionId { get; }
    private readonly FairAsyncReaderWriterAdmissionGate gate;

    private WorkflowOperatorAuthorityFixture(string locator, Guid actor, Guid sessionId, string reference, Guid boot,
        FairAsyncReaderWriterAdmissionGate gate)
    {
        this.gate = gate; SessionId = sessionId; Session = new(actor, reference, boot);
        var iam = new Factory<KordraIdentityDbContext>(() => Identity(new NpgsqlConnection(locator), true));
        var ten = new Factory<TenantDbContext>(() => Tenants(new NpgsqlConnection(locator), true));
        var config = new LocalAuthenticationConfiguration(CaseId, boot);
        var auth = new LocalAuthenticationService(iam, new NoAuthenticationWrites(), Hasher(), config, gate);
        var roles = new RoleDefinitionStore(iam, gate, new NoAuthenticationWrites(), config, auth);
        var owners = new Owners(locator);
        var grants = new TenantMembershipService(ten, roles, null!, owners, null!, gate, auth);
        Commands = new(new WorkflowEnvironmentAuthorityReader(auth, grants, roles, owners, owners, auth),
            new WorkflowActionPolicyReader(owners, owners, owners, auth), auth);
    }

    internal static async Task<WorkflowOperatorAuthorityFixture<TCase>> SeedAsync(NpgsqlConnection connection, string locator,
        WorkflowSpecification spec, FairAsyncReaderWriterAdmissionGate gate, CancellationToken ct, string action = "workflow.reconcile")
    {
        if (action is not ("workflow.cancel" or "workflow.reconcile")) throw new ArgumentException("Closed fixture action required.", nameof(action));
        if (spec.InstallationId != CaseId || spec.TenantId != CaseId) throw new InvalidOperationException("Fixture owner mismatch.");
        var actor = Guid.NewGuid(); var session = Guid.NewGuid(); var boot = Guid.NewGuid();
        var definition = Guid.NewGuid(); var member = Guid.NewGuid(); var raw = RandomNumberGenerator.GetBytes(32);
        var reference = Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var referenceHash = SHA256.HashData(raw); CryptographicOperations.ZeroMemory(raw);
        var now = DateTimeOffset.UtcNow;
        var policyTime = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        await using var root = await connection.BeginTransactionAsync(ct);
        await using var iam = Identity(connection); await using var ten = Tenants(connection); await using var prj = Projects(connection);
        await iam.Database.UseTransactionAsync(root, ct); await ten.Database.UseTransactionAsync(root, ct); await prj.Database.UseTransactionAsync(root, ct);
        var principal = Principal(actor, Hasher().HashPassword(new KordraIdentityUser(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        iam.Principals.Add(principal);
        iam.Sessions.Add(new() { SessionId = session, PrincipalId = actor, PrincipalRevision = 1, SessionReferenceHash = referenceHash,
            BootGeneration = boot, CreatedAtUtc = now, LastActivityAtUtc = now, AbsoluteExpiresAtUtc = now.AddHours(1) });
        iam.RoleDefinitions.Add(new() { DefinitionId = definition, Revision = 1, ManifestId = Guid.NewGuid(), Name = "Synthetic operator permission",
            ContentHash = new string('a', 64), RulesJson = JsonSerializer.Serialize(new[] { new PermissionRule(action, "environment") }) });
        ten.Organizations.Add(new() { Id = CaseId, Name = "Synthetic fixture" });
        ten.Memberships.Add(new() { Id = member, TenantId = CaseId, PrincipalId = actor });
        ten.Assignments.Add(new() { Id = Guid.NewGuid(), TenantId = CaseId, MembershipId = member,
            DefinitionId = definition, DefinitionRevision = 1, ScopeKind = "environment", ScopeId = spec.EnvironmentId,
            IsFounding = true, GrantorPrincipalId = actor });
        prj.Projects.Add(new() { Id = spec.ProjectId, TenantId = CaseId, Name = "Synthetic fixture" });
        prj.Environments.Add(new() { Id = spec.EnvironmentId, TenantId = CaseId, ProjectId = spec.ProjectId,
            Name = "Synthetic fixture", Classification = "Test" });
        await iam.SaveChangesAsync(ct); await ten.SaveChangesAsync(ct); await prj.SaveChangesAsync(ct);
        var policy = await new ResourcePolicyStore(iam).StagePublicationAsync(new Lease(), CaseId, spec.ProjectId, null, 0,
            new(2, action, "environment", "active", policyTime.AddMinutes(-1), null, [new("Test", "allow")]), policyTime, ct)
            ?? throw new InvalidOperationException("Fixture policy seed rejected.");
        await iam.SaveChangesAsync(ct);
        if (await new PolicyBindingStore(prj).StageAsync(CaseId, spec.ProjectId,
            new(null, "environment", spec.EnvironmentId, 0, action), new(policy.PolicyId, 1), Guid.NewGuid(), true, ct) is null)
            throw new InvalidOperationException("Fixture binding seed rejected.");
        await prj.SaveChangesAsync(ct); await root.CommitAsync(ct);
        return new(locator, actor, session, reference, boot, gate);
    }

    internal async Task RevokeSessionAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var held = await gate.AcquireWriteAsync(ct);
        await using var iam = Identity(connection);
        await iam.Sessions.Where(x => x.SessionId == SessionId).ExecuteUpdateAsync(x => x.SetProperty(y => y.IsRevoked, true)
            .SetProperty(y => y.RevokedAtUtc, DateTimeOffset.UtcNow).SetProperty(y => y.RevocationReason, "synthetic-fixture"), ct);
    }

    internal static KordraIdentityUser Principal(Guid actor, string hash) => new()
    {
        Id = actor, UserName = "fixture-" + actor.ToString("N"), NormalizedUserName = "FIXTURE-" + actor.ToString("N"),
        PasswordHash = hash, SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N")
    };

    private static PasswordHasher<KordraIdentityUser> Hasher() => new(Options.Create(new PasswordHasherOptions { IterationCount = 220_000 }));
    internal static KordraIdentityDbContext Identity(NpgsqlConnection connection, bool owned = false) =>
        new(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(connection, contextOwnsConnection: owned)
            .ReplaceService<IModelCustomizer, Customizer>().Options);
    internal static TenantDbContext Tenants(NpgsqlConnection connection, bool owned = false) =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connection, contextOwnsConnection: owned)
            .ReplaceService<IModelCustomizer, Customizer>().Options);
    internal static ProjectDbContext Projects(NpgsqlConnection connection, bool owned = false) =>
        new(new DbContextOptionsBuilder<ProjectDbContext>().UseNpgsql(connection, contextOwnsConnection: owned)
            .ReplaceService<IModelCustomizer, Customizer>().Options);
    private sealed class Customizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder model, DbContext context)
        {
            base.Customize(model, context); model.HasDefaultSchema(Schema);
            foreach (var entity in model.Model.GetEntityTypes()) entity.SetSchema(Schema);
        }
    }
    private sealed class Factory<T>(Func<T> create) : IDbContextFactory<T> where T : DbContext { public T CreateDbContext() => create(); }
    private sealed class Lease : IOfflineProvisioningLease { public void RequireHeld() { } }
    private sealed class NoAuthenticationWrites : IIdentityAuditTransactionFactory
    {
        public ValueTask<IIdentityAuditTransaction> EnlistAsync(System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction transaction, CancellationToken ct = default) => throw new InvalidOperationException("No fixture authentication mutation.");
    }
    private sealed class Owners(string locator) : IReleasePolicyEnvironmentReader, IActionPolicyBindingReader,
        ICurrentResourcePolicyReader, ITenantScopeOwner, IReleaseProjectAuthorityReader, IApprovalEnvironmentAuthorityReader
    {
        public async Task<ReleasePolicyEnvironment?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid environment, CancellationToken ct = default)
        { await using var db = Projects(new NpgsqlConnection(locator), true); return await new ReleasePolicyEnvironmentReader(db).ReadCurrentUnderAdmissionAsync(tenant, project, environment, ct); }
        public async Task<PolicyBinding?> ReadCurrentForActionUnderAdmissionAsync(Guid tenant, Guid project, Guid? environment, string action, CancellationToken ct = default)
        { await using var db = Projects(new NpgsqlConnection(locator), true); return await new PolicyBindingStore(db).ReadCurrentForActionUnderAdmissionAsync(tenant, project, environment, action, ct); }
        public async Task<ResourcePolicySnapshot?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid policy, long revision, CancellationToken ct = default)
        { await using var db = Identity(new NpgsqlConnection(locator), true); return await new ResourcePolicyStore(db).ReadCurrentUnderAdmissionAsync(tenant, project, policy, revision, ct); }
        public async Task<bool> BelongsToTenantAsync(Guid tenant, TenantScope scope, CancellationToken ct = default)
        { await using var db = Projects(new NpgsqlConnection(locator), true); return await new PolicyBindingStore(db).ScopeBelongsToTenantUnderAdmissionAsync(tenant, scope.Kind, scope.Id, ct); }
        public async Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid project, CancellationToken ct = default)
        {
            await using var db = Projects(new NpgsqlConnection(locator), true);
            var p = await new ProjectIdentityReader(db).ReadCurrentProjectUnderAdmissionAsync(project, ct);
            return p is null ? null : new(p.TenantId, p.ProjectId, p.Revision);
        }
        async Task<ApprovalEnvironmentAuthority?> IApprovalEnvironmentAuthorityReader.ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid environment, CancellationToken ct)
        { var p = await ReadCurrentUnderAdmissionAsync(tenant, project, environment, ct); return p is null ? null : new(tenant, project, environment, p.EnvironmentRevision); }
    }
}

internal interface IWorkflowOperatorAuthorityCase { static abstract Guid CaseId { get; } }
internal sealed class CommittedTimerAuthorityCase : IWorkflowOperatorAuthorityCase
{
    public static Guid CaseId => Guid.Parse("29feb3c5-e805-4827-b896-cdc7d8e0cc71");
}
internal sealed class RolledBackTimerAuthorityCase : IWorkflowOperatorAuthorityCase
{
    public static Guid CaseId => Guid.Parse("b81e2c0f-a23b-4277-96f0-512fe734edc8");
}

internal sealed class CancellationCommandFaultCase : IWorkflowOperatorAuthorityCase
{
    public static Guid CaseId => Guid.Parse("27cc9284-a9eb-4df2-9516-52e7bdd5e81c");
}
internal sealed class ReconciliationCommandFaultCase : IWorkflowOperatorAuthorityCase
{
    public static Guid CaseId => Guid.Parse("037a107b-3432-4d05-92e4-35b71a4ad2fa");
}
