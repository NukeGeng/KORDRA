using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using Kordra.Api;
using Kordra.ApprovalGovernance.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.Catalog.Tests;

public sealed class ApprovalPolicyCoordinatorTests : CatalogTestFixture
{
    private readonly Guid _installation = Guid.NewGuid();
    private readonly TestClock _clock = new();
    private OfflineApprovalPolicyCoordinator Coordinator(FairAsyncReaderWriterAdmissionGate? gate = null,
        IOfflinePolicyAuditTransactionFactory? audit = null, IAuditAppendPort? lookup = null) => new(_installation, Identity,
            lookup ?? new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(Locator))),
            gate ?? Gate, _clock, audit);
    private async Task MigrateApproval()
    {
        await using var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator));
        await db.Database.MigrateAsync();
    }
    private byte[] Manifest(Guid project, Guid environment, Guid policy, Guid manifestId, long policyRevision = 0, long environmentRevision = 1)
    {
        var value = new
        {
            schemaVersion = 1,
            manifestId = manifestId.ToString("D"),
            installationId = _installation.ToString("D"),
            expectedDatabase = "kordra_test",
            expectedRole = "kordra_admin",
            tenantId = Tenant.ToString("D"),
            projectId = project.ToString("D"),
            policyId = policy.ToString("D"),
            expectedPolicyRevision = policyRevision,
            payload = new
            {
                schemaVersion = 1,
                purpose = "release-intent-v1",
                status = "active",
                effectiveFromUtc = "2026-01-01T00:00:00.000000Z",
                expiresAtUtc = (string?)null,
                rules = new[] { new { classification = "Test", effect = "allow", separationOfDuties = "independent",
                    allowUnattestedProvenance = true, pendingLifetimeSeconds = 86400, grantLifetimeSeconds = 1800,
                    blockedWindowsUtc = Array.Empty<object>() } }
            },
            bindingChanges = new[] { new { environmentId = environment.ToString("D"), expectedEnvironmentRevision = environmentRevision,
                expectedBindingId = (string?)null, expectedBindingRevision = 0 } }
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
    }

    [Fact]
    public async Task Publish_and_exact_replay_preserve_one_owner_audit_and_floor_transaction()
    {
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var policy = Guid.NewGuid(); var manifestId = Guid.NewGuid(); var input = Manifest(project.Id, env.Id, policy, manifestId);
        var parsed = ApprovalPolicyManifest.Parse(input); using var lease = new OfflineLease();
        var coordinator = Coordinator();
        var preview = await coordinator.InspectAsync(lease, input);
        Assert.NotNull(preview); Assert.Equal(parsed.CommandHash, preview.ManifestHash); Assert.Single(preview.Bindings);
        Assert.Equal("denied", (await coordinator.ApplyAsync(lease, input, new string('0', 64))).Status);
        var first = await coordinator.ApplyAsync(lease, input, parsed.CommandHash);
        Assert.Equal("success", first.Status); Assert.False(first.Replayed); Assert.Equal(1, first.PolicyRevision);
        await using (var write = await Gate.AcquireWriteAsync())
        await using (var projectDb = await Projects.CreateDbContextAsync())
        {
            var row = await projectDb.Projects.SingleAsync(x => x.TenantId == Tenant && x.Id == project.Id);
            row.Status = "suspended"; row.Revision++; await projectDb.SaveChangesAsync();
        }
        // Exact historical receipt is reconciled before current parent eligibility; it does not republish.
        var replay = await coordinator.ApplyAsync(lease, input, parsed.CommandHash);
        Assert.Equal("success", replay.Status); Assert.True(replay.Replayed); Assert.Equal(first.CommandId, replay.CommandId);
        var changed = Encoding.UTF8.GetString(input).Replace("\"status\":\"active\"", "\"status\":\"inactive\"", StringComparison.Ordinal);
        var changedBytes = Encoding.UTF8.GetBytes(changed);
        Assert.Equal("conflict", (await coordinator.ApplyAsync(lease, changedBytes, ApprovalPolicyManifest.Parse(changedBytes).CommandHash)).Status);
        await using var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator));
        Assert.Equal(1, await db.Commands.CountAsync(x => x.InstallationId == _installation && x.ManifestId == manifestId));
        Assert.Equal(1, await db.BindingHistory.CountAsync(x => x.InstallationId == _installation && x.PolicyId == policy));
        await using var iam = await Identity.CreateDbContextAsync();
        Assert.True(await iam.AuthorityClockFloors.AnyAsync(x => x.InstallationId == _installation));
        var audit = new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(Locator)));
        Assert.Equal(AuditLookupStatus.Found, (await audit.LookupAsync(manifestId)).Status);
    }

    [Fact]
    public async Task Real_private_installation_lock_guards_policy_without_tls_or_runtime_configuration()
    {
        if (OperatingSystem.IsWindows()) return;
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var input = Manifest(project.Id, env.Id, Guid.NewGuid(), Guid.NewGuid());
        var product = new DirectoryInfo(AppContext.BaseDirectory);
        while (product is not null && !File.Exists(Path.Combine(product.FullName, "Kordra.slnx"))) product = product.Parent;
        Assert.NotNull(product);
        var root = Path.Combine(product!.FullName, ".amr-local", "t11-design", "custody-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, _installation.ToString("D"));
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var identity = Path.Combine(root, "installation.id"); File.WriteAllText(identity, _installation.ToString("D"));
        File.SetUnixFileMode(identity, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var type = typeof(LocalApprovalPolicyCommand).Assembly.GetType("Kordra.Api.OfflineArtifactInstallation", true)!;
        var acquire = type.GetMethod("AcquireFromRoot", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            using var held = Assert.IsAssignableFrom<IDisposable>(acquire.Invoke(null, [root]));
            var lease = Assert.IsAssignableFrom<IOfflineProvisioningLease>(held);
            var conflict = Assert.Throws<TargetInvocationException>(() => acquire.Invoke(null, [root]));
            Assert.IsType<InvalidOperationException>(conflict.InnerException);
            Assert.NotNull(await Coordinator().InspectAsync(lease, input));
            Assert.Equal("success", (await Coordinator().ApplyAsync(lease, input, ApprovalPolicyManifest.Parse(input).CommandHash)).Status);
            Assert.False(File.Exists(Path.Combine(directory, "local-pilot.json")));
            Assert.Single(Directory.GetFiles(directory)); // only the process lock; no TLS key or locator created
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Stale_owner_revision_never_persists_policy_or_audit()
    {
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var policy = Guid.NewGuid(); var manifestId = Guid.NewGuid(); var input = Manifest(project.Id, env.Id, policy, manifestId, environmentRevision: env.Revision + 1);
        using var lease = new OfflineLease();
        var result = await Coordinator().ApplyAsync(lease, input, ApprovalPolicyManifest.Parse(input).CommandHash);
        Assert.Equal("conflict", result.Status);
        await using var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator));
        Assert.False(await db.Heads.AnyAsync(x => x.InstallationId == _installation && x.PolicyId == policy));
        var audit = new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(Locator)));
        Assert.Equal(AuditLookupStatus.NotFound, (await audit.LookupAsync(manifestId)).Status);
    }

    [Fact]
    public async Task One_stale_binding_rejects_the_entire_multi_environment_publication()
    {
        await MigrateApproval(); var project = await Project();
        var first = await CreateEnvironment(project.Id, "Test"); var second = await CreateEnvironment(project.Id, "Test");
        var policy = Guid.NewGuid(); var manifestId = Guid.NewGuid();
        var root = JsonNode.Parse(Manifest(project.Id, first.Id, policy, manifestId))!;
        var environments = new[] { first, second }.OrderBy(x => x.Id.ToString("D"), StringComparer.Ordinal).ToArray();
        var changes = root["bindingChanges"]!.AsArray(); changes.Clear();
        foreach (var item in environments)
            changes.Add(new JsonObject
            {
                ["environmentId"] = item.Id.ToString("D"),
                ["expectedEnvironmentRevision"] = item.Revision,
                ["expectedBindingId"] = null,
                ["expectedBindingRevision"] = 0
            });
        changes[1]!["expectedEnvironmentRevision"] = environments[1].Revision + 1;
        var stale = Encoding.UTF8.GetBytes(root.ToJsonString()); using var lease = new OfflineLease();
        Assert.Equal("conflict", (await Coordinator().ApplyAsync(lease, stale, ApprovalPolicyManifest.Parse(stale).CommandHash)).Status);
        await using (var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator)))
        {
            Assert.False(await db.Heads.AnyAsync(x => x.InstallationId == _installation && x.PolicyId == policy));
            Assert.False(await db.Bindings.AnyAsync(x => x.InstallationId == _installation && x.ProjectId == project.Id));
        }
        changes[1]!["expectedEnvironmentRevision"] = environments[1].Revision;
        root["manifestId"] = Guid.NewGuid().ToString("D");
        var valid = Encoding.UTF8.GetBytes(root.ToJsonString());
        Assert.Equal("success", (await Coordinator().ApplyAsync(lease, valid, ApprovalPolicyManifest.Parse(valid).CommandHash)).Status);
        await using var check = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator));
        Assert.Equal(2, await check.Bindings.CountAsync(x => x.InstallationId == _installation && x.PolicyId == policy));
    }

    [Fact]
    public async Task Lost_commit_ack_closes_gate_and_exact_stopped_replay_reconciles()
    {
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var policy = Guid.NewGuid(); var input = Manifest(project.Id, env.Id, policy, Guid.NewGuid());
        var hash = ApprovalPolicyManifest.Parse(input).CommandHash; using var lease = new OfflineLease();
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var outcome = await Coordinator(gate, new LostAcknowledgementFactory()).ApplyAsync(lease, input, hash);
        Assert.Equal("unknown", outcome.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var unused = await gate.AcquireReadAsync(); });
        var afterRestart = await Coordinator(new FairAsyncReaderWriterAdmissionGate()).ApplyAsync(lease, input, hash);
        Assert.Equal("success", afterRestart.Status); Assert.True(afterRestart.Replayed);
        await using var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator));
        Assert.Equal(1, await db.Commands.CountAsync(x => x.InstallationId == _installation && x.PolicyId == policy));
    }

    [Fact]
    public async Task Failure_before_commit_rolls_back_and_keeps_gate_open()
    {
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var policy = Guid.NewGuid(); var input = Manifest(project.Id, env.Id, policy, Guid.NewGuid());
        var gate = new FairAsyncReaderWriterAdmissionGate(); using var lease = new OfflineLease();
        var outcome = await Coordinator(gate, new FailedEnlistmentFactory()).ApplyAsync(lease, input, ApprovalPolicyManifest.Parse(input).CommandHash);
        Assert.Equal("unavailable", outcome.Status);
        await using (var alive = await gate.AcquireReadAsync()) { }
        await using var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator));
        Assert.False(await db.Heads.AnyAsync(x => x.InstallationId == _installation && x.PolicyId == policy));
    }

    [Fact]
    public async Task Missing_audit_evidence_fences_replay_instead_of_claiming_success()
    {
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var input = Manifest(project.Id, env.Id, Guid.NewGuid(), Guid.NewGuid());
        var hash = ApprovalPolicyManifest.Parse(input).CommandHash; using var lease = new OfflineLease();
        Assert.Equal("success", (await Coordinator().ApplyAsync(lease, input, hash)).Status);
        var gate = new FairAsyncReaderWriterAdmissionGate();
        Assert.Equal("unknown", (await Coordinator(gate, lookup: new MissingAudit()).ApplyAsync(lease, input, hash)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var unused = await gate.AcquireReadAsync(); });
    }

    [Fact]
    public async Task Concurrent_fresh_manifests_on_one_head_cannot_both_publish()
    {
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var policy = Guid.NewGuid(); var left = Manifest(project.Id, env.Id, policy, Guid.NewGuid());
        var right = Manifest(project.Id, env.Id, policy, Guid.NewGuid()); using var lease = new OfflineLease();
        var coordinator = Coordinator();
        var outcomes = await Task.WhenAll(coordinator.ApplyAsync(lease, left, ApprovalPolicyManifest.Parse(left).CommandHash),
            coordinator.ApplyAsync(lease, right, ApprovalPolicyManifest.Parse(right).CommandHash));
        Assert.Equal(new[] { "conflict", "success" }, outcomes.Select(x => x.Status).OrderBy(x => x).ToArray());
        await using var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator));
        Assert.Equal(1, await db.Revisions.CountAsync(x => x.InstallationId == _installation && x.PolicyId == policy));
    }

    [Fact]
    public async Task Audit_only_durable_result_cannot_claim_rolled_back_policy_success()
    {
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var policy = Guid.NewGuid(); var input = Manifest(project.Id, env.Id, policy, Guid.NewGuid());
        var gate = new FairAsyncReaderWriterAdmissionGate(); using var lease = new OfflineLease();
        var outcome = await Coordinator(gate, new AuditOnlyDurableFactory()).ApplyAsync(lease, input, ApprovalPolicyManifest.Parse(input).CommandHash);
        Assert.Equal("unknown", outcome.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var unused = await gate.AcquireReadAsync(); });
        await using var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator));
        Assert.False(await db.Commands.AnyAsync(x => x.InstallationId == _installation && x.PolicyId == policy));
    }

    private sealed class AuditOnlyDurableFactory : IOfflinePolicyAuditTransactionFactory
    {
        public async ValueTask<IOfflinePolicyAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction,
            IOfflineAuditLease lease, CancellationToken ct = default) =>
            new Transaction(await new OfflinePolicyAuditTransactionFactory().EnlistAsync(connection, transaction, lease, ct), transaction);
        private sealed class Transaction(IOfflinePolicyAuditTransaction actual, DbTransaction transaction) : IOfflinePolicyAuditTransaction
        {
            private AuditIntent? _intent;
            public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct = default)
            { _intent = intent; return actual.StageAsync(intent, ct); }
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct = default)
            {
                await transaction.RollbackAsync(ct);
                var intent = _intent ?? throw new InvalidOperationException();
                return AuditAppendOutcome.Durable(new(Guid.NewGuid(), intent.IntentId, AuditIntentCanonicalizer.ComputeHash(intent), DateTimeOffset.UtcNow));
            }
            public ValueTask DisposeAsync() => actual.DisposeAsync();
        }
    }

    [Fact]
    public async Task Existing_audit_without_owner_receipt_fences_instead_of_retrying()
    {
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var policy = Guid.NewGuid(); var input = Manifest(project.Id, env.Id, policy, Guid.NewGuid());
        var gate = new FairAsyncReaderWriterAdmissionGate(); using var lease = new OfflineLease();
        var outcome = await Coordinator(gate, new AlreadyDurableAuditFactory()).ApplyAsync(lease, input, ApprovalPolicyManifest.Parse(input).CommandHash);
        Assert.Equal("unknown", outcome.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var unused = await gate.AcquireReadAsync(); });
        await using var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator));
        Assert.False(await db.Heads.AnyAsync(x => x.InstallationId == _installation && x.PolicyId == policy));
    }

    private sealed class AlreadyDurableAuditFactory : IOfflinePolicyAuditTransactionFactory
    {
        public ValueTask<IOfflinePolicyAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction,
            IOfflineAuditLease lease, CancellationToken ct = default) => ValueTask.FromResult<IOfflinePolicyAuditTransaction>(new Transaction());
        private sealed class Transaction : IOfflinePolicyAuditTransaction
        {
            public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct = default) =>
                Task.FromResult(new AuditTransactionStageOutcome(AuditTransactionStageStatus.AlreadyDurable, null, null));
            public Task<AuditAppendOutcome> CommitAsync(CancellationToken ct = default) => throw new InvalidOperationException("Must not commit");
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class MissingAudit : IAuditAppendPort
    {
        public ValueTask<AuditAppendOutcome> AppendAsync(AuditIntent intent, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AuditAppendOutcome.Unavailable());
        public ValueTask<AuditLookupOutcome> LookupAsync(Guid intentId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AuditLookupOutcome.NotFound());
    }

    [Fact]
    public async Task Concurrent_duplicate_manifest_commits_once_and_replays_once()
    {
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var policy = Guid.NewGuid(); var input = Manifest(project.Id, env.Id, policy, Guid.NewGuid());
        var hash = ApprovalPolicyManifest.Parse(input).CommandHash; using var lease = new OfflineLease();
        var coordinator = Coordinator();
        var results = await Task.WhenAll(coordinator.ApplyAsync(lease, input, hash), coordinator.ApplyAsync(lease, input, hash));
        Assert.All(results, x => Assert.Equal("success", x.Status));
        Assert.Single(results, x => x.Replayed);
        Assert.Equal(results[0].CommandId, results[1].CommandId);
        await using var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator));
        Assert.Equal(1, await db.Commands.CountAsync(x => x.InstallationId == _installation && x.PolicyId == policy));
    }

    [Fact]
    public async Task Unexpected_binding_history_is_treated_as_unknown_not_a_valid_replay()
    {
        await MigrateApproval(); var project = await Project(); var env = await CreateEnvironment(project.Id, "Test");
        var policy = Guid.NewGuid(); var manifestId = Guid.NewGuid(); var input = Manifest(project.Id, env.Id, policy, manifestId);
        var hash = ApprovalPolicyManifest.Parse(input).CommandHash; using var lease = new OfflineLease();
        Assert.Equal("success", (await Coordinator().ApplyAsync(lease, input, hash)).Status);
        await using (var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(Locator)))
        {
            var fakeBinding = Guid.NewGuid(); var fakeEnvironment = Guid.NewGuid();
            db.Bindings.Add(new()
            {
                BindingId = fakeBinding,
                InstallationId = _installation,
                TenantId = Tenant,
                ProjectId = project.Id,
                EnvironmentId = fakeEnvironment,
                BindingRevision = 1,
                PolicyId = policy,
                PolicyRevision = 1
            });
            db.BindingHistory.Add(new()
            {
                BindingId = fakeBinding,
                BindingRevision = 1,
                InstallationId = _installation,
                TenantId = Tenant,
                ProjectId = project.Id,
                EnvironmentId = fakeEnvironment,
                PolicyId = policy,
                PolicyRevision = 1,
                ManifestId = manifestId,
                BoundAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var gate = new FairAsyncReaderWriterAdmissionGate();
        Assert.Equal("unknown", (await Coordinator(gate).ApplyAsync(lease, input, hash)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var unused = await gate.AcquireReadAsync(); });
    }

    private sealed class TestClock : IAuthorityClock { public DateTimeOffset GetTrustedUtcNow() => DateTimeOffset.UtcNow; }
    private sealed class FailedEnlistmentFactory : IOfflinePolicyAuditTransactionFactory
    {
        public ValueTask<IOfflinePolicyAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction,
            IOfflineAuditLease lease, CancellationToken ct = default) => throw new InvalidOperationException("Synthetic pre-commit fault");
    }
    private sealed class LostAcknowledgementFactory : IOfflinePolicyAuditTransactionFactory
    {
        public async ValueTask<IOfflinePolicyAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction,
            IOfflineAuditLease lease, CancellationToken ct = default) =>
            new Transaction(await new OfflinePolicyAuditTransactionFactory().EnlistAsync(connection, transaction, lease, ct));
        private sealed class Transaction(IOfflinePolicyAuditTransaction actual) : IOfflinePolicyAuditTransaction
        {
            public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct = default) => actual.StageAsync(intent, ct);
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct = default)
            {
                var result = await actual.CommitAsync(ct);
                return result.Status == AuditAppendStatus.Durable ? AuditAppendOutcome.Unknown() : result;
            }
            public ValueTask DisposeAsync() => actual.DisposeAsync();
        }
    }
}
