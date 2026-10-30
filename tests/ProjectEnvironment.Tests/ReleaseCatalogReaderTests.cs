using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Api;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.Catalog.Tests;

public sealed class ReleaseCatalogReaderTests : CatalogTestFixture
{
    [Fact]
    public async Task Environment_identity_comes_from_unique_active_owner_without_caller_scope()
    {
        var project = await Project(); var environment = await CreateEnvironment(project.Id, "Test");
        IApprovalEnvironmentIdentityReader reader = new ApprovalEnvironmentAuthorityReader(Projects);
        await using (var held = await Gate.AcquireReadAsync())
        {
            var result = await reader.ReadCurrentUnderAdmissionAsync(environment.Id);
            Assert.NotNull(result); Assert.Equal(Tenant, result.TenantId); Assert.Equal(project.Id, result.ProjectId);
            Assert.Equal(environment.Id, result.EnvironmentId);
            Assert.Null(await reader.ReadCurrentUnderAdmissionAsync(Guid.Empty));
            Assert.Null(await reader.ReadCurrentUnderAdmissionAsync(Guid.NewGuid()));
        }
        await using (var held = await Gate.AcquireWriteAsync())
        await using (var db = await Projects.CreateDbContextAsync())
        {
            var otherTenant = Guid.NewGuid(); var otherProject = Guid.NewGuid();
            db.Projects.Add(new() { TenantId = otherTenant, Id = otherProject, Name = "Synthetic collision" });
            db.Environments.Add(new() { TenantId = otherTenant, ProjectId = otherProject, Id = environment.Id,
                Name = "Synthetic inactive collision", Status = "deleted", Classification = "Test" });
            await db.SaveChangesAsync();
        }
        await using var admission = await Gate.AcquireReadAsync();
        Assert.Null(await reader.ReadCurrentUnderAdmissionAsync(environment.Id));
    }
    [Fact]
    public async Task Approval_environment_adapter_reads_only_the_exact_active_owner()
    {
        var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var adapter = new ApprovalEnvironmentAuthorityReader(Projects);
        await using (var admission = await Gate.AcquireReadAsync())
        {
            var current = await adapter.ReadCurrentUnderAdmissionAsync(Tenant, project.Id, env.Id);
            Assert.NotNull(current); Assert.Equal(Tenant, current.TenantId); Assert.Equal(env.Id, current.EnvironmentId);
            Assert.Null(await adapter.ReadCurrentUnderAdmissionAsync(Guid.NewGuid(), project.Id, env.Id));
            Assert.Null(await adapter.ReadCurrentUnderAdmissionAsync(Tenant, project.Id, Guid.NewGuid()));
        }
        await using (var admission = await Gate.AcquireWriteAsync())
        await using (var db = await Projects.CreateDbContextAsync())
        {
            var row = await db.Environments.SingleAsync(x => x.TenantId == Tenant && x.ProjectId == project.Id && x.Id == env.Id);
            row.Status = "deleted"; row.Revision++; await db.SaveChangesAsync();
        }
        await using var held = await Gate.AcquireReadAsync();
        Assert.Null(await adapter.ReadCurrentUnderAdmissionAsync(Tenant, project.Id, env.Id));
    }
    [Fact]
    public async Task Neutral_owner_readers_require_exact_current_project_environment_and_target()
    {
        var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var application = await Catalog(new(Key(), "create", "application", Tenant, project.Id, Name: "Frontend"));
        Assert.Equal("success", application.Status);
        var target = await Target(new(Key(), "register", Tenant, project.Id, env.Id, Name: "Synthetic", Kind: "kubernetes"));
        Assert.Equal("success", target.Status);
        await using var admission = await Gate.AcquireReadAsync();
        await using var prj = await Projects.CreateDbContextAsync();
        await using var inv = await Inventory.CreateDbContextAsync();
        IReleaseCatalogReader projects = new ReleaseCatalogReader(prj);
        IReleaseTargetReader targets = new ReleaseTargetReader(inv);
        var selected = await projects.ReadUnderAdmissionAsync(Tenant, project.Id, application.Resource!.Id, env.Id, target.Resource!.TargetId);
        var selectedTarget = await targets.ReadUnderAdmissionAsync(Tenant, project.Id, env.Id, target.Resource.TargetId);
        Assert.NotNull(selected); Assert.NotNull(selectedTarget);
        Assert.Equal(selected.AssociationRevision, selectedTarget.AssociationRevision);
        Assert.Equal("Test", selected.Classification); Assert.Equal("kubernetes", selectedTarget.Kind);
        Assert.Null(await projects.ReadUnderAdmissionAsync(Guid.NewGuid(), project.Id, application.Resource.Id, env.Id, target.Resource.TargetId));
        Assert.Null(await projects.ReadUnderAdmissionAsync(Tenant, project.Id, Guid.NewGuid(), env.Id, target.Resource.TargetId));
        Assert.Null(await projects.ReadUnderAdmissionAsync(Tenant, project.Id, application.Resource.Id, Guid.NewGuid(), target.Resource.TargetId));
        Assert.Null(await targets.ReadUnderAdmissionAsync(Tenant, project.Id, Guid.NewGuid(), target.Resource.TargetId));
    }

    [Fact]
    public async Task Disabled_target_and_changed_association_do_not_become_a_release_selection()
    {
        var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var other = await CreateEnvironment(project.Id, "Test");
        var app = (await Catalog(new(Key(), "create", "application", Tenant, project.Id, Name: "Frontend"))).Resource!;
        var target = (await Target(new(Key(), "register", Tenant, project.Id, env.Id, Name: "Synthetic", Kind: "kubernetes"))).Resource!;
        await using (var admission = await Gate.AcquireWriteAsync())
        await using (var db = await Inventory.CreateDbContextAsync())
        {
            var row = await db.Targets.SingleAsync(x => x.Id == target.TargetId);
            row.Status = "disabled"; row.Revision++; await db.SaveChangesAsync();
        }
        await using (var admission = await Gate.AcquireReadAsync())
        await using (var inv = await Inventory.CreateDbContextAsync())
        {
            Assert.Null(await new ReleaseTargetReader(inv).ReadUnderAdmissionAsync(Tenant, project.Id, env.Id, target.TargetId));
        }
        await using (var admission = await Gate.AcquireWriteAsync())
        await using (var db = await Projects.CreateDbContextAsync())
        {
            var association = await db.Associations.SingleAsync(x => x.TargetId == target.TargetId);
            association.EnvironmentId = other.Id; association.Revision++; await db.SaveChangesAsync();
        }
        await using var readAdmission = await Gate.AcquireReadAsync();
        await using var prj = await Projects.CreateDbContextAsync();
        Assert.Null(await new ReleaseCatalogReader(prj).ReadUnderAdmissionAsync(Tenant, project.Id, app.Id, env.Id, target.TargetId));
    }

    [Fact]
    public async Task Inactive_or_cross_tenant_colliding_application_never_resolves()
    {
        var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var app = (await Catalog(new(Key(), "create", "application", Tenant, project.Id, Name: "Frontend"))).Resource!;
        var target = (await Target(new(Key(), "register", Tenant, project.Id, env.Id, Name: "Synthetic", Kind: "kubernetes"))).Resource!;
        var otherTenant = await NewTenant(await NewPrincipal()); var otherProject = Guid.NewGuid();
        await using (var admission = await Gate.AcquireWriteAsync())
        await using (var db = await Projects.CreateDbContextAsync())
        {
            db.Projects.Add(new ProjectRecord { Id = otherProject, TenantId = otherTenant, Name = "Other", Status = "active", Revision = 1 });
            db.Applications.Add(new ApplicationRecord
            {
                Id = app.Id,
                TenantId = otherTenant,
                ProjectId = otherProject,
                Name = "Collision",
                Status = "deleted",
                Revision = 1
            });
            await db.SaveChangesAsync();
        }
        await using var readAdmission = await Gate.AcquireReadAsync();
        await using var read = await Projects.CreateDbContextAsync();
        Assert.Null(await new ReleaseCatalogReader(read).ReadUnderAdmissionAsync(Tenant, project.Id, app.Id, env.Id, target.TargetId));
    }
}
