using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class ReleaseProjectAuthorityTests
{
    [Theory]
    [InlineData("release.read")]
    [InlineData("release.create")]
    [InlineData("release.retire")]
    public async Task Exact_project_action_uses_owner_tenant_and_current_subject(string action)
    {
        var f = new Fixture(); f.SetAction(action);
        var result = await f.Read(action);
        Assert.NotNull(result);
        Assert.Equal(f.Project!.TenantId, f.LastTenant);
        Assert.Equal(f.Subject!.PrincipalId, f.LastPrincipal);
        Assert.Equal((action, "project", f.Project.ProjectId), f.LastActionScope);
        Assert.Equal(f.Grant!.ExpiresAtUtc, result.ValidUntilUtc);
        Assert.Empty(typeof(ReleaseProjectAuthoritySnapshot).GetConstructors());
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("wrong-principal")]
    [InlineData("session")]
    [InlineData("boot")]
    [InlineData("principal-revision")]
    [InlineData("project")]
    [InlineData("project-id")]
    [InlineData("project-revision")]
    [InlineData("grant")]
    [InlineData("tenant")]
    [InlineData("membership")]
    [InlineData("tenant-revision")]
    [InlineData("membership-revision")]
    [InlineData("assignment")]
    [InlineData("definition-revision")]
    [InlineData("wrong-action")]
    [InlineData("organization")]
    [InlineData("environment")]
    [InlineData("platform")]
    [InlineData("wrong-scope")]
    [InlineData("expired-session")]
    [InlineData("expired-grant")]
    [InlineData("definition")]
    [InlineData("role-deny")]
    [InlineData("read-is-not-register")]
    [InlineData("non-utc-expiry")]
    [InlineData("unbounded-expiry")]
    [InlineData("reader-error")]
    public async Task Missing_mismatched_expired_or_denied_facts_never_produce_a_snapshot(string fault)
    {
        var f = new Fixture();
        switch (fault)
        {
            case "subject": f.Subject = null; break;
            case "wrong-principal": f.Subject = f.Subject! with { PrincipalId = Guid.NewGuid() }; break;
            case "session": f.Subject = f.Subject! with { SessionId = Guid.Empty }; break;
            case "boot": f.Subject = f.Subject! with { BootGeneration = Guid.NewGuid() }; break;
            case "principal-revision": f.Subject = f.Subject! with { PrincipalRevision = 0 }; break;
            case "project": f.Project = null; break;
            case "project-id": f.Project = f.Project! with { ProjectId = Guid.NewGuid() }; break;
            case "project-revision": f.Project = f.Project! with { Revision = 0 }; break;
            case "grant": f.Grant = null; break;
            case "tenant": f.Grant = f.Grant! with { TenantId = Guid.NewGuid() }; break;
            case "membership": f.Grant = f.Grant! with { MembershipId = Guid.Empty }; break;
            case "tenant-revision": f.Grant = f.Grant! with { TenantRevision = 0 }; break;
            case "membership-revision": f.Grant = f.Grant! with { MembershipRevision = 0 }; break;
            case "assignment": f.Grant = f.Grant! with { AssignmentRevision = 0 }; break;
            case "definition-revision": f.Grant = f.Grant! with { DefinitionRevision = 0 }; break;
            case "wrong-action": f.Grant = f.Grant! with { Permission = "release.read" }; break;
            case "organization": f.Grant = f.Grant! with { ScopeKind = "organization" }; break;
            case "environment": f.Grant = f.Grant! with { ScopeKind = "environment" }; break;
            case "platform": f.Grant = f.Grant! with { ScopeKind = "platform" }; break;
            case "wrong-scope": f.Grant = f.Grant! with { ScopeId = Guid.NewGuid() }; break;
            case "expired-session": f.Subject = f.Subject! with { IdleExpiresAtUtc = f.Now }; break;
            case "expired-grant": f.Grant = f.Grant! with { ExpiresAtUtc = f.Now }; break;
            case "definition": f.Role = null; break;
            case "role-deny": f.Role = f.Role! with { Rules = [new("release.create", "project"), new("release.create", "project", "deny")] }; break;
            case "read-is-not-register": f.Role = f.Role! with { Rules = [new("release.read", "project")] }; break;
            case "non-utc-expiry": f.Subject = f.Subject! with { AbsoluteExpiresAtUtc = f.Now.ToOffset(TimeSpan.FromHours(7)).AddHours(1) }; break;
            case "unbounded-expiry": f.Grant = f.Grant! with { ExpiresAtUtc = DateTimeOffset.MaxValue }; break;
            case "reader-error": f.FailReader = true; break;
        }
        Assert.Null(await f.Read());
    }

    [Theory]
    [InlineData("org.read")]
    [InlineData("target.read")]
    [InlineData("release.manage")]
    [InlineData("Release.Read")]
    public async Task Other_actions_do_not_reach_owner_reads(string action)
    {
        var f = new Fixture(); Assert.Null(await f.Read(action)); Assert.Equal(0, f.SubjectReads);
    }

    [Fact]
    public async Task Canonical_vector_pins_invocation_and_revisions_without_session_reference()
    {
        var f = new Fixture(); var first = await f.Read(); Assert.NotNull(first);
        // Independent Python compact-JSON/SHA-256 vector, with UTC dates rendered as seven-digit Z.
        Assert.Equal("17820bfa13b167963625fc349f04d3f96ad0078a4ad59ae1fc070398680a9ff9", first.Digest);
        f.Role = f.Role! with { Name = "Any display label" };
        Assert.Equal(first.Digest, (await f.Read())!.Digest);
        Assert.NotEqual(first.Digest, (await f.Reader.ReadCurrentUnderAdmissionAsync(f.Selection, f.ProjectId,
            "release.create", Guid.NewGuid()))!.Digest);
        f.Grant = f.Grant! with { MembershipRevision = 2 };
        Assert.NotEqual(first.Digest, (await f.Read())!.Digest);
        f.Grant = f.Grant with { MembershipRevision = 1 };
        f.Project = f.Project! with { Revision = 3 }; // change-and-back does not reuse a revision
        Assert.NotEqual(first.Digest, (await f.Read())!.Digest);
    }

    [Fact]
    public async Task Sliding_expiry_does_not_change_identity_but_current_cap_always_applies()
    {
        var f = new Fixture(); var first = await f.Read(); Assert.NotNull(first);
        f.Subject = f.Subject! with { IdleExpiresAtUtc = f.Now.AddHours(1) };
        f.Grant = f.Grant! with { ExpiresAtUtc = f.Now.AddMinutes(10) };
        var later = await f.Read(); Assert.NotNull(later);
        Assert.Equal(first.Digest, later.Digest);
        Assert.Equal(f.Now.AddMinutes(10), later.ValidUntilUtc);
        f.AfterDefinition = () => f.Now = f.Now.AddMinutes(10);
        Assert.Null(await f.Read()); // equality at the second clock read is expired
    }

    [Fact]
    public async Task Clock_failure_rollback_and_cancellation_do_not_authorize()
    {
        var f = new Fixture { FailClock = true }; Assert.Null(await f.Read());
        f = new Fixture(); f.AfterDefinition = () => f.Now = f.Now.AddSeconds(-1); Assert.Null(await f.Read());
        f = new Fixture(); using var cancelled = new CancellationTokenSource(); f.AfterDefinition = cancelled.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Read(ct: cancelled.Token));
    }

    [Theory]
    [InlineData("release.read")]
    [InlineData("release.create")]
    [InlineData("release.retire")]
    public void Permission_adoption_is_project_only_and_deny_wins(string action)
    {
        Assert.True(FoundationPermissions.IsValid(new(action, "project")));
        Assert.False(FoundationPermissions.IsValid(new(action, "organization")));
        Assert.False(FoundationPermissions.IsValid(new(action, "environment")));
        Assert.False(FoundationPermissions.IsValid(new(action, "platform")));
        Assert.False(FoundationPermissions.Allows(new(Guid.NewGuid(), 1, "Owner", [new(action, "project"),
            new(action, "project", "deny")]), action, "project"));
    }

    private sealed class Fixture : ICurrentSubjectReader, IScopedGrantReader, IRoleDefinitionReader,
        IReleaseProjectAuthorityReader, IAuthorityClock
    {
        public DateTimeOffset Now = new(2026, 10, 25, 13, 0, 0, TimeSpan.Zero);
        public CurrentSubject? Subject;
        public ScopedGrant? Grant;
        public RoleDefinitionSnapshot? Role;
        public ReleaseProjectAuthority? Project;
        public bool FailReader, FailClock;
        public Action? AfterDefinition;
        public int SubjectReads;
        public Guid LastTenant, LastPrincipal;
        public (string, string, Guid) LastActionScope;
        public Guid ProjectId { get; } = Guid.Parse("55555555-5555-5555-5555-555555555555");
        public Guid InvocationId { get; } = Guid.Parse("99999999-9999-9999-9999-999999999999");
        public SessionSelection Selection { get; }
        public ReleaseProjectAuthorityReader Reader { get; }
        public Fixture()
        {
            var principal = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var boot = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var tenant = Guid.Parse("44444444-4444-4444-4444-444444444444");
            Subject = new(principal, Guid.Parse("22222222-2222-2222-2222-222222222222"), boot, 1, Now.AddHours(2), Now.AddMinutes(30));
            Project = new(tenant, ProjectId, 1);
            Grant = new(principal, tenant, Guid.Parse("66666666-6666-6666-6666-666666666666"), 1, 1,
                Guid.Parse("77777777-7777-7777-7777-777777777777"), 1,
                Guid.Parse("88888888-8888-8888-8888-888888888888"), 1, "release.create", "project", ProjectId, Now.AddMinutes(15));
            Role = new(Grant.DefinitionId, 1, "Admin is only a label", [new("release.create", "project")]);
            Selection = new(principal, "private-test-session-reference", boot);
            Reader = new(this, this, this, this, this);
        }
        public void SetAction(string action) { Grant = Grant! with { Permission = action }; Role = Role! with { Rules = [new(action, "project")] }; }
        public Task<ReleaseProjectAuthoritySnapshot?> Read(string action = "release.create", CancellationToken ct = default) =>
            Reader.ReadCurrentUnderAdmissionAsync(Selection, ProjectId, action, InvocationId, ct);
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default)
        { SubjectReads++; return Task.FromResult(Subject); }
        public Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid projectId, CancellationToken ct = default) =>
            FailReader ? throw new InvalidOperationException("Synthetic owner unavailable") : Task.FromResult(Project);
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission,
            string scopeKind, Guid scopeId, CancellationToken ct = default)
        { LastPrincipal = principal; LastTenant = tenant; LastActionScope = (permission, scopeKind, scopeId); return Task.FromResult(Grant); }
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken ct = default)
        { AfterDefinition?.Invoke(); return Task.FromResult(Role); }
        public Task<bool> IsActivePrincipalAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
        public DateTimeOffset GetTrustedUtcNow() => FailClock ? throw new InvalidOperationException("Synthetic clock unavailable") : Now;
    }
}
