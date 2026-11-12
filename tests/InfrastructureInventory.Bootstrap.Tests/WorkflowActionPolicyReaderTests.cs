using Kordra.Api;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowActionPolicyReaderTests
{
    [Theory]
    [InlineData("workflow.start")]
    [InlineData("workflow.read")]
    [InlineData("workflow.cancel")]
    [InlineData("workflow.reconcile")]
    public async Task StableRevisionEvidenceDoesNotChangeWithObservationTime(string action)
    {
        var f = new Facts(action);
        var first = await f.Read(action);
        Assert.NotNull(first);
        f.Now = f.Now.AddSeconds(1);
        var next = await f.Read(action);
        Assert.NotNull(next);
        Assert.Equal(first.AuthorityDigest, next.AuthorityDigest);
        Assert.True(next.ValidUntilUtc > first.ValidUntilUtc);
        f.Owner = f.Owner with { EnvironmentRevision = 2 };
        Assert.NotEqual(first.AuthorityDigest, (await f.Read(action))!.AuthorityDigest);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("tenant")]
    [InlineData("action")]
    [InlineData("classification")]
    [InlineData("revision")]
    [InlineData("deny")]
    [InlineData("expired")]
    public async Task MissingOrChangedCurrentFactsDeny(string fault)
    {
        var f = new Facts("workflow.read");
        switch (fault)
        {
            case "binding": f.Binding = null; break;
            case "tenant": f.Owner = f.Owner with { TenantId = Guid.NewGuid() }; break;
            case "action": f.Binding = f.Binding! with { Action = "workflow.start" }; break;
            case "classification": f.Owner = f.Owner with { Classification = "Production" }; break;
            case "revision": f.Binding = f.Binding! with { PolicyRevision = 2 }; break;
            case "deny": f.Policy = f.Policy with
                { Payload = f.Policy.Payload with { Rules = [new("Test", "deny")] } };
                f.Policy = f.Policy with { ContentHash = ResourcePolicies.Hash(f.Policy.Payload) }; break;
            case "expired": f.Policy = f.Policy with { Payload = f.Policy.Payload with { ExpiresAtUtc = f.Now } };
                f.Policy = f.Policy with { ContentHash = ResourcePolicies.Hash(f.Policy.Payload) }; break;
        }
        Assert.Null(await f.Read("workflow.read"));
    }

    [Theory]
    [InlineData("workflow.start")]
    [InlineData("workflow.read")]
    [InlineData("workflow.cancel")]
    [InlineData("workflow.reconcile")]
    public async Task CombinedAdmissionRequiresGrantAndStablePolicyWithinOriginalCap(string action)
    {
        var f = new Facts(action);
        var original = await f.Admit(action);
        Assert.NotNull(original);
        f.Now = f.Now.AddSeconds(1);
        Assert.True(original.IsCurrentSuccessor(await f.Admit(action)));
        f.Binding = f.Binding! with { BindingRevision = 2 };
        Assert.False(original.IsCurrentSuccessor(await f.Admit(action)));
        f.Binding = f.Binding with { BindingRevision = 1 };
        f.Grant = null;
        Assert.Null(await f.Admit(action));
    }

    private sealed class Facts : IReleasePolicyEnvironmentReader, IActionPolicyBindingReader, ICurrentResourcePolicyReader, IAuthorityClock, ICurrentSubjectReader, IScopedGrantReader, IRoleDefinitionReader, IReleaseProjectAuthorityReader, IApprovalEnvironmentAuthorityReader
    {
        public DateTimeOffset Now = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
        public WorkflowReadScope Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        public ReleasePolicyEnvironment Owner;
        public PolicyBinding? Binding;
        public ResourcePolicySnapshot Policy;
        private readonly WorkflowActionPolicyReader reader;
        private readonly WorkflowCommandAdmissionReader admission;
        private readonly SessionSelection session = new(Guid.NewGuid(), "synthetic-session", Guid.NewGuid());
        private readonly Guid sessionId = Guid.NewGuid();
        private readonly DateTimeOffset expires = new(2026, 11, 7, 1, 0, 0, TimeSpan.Zero);
        public ScopedGrant? Grant;

        public Facts(string action)
        {
            Owner = new(Scope.TenantId, Scope.ProjectId, 1, Scope.EnvironmentId, 1, "Test");
            var payload = new ResourcePolicyPayload(2, action, "environment", "active", Now.AddMinutes(-1), null, [new("Test", "allow")]);
            Policy = new(Guid.NewGuid(), Scope.TenantId, Scope.ProjectId, 1, payload, ResourcePolicies.Hash(payload));
            Binding = new(Guid.NewGuid(), Scope.TenantId, Scope.ProjectId, Scope.EnvironmentId, "environment", action, Policy.PolicyId, 1, 1);
            reader = new(this, this, this, this);
            Grant = new(session.PrincipalId, Scope.TenantId, Guid.NewGuid(), 1, 1, Guid.NewGuid(), 1,
                Guid.NewGuid(), 1, action, "environment", Scope.EnvironmentId, expires);
            admission = new(new(this, this, this, this, this, this), reader, this);
        }

        public Task<WorkflowReadPolicy?> Read(string action) => reader.ReadAllowedUnderAdmissionAsync(Scope, action);
        public Task<WorkflowCommandAdmission?> Admit(string action) =>
            admission.ReadUnderAdmissionAsync(session, Scope, action);
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default) =>
            Task.FromResult<CurrentSubject?>(new(session.PrincipalId, sessionId, session.BootGeneration, 1, expires, expires));
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission,
            string scopeKind, Guid scopeId, CancellationToken ct = default) => Task.FromResult(Grant);
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken ct = default) =>
            Task.FromResult<RoleDefinitionSnapshot?>(new(id, revision, "Synthetic", [new(Grant!.Permission, "environment")]));
        public Task<bool> IsActivePrincipalAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid project, CancellationToken ct = default) =>
            Task.FromResult<ReleaseProjectAuthority?>(new(Scope.TenantId, Scope.ProjectId, Owner.ProjectRevision));
        Task<ApprovalEnvironmentAuthority?> IApprovalEnvironmentAuthorityReader.ReadCurrentUnderAdmissionAsync(
            Guid tenant, Guid project, Guid environment, CancellationToken ct) =>
            Task.FromResult<ApprovalEnvironmentAuthority?>(new(Scope.TenantId, Scope.ProjectId, Scope.EnvironmentId, Owner.EnvironmentRevision));

        public DateTimeOffset GetTrustedUtcNow() => Now;
        public Task<ReleasePolicyEnvironment?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid environmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReleasePolicyEnvironment?>(Owner);
        public Task<PolicyBinding?> ReadCurrentForActionUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid? environmentId, string action, CancellationToken ct = default) =>
            Task.FromResult(Binding);
        public Task<ResourcePolicySnapshot?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid policyId, long exactRevision, CancellationToken ct = default) =>
            Task.FromResult<ResourcePolicySnapshot?>(Policy);
    }
}
