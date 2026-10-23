using System.Text;
using Kordra.Api;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.Catalog.Tests;

public sealed class ResourcePolicyIntegrationTests : CatalogTestFixture
{
    private readonly Guid _installation = Guid.NewGuid();
    private readonly PolicyClock _clock = new();
    private OfflineResourcePolicyCoordinator PolicyCoordinator() => new(_installation, Identity, TenantService,
        new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(Locator))), Gate, _clock);
    private ResourcePolicyPayload Payload(string scope = "project_without_environment") => new(1, "target.read", scope, "active", _clock.Now.AddMinutes(-1), null,
        scope == "environment" ? [new("Production", "allow"), new("Staging", "allow")] : [new("NoEnvironment", "allow")]);
    private ResourcePolicyManifest Manifest(Guid project, ResourcePolicyPayload? payload, params PolicyBindingChange[] bindings) =>
        new(1, Guid.NewGuid(), _installation, new("kordra_test", "kordra_test"), Tenant, project, "create", null, 0, payload, Array.AsReadOnly(bindings));
    private async Task<ResourcePolicyOperationResult> Apply(ResourcePolicyManifest m)
    { using var lease = new OfflineLease(); return await PolicyCoordinator().ExecuteAsync(lease, Encoding.UTF8.GetString(m.CanonicalBytes()), m.Hash()); }

    [Fact]
    public async Task Exact_current_policy_and_binding_replace_fixture_authority_without_an_implicit_grant()
    {
        var project = await Project();
        var target = await Target(new(Key(), "register", Tenant, project.Id, Name: "Synthetic", Kind: "metadata"));
        Assert.Equal("success", target.Status);
        var m = Manifest(project.Id, Payload(), new PolicyBindingChange(null, "project_without_environment", null, 0));
        using var lease = new OfflineLease();
        var preview = await PolicyCoordinator().InspectAsync(lease, Encoding.UTF8.GetString(m.CanonicalBytes()));
        Assert.NotNull(preview); Assert.Equal(0, preview.ExistingBindings);
        var first = await Apply(m); Assert.Equal("success", first.Status);
        var policyReader = new CurrentResourcePolicyReader(Identity, Projects, _clock);
        var owners = new CurrentProjectOwnershipReader(Projects, policyReader);
        var facts = await new CurrentTargetAuthorityReader(Inventory, owners).ReadTargetUnderAdmissionAsync(target.Resource!.TargetId);
        Assert.NotNull(facts);
        var evaluated = await policyReader.ReadPolicyUnderAdmissionAsync(facts, "target.read");
        Assert.Equal(ResourceEligibility.Allow, evaluated!.Decision);
        Assert.Equal(first.PolicyId, evaluated.PolicyId); Assert.Equal(1, evaluated.BindingRevision);
        Assert.False(await policyReader.IsApplicableAsync(Tenant, project.Id, "Production", 1));
        Assert.Null(await policyReader.ReadPolicyUnderAdmissionAsync(facts with { TenantId = Guid.NewGuid() }, "target.read"));
        await using var db = await Identity.CreateDbContextAsync();
        var store = new ResourcePolicyStore(db);
        Assert.Null(await store.ReadCurrentUnderAdmissionAsync(Guid.NewGuid(), project.Id, first.PolicyId, 1));
        Assert.Null(await store.ReadCurrentUnderAdmissionAsync(Tenant, project.Id, first.PolicyId, 2));
    }

    [Fact]
    public async Task Shared_head_updates_only_listed_bindings_and_exact_old_receipt_replays_after_expiry()
    {
        var project = await Project(); var prod = await CreateEnvironment(project.Id); var staging = await CreateEnvironment(project.Id, "Staging");
        var initial = Manifest(project.Id, Payload("environment") with { ExpiresAtUtc = _clock.Now.AddMinutes(1) },
            new(null, "environment", prod.Id, 0), new(null, "environment", staging.Id, 0));
        var first = await Apply(initial); Assert.Equal("success", first.Status);
        var selected = first.Bindings!.Single(x => x.EnvironmentId == prod.Id);
        var update = initial with { ManifestId = Guid.NewGuid(), Operation = "publish", PolicyId = first.PolicyId, ExpectedCurrentRevision = 1,
            Payload = Payload("environment"), Bindings = [new(selected.BindingId, "environment", prod.Id, 1)] };
        var second = await Apply(update); Assert.Equal("success", second.Status); Assert.Equal(2, second.Revision);
        await using var db = await Projects.CreateDbContextAsync();
        var store = new PolicyBindingStore(db);
        var prodBinding = await store.ReadCurrentUnderAdmissionAsync(Tenant, project.Id, prod.Id);
        var stagingBinding = await store.ReadCurrentUnderAdmissionAsync(Tenant, project.Id, staging.Id);
        Assert.Equal(2, prodBinding!.PolicyRevision); Assert.Equal(2, prodBinding.BindingRevision); Assert.Equal(1, stagingBinding!.PolicyRevision);
        await using var iam = await Identity.CreateDbContextAsync();
        Assert.Null(await new ResourcePolicyStore(iam).ReadCurrentUnderAdmissionAsync(Tenant, project.Id, first.PolicyId, stagingBinding.PolicyRevision));
        _clock.Now = _clock.Now.AddHours(1);
        var replay = await Apply(initial); Assert.Equal("success", replay.Status); Assert.Equal(first.PolicyId, replay.PolicyId); Assert.Equal(1, replay.Revision);
        Assert.Equal("conflict", (await Apply(initial with { Payload = initial.Payload! with { Status = "inactive" } })).Status);
        var stale = update with { ManifestId = Guid.NewGuid() };
        Assert.Equal("conflict", (await Apply(stale)).Status);
    }

    [Fact]
    public async Task Failure_after_IAM_save_before_PRJ_save_rolls_back_every_owner_and_does_not_poison_admission()
    {
        var project = await Project(); var m = Manifest(project.Id, Payload(), new PolicyBindingChange(null, "project_without_environment", null, 0));
        var name = "t06_binding_fault_" + Guid.NewGuid().ToString("N");
        await using var db = await Projects.CreateDbContextAsync();
        var sql = $"CREATE FUNCTION app.{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.project_id = '{project.Id:D}'::uuid THEN RAISE EXCEPTION 'synthetic T06 owner failure'; END IF; RETURN NEW; END; $$; CREATE TRIGGER {name} BEFORE INSERT ON app.prj_policy_bindings FOR EACH ROW EXECUTE FUNCTION app.{name}();";
        await db.Database.ExecuteSqlRawAsync(sql);
        try
        {
            Assert.Equal("unavailable", (await Apply(m)).Status);
            await using var iam = await Identity.CreateDbContextAsync();
            Assert.False(await iam.Set<ResourcePolicyIdentity>().AnyAsync(x => x.TenantId == Tenant));
            Assert.False(await iam.Set<ResourcePolicyCommandReceipt>().AnyAsync(x => x.ManifestId == m.ManifestId));
            Assert.False(await db.Set<PolicyBindingRecord>().AnyAsync(x => x.ProjectId == project.Id));
            await using var stillOpen = await Gate.AcquireReadAsync();
        }
        finally { var cleanupSql = $"DROP TRIGGER {name} ON app.prj_policy_bindings; DROP FUNCTION app.{name}();"; await db.Database.ExecuteSqlRawAsync(cleanupSql); }
        Assert.Equal("success", (await Apply(m)).Status);
    }

    [Fact]
    public async Task Publication_requires_exact_hash_installation_live_custody_and_explicit_scope()
    {
        var project = await Project(); var m = Manifest(project.Id, Payload(), new PolicyBindingChange(null, "project_without_environment", null, 0));
        using var lease = new OfflineLease(); var coordinator = PolicyCoordinator(); var json = Encoding.UTF8.GetString(m.CanonicalBytes());
        Assert.Equal("denied", (await coordinator.ExecuteAsync(lease, json, new string('0', 64))).Status);
        var foreign = m with { InstallationId = Guid.NewGuid() };
        Assert.Equal("invalid", (await coordinator.ExecuteAsync(lease, Encoding.UTF8.GetString(foreign.CanonicalBytes()), foreign.Hash())).Status);
        lease.Dispose(); await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAsync(lease, json, m.Hash()));
        Assert.Equal("denied", (await Apply(m with { Bindings = [new(null, "environment", Guid.NewGuid(), 0)] })).Status);
        await using var iam = await Identity.CreateDbContextAsync(); Assert.False(await iam.Set<ResourcePolicyIdentity>().AnyAsync(x => x.TenantId == Tenant));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Setup_failure_is_reversible_but_lost_commit_ACK_fences_and_reconciles_without_republication(bool afterCommit)
    {
        var project = await Project(); var m = Manifest(project.Id, Payload(), new PolicyBindingChange(null, "project_without_environment", null, 0));
        var lookup = new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(Locator)));
        var coordinator = new OfflineResourcePolicyCoordinator(_installation, Identity, TenantService, lookup, Gate, _clock, new FaultPolicyAudit(afterCommit));
        using var lease = new OfflineLease(); var json = Encoding.UTF8.GetString(m.CanonicalBytes());
        Assert.Equal(afterCommit ? "unknown" : "unavailable", (await coordinator.ExecuteAsync(lease, json, m.Hash())).Status);
        if (afterCommit)
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var denied = await Gate.AcquireReadAsync(); });
        else { await using var available = await Gate.AcquireReadAsync(); }
        var fresh = new OfflineResourcePolicyCoordinator(_installation, Identity, TenantService, lookup, new FairAsyncReaderWriterAdmissionGate(), _clock);
        var resolved = await fresh.ExecuteAsync(lease, json, m.Hash()); Assert.Equal("success", resolved.Status);
        await using var db = await Identity.CreateDbContextAsync();
        Assert.Equal(1, await db.Set<ResourcePolicyIdentity>().CountAsync(x => x.TenantId == Tenant));
        Assert.Equal(1, await db.Set<ResourcePolicyCommandReceipt>().CountAsync(x => x.ManifestId == m.ManifestId));
        await using var audit = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(Locator));
        Assert.Equal(1, await audit.Entries.CountAsync(x => x.IntentId == m.ManifestId));
    }

    [Fact]
    public async Task PostgreSQL_enforces_null_scope_uniqueness_and_exact_history_environment_binding()
    {
        var project = await Project(); var noEnvironment = await Apply(Manifest(project.Id, Payload(), new PolicyBindingChange(null, "project_without_environment", null, 0)));
        Assert.Equal("success", noEnvironment.Status);
        await using (var db = await Projects.CreateDbContextAsync())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Add(new PolicyBindingRecord { BindingId = Guid.NewGuid(), TenantId = Tenant, ProjectId = project.Id, ScopeId = project.Id,
                ScopeKind = "project_without_environment", PolicyId = noEnvironment.PolicyId, PolicyRevision = 1, BindingRevision = 1 });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("23505", Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState);
        }
        var first = await CreateEnvironment(project.Id); var second = await CreateEnvironment(project.Id, "Staging");
        var published = await Apply(Manifest(project.Id, Payload("environment"), new PolicyBindingChange(null, "environment", first.Id, 0)));
        Assert.Equal("success", published.Status); var binding = Assert.Single(published.Bindings!);
        await using (var db = await Projects.CreateDbContextAsync())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Add(new PolicyBindingHistoryRecord { BindingId = binding.BindingId, BindingRevision = 2, TenantId = Tenant, ProjectId = project.Id,
                EnvironmentId = second.Id, ScopeId = second.Id, ScopeKind = "environment", PolicyId = published.PolicyId, PolicyRevision = 1,
                ManifestId = Guid.NewGuid(), ContentHash = new byte[32] });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("23503", Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState);
        }
    }

    [Fact]
    public async Task Isolated_database_catalog_confirms_runtime_cannot_modify_policy_heads_or_rebind_history()
    {
        await using var db = await Projects.CreateDbContextAsync(); await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT has_table_privilege('kordra_app', @table, @privilege)";
        var table = command.CreateParameter(); table.ParameterName = "table"; command.Parameters.Add(table);
        var privilege = command.CreateParameter(); privilege.ParameterName = "privilege"; command.Parameters.Add(privilege);
        foreach (var name in new[] { "iam_resource_policies", "iam_resource_policy_revisions", "iam_resource_policy_rules", "iam_resource_policy_heads", "iam_resource_policy_commands", "prj_policy_bindings", "prj_policy_binding_history" })
        foreach (var action in new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
        {
            table.Value = "app." + name; privilege.Value = action;
            Assert.Equal(action == "SELECT" || name.StartsWith("prj_", StringComparison.Ordinal) && action == "INSERT", (bool)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task A_higher_stored_revision_without_head_publication_never_becomes_current()
    {
        var project = await Project(); var result = await Apply(Manifest(project.Id, Payload())); Assert.Equal("success", result.Status);
        await using var db = await Identity.CreateDbContextAsync(); await using var transaction = await db.Database.BeginTransactionAsync();
        var original = await db.Set<ResourcePolicyRevision>().AsNoTracking().SingleAsync(x => x.PolicyId == result.PolicyId && x.Revision == 1);
        db.Add(new ResourcePolicyRevision { PolicyId = result.PolicyId, Revision = 99, SchemaVersion = original.SchemaVersion, Action = original.Action,
            ScopeKind = original.ScopeKind, Status = original.Status, EffectiveFromUtc = original.EffectiveFromUtc, ExpiresAtUtc = original.ExpiresAtUtc,
            ContentHash = original.ContentHash, PublishedAtUtc = original.PublishedAtUtc });
        db.Add(new ResourcePolicyRule { PolicyId = result.PolicyId, Revision = 99, Case = "NoEnvironment", Effect = "allow" });
        await db.SaveChangesAsync();
        var store = new ResourcePolicyStore(db);
        Assert.NotNull(await store.ReadCurrentUnderAdmissionAsync(Tenant, project.Id, result.PolicyId, 1));
        Assert.Null(await store.ReadCurrentUnderAdmissionAsync(Tenant, project.Id, result.PolicyId, 99));
    }

    private sealed class FaultPolicyAudit(bool afterCommit) : IOfflinePolicyAuditTransactionFactory
    {
        public async ValueTask<IOfflinePolicyAuditTransaction> EnlistAsync(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, IOfflineAuditLease lease, CancellationToken ct = default)
        {
            if (!afterCommit) throw new IOException("Synthetic setup-only failure.");
            return new LostAcknowledgement(await new OfflinePolicyAuditTransactionFactory().EnlistAsync(connection, transaction, lease, ct));
        }
        private sealed class LostAcknowledgement(IOfflinePolicyAuditTransaction real) : IOfflinePolicyAuditTransaction
        {
            public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct = default) => real.StageAsync(intent, ct);
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct = default)
            { var outcome = await real.CommitAsync(ct); return outcome.Status == AuditAppendStatus.Durable ? AuditAppendOutcome.Unknown() : outcome; }
            public ValueTask DisposeAsync() => real.DisposeAsync();
        }
    }

    private sealed class PolicyClock : IAuthorityClock
    {
        public DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public DateTimeOffset GetTrustedUtcNow() => Now;
    }
}
