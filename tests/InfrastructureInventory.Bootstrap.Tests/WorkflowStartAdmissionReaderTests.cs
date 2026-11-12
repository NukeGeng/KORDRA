using Kordra.Api;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>Pure join/expiry tests with explicit synthetic REL/APR facts. Not actual REL/APR persistence qualification.</summary>
public sealed class WorkflowStartAdmissionReaderTests
{
    [Fact]
    public async Task StartPolicyAndCommandDoNotRequireAllocatingAnInstance()
    {
        var f = new Facts();
        var first = await f.Read(); Assert.NotNull(first);
        var deadline = f.Now.AddHours(1);
        var original = first.BindCommand(deadline, "synthetic-key");
        Assert.Equal(f.Selection.TenantId, original.TenantId);
        Assert.Equal(f.Session.PrincipalId, original.ActorId);
        f.Now = f.Now.AddSeconds(1);
        var next = await f.Read(); Assert.NotNull(next);
        Assert.True(first.IsCurrentSuccessor(next));
        var retry = next.BindCommand(deadline, "synthetic-key");
        Assert.Equal(original.BusinessHash, retry.BusinessHash);
        Assert.Equal(original.ScopedKeyHash, retry.ScopedKeyHash);
        Assert.Equal(f.ApprovalId, next.Eligibility.ApprovalId);
        Assert.Equal("workflow.start", f.RequestedAction);
    }

    [Fact]
    public async Task MutationAuditPinsTheRetainedAdmissionAndFreshOwner()
    {
        var f = new Facts(); var admission = await f.Read(); Assert.NotNull(admission);
        var command = admission.BindCommand(f.Now.AddHours(1), "synthetic-key");
        var spec = Specification(f);
        Assert.True(admission.MatchesSpecification(spec, command));
        var auditId = Guid.NewGuid(); var audit = admission.CreateMutationAudit(spec, command, auditId);
        Assert.Equal(auditId, audit.IntentId); Assert.Equal(f.Invocation, audit.InvocationId);
        Assert.Equal(AuditIntentStage.Mutation, audit.Stage); Assert.Equal(spec.InstanceId, audit.ResourceRef);
        Assert.Equal(f.Session.PrincipalId, audit.SubjectRef); Assert.Equal(f.Selection.EnvironmentId, audit.ScopeId);
        Assert.Equal(admission.Policy.Policy.PolicyRevision, audit.PolicyRevision);
        Assert.Equal(admission.Eligibility.WorkflowAuthority.Grant.AssignmentRevision, audit.AuthorityRevision);
        Assert.Equal("workflow-start-" + command.BusinessHash, audit.SafeReason);
        Assert.Equal(f.Request, audit.CorrelationId); Assert.Equal(spec.CreatedAtUtc, audit.OccurredAtUtc);
        Assert.Throws<ArgumentException>(() => admission.CreateMutationAudit(spec, command, Guid.Empty));
    }

    [Theory]
    [InlineData("application")]
    [InlineData("target")]
    [InlineData("classification")]
    [InlineData("installation")]
    [InlineData("tenant")]
    [InlineData("release")]
    [InlineData("created-before")]
    [InlineData("created-expired")]
    public async Task FreshAllocationCannotSubstituteOwnerFieldsOrCreationWindow(string fault)
    {
        var f = new Facts(); var admission = await f.Read(); Assert.NotNull(admission);
        var command = admission.BindCommand(f.Now.AddHours(1), "synthetic-key");
        var spec = Specification(f, fault);
        Assert.False(admission.MatchesSpecification(spec, command));
        Assert.Throws<ArgumentException>(() => admission.CreateMutationAudit(spec, command, Guid.NewGuid()));
    }

    [Fact]
    public async Task OriginalReceiptOwnerBindingDoesNotAllocateAgainOrRequireANewCreationTime()
    {
        var f = new Facts(); var admission = await f.Read(); Assert.NotNull(admission);
        var command = admission.BindCommand(f.Now.AddHours(1), "synthetic-key");
        var original = Specification(f, "created-before");
        Assert.True(admission.MatchesOwnerBinding(original, command));
        Assert.False(admission.MatchesSpecification(original, command));
        // Receipt lookup can validate the historical owner, but cannot append a new mutation audit for it.
        Assert.Throws<ArgumentException>(() => admission.CreateMutationAudit(original, command, Guid.NewGuid()));
    }

    private static WorkflowSpecification Specification(Facts f, string? fault = null) => new(
        fault == "installation" ? Guid.NewGuid() : f.Installation,
        fault == "tenant" ? Guid.NewGuid() : f.Selection.TenantId,
        Guid.NewGuid(), f.Request, f.Selection.ProjectId,
        fault == "application" ? Guid.NewGuid() : f.Application,
        f.Selection.EnvironmentId, fault == "target" ? Guid.NewGuid() : f.Target,
        fault == "release" ? Guid.NewGuid() : f.Selection.ReleaseId,
        f.Selection.ReleaseRevision, f.Selection.ReleaseHash,
        fault == "classification" ? WorkflowEnvironment.Development : WorkflowEnvironment.Test,
        fault == "created-before" ? f.Now.AddMinutes(-1) : fault == "created-expired" ? f.Now.AddSeconds(10) : f.Now,
        f.Now.AddHours(1), f.Now.AddMinutes(3));

    [Theory]
    [InlineData("missing-eligibility")]
    [InlineData("workflow-grant")]
    [InlineData("release-grant")]
    [InlineData("binding")]
    [InlineData("policy-action")]
    [InlineData("policy-head")]
    [InlineData("policy-deny")]
    [InlineData("unbound-eligibility")]
    [InlineData("approval-expired")]
    [InlineData("installation")]
    [InlineData("selection")]
    [InlineData("approval")]
    [InlineData("release-version")]
    [InlineData("classification")]
    [InlineData("release-session")]
    [InlineData("owner-between-reads")]
    [InlineData("clock-between-reads")]
    [InlineData("unavailable-policy")]
    public async Task MissingStaleOrConflictingFactsDeny(string fault)
    {
        var f = new Facts();
        switch (fault)
        {
            case "missing-eligibility": f.Missing = true; break;
            case "workflow-grant": f.WorkflowGrant = null; break;
            case "release-grant": f.ReleaseGrant = null; break;
            case "binding": f.Binding = null; break;
            case "policy-action": f.Binding = f.Binding! with { Action = "workflow.read" }; break;
            case "policy-head": f.Policy = f.Policy with { Revision = 2 }; break;
            case "policy-deny":
                f.Policy = f.Policy with { Payload = f.Policy.Payload with { Rules = [new("Test", "deny")] } };
                f.Policy = f.Policy with { ContentHash = ResourcePolicies.Hash(f.Policy.Payload) }; break;
            case "unbound-eligibility": f.Transform = x => x with { InstallationId = Guid.Empty, Selection = null }; break;
            case "approval-expired": f.Until = f.Now; break;
            case "installation": f.Transform = x => x with { InstallationId = Guid.NewGuid() }; break;
            case "selection": f.Transform = x => x with { Selection = f.Selection with { ReleaseId = Guid.NewGuid() } }; break;
            case "approval": f.Transform = x => x with { ApprovalVersion = 0 }; break;
            case "release-version": f.Transform = x => x with { ReleaseOwnerVersion = 0 }; break;
            case "classification": f.Transform = x => x with { Classification = WorkflowEnvironment.Development }; break;
            case "release-session": f.ReleaseSubject = f.Subject with { SessionId = Guid.NewGuid() }; break;
            case "owner-between-reads": f.AfterEligibility = () => f.Owner = f.Owner with { EnvironmentRevision = 2 }; break;
            case "clock-between-reads": f.AfterEligibility = () => f.Now = f.Now.AddSeconds(-1); break;
            case "unavailable-policy": f.ThrowPolicy = true; break;
        }
        Assert.Null(await f.Read());
    }

    [Theory]
    [InlineData("approval")]
    [InlineData("release")]
    [InlineData("target")]
    [InlineData("application")]
    [InlineData("binding")]
    [InlineData("project")]
    [InlineData("environment")]
    [InlineData("principal-revision")]
    [InlineData("assignment")]
    [InlineData("request")]
    [InlineData("invocation")]
    public async Task CurrentEvidenceCannotSubstituteDifferentAcceptedFacts(string changed)
    {
        var f = new Facts(); var original = await f.Read(); Assert.NotNull(original);
        switch (changed)
        {
            case "approval": f.Transform = x => x with { ApprovalVersion = x.ApprovalVersion + 1 }; break;
            case "release": f.Transform = x => x with { ReleaseOwnerVersion = x.ReleaseOwnerVersion + 1 }; break;
            case "target": f.Transform = x => x with { TargetId = Guid.NewGuid() }; break;
            case "application": f.Transform = x => x with { ApplicationId = Guid.NewGuid() }; break;
            case "binding": f.Binding = f.Binding! with { BindingRevision = 2 }; break;
            case "project": f.Owner = f.Owner with { ProjectRevision = 2 }; break;
            case "environment": f.Owner = f.Owner with { EnvironmentRevision = 2 }; break;
            case "principal-revision": f.Subject = f.Subject with { PrincipalRevision = 2 }; break;
            case "assignment": f.WorkflowGrant = f.WorkflowGrant! with { AssignmentRevision = 2 }; break;
            case "request": f.Request = Guid.NewGuid(); break;
            case "invocation": f.Invocation = Guid.NewGuid(); break;
        }
        var next = await f.Read(); Assert.NotNull(next);
        Assert.False(original.IsCurrentSuccessor(next));
    }

    [Fact]
    public async Task FreshObservationNeverExtendsTheOriginalPolicyOrApprovalCap()
    {
        var f = new Facts(); var original = await f.Read(); Assert.NotNull(original);
        f.Now = original.ValidUntilUtc;
        var fresh = await f.Read(); Assert.NotNull(fresh);
        Assert.True(fresh.ValidUntilUtc > original.ValidUntilUtc);
        Assert.False(original.IsCurrentSuccessor(fresh));
        var approval = new Facts(); approval.Until = approval.Now.AddSeconds(2);
        var shortLived = await approval.Read(); Assert.NotNull(shortLived);
        approval.Now = approval.Now.AddSeconds(3); approval.Until = approval.Now.AddHours(1);
        var renewed = await approval.Read(); Assert.NotNull(renewed);
        Assert.False(shortLived.IsCurrentSuccessor(renewed));
    }

    [Fact]
    public async Task InvalidSelectionAndCancellationDoNotReadOwners()
    {
        var f = new Facts(); f.Selection = f.Selection with { ReleaseHash = "invalid" };
        Assert.Null(await f.Read()); Assert.Equal(0, f.Reads);
        var valid = new Facts(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => valid.Read(cancelled.Token));
        Assert.Equal(0, valid.Reads);
    }

    private sealed class Facts : IWorkflowStartEligibilityReader, ICurrentSubjectReader, IScopedGrantReader,
        IRoleDefinitionReader, IReleaseProjectAuthorityReader, IApprovalEnvironmentAuthorityReader,
        IReleasePolicyEnvironmentReader, IActionPolicyBindingReader, ICurrentResourcePolicyReader, IAuthorityClock
    {
        public readonly Guid Installation = Guid.NewGuid(), ApprovalId = Guid.NewGuid(), Application = Guid.NewGuid(), Target = Guid.NewGuid();
        public Guid Request = Guid.NewGuid(), Invocation = Guid.NewGuid();
        public WorkflowStartSelection Selection = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 1, new string('a', 64), WorkflowSpecification.TemplateId, WorkflowSpecification.TemplateVersion);
        public readonly SessionSelection Session = new(Guid.NewGuid(), new string('a', 43), Guid.NewGuid());
        public DateTimeOffset Now = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Until;
        public CurrentSubject Subject; public CurrentSubject? ReleaseSubject;
        public ScopedGrant? WorkflowGrant, ReleaseGrant;
        public ReleasePolicyEnvironment Owner;
        public PolicyBinding? Binding; public ResourcePolicySnapshot Policy;
        public int Reads; public bool Missing, ThrowPolicy; public string? RequestedAction;
        public Func<WorkflowStartEligibility, WorkflowStartEligibility> Transform = x => x;
        public Action? AfterEligibility;
        private readonly WorkflowStartAdmissionReader reader;
        private readonly WorkflowEnvironmentAuthorityReader workflows;
        private readonly ReleaseProjectAuthorityReader releases;

        public Facts()
        {
            Until = Now.AddHours(1);
            Subject = new(Session.PrincipalId, Guid.NewGuid(), Session.BootGeneration, 1, Until, Until);
            WorkflowGrant = Grant("workflow.start", "environment", Selection.EnvironmentId);
            ReleaseGrant = Grant("release.read", "project", Selection.ProjectId);
            Owner = new(Selection.TenantId, Selection.ProjectId, 1, Selection.EnvironmentId, 1, "Test");
            var payload = new ResourcePolicyPayload(2, "workflow.start", "environment", "active", Now.AddMinutes(-1), null, [new("Test", "allow")]);
            Policy = new(Guid.NewGuid(), Selection.TenantId, Selection.ProjectId, 1, payload, ResourcePolicies.Hash(payload));
            Binding = new(Guid.NewGuid(), Selection.TenantId, Selection.ProjectId, Selection.EnvironmentId,
                "environment", "workflow.start", Policy.PolicyId, 1, 1);
            workflows = new(this, this, this, this, this, this);
            releases = new(new ReleaseSubjects(this), this, this, this, this);
            reader = new(Installation, this, new(this, this, this, this), this);
        }
        private ScopedGrant Grant(string permission, string kind, Guid scope) => new(Session.PrincipalId,
            Selection.TenantId, Guid.NewGuid(), 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(), 1, permission, kind, scope, null);
        public Task<WorkflowStartAdmission?> Read(CancellationToken ct = default) => reader.ReadUnderAdmissionAsync(Session, Selection, Request, Invocation, ct);
        public async Task<WorkflowStartEligibility?> ReadUnderAdmissionAsync(SessionSelection session, WorkflowStartSelection selected, Guid invocation, CancellationToken ct = default)
        {
            Reads++; if (Missing) return null;
            var workflow = await workflows.ReadCurrentUnderAdmissionAsync(session, selected.TenantId, selected.ProjectId, selected.EnvironmentId, "workflow.start", ct);
            var release = await releases.ReadCurrentUnderAdmissionAsync(session, selected.ProjectId, "release.read", invocation, ct);
            if (workflow is null || release is null) return null;
            var facts = Transform(new(workflow, release, ApprovalId, 1, Application, Target, 1, WorkflowEnvironment.Test, Until, Installation, selected));
            AfterEligibility?.Invoke(); return facts;
        }
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default) => Task.FromResult<CurrentSubject?>(Subject);
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission, string kind, Guid id, CancellationToken ct = default) =>
            Task.FromResult(permission == "workflow.start" ? WorkflowGrant : ReleaseGrant);
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken ct = default) =>
            Task.FromResult<RoleDefinitionSnapshot?>(new(id, revision, "Synthetic", [new("workflow.start", "environment"), new("release.read", "project")]));
        public Task<bool> IsActivePrincipalAsync(Guid principal, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid project, CancellationToken ct = default) =>
            Task.FromResult<ReleaseProjectAuthority?>(new(Owner.TenantId, Owner.ProjectId, Owner.ProjectRevision));
        Task<ApprovalEnvironmentAuthority?> IApprovalEnvironmentAuthorityReader.ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid environment, CancellationToken ct) =>
            Task.FromResult<ApprovalEnvironmentAuthority?>(new(Owner.TenantId, Owner.ProjectId, Owner.EnvironmentId, Owner.EnvironmentRevision));
        public Task<ReleasePolicyEnvironment?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid environment, CancellationToken ct = default) => Task.FromResult<ReleasePolicyEnvironment?>(Owner);
        public Task<PolicyBinding?> ReadCurrentForActionUnderAdmissionAsync(Guid tenant, Guid project, Guid? environment, string action, CancellationToken ct = default)
        { RequestedAction = action; return Task.FromResult(Binding); }
        public Task<ResourcePolicySnapshot?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid policy, long revision, CancellationToken ct = default) =>
            ThrowPolicy ? throw new InvalidOperationException("Synthetic unavailable policy.") : Task.FromResult<ResourcePolicySnapshot?>(Policy);
        public DateTimeOffset GetTrustedUtcNow() => Now;
        private sealed class ReleaseSubjects(Facts owner) : ICurrentSubjectReader
        {
            public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection session, CancellationToken ct = default) =>
                Task.FromResult<CurrentSubject?>(owner.ReleaseSubject ?? owner.Subject);
        }
    }
}
