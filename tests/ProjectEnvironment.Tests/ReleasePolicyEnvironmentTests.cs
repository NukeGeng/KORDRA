using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.Catalog.Tests;

public sealed class ReleasePolicyEnvironmentTests : CatalogTestFixture
{
    [Fact]
    public async Task Neutral_reader_requires_exact_active_owner_chain_and_revisions()
    {
        var project = await Project();
        var environment = await CreateEnvironment(project.Id, "Test");
        await using var admission = await Gate.AcquireReadAsync();
        await using var db = await Projects.CreateDbContextAsync();
        var reader = new ReleasePolicyEnvironmentReader(db);
        var fact = await reader.ReadCurrentUnderAdmissionAsync(Tenant, project.Id, environment.Id);
        Assert.NotNull(fact); Assert.Equal("Test", fact.Classification); Assert.Equal(environment.Revision, fact.EnvironmentRevision);
        Assert.Null(await reader.ReadCurrentUnderAdmissionAsync(Guid.NewGuid(), project.Id, environment.Id));
        Assert.Null(await reader.ReadCurrentUnderAdmissionAsync(Tenant, Guid.NewGuid(), environment.Id));
        Assert.Null(await reader.ReadCurrentUnderAdmissionAsync(Tenant, project.Id, Guid.Empty));
    }

    [Fact]
    public async Task Reader_rejects_inactive_or_cross_tenant_colliding_environment()
    {
        var project = await Project();
        var environment = await CreateEnvironment(project.Id, "Test");
        var otherTenant = await NewTenant(await NewPrincipal());
        var otherProject = Guid.NewGuid();
        await using (var lease = await Gate.AcquireWriteAsync())
        await using (var write = await Projects.CreateDbContextAsync())
        {
            write.Projects.Add(new ProjectRecord { Id = otherProject, TenantId = otherTenant, Name = "Other", Status = "active", Revision = 1 });
            write.Environments.Add(new EnvironmentRecord
            {
                Id = environment.Id,
                TenantId = otherTenant,
                ProjectId = otherProject,
                Name = "Collision",
                Classification = "Test",
                Status = "deleted",
                Revision = 1
            });
            await write.SaveChangesAsync();
        }
        await using var admission = await Gate.AcquireReadAsync();
        await using var read = await Projects.CreateDbContextAsync();
        Assert.Null(await new ReleasePolicyEnvironmentReader(read).ReadCurrentUnderAdmissionAsync(Tenant, project.Id, environment.Id));
    }
}
