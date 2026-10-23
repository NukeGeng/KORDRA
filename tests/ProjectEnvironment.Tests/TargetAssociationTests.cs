using Npgsql;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.Catalog.Tests;

public sealed class TargetAssociationTests : CatalogTestFixture
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Register_replay_and_read_require_matching_current_two_owner_binding(bool withEnvironment)
    {
        var project = await Project(); var env = withEnvironment ? await CreateEnvironment(project.Id) : null;
        var command = new TargetCommand(Key(), "register", Tenant, project.Id, env?.Id, Name: "cluster metadata", Kind: "kubernetes");
        var result = await Target(command); Assert.Equal("success", result.Status); Assert.Equal(result, await Target(command));
        Assert.Equal("conflict", (await Target(command with { Name = "changed" })).Status);
        Assert.Equal(result.Resource, await ReadTarget(Tenant, result.Resource!.TargetId));
        Assert.Null(await ReadTarget(Guid.NewGuid(), result.Resource.TargetId));
        await using var prj = await Projects.CreateDbContextAsync(); await using var inv = await Inventory.CreateDbContextAsync();
        Assert.Equal(1, await prj.Associations.CountAsync(x => x.TenantId == Tenant)); Assert.Equal(1, await inv.Targets.CountAsync(x => x.TenantId == Tenant));
        await using var audit = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(Locator));
        var entry = Assert.Single(await audit.Entries.Where(x => x.TenantRef == Tenant && x.ResourceRef == result.Resource.TargetId).ToArrayAsync());
        Assert.Equal(Principal, entry.SubjectRef); Assert.Equal("target.manage", entry.Permission);
        Assert.Equal(withEnvironment ? "environment" : "project", entry.ScopeKind);
    }

    [Fact]
    public async Task Missing_environment_cross_project_links_and_ungranted_subject_cannot_register()
    {
        var project = await Project(); var other = await Project(); var env = await CreateEnvironment(other.Id);
        Assert.Equal("denied", (await Target(new(Key(), "register", Tenant, project.Id, env.Id, Name: "cross project", Kind: "kubernetes"))).Status);
        Assert.Equal("denied", (await Target(new(Key(), "register", Tenant, project.Id, Guid.NewGuid(), Name: "unknown", Kind: "kubernetes"))).Status);
        var ungranted = await Catalog(new(Key(), "create", "project", Tenant, Name: "no target grant"));
        Assert.Equal("denied", (await Target(new(Key(), "register", Tenant, ungranted.Resource!.Id, Name: "not inherited", Kind: "kubernetes"))).Status);
        CurrentSubject.Principal = await NewPrincipal();
        Assert.Equal("denied", (await Target(new(Key(), "register", Tenant, project.Id, Name: "ungranted", Kind: "kubernetes"))).Status);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrong-tenant")]
    [InlineData("unresolved-absence")]
    [InlineData("ambiguous-absence")]
    public async Task Unknown_or_conflicting_owner_result_never_becomes_project_only_absence(string condition)
    {
        var project = await Project(); await using var db = await Inventory.CreateDbContextAsync();
        var snapshot = condition == "unknown" ? null : new ProjectOwnership(condition == "wrong-tenant" ? Guid.NewGuid() : Tenant,
            project.Id, 1, condition == "unresolved-absence" ? EnvironmentSelection.ResolvedPresent : EnvironmentSelection.ResolvedAbsent, null, null, null, condition == "ambiguous-absence" ? 1 : null);
        var store = new TargetRegistryStore(db, new StubOwner(snapshot));
        var command = new TargetCommand(Key(), "register", Tenant, project.Id, Name: "metadata", Kind: "kubernetes");
        Assert.Equal("denied", (await store.PrepareAsync(command, Key(), Key())).Status);
    }

    [Fact]
    public async Task Invalidating_project_environment_policy_or_binding_makes_target_facts_unavailable()
    {
        var project = await Project(); var env = await CreateEnvironment(project.Id);
        var target = (await Target(new(Key(), "register", Tenant, project.Id, env.Id, Name: "metadata", Kind: "kubernetes"))).Resource!;
        Assert.NotNull(await ReadTarget(Tenant, target.TargetId));
        Policy.Valid.Clear(); Assert.Null(await ReadTarget(Tenant, target.TargetId));
        Policy.Valid.Add((Tenant, project.Id, "Production", 1));
        Assert.Equal("success", (await Catalog(new(Key(), "suspend", "environment", Tenant, project.Id, env.Id, 1))).Status);
        Assert.Null(await ReadTarget(Tenant, target.TargetId));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("revision")]
    [InlineData("scope")]
    public async Task Conflicting_or_missing_PRJ_association_cannot_authorize_an_existing_INV_target(string fault)
    {
        var project = await Project();
        var target = (await Target(new(Key(), "register", Tenant, project.Id, Name: "metadata", Kind: "kubernetes"))).Resource!;
        await using (var admission = await Gate.AcquireWriteAsync())
        await using (var db = await Projects.CreateDbContextAsync())
        {
            var association = await db.Associations.SingleAsync(x => x.TargetId == target.TargetId);
            if (fault == "missing") db.Associations.Remove(association);
            else if (fault == "revision") association.Revision++;
            else
            {
                var other = new Kordra.Modules.ProjectEnvironment.Infrastructure.ProjectRecord { Id = Guid.NewGuid(), TenantId = Tenant, Name = "Synthetic conflicting owner" };
                db.Projects.Add(other); association.ProjectId = other.Id;
            }
            await db.SaveChangesAsync();
        }
        Assert.Null(await ReadTarget(Tenant, target.TargetId));
        Assert.Equal("denied", (await Target(new(Key(), "rename", Tenant, project.Id, TargetId: target.TargetId, ExpectedRevision: 1, Name: "unsafe"))).Status);
    }

    [Fact]
    public async Task Lifecycle_revision_and_exact_association_prevent_moves_stale_updates_and_revived_deleted_identity()
    {
        var project = await Project(); var other = await Project();
        var target = (await Target(new(Key(), "register", Tenant, project.Id, Name: "metadata", Kind: "kubernetes"))).Resource!;
        Assert.Equal("denied", (await Target(new(Key(), "rename", Tenant, other.Id, TargetId: target.TargetId, ExpectedRevision: 1, Name: "move"))).Status);
        Assert.Equal("conflict", (await Target(new(Key(), "disable", Tenant, project.Id, TargetId: target.TargetId, ExpectedRevision: 999))).Status);
        Assert.Equal("success", (await Target(new(Key(), "disable", Tenant, project.Id, TargetId: target.TargetId, ExpectedRevision: 1))).Status);
        Assert.Null(await ReadTarget(Tenant, target.TargetId));
        Assert.Equal("success", (await Target(new(Key(), "activate", Tenant, project.Id, TargetId: target.TargetId, ExpectedRevision: 2))).Status);
        Assert.Equal(3, (await ReadTarget(Tenant, target.TargetId))!.Revision);
        Assert.Equal("success", (await Target(new(Key(), "delete", Tenant, project.Id, TargetId: target.TargetId, ExpectedRevision: 3))).Status);
        Assert.Equal("denied", (await Target(new(Key(), "activate", Tenant, project.Id, TargetId: target.TargetId, ExpectedRevision: 4))).Status);
    }

    [Fact]
    public async Task Composite_association_foreign_key_rejects_environment_from_another_project()
    {
        var first = await Project(); var second = await Project(); var environment = await CreateEnvironment(second.Id);
        await using var db = await Projects.CreateDbContextAsync();
        db.Associations.Add(new() { TargetId = Guid.NewGuid(), TenantId = Tenant, ProjectId = first.Id, EnvironmentId = environment.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Failure_after_PRJ_save_rolls_back_both_owners_receipt_and_audit()
    {
        var project = await Project(); var command = new TargetCommand(Key(), "register", Tenant, project.Id, Name: "fail-between-owners-" + Key(), Kind: "kubernetes");
        var suffix = Key(); var function = "t05_fail_" + suffix; var trigger = "t05_trigger_" + suffix;
        await using var db = await Inventory.CreateDbContextAsync();
        // Only a dedicated synthetic-row trigger on the explicitly isolated test database.
        var nameLiteral = command.Name!.Replace("'", "''");
        await FixtureDdl($"CREATE FUNCTION app.{function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic second owner failure'; END $$");
        try
        {
            await FixtureDdl($"CREATE TRIGGER {trigger} BEFORE INSERT ON app.inv_targets FOR EACH ROW WHEN (NEW.name = '{nameLiteral}') EXECUTE FUNCTION app.{function}()");
            try
            {
                Assert.Equal("unavailable", (await Target(command)).Status);
                await using var prj = await Projects.CreateDbContextAsync();
                Assert.Empty(await prj.Associations.Where(x => x.TenantId == Tenant).ToArrayAsync());
                Assert.Equal(1, (await prj.Projects.SingleAsync(x => x.TenantId == Tenant && x.Id == project.Id)).Revision);
                Assert.Empty(await db.Targets.Where(x => x.TenantId == Tenant).ToArrayAsync());
                await using var audit = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(Locator));
                Assert.Equal(0, await audit.Entries.CountAsync(x => x.TenantRef == Tenant && x.Permission == "target.manage"));
            }
            finally { await FixtureDdl($"DROP TRIGGER {trigger} ON app.inv_targets"); }
        }
        finally { await FixtureDdl($"DROP FUNCTION app.{function}()"); }
        Assert.Equal("success", (await Target(command)).Status); // rollback left no command receipt
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unknown_commit_requires_fresh_gate_and_exact_receipt_reconciliation(bool actuallyCommitted)
    {
        var project = await Project(); var command = new TargetCommand(Key(), "register", Tenant, project.Id, Name: "metadata", Kind: "kubernetes");
        Assert.Equal("unavailable", (await Target(command, new FaultAudit(unknownAfterCommit: actuallyCommitted, unknownWithoutCommit: !actuallyCommitted))).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var admission = await Gate.AcquireReadAsync(); });
        Gate = new();
        var recovered = await Target(command); Assert.Equal("success", recovered.Status);
        Assert.Equal(recovered, await Target(command));
        await using var inv = await Inventory.CreateDbContextAsync(); await using var prj = await Projects.CreateDbContextAsync();
        Assert.Equal(1, await inv.Targets.CountAsync(x => x.TenantId == Tenant)); Assert.Equal(1, await prj.Associations.CountAsync(x => x.TenantId == Tenant));
    }

    [Fact]
    public async Task Setup_failure_and_subject_revocation_before_save_do_not_poison_gate_or_leave_a_binding()
    {
        var project = await Project(); var command = new TargetCommand(Key(), "register", Tenant, project.Id, Name: "metadata", Kind: "kubernetes");
        await Assert.ThrowsAsync<TimeoutException>(() => Target(command, new FaultAudit(failSetup: true)));
        Assert.Equal("unavailable", (await Target(command, new FaultAudit(beforeSave: () => CurrentSubject.Principal = null))).Status);
        CurrentSubject.Principal = Principal; Assert.Equal("success", (await Target(command)).Status);
    }
    private async Task FixtureDdl(string sql)
    {
        await using var connection = new NpgsqlConnection(Locator);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
    private sealed class StubOwner(ProjectOwnership? snapshot) : IProjectOwnershipReader
    {
        public Task<ProjectOwnership?> ResolveOwnershipAsync(Guid tenantId, Guid projectId, Guid? environmentId, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
        public Task<TargetAssociation?> ReadAssociationAsync(Guid tenantId, Guid targetId, CancellationToken cancellationToken = default) => Task.FromResult<TargetAssociation?>(null);
    }
}
