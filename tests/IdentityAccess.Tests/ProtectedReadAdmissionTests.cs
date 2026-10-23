using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class ProtectedReadAdmissionTests
{
    [Fact]
    public async Task One_typed_read_and_exact_result_bound_distinct_U_D_intents_precede_only_caller_handoff()
    {
        var f = new Fixture(); var result = await f.Execute();
        Assert.Equal(ProtectedReadStatus.Completed, result); Assert.Equal(1, f.Reads); Assert.Equal(1, f.Outputs);
        Assert.Equal(new[] { AuthorityStage.Use, AuthorityStage.Disclosure }, f.Intents.Select(x => x.Stage));
        Assert.Equal(f.Intents[0].InvocationId, f.Intents[1].InvocationId); Assert.NotEqual(f.Intents[0].IntentId, f.Intents[1].IntentId);
        Assert.Null(f.Intents[0].ResultDigest); Assert.Equal(64, f.Intents[1].ResultDigest!.Length); Assert.Equal(2, f.Intents[1].ResultSize);
        Assert.Equal(f.Target.TargetId, f.Selections.Single().TargetId); Assert.Equal(f.Target.TenantId, f.Selections.Single().TenantId);
        Assert.Equal(f.Intents[0].BindingDigest, f.Intents[1].BindingDigest);
    }

    [Theory]
    [InlineData("subject")][InlineData("session")][InlineData("principal-revision")]
    [InlineData("tenant")][InlineData("membership")][InlineData("assignment")][InlineData("definition")]
    [InlineData("permission")][InlineData("scope")][InlineData("target")][InlineData("project")]
    [InlineData("association")][InlineData("environment-absence")][InlineData("expired-session")]
    [InlineData("expired-grant")][InlineData("role-deny")][InlineData("policy-missing")]
    [InlineData("policy-deny")][InlineData("policy-incomplete")][InlineData("policy-conditions")]
    [InlineData("policy-tenant")][InlineData("policy-action")]
    public async Task Missing_mismatched_expired_or_denying_owner_facts_do_not_admit_or_disclose(string fault)
    {
        var f = new Fixture();
        switch (fault)
        {
            case "subject": f.Subject = null; break;
            case "session": f.Subject = f.Subject! with { SessionId = Guid.Empty }; break;
            case "principal-revision": f.Subject = f.Subject! with { PrincipalRevision = 0 }; break;
            case "tenant": f.Grant = f.Grant! with { TenantId = Guid.NewGuid() }; break;
            case "membership": f.Grant = f.Grant! with { MembershipId = Guid.Empty }; break;
            case "assignment": f.Grant = f.Grant! with { AssignmentRevision = 0 }; break;
            case "definition": f.Role = f.Role! with { Revision = 99 }; break;
            case "permission": f.Grant = f.Grant! with { Permission = "target.manage" }; break;
            case "scope": f.Grant = f.Grant! with { ScopeId = Guid.NewGuid() }; break;
            case "target": f.Target = f.Target with { TargetId = Guid.NewGuid() }; break;
            case "project": f.Target = f.Target with { ProjectRevision = 0 }; break;
            case "association": f.Target = f.Target with { AssociationRevision = 0 }; break;
            case "environment-absence": f.Target = f.Target with { Classification = "Production" }; break;
            case "expired-session": f.Subject = f.Subject! with { IdleExpiresAtUtc = f.Now }; break;
            case "expired-grant": f.Grant = f.Grant! with { ExpiresAtUtc = f.Now }; break;
            case "role-deny": f.Role = f.Role! with { Rules = [new("target.read", "project"), new("target.read", "project", "deny")] }; break;
            case "policy-missing": f.Policy = null; break;
            case "policy-deny": f.Policy = f.Policy! with { Decision = ResourceEligibility.Deny }; break;
            case "policy-incomplete": f.Policy = f.Policy! with { BindingId = Guid.Empty }; break;
            case "policy-conditions": f.Policy = f.Policy! with { Decision = ResourceEligibility.Unavailable }; break;
            case "policy-tenant": f.Policy = f.Policy! with { TenantId = Guid.NewGuid() }; break;
            case "policy-action": f.Policy = f.Policy! with { Permission = "target.manage" }; break;
        }
        Assert.Equal(ProtectedReadStatus.Denied, await f.Execute()); Assert.Equal(0, f.Reads); Assert.Equal(0, f.Outputs); Assert.Empty(f.Intents);
    }

    [Theory]
    [InlineData(AuthorityStage.Use)]
    [InlineData(AuthorityStage.Disclosure)]
    public async Task Expiry_is_rechecked_after_durable_audit_at_each_actual_commitment(AuthorityStage at)
    {
        var f = new Fixture(); f.OnAudit = c => { if (c.Stage == at) f.Now = f.Now.AddHours(1); return Task.FromResult(AuthorityCommitStatus.Durable); };
        Assert.Equal(ProtectedReadStatus.Unavailable, await f.Execute()); Assert.Equal(at == AuthorityStage.Use ? 0 : 1, f.Reads); Assert.Equal(0, f.Outputs);
    }

    [Theory]
    [InlineData("revoked")][InlineData("membership-revision")][InlineData("project-revision")][InlineData("policy-revision")]
    [InlineData("policy-identity")][InlineData("binding-identity")][InlineData("binding-change-back")][InlineData("policy-hash")]
    public async Task Intervening_owner_change_discards_previously_admitted_result_even_if_new_state_can_allow(string change)
    {
        var f = new Fixture(); var admitted = Signal(); var done = new TaskCompletionSource<PrivateReadBuffer>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnRead = (_, _) => { admitted.TrySetResult(); return done.Task; };
        var executing = f.Execute(); await admitted.Task;
        await using (var writer = await f.Gate.AcquireWriteAsync())
        {
            switch (change)
            {
                case "revoked": f.Grant = null; break;
                case "membership-revision": f.Grant = f.Grant! with { MembershipRevision = 3 }; break;
                case "project-revision": f.Target = f.Target with { ProjectRevision = 3 }; break;
                case "policy-revision": f.Policy = f.Policy! with { Revision = 3 }; break;
                case "policy-identity": f.Policy = f.Policy! with { PolicyId = Guid.NewGuid() }; break;
                case "binding-identity": f.Policy = f.Policy! with { BindingId = Guid.NewGuid() }; break;
                case "binding-change-back": f.Policy = f.Policy! with { BindingRevision = 3 }; break;
                case "policy-hash": f.Policy = f.Policy! with { ContentHash = new string('b', 64) }; break;
            }
            done.TrySetResult(new("[]"u8, 0));
            Assert.False(executing.IsCompleted); // D cannot overtake this invalidating writer
        }
        Assert.Equal(ProtectedReadStatus.Denied, await executing); Assert.Equal(1, f.Reads); Assert.Equal(0, f.Outputs);
        Assert.Single(f.Intents); // no D intent for invalid facts
    }

    [Fact]
    public async Task Earlier_D_holds_admission_until_transport_unwinds_before_writer_can_commit()
    {
        var f = new Fixture(); var accepted = Signal(); var release = Signal();
        f.OnWrite = async (_, _) => { accepted.TrySetResult(); await release.Task; };
        var executing = f.Execute(); await accepted.Task;
        var pendingWriter = f.Gate.AcquireWriteAsync().AsTask(); Assert.False(pendingWriter.IsCompleted);
        release.TrySetResult(); Assert.Equal(ProtectedReadStatus.Completed, await executing);
        await using var writer = await pendingWriter; f.Grant = null;
        Assert.Equal(1, f.Outputs);
    }

    [Fact]
    public async Task Accepted_D_can_finish_bounded_transport_after_time_expiry_without_retracting_already_handed_bytes()
    {
        var f = new Fixture(); f.OnWrite = (_, _) => { f.Now = f.Now.AddHours(1); return Task.CompletedTask; };
        Assert.Equal(ProtectedReadStatus.Completed, await f.Execute()); Assert.Equal(1, f.Outputs);
    }

    [Theory]
    [InlineData(AuthorityStage.Use, AuthorityCommitStatus.Unavailable)]
    [InlineData(AuthorityStage.Use, AuthorityCommitStatus.Unknown)]
    [InlineData(AuthorityStage.Disclosure, AuthorityCommitStatus.Rejected)]
    [InlineData(AuthorityStage.Disclosure, AuthorityCommitStatus.Unknown)]
    public async Task Audit_failure_never_authorizes_use_or_disclosure(AuthorityStage at, AuthorityCommitStatus outcome)
    {
        var f = new Fixture(); f.OnAudit = c => Task.FromResult(c.Stage == at ? outcome : AuthorityCommitStatus.Durable);
        Assert.Equal(ProtectedReadStatus.Unavailable, await f.Execute()); Assert.Equal(0, f.Outputs);
        if (outcome == AuthorityCommitStatus.Unknown) await AssertClosed(f.Gate);
        else { await using var available = await f.Gate.AcquireReadAsync(); }
    }

    [Fact]
    public async Task Hung_commit_is_bounded_and_closes_new_admissions_before_releasing_a_fence()
    {
        var f = new Fixture(); var stuck = new TaskCompletionSource<AuthorityCommitStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnAudit = _ => stuck.Task;
        Assert.Equal(ProtectedReadStatus.Unavailable, await f.Execute().WaitAsync(TimeSpan.FromSeconds(3)));
        await AssertClosed(f.Gate); Assert.True(f.Aborts > 0); Assert.Equal(0, f.Reads); Assert.Equal(0, f.Outputs);
        stuck.TrySetResult(AuthorityCommitStatus.Unknown);
    }

    [Fact]
    public async Task Sixteen_inflight_reads_are_the_capacity_and_seventeenth_is_not_admitted()
    {
        var f = new Fixture(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<PrivateReadBuffer>(TaskCreationOptions.RunContinuationsAsynchronously); var count = 0;
        f.OnRead = (_, _) => { if (Interlocked.Increment(ref count) == 16) started.TrySetResult(); return release.Task; };
        var active = Enumerable.Range(0, 16).Select(_ => f.Execute()).ToArray(); await started.Task;
        Assert.Equal(ProtectedReadStatus.Unavailable, await f.Execute()); Assert.Equal(16, f.Reads);
        var otherInstance = new Fixture(f.Gate);
        Assert.Equal(ProtectedReadStatus.Unavailable, await otherInstance.Execute()); Assert.Equal(0, otherInstance.Reads);
        release.TrySetResult(new("[]"u8, 0));
        Assert.All(await Task.WhenAll(active), r => Assert.Equal(ProtectedReadStatus.Completed, r)); Assert.Equal(16, f.Outputs);
    }

    [Fact]
    public async Task Cancellation_while_queued_for_admission_does_not_poison_shared_gate()
    {
        var f = new Fixture(); await using (var writer = await f.Gate.AcquireWriteAsync())
        {
            using var cancellation = new CancellationTokenSource(); var executing = f.Execute(cancellation.Token); cancellation.Cancel();
            Assert.Equal(ProtectedReadStatus.Cancelled, await executing); Assert.Equal(0, f.Reads);
        }
        await using var open = await f.Gate.AcquireReadAsync();
    }

    [Fact]
    public async Task Partial_transport_failure_is_unknown_aborted_and_never_replayed()
    {
        var f = new Fixture(); f.OnWrite = (_, _) => throw new IOException("Synthetic partial response failure.");
        Assert.Equal(ProtectedReadStatus.Unknown, await f.Execute()); Assert.Equal(1, f.Reads); Assert.Equal(1, f.Outputs); Assert.True(f.Aborts > 0);
        Assert.Equal(2, f.Intents.Count);
    }

    [Theory]
    [InlineData(262145, 1)][InlineData(1, 201)]
    public void Oversized_private_result_is_rejected_before_any_disclosure(int bytes, int records) =>
        Assert.Throws<InvalidDataException>(() => new PrivateReadBuffer(new byte[bytes], records));

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task AssertClosed(FairAsyncReaderWriterAdmissionGate gate) =>
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var lease = await gate.AcquireReadAsync(); });

    private sealed class Fixture : ICurrentSubjectReader, IScopedGrantReader, IRoleDefinitionReader, ITargetAuthorityReader, IResourcePolicyReader,
        IAuthorityClock, IAuthorityCommitter, IProtectedTargetRead, ICallerBoundResponseCommitter
    {
        public FairAsyncReaderWriterAdmissionGate Gate = new();
        public DateTimeOffset Now = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        public CurrentSubject? Subject; public ScopedGrant? Grant; public RoleDefinitionSnapshot? Role;
        public TargetAuthority Target; public ResourcePolicyAuthority? Policy;
        public readonly List<AuthorityCommitment> Intents = []; public readonly List<TypedReadSelection> Selections = [];
        public int Reads, Outputs, Aborts;
        public Func<AuthorityCommitment, Task<AuthorityCommitStatus>>? OnAudit;
        public Func<TypedReadSelection, CancellationToken, Task<PrivateReadBuffer>>? OnRead;
        public Func<ReadOnlyMemory<byte>, CancellationToken, Task>? OnWrite;
        private readonly ProtectedReadAdmission _sut;
        private readonly SessionSelection _selection;
        public Fixture(FairAsyncReaderWriterAdmissionGate? sharedGate = null)
        {
            if (sharedGate is not null) Gate = sharedGate;
            var principal = Guid.NewGuid(); var tenant = Guid.NewGuid(); var project = Guid.NewGuid(); var definition = Guid.NewGuid(); var boot = Guid.NewGuid();
            Subject = new(principal, Guid.NewGuid(), boot, 1, Now.AddHours(1), Now.AddMinutes(30));
            Grant = new(principal, tenant, Guid.NewGuid(), 1, 1, Guid.NewGuid(), 1, definition, 1, "target.read", "project", project, Now.AddMinutes(10));
            Role = new(definition, 1, "Admin is only a label", [new("target.read", "project")]);
            Target = new(Guid.NewGuid(), tenant, project, 1, 1, 1, null, null, null);
            Policy = new(tenant, project, null, "target.read", 1, Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), Now.AddMinutes(-1), null, ResourceEligibility.Allow);
            _selection = new(principal, "not-a-real-session-token", boot); _target = Target.TargetId;
            _sut = new(Gate, this, this, this, this, this, this, this);
        }
        private readonly Guid _target;
        public Task<ProtectedReadStatus> Execute(CancellationToken ct = default) => _sut.ExecuteAsync(_selection, _target, this, this, ct);
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default) => Task.FromResult(Subject);
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission, string scopeKind, Guid scopeId, CancellationToken ct = default) => Task.FromResult(Grant);
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid definitionId, long exactRevision, CancellationToken cancellationToken = default) => Task.FromResult(Role);
        public Task<bool> IsActivePrincipalAsync(Guid principalId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<TargetAuthority?> ReadTargetUnderAdmissionAsync(Guid target, CancellationToken ct = default) => Task.FromResult<TargetAuthority?>(Target);
        public Task<ResourcePolicyAuthority?> ReadPolicyUnderAdmissionAsync(TargetAuthority target, string permission, CancellationToken ct = default) => Task.FromResult(Policy);
        public DateTimeOffset GetTrustedUtcNow() => Now;
        public Task<AuthorityCommitStatus> CommitUnderAdmissionAsync(AuthorityCommitment c, CancellationToken ct = default)
        { Intents.Add(c); return OnAudit?.Invoke(c) ?? Task.FromResult(AuthorityCommitStatus.Durable); }
        public Task<PrivateReadBuffer> Admit(TypedReadSelection selection, CancellationToken ct)
        { Interlocked.Increment(ref Reads); Selections.Add(selection); return OnRead?.Invoke(selection, ct) ?? Task.FromResult(new PrivateReadBuffer("[]"u8, 0)); }
        public Task CommitAsync(TypedReadSelection selection, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        { Interlocked.Increment(ref Outputs); return OnWrite?.Invoke(bytes, ct) ?? Task.CompletedTask; }
        public void Abort() => Interlocked.Increment(ref Aborts);
    }
}
