using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.Catalog.Tests;

public sealed class ProjectCatalogTests : CatalogTestFixture
{
    [Fact]
    public async Task Project_application_environment_commands_replay_exact_result_and_conflict_on_changed_content()
    {
        var projectCommand = new CatalogCommand(Key(), "create", "project", Tenant, Name: "Original project");
        var first = await Catalog(projectCommand); Assert.Equal("success", first.Status);
        Assert.Equal(first, await Catalog(projectCommand));
        Assert.Equal("conflict", (await Catalog(projectCommand with { Name = "Changed" })).Status);
        // Creation itself did not grant project scope or target rights.
        Assert.Equal("denied", (await Catalog(new(Key(), "create", "application", Tenant, first.Resource!.Id, Name: "app"))).Status);
        var project = await Project();
        var application = new CatalogCommand(Key(), "create", "application", Tenant, project.Id, Name: "Application");
        var app = await Catalog(application); Assert.Equal("success", app.Status); Assert.Equal(app, await Catalog(application));
        var environment = await CreateEnvironment(project.Id);
        Assert.Equal("Production", environment.Classification); Assert.Equal(1, environment.PolicyRevision);
        await using var db = await Projects.CreateDbContextAsync();
        Assert.Equal(2, await db.Projects.CountAsync(x => x.TenantId == Tenant));
        Assert.Equal(1, await db.Applications.CountAsync(x => x.TenantId == Tenant));
        Assert.Equal(1, await db.Environments.CountAsync(x => x.TenantId == Tenant));
    }

    [Theory]
    [InlineData("production", 1)]
    [InlineData("Production", 0)]
    [InlineData("Production", 999)]
    public async Task Unsupported_classification_or_unowned_policy_cannot_create_environment(string classification, long policy)
    {
        var project = await Project();
        Assert.Equal("denied", (await Catalog(new(Key(), "create", "environment", Tenant, project.Id, Name: "dev", Classification: classification, PolicyRevision: policy))).Status);
        await using var db = await Projects.CreateDbContextAsync(); Assert.Empty(await db.Environments.Where(x => x.TenantId == Tenant).ToArrayAsync());
    }

    [Fact]
    public async Task Rename_does_not_downgrade_classification_or_rebind_policy()
    {
        var project = await Project(); var environment = await CreateEnvironment(project.Id);
        var command = new CatalogCommand(Key(), "rename", "environment", Tenant, project.Id, environment.Id, 1, Name: "development");
        var renamed = await Catalog(command); Assert.Equal("success", renamed.Status);
        Assert.Equal("Production", renamed.Resource!.Classification); Assert.Equal(1, renamed.Resource.PolicyRevision); Assert.Equal(2, renamed.Resource.Revision);
        Assert.Equal("invalid", (await Catalog(command with { Key = Key(), ExpectedRevision = 2, Classification = "Development" })).Status);
        Assert.Equal("conflict", (await Catalog(command with { Key = Key() })).Status);
    }

    [Fact]
    public async Task Current_subject_tenant_scope_and_project_owner_are_required()
    {
        var project = await Project(); var otherTenant = await NewTenant(await NewPrincipal());
        Assert.Equal("denied", (await Catalog(new(Key(), "create", "application", otherTenant, project.Id, Name: "foreign"))).Status);
        CurrentSubject.Principal = await NewPrincipal();
        Assert.Equal("denied", (await Catalog(new(Key(), "create", "project", Tenant, Name: "ungranted"))).Status);
        CurrentSubject.Principal = null;
        Assert.Equal("denied", (await Catalog(new(Key(), "create", "project", Tenant, Name: "anonymous"))).Status);
    }

    [Fact]
    public async Task Suspend_reactivate_change_back_and_delete_never_reuse_authority_revision()
    {
        var project = await Project();
        var suspended = await Catalog(new(Key(), "suspend", "project", Tenant, project.Id, project.Id, 1));
        Assert.Equal("success", suspended.Status);
        await using var db = await Projects.CreateDbContextAsync();
        Assert.Null(await new ProjectCatalogStore(db, Policy).ResolveOwnershipAsync(Tenant, project.Id, null));
        // Restore is an offline/organization-authorized administrative operation in later wiring;
        // the project-scoped grant is invalid while the parent is suspended, so no online self-revival.
        Assert.Equal("denied", (await Catalog(new(Key(), "activate", "project", Tenant, project.Id, project.Id, 2))).Status);
        await using (var admission = await Gate.AcquireWriteAsync())
        {
            var owner = await db.Projects.SingleAsync(x => x.TenantId == Tenant && x.Id == project.Id);
            owner.Status = "active"; owner.Revision++; await db.SaveChangesAsync(); // synthetic owner recovery, not production path
        }
        var current = await new ProjectCatalogStore(db, Policy).ResolveOwnershipAsync(Tenant, project.Id, null);
        Assert.Equal(3, current!.ProjectRevision);
        Assert.Equal("conflict", (await Catalog(new(Key(), "rename", "project", Tenant, project.Id, project.Id, 1, Name: "stale"))).Status);
        Assert.Equal("success", (await Catalog(new(Key(), "delete", "project", Tenant, project.Id, project.Id, 3))).Status);
        Assert.Null(await new ProjectCatalogStore(db, Policy).ResolveOwnershipAsync(Tenant, project.Id, null));
    }

    [Fact]
    public async Task Authority_or_policy_loss_during_audit_staging_rolls_back_catalog_and_receipt()
    {
        var project = await Project(); Policy.Valid.Add((Tenant, project.Id, "Production", 1));
        var command = new CatalogCommand(Key(), "create", "environment", Tenant, project.Id, Name: "prod", Classification: "Production", PolicyRevision: 1);
        Assert.Equal("unavailable", (await Catalog(command, new FaultAudit(beforeSave: () => Policy.Valid.Clear()))).Status);
        await using var db = await Projects.CreateDbContextAsync(); Assert.Empty(await db.Environments.Where(x => x.TenantId == Tenant).ToArrayAsync());
        Policy.Valid.Add((Tenant, project.Id, "Production", 1));
        Assert.Equal("success", (await Catalog(command)).Status);
        var next = new CatalogCommand(Key(), "create", "application", Tenant, project.Id, Name: "app");
        Assert.Equal("unavailable", (await Catalog(next, new FaultAudit(beforeSave: () => CurrentSubject.Principal = null))).Status);
        CurrentSubject.Principal = Principal; Assert.Equal("success", (await Catalog(next)).Status);
    }

    [Fact]
    public async Task Composite_database_foreign_key_rejects_application_from_foreign_tenant()
    {
        var project = await Project();
        await using var db = await Projects.CreateDbContextAsync();
        db.Applications.Add(new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = project.Id, Name = "wrong tenant" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Port=5432;Database=kordra;Username=kordra_app")]
    [InlineData("Host=localhost;Port=5432;Database=kordra_test;Username=kordra_test")]
    public void Guard_rejects_any_unapproved_test_locator(string locator) => Assert.Throws<InvalidOperationException>(() => ProjectDbContextFactory.CreateTestOptions(locator));
}
