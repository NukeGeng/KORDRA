using Kordra.Api;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.Catalog.Tests;

public sealed class ArtifactProjectIdentityTests : CatalogTestFixture
{
    [Fact]
    public async Task Owner_lookup_and_Host_adapter_resolve_current_project_without_a_body_tenant()
    {
        var project = await Project();
        var reader = new CurrentProjectIdentityReader(Projects);
        await using var admission = await Gate.AcquireReadAsync();
        var selected = await reader.ReadCurrentProjectUnderAdmissionAsync(project.Id);
        Assert.NotNull(selected); Assert.Equal(Tenant, selected.TenantId); Assert.Equal(project.Id, selected.ProjectId);
        var adapted = await new CurrentArtifactProjectAuthorityReader(reader).ReadCurrentUnderAdmissionAsync(project.Id);
        Assert.NotNull(adapted); Assert.Equal(selected.Revision, adapted.Revision); Assert.Equal(selected.TenantId, adapted.TenantId);
        Assert.Null(await reader.ReadCurrentProjectUnderAdmissionAsync(Guid.Empty));
        Assert.Null(await reader.ReadCurrentProjectUnderAdmissionAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_colliding_inactive_project_ID_does_not_select_the_other_active_tenant()
    {
        var project = await Project();
        var otherTenant = await NewTenant(await NewPrincipal());
        await using (var write = await Gate.AcquireWriteAsync())
        await using (var db = await Projects.CreateDbContextAsync())
        {
            db.Projects.Add(new ProjectRecord { Id = project.Id, TenantId = otherTenant, Name = "Synthetic collision", Status = "deleted", Revision = 1 });
            await db.SaveChangesAsync();
        }
        await using var admission = await Gate.AcquireReadAsync();
        await using var read = await Projects.CreateDbContextAsync();
        Assert.Null(await new ProjectIdentityReader(read).ReadCurrentProjectUnderAdmissionAsync(project.Id));
    }

    [Fact]
    public async Task Real_TEN_and_IAM_roles_require_an_explicit_artifact_grant_and_observe_project_changes()
    {
        var project = await Project();
        var subject = new SyntheticSession(Principal);
        var resolver = new ArtifactProjectAuthorityReader(subject, TenantService, Roles,
            new CurrentArtifactProjectAuthorityReader(new CurrentProjectIdentityReader(Projects)), subject);
        var invocation = Guid.NewGuid();
        async Task<ArtifactProjectAuthoritySnapshot?> Read(string action = "artifact.read")
        {
            await using var admission = await Gate.AcquireReadAsync();
            return await resolver.ReadCurrentUnderAdmissionAsync(subject.Selection, project.Id, action, invocation);
        }
        Assert.Null(await Read()); // project.read/target.read from Project() grant no artifact authority
        await Grant(new("project", project.Id), [new("artifact.read", "project")]);
        var original = await Read(); Assert.NotNull(original); Assert.Equal(Tenant, original.Project.TenantId);
        Assert.Null(await Read("artifact.register"));
        await using (var write = await Gate.AcquireWriteAsync())
        await using (var db = await Projects.CreateDbContextAsync())
        {
            var row = await db.Projects.SingleAsync(p => p.Id == project.Id && p.TenantId == Tenant);
            row.Status = "suspended"; row.Revision++; await db.SaveChangesAsync();
        }
        Assert.Null(await Read());
        await using (var write = await Gate.AcquireWriteAsync())
        await using (var db = await Projects.CreateDbContextAsync())
        {
            var row = await db.Projects.SingleAsync(p => p.Id == project.Id && p.TenantId == Tenant);
            row.Status = "active"; row.Revision++; await db.SaveChangesAsync();
        }
        var restored = await Read(); Assert.NotNull(restored); Assert.NotEqual(original.Digest, restored.Digest);
    }

    [Fact]
    public async Task Revoked_membership_denies_even_when_a_synthetic_session_reference_is_still_present()
    {
        var project = await Project(); await Grant(new("project", project.Id), [new("artifact.register", "project")]);
        var subject = new SyntheticSession(Principal);
        var resolver = new ArtifactProjectAuthorityReader(subject, TenantService, Roles,
            new CurrentArtifactProjectAuthorityReader(new CurrentProjectIdentityReader(Projects)), subject);
        async Task<ArtifactProjectAuthoritySnapshot?> Read()
        {
            await using var admission = await Gate.AcquireReadAsync();
            return await resolver.ReadCurrentUnderAdmissionAsync(subject.Selection, project.Id, "artifact.register", Guid.NewGuid());
        }
        Assert.NotNull(await Read());
        await using (var write = await Gate.AcquireWriteAsync())
        await using (var db = await Tenants.CreateDbContextAsync())
        {
            var member = await db.Memberships.SingleAsync(m => m.Id == Member && m.TenantId == Tenant);
            member.Status = "revoked"; member.Revision++; await db.SaveChangesAsync();
        }
        Assert.Null(await Read());
    }

    // This fixture isolates project/grant owner behavior. Actual cookie/session and HTTP
    // handoff are not proven by it and remain part of the guarded T10.1e tests.
    private sealed class SyntheticSession(Guid principal) : ICurrentSubjectReader, IAuthorityClock
    {
        private readonly Guid _session = Guid.NewGuid(), _boot = Guid.NewGuid();
        private readonly DateTimeOffset _absoluteExpiry = DateTimeOffset.UtcNow.AddHours(1),
            _idleExpiry = DateTimeOffset.UtcNow.AddMinutes(30);
        public SessionSelection Selection => new(principal, "synthetic-not-an-authenticated-cookie", _boot);
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default) =>
            Task.FromResult<CurrentSubject?>(new(principal, _session, _boot, 1, _absoluteExpiry, _idleExpiry));
        public DateTimeOffset GetTrustedUtcNow() => DateTimeOffset.UtcNow;
    }
}
