using System.Data.Common;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.TenantManagement.Contracts;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.Modules.TenantManagement.Tests;

public sealed class TenantMembershipTests : IAsyncLifetime
{
    private PooledDbContextFactory<TenantDbContext> _ten = null!;
    private PooledDbContextFactory<KordraIdentityDbContext> _iam = null!;
    private readonly List<Guid> _tenants = [], _principals = [], _definitions = [];
    private readonly Subject _subject = new();
    private readonly Scopes _scopes = new();
    private readonly Clock _clock = new();
    private readonly OfflineLease _offline = new();
    private FairAsyncReaderWriterAdmissionGate _gate = new();
    private RoleDefinitionStore _roles = null!;
    private TenantMembershipService _service = null!;
    private Guid _founder, _recipient, _tenant, _founderMember, _recipientMember;
    private DefinitionWriteResult _organizationRole = null!;

    public async Task InitializeAsync()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private exact kordra_test locator is required; no database substituted.");
        _ten = new(TenantDbContextFactory.CreateTestOptions(locator)); // exact target/role guard before all connections
        _iam = new(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(locator,
            p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
        await using (var audit = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(locator))) await audit.Database.MigrateAsync();
        await using (var iam = await _iam.CreateDbContextAsync()) await iam.Database.MigrateAsync();
        await using (var ten = await _ten.CreateDbContextAsync()) await ten.Database.MigrateAsync();
        _roles = new(_iam, _gate, new IdentityAccessAuditTransactionAdapter());
        _service = Service();
        _founder = await Principal(); _recipient = await Principal();
        _subject.Principal = _founder;
        _organizationRole = await Role([new("org.read", "organization"), new("org.manage", "organization"), new("membership.assign-role", "organization")]);
        var initialized = await Initialize(_founder);
        _tenant = initialized.TenantId;
        await using var db = await _ten.CreateDbContextAsync();
        _founderMember = (await db.Memberships.SingleAsync(x => x.TenantId == _tenant && x.PrincipalId == _founder)).Id;
        var added = await _service.AddMemberOfflineAsync(_offline, Guid.NewGuid(), _tenant, _recipient, 1);
        Assert.Equal("success", added.Status); _recipientMember = added.ResourceId;
    }

    [Fact]
    public async Task Membership_selector_is_subject_bound_and_provisioned_members_have_no_implicit_permissions()
    {
        var second = await Initialize(await Principal());
        _subject.Principal = _recipient;
        var memberships = Assert.Single((await _service.MyMembershipsAsync())!);
        Assert.Equal(_tenant, memberships.TenantId);
        Assert.Null(await Effective(_recipient, "org.read", new("organization", _tenant)));
        Assert.Null(await Effective(_founder, "org.read", new("organization", second.TenantId), second.TenantId));
        _subject.Principal = null; Assert.Null(await _service.MyMembershipsAsync());
        _subject.Principal = Guid.NewGuid(); Assert.Null(await _service.MyMembershipsAsync());
    }

    [Fact]
    public async Task Initialization_manifest_replay_does_not_duplicate_and_changed_content_conflicts()
    {
        var manifest = Guid.NewGuid(); var principal = await Principal();
        var first = await Initialize(principal, manifest);
        var replay = await Initialize(principal, manifest);
        Assert.Equal(first, replay);
        Assert.Equal("conflict", (await _service.InitializeOfflineAsync(_offline, manifest, principal, "changed", _organizationRole.DefinitionId, 1)).Status);
        await using var db = await _ten.CreateDbContextAsync();
        Assert.Equal(1, await db.Memberships.CountAsync(x => x.TenantId == first.TenantId));
        Assert.Equal(1, await db.Assignments.CountAsync(x => x.TenantId == first.TenantId));
    }

    [Fact]
    public async Task Grantor_cannot_mint_unowned_project_permission_or_cross_tenant_membership()
    {
        var scope = new TenantScope("project", Guid.NewGuid()); _scopes.Add(_tenant, scope);
        var role = await Role([new("target.read", "project")]);
        var command = Assign(role, scope);
        Assert.Equal("denied", (await _service.ExecuteAsync(command)).Status);
        var other = await Initialize(await Principal());
        Assert.Equal("denied", (await _service.ExecuteAsync(command with { TenantId = other.TenantId })).Status);
        _subject.Principal = _recipient;
        Assert.Equal("denied", (await _service.ExecuteAsync(command)).Status);
        _subject.Principal = null;
        Assert.Equal("denied", (await _service.ExecuteAsync(command)).Status);
        await using var db = await _ten.CreateDbContextAsync();
        Assert.Empty(await db.Assignments.Where(x => x.TenantId == _tenant && x.MembershipId == _recipientMember).ToArrayAsync());
    }

    [Fact]
    public async Task Delegation_is_exact_scope_idempotent_and_invalidated_by_parent_revocation()
    {
        var (role, scope, root) = await ProjectCapability();
        var command = Assign(role, scope);
        var first = await _service.ExecuteAsync(command); Assert.Equal("success", first.Status);
        Assert.Equal(first, await _service.ExecuteAsync(command));
        Assert.Equal("conflict", (await _service.ExecuteAsync(command with { ExpectedRevision = 2 })).Status);
        Assert.NotNull(await Effective(_recipient, "target.read", scope));
        var other = new TenantScope("project", Guid.NewGuid()); _scopes.Add(_tenant, other);
        Assert.Null(await Effective(_recipient, "target.read", other));
        Assert.Null(await Effective(_recipient, "target.read", new("environment", scope.Id)));
        Assert.Equal("success", (await _service.ExecuteAsync(new(Guid.NewGuid().ToString("N"), "revoke-assignment", _tenant,
            _founderMember, 1, AssignmentId: root.ResourceId))).Status);
        Assert.Null(await Effective(_recipient, "target.read", scope));
        await using var db = await _ten.CreateDbContextAsync();
        Assert.Equal(1, await db.Assignments.CountAsync(x => x.TenantId == _tenant && x.MembershipId == _recipientMember));
    }

    [Theory]
    [InlineData("ancestor")]
    [InlineData("selected")]
    [InlineData("unbounded-leaf")]
    public async Task Current_grant_deadline_is_minimum_of_selected_and_every_provenance_branch(string variant)
    {
        var (role, scope, deadline) = await EarlierProvenance(variant);
        var effective = await Effective(_recipient, "target.read", scope);
        Assert.NotNull(effective); Assert.Equal(deadline, effective.ExpiresAtUtc);
        await using (var admission = await _gate.AcquireReadAsync())
        {
            var grant = await _service.ReadCurrentGrantUnderAdmissionAsync(_recipient, _tenant, "target.read", scope.Kind, scope.Id);
            Assert.NotNull(grant); Assert.Equal(deadline, grant.ExpiresAtUtc);
        }
        _clock.Now = deadline; // equality is expired, without a writer
        Assert.Null(await Effective(_recipient, "target.read", scope));
    }

    [Theory]
    [InlineData("use-audit")]
    [InlineData("disclosure-audit")]
    [InlineData("accepted-transport")]
    [InlineData("no-expiry")]
    public async Task Real_TEN_provenance_deadline_caps_admission_before_U_and_D_handoff(string phase)
    {
        var (role, scope, deadline) = await EarlierProvenance("ancestor");
        var boundary = new ReadBoundary(_recipient, _tenant, scope.Id, _clock,
            (stage) => { if (phase == "use-audit" && stage == AuthorityStage.Use || phase == "disclosure-audit" && stage == AuthorityStage.Disclosure) _clock.Now = deadline; },
            () => { if (phase == "accepted-transport") _clock.Now = deadline; });
        var enforcer = new ProtectedReadAdmission(_gate, boundary, _service, _roles, boundary, boundary, boundary, _clock);
        var status = await enforcer.ExecuteAsync(boundary.Selection, boundary.TargetId, boundary, boundary);
        var rejected = phase is "use-audit" or "disclosure-audit";
        Assert.Equal(rejected ? ProtectedReadStatus.Unavailable : ProtectedReadStatus.Completed, status);
        Assert.Equal(phase == "use-audit" ? 0 : 1, boundary.Reads);
        Assert.Equal(rejected ? 0 : 1, boundary.Outputs);
        Assert.Equal(phase == "use-audit" ? new[] { AuthorityStage.Use } : new[] { AuthorityStage.Use, AuthorityStage.Disclosure }, boundary.Stages);
        // Expiry before D refuses disclosure. Already accepted bounded transport retains its existing semantics.
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("stale")]
    [InlineData("missing")]
    [InlineData("expired")]
    public async Task Invalid_provenance_is_not_reinterpreted_as_valid_unbounded_authority(string fault)
    {
        var (_, scope, _) = await EarlierProvenance("ancestor");
        await using (var db = await _ten.CreateDbContextAsync())
        {
            var root = await db.Assignments.SingleAsync(x => x.TenantId == _tenant && x.MembershipId == _founderMember && x.ScopeKind == "project");
            var admin = await db.Assignments.SingleAsync(x => x.TenantId == _tenant && x.MembershipId == _founderMember && x.ScopeKind == "organization");
            if (fault == "cycle")
            {
                admin.IsFounding = false;
                admin.ProvenanceJson = System.Text.Json.JsonSerializer.Serialize(new[] { new { Id = root.Id, root.Revision } });
            }
            if (fault == "stale") admin.Revision++;
            if (fault == "missing") root.ProvenanceJson = System.Text.Json.JsonSerializer.Serialize(new[] { new { Id = Guid.NewGuid(), Revision = 1L } });
            if (fault == "expired") admin.ExpiresAtUtc = _clock.Now;
            await db.SaveChangesAsync();
        }
        await using var admission = await _gate.AcquireReadAsync();
        Assert.Null(await _service.ReadCurrentGrantUnderAdmissionAsync(_recipient, _tenant, "target.read", scope.Kind, scope.Id));
    }

    [Fact]
    public async Task Valid_unbounded_provenance_retains_unbounded_deadline()
    {
        var (role, scope, _) = await ProjectCapability();
        Assert.Equal("success", (await _service.ExecuteAsync(Assign(role, scope))).Status);
        await using var admission = await _gate.AcquireReadAsync();
        var grant = await _service.ReadCurrentGrantUnderAdmissionAsync(_recipient, _tenant, "target.read", scope.Kind, scope.Id);
        Assert.NotNull(grant); Assert.Null(grant.ExpiresAtUtc);
    }

    private async Task<(DefinitionWriteResult Role, TenantScope Scope, DateTimeOffset Deadline)> EarlierProvenance(string variant)
    {
        var (role, scope, root) = await ProjectCapability(_clock.Now.AddMinutes(5));
        var child = await _service.ExecuteAsync(Assign(role, scope) with { ExpiresAtUtc = _clock.Now.AddMinutes(4) });
        Assert.Equal("success", child.Status);
        await using var db = await _ten.CreateDbContextAsync();
        var administrative = await db.Assignments.SingleAsync(x => x.TenantId == _tenant && x.MembershipId == _founderMember && x.ScopeKind == "organization");
        var project = await db.Assignments.SingleAsync(x => x.Id == root.ResourceId && x.TenantId == _tenant);
        var selected = await db.Assignments.SingleAsync(x => x.Id == child.ResourceId && x.TenantId == _tenant);
        // Synthetic stored-state fixture, not a claim that public grant creation permits an overlong child.
        // The earlier source is both a direct branch and an ancestor of the project grant (a shared DAG node).
        administrative.ExpiresAtUtc = _clock.Now.AddMinutes(1);
        project.IsFounding = false;
        project.ProvenanceJson = System.Text.Json.JsonSerializer.Serialize(new[] { new { Id = administrative.Id, administrative.Revision } });
        if (variant == "unbounded-leaf") selected.ExpiresAtUtc = null;
        if (variant == "selected") selected.ExpiresAtUtc = _clock.Now.AddSeconds(30);
        await db.SaveChangesAsync();
        return (role, scope, variant == "selected" ? selected.ExpiresAtUtc!.Value : administrative.ExpiresAtUtc.Value);
    }

    private sealed class ReadBoundary : ICurrentSubjectReader, ITargetAuthorityReader, IResourcePolicyReader,
        IAuthorityCommitter, IProtectedTargetRead, ICallerBoundResponseCommitter
    {
        private readonly CurrentSubject _subject;
        private readonly TargetAuthority _target;
        private readonly ResourcePolicyAuthority _policy;
        private readonly Action<AuthorityStage> _audit;
        private readonly Action _write;
        public SessionSelection Selection { get; }
        public Guid TargetId => _target.TargetId;
        public int Reads, Outputs;
        public readonly List<AuthorityStage> Stages = [];
        public ReadBoundary(Guid principal, Guid tenant, Guid project, Clock clock, Action<AuthorityStage> audit, Action write)
        {
            var boot = Guid.NewGuid(); Selection = new(principal, "synthetic-reference", boot);
            _subject = new(principal, Guid.NewGuid(), boot, 1, clock.Now.AddHours(1), clock.Now.AddMinutes(30));
            _target = new(Guid.NewGuid(), tenant, project, 1, 1, 1, null, null, null);
            _policy = new(tenant, project, null, "target.read", 1, Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), clock.Now.AddMinutes(-1), null, ResourceEligibility.Allow);
            _audit = audit; _write = write;
        }
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default) => Task.FromResult<CurrentSubject?>(_subject);
        public Task<TargetAuthority?> ReadTargetUnderAdmissionAsync(Guid target, CancellationToken ct = default) => Task.FromResult<TargetAuthority?>(_target);
        public Task<ResourcePolicyAuthority?> ReadPolicyUnderAdmissionAsync(TargetAuthority target, string permission, CancellationToken ct = default) => Task.FromResult<ResourcePolicyAuthority?>(_policy);
        public Task<AuthorityCommitStatus> CommitUnderAdmissionAsync(AuthorityCommitment commitment, CancellationToken ct = default)
        { Stages.Add(commitment.Stage); _audit(commitment.Stage); return Task.FromResult(AuthorityCommitStatus.Durable); }
        public Task<PrivateReadBuffer> Admit(TypedReadSelection selection, CancellationToken ct)
        { Reads++; return Task.FromResult(new PrivateReadBuffer("[]"u8, 0)); }
        public Task CommitAsync(TypedReadSelection selection, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        { Outputs++; _write(); return Task.CompletedTask; }
        public void Abort() { }
    }

    [Fact]
    public async Task Grant_expiry_cannot_outlive_grantor_and_is_rechecked_at_commit()
    {
        var (role, scope, _) = await ProjectCapability(_clock.Now.AddMinutes(5));
        Assert.Equal("denied", (await _service.ExecuteAsync(Assign(role, scope))).Status);
        Assert.Equal("denied", (await _service.ExecuteAsync(Assign(role, scope) with { ExpiresAtUtc = _clock.Now.AddMinutes(6) })).Status);
        var command = Assign(role, scope) with { ExpiresAtUtc = _clock.Now.AddMinutes(4) };
        var service = Service(new FaultAudit(beforeSave: () => _clock.Now = _clock.Now.AddMinutes(6)));
        Assert.Equal("unavailable", (await service.ExecuteAsync(command)).Status);
        await using var db = await _ten.CreateDbContextAsync();
        Assert.Empty(await db.Assignments.Where(x => x.TenantId == _tenant && x.MembershipId == _recipientMember).ToArrayAsync());
        await AssertGateOpen();
    }

    [Theory]
    [InlineData("retire")]
    [InlineData("revision")]
    [InlineData("deny")]
    [InlineData("ambiguous")]
    public async Task Stale_retired_denied_or_ambiguous_role_facts_fail_closed(string scenario)
    {
        var (role, scope, _) = await ProjectCapability();
        Assert.Equal("success", (await _service.ExecuteAsync(Assign(role, scope))).Status);
        if (scenario is "retire" or "revision")
            Assert.Equal("success", (await _roles.PublishOfflineAsync(_offline, Guid.NewGuid(), role.DefinitionId, 1,
                "revision2", [new("target.read", "project")], retire: scenario == "retire")).Status);
        else
        {
            var extra = scenario == "deny" ? await Role([new("target.read", "project", "deny")]) : role;
            Assert.Equal("success", (await _service.GrantOfflineAsync(_offline, Guid.NewGuid(), _tenant, _recipientMember, 2,
                extra.DefinitionId, extra.Revision, scope, null)).Status);
        }
        Assert.Null(await Effective(_recipient, "target.read", scope));
    }

    [Theory]
    [InlineData("revoke-membership")]
    [InlineData("suspend")]
    public async Task Lifecycle_changes_remove_authority_and_stale_revisions_do_not_mutate(string kind)
    {
        var command = new TenantCommand(Guid.NewGuid().ToString("N"), kind, _tenant, _recipientMember, 999);
        Assert.Equal("conflict", (await _service.ExecuteAsync(command)).Status);
        command = command with { ExpectedRevision = kind == "suspend" ? 2 : 1 };
        Assert.Equal("success", (await _service.ExecuteAsync(command)).Status);
        _subject.Principal = _recipient;
        Assert.Empty((await _service.MyMembershipsAsync())!);
    }

    [Theory]
    [InlineData("suspend")]
    [InlineData("revoke-membership")]
    public async Task Committed_self_invalidating_command_reconciles_exact_receipt_without_regranting_authority(string kind)
    {
        var command = new TenantCommand(Guid.NewGuid().ToString("N"), kind, _tenant, _founderMember, kind == "suspend" ? 2 : 1);
        var committed = await _service.ExecuteAsync(command); Assert.Equal("success", committed.Status);
        Assert.Equal(committed, await _service.ExecuteAsync(command));
        Assert.Equal("conflict", (await _service.ExecuteAsync(command with { ExpectedRevision = 999 })).Status);
        Assert.Equal("denied", (await _service.ExecuteAsync(command with { Key = Guid.NewGuid().ToString("N") })).Status);
        _subject.Principal = _recipient;
        Assert.Equal("conflict", (await _service.ExecuteAsync(command)).Status); // another subject cannot claim the receipt
        _subject.Principal = null; Assert.Equal("denied", (await _service.ExecuteAsync(command)).Status);
        await using var db = await _ten.CreateDbContextAsync();
        Assert.Equal(kind == "suspend" ? "suspended" : "active", (await db.Organizations.SingleAsync(x => x.Id == _tenant)).Status);
        Assert.Equal(kind == "revoke-membership" ? "revoked" : "active", (await db.Memberships.SingleAsync(x => x.TenantId == _tenant && x.Id == _founderMember)).Status);
    }

    [Fact]
    public async Task Composite_foreign_key_rejects_cross_tenant_assignment_even_if_owner_is_bypassed()
    {
        var other = await Initialize(await Principal());
        await using var db = await _ten.CreateDbContextAsync();
        db.Assignments.Add(new() { Id = Guid.NewGuid(), TenantId = other.TenantId, MembershipId = _recipientMember,
            DefinitionId = _organizationRole.DefinitionId, DefinitionRevision = 1, ScopeKind = "organization", ScopeId = other.TenantId });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Setup_failure_does_not_close_admission_and_subject_loss_rolls_back_owner_and_receipt()
    {
        var (role, scope, _) = await ProjectCapability(); var command = Assign(role, scope);
        await Assert.ThrowsAsync<TimeoutException>(() => Service(new FaultAudit(failSetup: true)).ExecuteAsync(command));
        await AssertGateOpen();
        Assert.Equal("unavailable", (await Service(new FaultAudit(beforeSave: () => _subject.Principal = null)).ExecuteAsync(command)).Status);
        await AssertGateOpen();
        _subject.Principal = _founder;
        Assert.Equal("success", (await _service.ExecuteAsync(command)).Status); // rejected attempt wrote no receipt
    }

    [Fact]
    public async Task Lost_commit_ack_closes_admission_and_restart_replays_durable_receipt_once()
    {
        var (role, scope, _) = await ProjectCapability(); var command = Assign(role, scope);
        Assert.Equal("unavailable", (await Service(new FaultAudit(unknownAfterCommit: true)).ExecuteAsync(command)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var closed = await _gate.AcquireReadAsync(); });
        _gate = new(); _service = Service();
        var recovered = await _service.ExecuteAsync(command); Assert.Equal("success", recovered.Status);
        await using var db = await _ten.CreateDbContextAsync();
        Assert.Equal(1, await db.Assignments.CountAsync(x => x.TenantId == _tenant && x.MembershipId == _recipientMember));
    }

    [Fact]
    public async Task Writer_waits_for_existing_read_admission_and_cancellation_does_not_close_gate()
    {
        await using var lease = await _gate.AcquireReadAsync();
        using var cancellation = new CancellationTokenSource();
        var pending = _service.ExecuteAsync(new(Guid.NewGuid().ToString("N"), "suspend", _tenant, Guid.Empty, 2), cancellation.Token);
        Assert.False(pending.IsCompleted); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await AssertGateOpen();
    }

    [Fact]
    public async Task One_principal_can_select_two_tenants_without_merging_authority()
    {
        var other = await Initialize(_recipient);
        _subject.Principal = _recipient;
        Assert.Equal(2, (await _service.MyMembershipsAsync())!.Count);
        Assert.Null(await Effective(_recipient, "org.read", new("organization", _tenant)));
        Assert.NotNull(await Effective(_recipient, "org.read", new("organization", other.TenantId), other.TenantId));
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("disabled-principal")]
    [InlineData("membership-revoked")]
    public async Task Previously_effective_grant_is_invalidated_by_current_owner_facts(string reason)
    {
        var (role, scope, _) = await ProjectCapability(_clock.Now.AddMinutes(5));
        Assert.Equal("success", (await _service.ExecuteAsync(Assign(role, scope) with { ExpiresAtUtc = _clock.Now.AddMinutes(4) })).Status);
        Assert.NotNull(await Effective(_recipient, "target.read", scope));
        if (reason == "expiry") _clock.Now = _clock.Now.AddMinutes(4); // equality is expired
        else if (reason == "membership-revoked")
            Assert.Equal("success", (await _service.ExecuteAsync(new(Guid.NewGuid().ToString("N"), "revoke-membership", _tenant, _recipientMember, 2))).Status);
        else
        {
            // Synthetic owner mutation under the same shared writer gate; no cross-owner runtime SQL.
            await using var admission = await _gate.AcquireWriteAsync();
            await using var db = await _iam.CreateDbContextAsync();
            var principal = await db.Principals.SingleAsync(x => x.Id == _recipient);
            principal.AccountStatus = "disabled"; principal.Revision++; await db.SaveChangesAsync();
        }
        Assert.Null(await Effective(_recipient, "target.read", scope));
    }

    [Fact]
    public async Task Owner_mutation_and_audit_commit_once_and_rollback_together()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")!;
        await using var audit = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(locator));
        var before = await audit.Entries.CountAsync(x => x.TenantRef == _tenant);
        var (role, scope, _) = await ProjectCapability();
        var command = Assign(role, scope);
        Assert.Equal("unavailable", (await Service(new FaultAudit(beforeSave: () => _subject.Principal = null)).ExecuteAsync(command)).Status);
        Assert.Equal(before + 1, await audit.Entries.CountAsync(x => x.TenantRef == _tenant)); // only founding project grant
        _subject.Principal = _founder;
        var result = await _service.ExecuteAsync(command); Assert.Equal("success", result.Status);
        Assert.Equal(result, await _service.ExecuteAsync(command));
        var entry = Assert.Single(await audit.Entries.Where(x => x.TenantRef == _tenant && x.ResourceRef == result.ResourceId).ToArrayAsync());
        Assert.Equal(_founder, entry.SubjectRef); Assert.Equal(_tenant, entry.ScopeId);
        Assert.Equal("membership.assign-role", entry.Permission); Assert.NotEqual(Guid.Empty, entry.CorrelationId);
        Assert.Equal(before + 2, await audit.Entries.CountAsync(x => x.TenantRef == _tenant));
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Port=5432;Database=kordra;Username=kordra_app")]
    [InlineData("Host=localhost;Port=5432;Database=kordra_test;Username=kordra_test")]
    [InlineData("Host=127.0.0.1;Port=5433;Database=kordra_test;Username=kordra_test")]
    public void Test_fixture_rejects_unapproved_database_identity_before_connecting(string locator) =>
        Assert.Throws<InvalidOperationException>(() => TenantDbContextFactory.CreateTestOptions(locator));

    private async Task AssertGateOpen() { await using var lease = await _gate.AcquireReadAsync(); }
    private TenantMembershipService Service(ITenantAuditTransactionFactory? audit = null) =>
        new(_ten, _roles, _subject, _scopes, audit ?? new TenantAuditTransactionAdapter(), _gate, _clock);
    private TenantCommand Assign(DefinitionWriteResult role, TenantScope scope) => new(Guid.NewGuid().ToString("N"), "assign", _tenant,
        _recipientMember, 1, role.DefinitionId, role.Revision, scope);
    private async Task<EffectiveAssignment?> Effective(Guid principal, string permission, TenantScope scope, Guid? tenant = null)
    {
        await using var admission = await _gate.AcquireReadAsync();
        return await _service.ReadEffectiveUnderAdmissionAsync(tenant ?? _tenant, principal, permission, scope);
    }
    private async Task<(DefinitionWriteResult, TenantScope, TenantCommandResult)> ProjectCapability(DateTimeOffset? expiry = null)
    {
        var role = await Role([new("target.read", "project")]); var scope = new TenantScope("project", Guid.NewGuid()); _scopes.Add(_tenant, scope);
        var granted = await _service.GrantOfflineAsync(_offline, Guid.NewGuid(), _tenant, _founderMember, 1, role.DefinitionId, 1, scope, expiry);
        Assert.Equal("success", granted.Status); return (role, scope, granted);
    }
    private async Task<Guid> Principal()
    {
        await using var db = await _iam.CreateDbContextAsync();
        var id = Guid.NewGuid(); var name = "t04-" + id.ToString("N");
        db.Principals.Add(new() { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString("N"), PasswordHash = "synthetic-not-a-login-hash" });
        await db.SaveChangesAsync(); _principals.Add(id); return id;
    }
    private async Task<DefinitionWriteResult> Role(IReadOnlyList<PermissionRule> rules)
    {
        var result = await _roles.PublishOfflineAsync(_offline, Guid.NewGuid(), Guid.Empty, 0, "Synthetic role", rules);
        Assert.Equal("success", result.Status); _definitions.Add(result.DefinitionId); return result;
    }
    private async Task<TenantCommandResult> Initialize(Guid principal, Guid? manifest = null)
    {
        var result = await _service.InitializeOfflineAsync(_offline, manifest ?? Guid.NewGuid(), principal, "Synthetic tenant", _organizationRole.DefinitionId, 1);
        Assert.Equal("success", result.Status); if (!_tenants.Contains(result.TenantId)) _tenants.Add(result.TenantId); return result;
    }
    public async Task DisposeAsync()
    {
        await using var ten = await _ten.CreateDbContextAsync();
        await ten.Commands.Where(x => _tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await ten.Assignments.Where(x => _tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await ten.Memberships.Where(x => _tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await ten.Organizations.Where(x => _tenants.Contains(x.Id)).ExecuteDeleteAsync();
        await using var iam = await _iam.CreateDbContextAsync();
        await iam.RoleDefinitions.Where(x => _definitions.Contains(x.DefinitionId)).ExecuteDeleteAsync();
        await iam.Principals.Where(x => _principals.Contains(x.Id)).ExecuteDeleteAsync();
        _offline.Dispose(); // immutable AUD evidence is deliberately retained
    }
    private sealed class Subject : ITenantSubjectReader
    {
        public Guid? Principal;
        public Task<Guid?> CurrentPrincipalAsync(CancellationToken cancellationToken = default) => Task.FromResult(Principal);
    }
    private sealed class Scopes : ITenantScopeOwner
    {
        private readonly HashSet<(Guid, TenantScope)> _owned = [];
        public void Add(Guid tenant, TenantScope scope) => _owned.Add((tenant, scope));
        public Task<bool> BelongsToTenantAsync(Guid tenantId, TenantScope scope, CancellationToken cancellationToken = default) => Task.FromResult(_owned.Contains((tenantId, scope)));
    }
    private sealed class Clock : IAuthorityClock
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public DateTimeOffset GetTrustedUtcNow() => Now;
    }
    private sealed class OfflineLease : IOfflineProvisioningLease, IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "kordra-ten-test-" + Guid.NewGuid().ToString("N"));
        private readonly FileStream _file;
        private bool _disposed;
        public OfflineLease() => _file = new(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        public void RequireHeld() { if (_disposed) throw new InvalidOperationException("Offline test lease released."); }
        public void Dispose() { _disposed = true; _file.Dispose(); File.Delete(_path); }
    }
    private sealed class FaultAudit(Action? beforeSave = null, bool failSetup = false, bool unknownAfterCommit = false) : ITenantAuditTransactionFactory
    {
        public async ValueTask<ITenantAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken = default)
        {
            if (failSetup) throw new TimeoutException("Synthetic pre-commit audit setup failure.");
            return new Transaction(await new TenantAuditTransactionAdapter().EnlistAsync(connection, transaction, cancellationToken), beforeSave, unknownAfterCommit);
        }
        private sealed class Transaction(ITenantAuditTransaction inner, Action? before, bool unknown) : ITenantAuditTransaction
        {
            public async Task<AuditAppendOutcome> CommitAsync(AuditIntent intent, Func<CancellationToken, Task<bool>> saveOwnerAsync, CancellationToken cancellationToken = default)
            {
                var result = await inner.CommitAsync(intent, token => { before?.Invoke(); return saveOwnerAsync(token); }, cancellationToken);
                return unknown && result.Status == AuditAppendStatus.Durable ? AuditAppendOutcome.Unknown() : result;
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
