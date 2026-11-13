using Kordra.Api;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>12.2f Host binding of current start admission to an existing immutable workflow; offline, no IO.</summary>
public sealed class WorkflowReauthorizationAdmissionTests
{
    [Fact]
    public async Task CurrentStartAdmissionBindsOnlyTheExactOriginalWorkflowActorAndRequest()
    {
        var f = new Facts(); var admission = await f.Read(); Assert.NotNull(admission);
        var spec = f.Specification(); var command = f.Command(spec);
        Assert.True(WorkflowReauthorizationAdmission.Binds(admission, spec, command, f.Session));
        Assert.Equal(f.Selection, WorkflowReauthorizationAdmission.SelectionFor(spec, f.Selection.ReleaseRevisionId));

        var audit = WorkflowReauthorizationAdmission.CreateMutationAudit(admission!, spec, command, f.Session, Guid.NewGuid());
        Assert.Equal(AuditIntentStage.Mutation, audit.Stage); Assert.Equal("workflow.start", audit.Permission);
        Assert.Equal("environment", audit.ScopeKind); Assert.Equal(spec.EnvironmentId, audit.ScopeId);
        Assert.Equal(spec.InstanceId, audit.ResourceRef); Assert.Equal(command.RequestId, audit.CorrelationId);
        Assert.Equal("workflow-reauthorize-" + command.CommandHash, audit.SafeReason);
        Assert.Equal(admission!.InvocationId, audit.InvocationId);
    }

    [Theory]
    [InlineData("actor")][InlineData("session")][InlineData("request")][InlineData("tenant")][InlineData("project")]
    [InlineData("environment")][InlineData("release-hash")][InlineData("release-revision")][InlineData("application")]
    [InlineData("target")][InlineData("specification")][InlineData("installation")]
    public async Task AnyDifferentFactRejectsBindingAndAudit(string fault)
    {
        var f = new Facts(); var admission = await f.Read(); Assert.NotNull(admission);
        var spec = f.Specification(fault); var command = f.Command(spec, fault);
        var session = fault == "session" ? new SessionSelection(Guid.NewGuid(), new string('a', 43), f.Session.BootGeneration) : f.Session;
        Assert.False(WorkflowReauthorizationAdmission.Binds(admission, spec, command, session));
        Assert.Throws<ArgumentException>(() => WorkflowReauthorizationAdmission.CreateMutationAudit(admission!, spec, command, session, Guid.NewGuid()));
    }

    [Fact]
    public void MissingAdmissionNeverBinds()
    {
        var f = new Facts(); var spec = f.Specification();
        Assert.False(WorkflowReauthorizationAdmission.Binds(null, spec, f.Command(spec), f.Session));
    }

    [Fact]
    public async Task TransactionRejectsInexactMutationAuditBeforeAnyDatabaseUse()
    {
        var f = new Facts(); var spec = f.Specification(); var command = f.Command(spec);
        await using var connection = new Npgsql.NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        var exact = new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation, command.ActorId, command.TenantId,
            command.InstanceId, "workflow.start", "environment", spec.EnvironmentId, 1, 1, "workflow-reauthorize-" + command.CommandHash,
            f.Now, command.RequestId);
        foreach (var mutation in new[] { exact with { Permission = "workflow.cancel" }, exact with { SubjectRef = Guid.NewGuid() },
                     exact with { SafeReason = "workflow-cancel-" + command.CommandHash }, exact with { ScopeId = Guid.NewGuid() },
                     exact with { CorrelationId = Guid.NewGuid() }, exact with { PolicyRevision = null } })
            await Assert.ThrowsAsync<InvalidOperationException>(() => WorkflowReauthorizationTransaction.ExecuteIsolatedAsync(connection, null!,
                command.InstallationId, command, spec.ProjectId, spec.EnvironmentId, mutation, new(), _ => Task.FromResult(true), new DenyReissuer()));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void NoProductionTypeImplementsTheDelegationPortOrRegistersIt()
    {
        var api = typeof(IWorkflowDelegationReissuer).Assembly;
        Assert.DoesNotContain(api.GetTypes(), t => !t.IsInterface && typeof(IWorkflowDelegationReissuer).IsAssignableFrom(t));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kordra.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var sources = Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("<IWorkflowDelegationReissuer", text);
            Assert.DoesNotContain("WorkflowReauthorizationTransaction", Path.GetFileName(file) == "WorkflowReauthorizationTransaction.cs" ? "" : text);
        }
    }

    private sealed class DenyReissuer : IWorkflowDelegationReissuer
    {
        public Task<Kordra.Modules.WorkflowOrchestration.Contracts.WorkflowDelegationReference?> StageReissueAsync(
            System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction root, WorkflowDelegationReissueRequest request,
            CancellationToken ct = default) => throw new InvalidOperationException("Port must not be reached.");
    }

    /// <summary>Synthetic current owner facts (pattern of WorkflowStartAdmissionReaderTests); grants nothing.</summary>
    private sealed class Facts : IWorkflowStartEligibilityReader, ICurrentSubjectReader, IScopedGrantReader,
        IRoleDefinitionReader, IReleaseProjectAuthorityReader, IApprovalEnvironmentAuthorityReader,
        IReleasePolicyEnvironmentReader, IActionPolicyBindingReader, ICurrentResourcePolicyReader, IAuthorityClock
    {
        public readonly Guid Installation = Guid.NewGuid(), ApprovalId = Guid.NewGuid(), Application = Guid.NewGuid(), Target = Guid.NewGuid();
        public readonly Guid Request = Guid.NewGuid(), Invocation = Guid.NewGuid(), Instance = Guid.NewGuid(), OriginalRequest = Guid.NewGuid();
        public readonly WorkflowStartSelection Selection = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 3, new string('a', 64), WorkflowSpecification.TemplateId, WorkflowSpecification.TemplateVersion);
        public readonly SessionSelection Session = new(Guid.NewGuid(), new string('a', 43), Guid.NewGuid());
        public readonly DateTimeOffset Now = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        private readonly DateTimeOffset until;
        private readonly CurrentSubject subject;
        private readonly ReleasePolicyEnvironment owner;
        private readonly PolicyBinding binding; private readonly ResourcePolicySnapshot policy;
        private readonly WorkflowStartAdmissionReader reader;
        private readonly WorkflowEnvironmentAuthorityReader workflows;
        private readonly ReleaseProjectAuthorityReader releases;

        public Facts()
        {
            until = Now.AddHours(1);
            subject = new(Session.PrincipalId, Guid.NewGuid(), Session.BootGeneration, 1, until, until);
            owner = new(Selection.TenantId, Selection.ProjectId, 1, Selection.EnvironmentId, 1, "Test");
            var payload = new ResourcePolicyPayload(2, "workflow.start", "environment", "active", Now.AddMinutes(-1), null, [new("Test", "allow")]);
            policy = new(Guid.NewGuid(), Selection.TenantId, Selection.ProjectId, 1, payload, ResourcePolicies.Hash(payload));
            binding = new(Guid.NewGuid(), Selection.TenantId, Selection.ProjectId, Selection.EnvironmentId, "environment", "workflow.start", policy.PolicyId, 1, 1);
            workflows = new(this, this, this, this, this, this);
            releases = new(new ReleaseSubjects(this), this, this, this, this);
            reader = new(Installation, this, new(this, this, this, this), this);
        }

        public Task<WorkflowStartAdmission?> Read() => reader.ReadUnderAdmissionAsync(Session, Selection, Request, Invocation);

        public WorkflowSpecification Specification(string? fault = null) => new(
            fault == "installation" ? Guid.NewGuid() : Installation, fault == "tenant" ? Guid.NewGuid() : Selection.TenantId, Instance,
            OriginalRequest, fault == "project" ? Guid.NewGuid() : Selection.ProjectId, fault == "application" ? Guid.NewGuid() : Application,
            fault == "environment" ? Guid.NewGuid() : Selection.EnvironmentId, fault == "target" ? Guid.NewGuid() : Target,
            Selection.ReleaseId, fault == "release-revision" ? 4 : Selection.ReleaseRevision,
            fault == "release-hash" ? new string('b', 64) : Selection.ReleaseHash, WorkflowEnvironment.Test, Now, Now.AddHours(2), Now.AddMinutes(3));

        public WorkflowReauthorizationCommand Command(WorkflowSpecification spec, string? fault = null) => new(spec.InstallationId, spec.TenantId,
            spec.InstanceId, fault == "request" ? Guid.NewGuid() : Request, fault == "actor" ? Guid.NewGuid() : Session.PrincipalId, 4,
            fault == "specification" ? new string('c', 64) : spec.SpecificationHash);

        public async Task<WorkflowStartEligibility?> ReadUnderAdmissionAsync(SessionSelection session, WorkflowStartSelection selected, Guid invocation, CancellationToken ct = default)
        {
            var workflow = await workflows.ReadCurrentUnderAdmissionAsync(session, selected.TenantId, selected.ProjectId, selected.EnvironmentId, "workflow.start", ct);
            var release = await releases.ReadCurrentUnderAdmissionAsync(session, selected.ProjectId, "release.read", invocation, ct);
            return workflow is null || release is null ? null
                : new(workflow, release, ApprovalId, 1, Application, Target, 1, WorkflowEnvironment.Test, until, Installation, selected);
        }
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default) => Task.FromResult<CurrentSubject?>(subject);
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission, string kind, Guid id, CancellationToken ct = default) =>
            Task.FromResult<ScopedGrant?>(new(Session.PrincipalId, Selection.TenantId, Guid.NewGuid(), 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(), 1,
                permission, kind, permission == "workflow.start" ? Selection.EnvironmentId : Selection.ProjectId, null));
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken ct = default) =>
            Task.FromResult<RoleDefinitionSnapshot?>(new(id, revision, "Synthetic", [new("workflow.start", "environment"), new("release.read", "project")]));
        public Task<bool> IsActivePrincipalAsync(Guid principal, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid project, CancellationToken ct = default) =>
            Task.FromResult<ReleaseProjectAuthority?>(new(owner.TenantId, owner.ProjectId, owner.ProjectRevision));
        Task<ApprovalEnvironmentAuthority?> IApprovalEnvironmentAuthorityReader.ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid environment, CancellationToken ct) =>
            Task.FromResult<ApprovalEnvironmentAuthority?>(new(owner.TenantId, owner.ProjectId, owner.EnvironmentId, owner.EnvironmentRevision));
        public Task<ReleasePolicyEnvironment?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid environment, CancellationToken ct = default) =>
            Task.FromResult<ReleasePolicyEnvironment?>(owner);
        public Task<PolicyBinding?> ReadCurrentForActionUnderAdmissionAsync(Guid tenant, Guid project, Guid? environment, string action, CancellationToken ct = default) =>
            Task.FromResult<PolicyBinding?>(binding);
        public Task<ResourcePolicySnapshot?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid policyId, long revision, CancellationToken ct = default) =>
            Task.FromResult<ResourcePolicySnapshot?>(policy);
        public DateTimeOffset GetTrustedUtcNow() => Now;
        private sealed class ReleaseSubjects(Facts facts) : ICurrentSubjectReader
        {
            public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection session, CancellationToken ct = default) =>
                Task.FromResult<CurrentSubject?>(facts.subject);
        }
    }
}
