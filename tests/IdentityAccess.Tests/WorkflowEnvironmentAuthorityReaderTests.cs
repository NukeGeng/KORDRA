using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class WorkflowEnvironmentAuthorityReaderTests
{
    [Theory]
    [InlineData("workflow.start")]
    [InlineData("workflow.read")]
    [InlineData("workflow.cancel")]
    [InlineData("workflow.reconcile")]
    public async Task FactsUseExactEnvironmentGrantAndEarliestExpiry(string permission)
    {
        var fixture = new Fixture(permission);

        var result = await fixture.Read(permission);

        Assert.NotNull(result);
        Assert.Empty(typeof(WorkflowEnvironmentAuthoritySnapshot).GetConstructors());
        Assert.Equal(fixture.Now.AddMinutes(5), result.ValidUntilUtc);
        Assert.Equal((fixture.Selection.PrincipalId, fixture.Tenant, permission, "environment", fixture.Environment),
            fixture.RequestedGrant);
    }

    [Theory]
    [InlineData("project-tenant")]
    [InlineData("environment-project")]
    [InlineData("environment-id")]
    [InlineData("boot")]
    [InlineData("grant-tenant")]
    [InlineData("grant-scope")]
    [InlineData("grant-permission")]
    [InlineData("missing-grant")]
    [InlineData("definition-revision")]
    [InlineData("explicit-deny")]
    [InlineData("expired-grant")]
    [InlineData("clock-rollback")]
    public async Task MismatchedExpiredOrDeniedFactsFailClosed(string fault)
    {
        var fixture = new Fixture("workflow.read");
        switch (fault)
        {
            case "project-tenant": fixture.ProjectFact = fixture.ProjectFact with { TenantId = Guid.NewGuid() }; break;
            case "environment-project": fixture.EnvironmentFact = fixture.EnvironmentFact with { ProjectId = Guid.NewGuid() }; break;
            case "environment-id": fixture.EnvironmentFact = fixture.EnvironmentFact with { EnvironmentId = Guid.NewGuid() }; break;
            case "boot": fixture.Subject = fixture.Subject with { BootGeneration = Guid.NewGuid() }; break;
            case "grant-tenant": fixture.Grant = fixture.Grant! with { TenantId = Guid.NewGuid() }; break;
            case "grant-scope": fixture.Grant = fixture.Grant! with { ScopeKind = "project" }; break;
            case "grant-permission": fixture.Grant = fixture.Grant! with { Permission = "workflow.cancel" }; break;
            case "missing-grant": fixture.Grant = null; break;
            case "definition-revision": fixture.Role = fixture.Role with { Revision = 2 }; break;
            case "explicit-deny": fixture.Role = fixture.Role with
                { Rules = [new("workflow.read", "environment"), new("workflow.read", "environment", "deny")] }; break;
            case "expired-grant": fixture.AfterDefinition = () => fixture.Now = fixture.Now.AddMinutes(5); break;
            case "clock-rollback": fixture.AfterDefinition = () => fixture.Now = fixture.Now.AddSeconds(-1); break;
        }

        Assert.Null(await fixture.Read("workflow.read"));
    }

    [Fact]
    public async Task CancellationPropagatesBeforeFactsAreReturned()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture("workflow.read") { AfterDefinition = cancellation.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Read("workflow.read", cancellation.Token));
    }

    private sealed class Fixture : ICurrentSubjectReader, IScopedGrantReader, IRoleDefinitionReader,
        IReleaseProjectAuthorityReader, IApprovalEnvironmentAuthorityReader, IAuthorityClock
    {
        public readonly Guid Tenant = Guid.NewGuid();
        public readonly Guid Project = Guid.NewGuid();
        public readonly Guid Environment = Guid.NewGuid();
        public readonly SessionSelection Selection;
        private readonly WorkflowEnvironmentAuthorityReader reader;
        public DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
        public CurrentSubject Subject;
        public ScopedGrant? Grant;
        public ReleaseProjectAuthority ProjectFact;
        public ApprovalEnvironmentAuthority EnvironmentFact;
        public RoleDefinitionSnapshot Role;
        public Action? AfterDefinition;
        public (Guid Principal, Guid Tenant, string Permission, string Scope, Guid Id) RequestedGrant;

        public Fixture(string permission)
        {
            Selection = new(Guid.NewGuid(), "synthetic-session", Guid.NewGuid());
            Subject = new(Selection.PrincipalId, Guid.NewGuid(), Selection.BootGeneration, 1, Now.AddHours(1), Now.AddMinutes(10));
            ProjectFact = new(Tenant, Project, 1);
            EnvironmentFact = new(Tenant, Project, Environment, 1);
            Grant = new(Selection.PrincipalId, Tenant, Guid.NewGuid(), 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(),
                1, permission, "environment", Environment, Now.AddMinutes(5));
            Role = new(Grant.DefinitionId, 1, "Fixture", [new(permission, "environment")]);
            reader = new(this, this, this, this, this, this);
        }

        public Task<WorkflowEnvironmentAuthoritySnapshot?> Read(string permission, CancellationToken ct = default) =>
            reader.ReadCurrentUnderAdmissionAsync(Selection, Tenant, Project, Environment, permission, ct);

        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default) =>
            Task.FromResult<CurrentSubject?>(Subject);
        public Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult<ReleaseProjectAuthority?>(ProjectFact);
        public Task<ApprovalEnvironmentAuthority?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId,
            Guid environmentId, CancellationToken ct = default) => Task.FromResult<ApprovalEnvironmentAuthority?>(EnvironmentFact);
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission,
            string scopeKind, Guid scopeId, CancellationToken ct = default)
        {
            RequestedGrant = (principal, tenant, permission, scopeKind, scopeId);
            return Task.FromResult(Grant);
        }

        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken ct = default)
        {
            AfterDefinition?.Invoke();
            return Task.FromResult<RoleDefinitionSnapshot?>(Role);
        }

        public Task<bool> IsActivePrincipalAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
        public DateTimeOffset GetTrustedUtcNow() => Now;
    }
}
