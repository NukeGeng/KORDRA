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
    public async Task Artifact_and_legacy_inventory_share_one_sixteen_slot_capacity()
    {
        var f = new Fixture();
        var reserved = Enumerable.Range(0, 15).Select(_ => ProtectedInvocationCapacity.TryReserve(f.Gate)).ToArray();
        Assert.All(reserved, item => Assert.NotNull(item));
        var started = Signal();
        var release = new TaskCompletionSource<PrivateReadBuffer>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnRead = (_, _) => { started.TrySetResult(); return release.Task; };
        try
        {
            var inventory = f.Execute();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Null(ProtectedInvocationCapacity.TryReserve(f.Gate));
            Assert.Equal(ProtectedReadStatus.Unavailable, await new Fixture(f.Gate).Execute());
            release.TrySetResult(new("[]"u8, 0));
            Assert.Equal(ProtectedReadStatus.Completed, await inventory);
            using var available = ProtectedInvocationCapacity.TryReserve(f.Gate);
            Assert.NotNull(available);
            available.Dispose(); available.Dispose(); // one slot is released only once
            using var independent = ProtectedInvocationCapacity.TryReserve(new FairAsyncReaderWriterAdmissionGate());
            Assert.NotNull(independent);
        }
        finally
        {
            release.TrySetResult(new("[]"u8, 0));
            foreach (var lease in reserved) lease?.Dispose();
        }
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

    [Fact]
    public async Task Result_deadline_is_checked_before_D_and_again_after_durable_audit()
    {
        foreach (var duringAudit in new[] { false, true })
        {
            var f = new Fixture(); var cap = f.Now.AddSeconds(15);
            f.OnRead = (_, _) => { if (!duringAudit) f.Now = cap; return Task.FromResult(new PrivateReadBuffer("[]"u8, 0, cap)); };
            if (duringAudit) f.OnAudit = c => { if (c.Stage == AuthorityStage.Disclosure) f.Now = cap; return Task.FromResult(AuthorityCommitStatus.Durable); };
            Assert.Equal(ProtectedReadStatus.Unavailable, await f.Execute()); Assert.Equal(0, f.Outputs);
            Assert.Equal(duringAudit ? 2 : 1, f.Intents.Count);
        }
    }

    [Fact]
    public async Task A_fresh_cap_does_not_extend_session_expiry_and_legacy_buffer_has_no_cap()
    {
        Assert.Null(new PrivateReadBuffer("[]"u8, 0).DisclosureValidUntilUtc);
        var f = new Fixture(); var done = new TaskCompletionSource<PrivateReadBuffer>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnRead = (_, _) => done.Task;
        var executing = f.Execute(); f.Now = f.Subject!.IdleExpiresAtUtc;
        done.SetResult(new PrivateReadBuffer("[]"u8, 0, f.Now.AddMinutes(1)));
        Assert.Equal(ProtectedReadStatus.Denied, await executing); Assert.Equal(0, f.Outputs);
    }

    [Fact]
    public void Result_cap_requires_a_finite_UTC_timestamp()
    {
        foreach (var bad in new[] { default(DateTimeOffset), DateTimeOffset.MaxValue, new DateTimeOffset(2026,10,24,0,0,0,TimeSpan.FromHours(1)) })
            Assert.Throws<InvalidDataException>(() => new PrivateReadBuffer("[]"u8,0,bad));
    }

    [Fact]
    public async Task Configured_plan_binds_exact_owner_scope_and_retains_the_original_SEC_digest()
    {
        var f = new Fixture(); var plan = ProtectedReadPlan.Credential(Guid.NewGuid()) with { ConsumerConfigurationDigest = new string('c',64) };
        Assert.Equal(ProtectedReadStatus.Completed, await f.ExecuteCredential(plan));
        Assert.All(f.DependencySelections, x => { Assert.Equal("project", x.ScopeKind); Assert.Equal(f.Target.ProjectId,x.ScopeId); });
        Assert.Equal(new string('b',64), f.ConsumedDependency!.Digest);
        Assert.Equal(3,f.Intents.Count); Assert.Single(f.Intents.Select(x => x.BindingDigest).Distinct());
    }

    [Theory]
    [InlineData("scope")][InlineData("scope-id")][InlineData("tenant")][InlineData("target")][InlineData("digest")]
    public async Task Dependency_with_foreign_scope_or_config_cannot_admit_a_consumer(string mismatch)
    {
        var f = new Fixture(); var plan = ProtectedReadPlan.Credential(Guid.NewGuid()) with { ConsumerConfigurationDigest = new string('c',64) };
        f.OnDependency = d => d with { Selection = mismatch switch
        {
            "scope" => d.Selection with { ScopeKind = "environment" },
            "scope-id" => d.Selection with { ScopeId = Guid.NewGuid() },
            "tenant" => d.Selection with { TenantId = Guid.NewGuid() },
            "target" => d.Selection with { TargetId = Guid.NewGuid() },
            _ => d.Selection with { Plan = plan with { ConsumerConfigurationDigest = new string('d',64) } }
        }};
        Assert.Equal(ProtectedReadStatus.Denied,await f.ExecuteCredential(plan)); Assert.Equal(0,f.Reads); Assert.Empty(f.Intents);
    }

    [Fact]
    public async Task Config_changes_affect_authority_hash_while_legacy_credential_v2_shape_is_preserved()
    {
        var f = new Fixture(); var plan = ProtectedReadPlan.Credential(Guid.Parse("00000001-0000-0000-0000-000000000000"));
        Assert.Equal(ProtectedReadStatus.Completed,await f.ExecuteCredential(plan));
        Assert.All(f.DependencySelections,x => {Assert.Null(x.ScopeKind);Assert.Null(x.ScopeId);});
        var dependency=f.ConsumedDependency!;
        var authority=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {f.Subject!.PrincipalId,f.Subject.SessionId,f.Subject.BootGeneration,
            f.Subject.PrincipalRevision,f.Subject.AbsoluteExpiresAtUtc,Grant=f.Grant! with {ExpiresAtUtc=null},target=f.Target,Policy=f.Policy! with {ExpiresAtUtc=null}});
        var oldPlan=new {plan.Kind,plan.InstallationId,plan.ConsumerId,plan.IsCredential,plan.IsValid};
        var oldDependency=new {dependency.Kind,Selection=new {Plan=oldPlan,dependency.Selection.TenantId,dependency.Selection.TargetId},
            dependency.BindingId,dependency.Revision,dependency.Digest,dependency.NotBeforeUtc,dependency.ExpiresAtUtc};
        var expected=System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {schemaVersion=2,
            authorityDigest=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(authority)),plan=oldPlan,dependency=oldDependency}));
        Assert.Equal(Convert.ToHexStringLower(expected), f.Intents[0].BindingDigest);
        var legacyHash=f.Intents[0].BindingDigest;f.Intents.Clear();
        Assert.Equal(ProtectedReadStatus.Completed,await f.ExecuteCredential(plan with {ConsumerConfigurationDigest=new string('c',64)}));
        var configuredHash=f.Intents[0].BindingDigest;Assert.NotEqual(legacyHash,configuredHash);f.Intents.Clear();
        Assert.Equal(ProtectedReadStatus.Completed,await f.ExecuteCredential(plan with {ConsumerConfigurationDigest=new string('d',64)}));
        Assert.NotEqual(configuredHash,f.Intents[0].BindingDigest);
    }

    [Theory]
    [InlineData("")][InlineData("not-a-hash")][InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Invalid_config_digest_or_metadata_provider_plan_never_starts_a_read(string digest)
    {
        var f=new Fixture();var plan=ProtectedReadPlan.Credential(Guid.NewGuid()) with {ConsumerConfigurationDigest=digest};
        Assert.False(plan.IsValid);Assert.Equal(ProtectedReadStatus.Denied,await f.ExecuteCredential(plan));Assert.Empty(f.Intents);
        Assert.False((ProtectedReadPlan.Metadata with {ConsumerConfigurationDigest=new string('a',64)}).IsValid);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task AssertClosed(FairAsyncReaderWriterAdmissionGate gate) =>
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var lease = await gate.AcquireReadAsync(); });

    private sealed class Fixture : ICurrentSubjectReader, IScopedGrantReader, IRoleDefinitionReader, ITargetAuthorityReader, IResourcePolicyReader,
        IAuthorityClock, IAuthorityCommitter, IProtectedTargetRead, ICallerBoundResponseCommitter, IProtectedReadDependencyReader
    {
        public FairAsyncReaderWriterAdmissionGate Gate = new();
        public DateTimeOffset Now = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        public CurrentSubject? Subject; public ScopedGrant? Grant; public RoleDefinitionSnapshot? Role;
        public TargetAuthority Target; public ResourcePolicyAuthority? Policy;
        public readonly List<AuthorityCommitment> Intents = []; public readonly List<TypedReadSelection> Selections = [];
        public int Reads, Outputs, Aborts;
        public readonly List<ReadDependencySelection> DependencySelections = [];
        public ReadDependencySnapshot? ConsumedDependency;
        public Func<ReadDependencySnapshot,ReadDependencySnapshot>? OnDependency;
        private readonly Guid _bindingId=Guid.NewGuid();
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
            _sut = new(Gate, this, this, this, this, this, this, this, this);
        }
        private readonly Guid _target;
        public Task<ProtectedReadStatus> Execute(CancellationToken ct = default) => _sut.ExecuteAsync(_selection, _target, this, this, ct);
        public Task<ProtectedReadStatus> ExecuteCredential(ProtectedReadPlan plan) => _sut.ExecuteCredentialAsync(_selection,_target,new CredentialRead(this,plan),this);
        public Task<ReadDependencySnapshot?> ReadCurrentUnderAdmissionAsync(ReadDependencySelection selected,CancellationToken ct=default)
        {
            DependencySelections.Add(selected);
            var d=new ReadDependencySnapshot("credential-binding",selected,_bindingId,1,new string('b',64),Now.AddDays(-1),Now.AddHours(1));
            return Task.FromResult<ReadDependencySnapshot?>(OnDependency?.Invoke(d)??d);
        }
        private sealed class CredentialRead(Fixture f,ProtectedReadPlan plan):IProtectedCredentialRead,IProtectedCredentialReadReservation
        {
            public ProtectedReadPlan Plan=>plan;
            public IProtectedCredentialReadReservation TryReserve()=>this;
            public Task<PrivateReadBuffer> Admit(TypedReadSelection selection,ReadDependencySnapshot dependency,IProtectedUseBoundary use,CancellationToken ct)
            {f.ConsumedDependency=dependency;return use.RunAsync(token=>f.Admit(selection,token),ct);}
            public void Dispose(){}
        }
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
