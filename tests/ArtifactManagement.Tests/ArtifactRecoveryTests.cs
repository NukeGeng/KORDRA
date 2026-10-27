using System.Text;
using System.Diagnostics;
using System.Reflection;
using Kordra.Api;
using Kordra.ArtifactManagement.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.Artifacts.Tests;

public sealed class ArtifactRecoveryTests : IAsyncLifetime
{
    private readonly Guid _installation = Guid.NewGuid();
    private readonly FairAsyncReaderWriterAdmissionGate _gate = new();
    private readonly Clock _clock = new(); private readonly Quiescence _processes = new(); private readonly Lease _lease = new();
    private IDbContextFactory<KordraIdentityDbContext> _iam = null!;
    private IDbContextFactory<ArtifactDbContext> _art = null!;
    private IDbContextFactory<AuditDbContext> _aud = null!;
    private ArtifactRegistrationReceipt _receipt = null!;
    private ArtifactRecoveryPlan _plan = null!;
    private OfflineArtifactRecoveryCoordinator Coordinator => new(_installation, _iam, _gate, _clock, _processes);
    public async Task InitializeAsync()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Private isolated test locator required.");
        _iam = new PooledDbContextFactory<KordraIdentityDbContext>(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(locator,
            p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
        _art = new PooledDbContextFactory<ArtifactDbContext>(ArtifactDbContextFactory.CreateTestOptions(locator));
        _aud = new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(locator));
        await using (var db = await _iam.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _art.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _aud.CreateDbContextAsync()) await db.Database.MigrateAsync();
        var identity = ArtifactIdentity.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "team/recovery",
            OCIImageDigest.Parse("sha256:" + new string('a', 64)), ArtifactPlatform.Create("linux", "amd64", null));
        _receipt = ArtifactRegistrationReceipt.Pending(Guid.NewGuid(), _installation, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new(identity, null), "synthetic-recovery-key-0001", _clock.Now.AddSeconds(-1));
        var staged = await new ArtifactOwnerTransaction(_installation, _iam, new ArtifactAuditTransactionFactory(), _gate, _clock).RunAsync(
            _ => Use(), async (store, ct) => await store.PreparePendingAsync(_receipt, ct), _ => Task.FromResult(true), default);
        Assert.Equal("durable", staged.Status);
        _plan = new(1, Guid.NewGuid(), _installation, _receipt.Id, _receipt.BootId, _receipt.InvocationId, int.MaxValue, null, null);
    }
    public Task DisposeAsync() => Task.CompletedTask; // Immutable UUID-scoped evidence, no guard bypass/deletion.
    private AuditIntent Use() => new(1, Guid.NewGuid(), _receipt.InvocationId, AuditIntentStage.Use, _receipt.PrincipalId,
        _receipt.Request.Identity.TenantId, _receipt.Id, "artifact.register", "project", _receipt.Request.Identity.ProjectId, null, 1,
        "artifact-u-" + new string('a',64), _receipt.CreatedAtUtc, Guid.NewGuid());
    private async Task<ArtifactRecoveryPlan> Inspect() => Assert.IsType<ArtifactRecoveryPlan>(await Coordinator.InspectAsync(_lease, _plan.CanonicalBytes()));

    [Fact]
    public async Task Exact_stopped_pending_recovery_is_atomic_and_replays_without_new_side_effects()
    {
        var plan = await Inspect(); Assert.NotNull(plan.ReceiptHash); Assert.NotNull(plan.AuditHash);
        var first = await Coordinator.ExecuteAsync(_lease, plan.CanonicalBytes(), plan.Hash()); Assert.Equal("success", first.Status);
        var second = await Coordinator.ExecuteAsync(_lease, plan.CanonicalBytes(), plan.Hash()); Assert.Equal(first, second);
        await using var art = await _art.CreateDbContextAsync();
        var row = await art.Registrations.SingleAsync(r => r.Id == _receipt.Id); Assert.Equal("Abandoned", row.State);
        Assert.Null(row.ArtifactId); Assert.Equal("interrupted_attempt", row.SafeReason);
        Assert.False(await art.Artifacts.AnyAsync(a => a.ProjectId == row.ProjectId));
        Assert.False(await art.ProvenanceClaims.AnyAsync(c => c.RegistrationId == row.Id));
        await using var aud = await _aud.CreateDbContextAsync();
        var history = await aud.Entries.Where(e => e.InvocationId == row.InvocationId).ToArrayAsync();
        Assert.Equal(2, history.Length); Assert.Single(history, e => e.IntentId == plan.PlanId && e.Stage == "outcome" && e.Permission == "artifact.recover");
        await using var stillOpen = await _gate.AcquireReadAsync();
    }

    [Fact]
    public async Task Wrong_approved_hash_or_unconfirmed_process_exit_cannot_mutate()
    {
        var plan = await Inspect();
        Assert.Equal("denied", (await Coordinator.ExecuteAsync(_lease, plan.CanonicalBytes(), new string('0',64))).Status);
        _processes.Stopped = false;
        Assert.Null(await Coordinator.InspectAsync(_lease, _plan.CanonicalBytes()));
        Assert.Equal("denied", (await Coordinator.ExecuteAsync(_lease, plan.CanonicalBytes(), plan.Hash())).Status);
        await using var art = await _art.CreateDbContextAsync(); Assert.Equal("Pending", (await art.Registrations.SingleAsync(r => r.Id == _receipt.Id)).State);
    }

    [Fact]
    public async Task Changed_receipt_selection_or_added_unknown_audit_stays_fenced()
    {
        var plan = await Inspect();
        var unknown = Use() with { SafeReason = "unrecognized-" + new string('b',64) };
        var append = new AuditAppendStore(_aud); Assert.Equal(AuditAppendStatus.Durable, (await append.AppendAsync(unknown)).Status);
        Assert.Null(await Coordinator.InspectAsync(_lease, _plan.CanonicalBytes()));
        Assert.Equal("unknown", (await Coordinator.ExecuteAsync(_lease, plan.CanonicalBytes(), plan.Hash())).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var closed = await _gate.AcquireReadAsync(); });
        await using var art = await _art.CreateDbContextAsync(); Assert.Equal("Pending", (await art.Registrations.SingleAsync(r => r.Id == _receipt.Id)).State);
    }

    [Fact]
    public async Task Missing_custody_and_live_current_PID_are_rejected()
    {
        _lease.Held = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator.InspectAsync(_lease, _plan.CanonicalBytes()));
        Assert.False(new ArtifactProcessQuiescence().IsQuiescent(Environment.ProcessId));
        Assert.False(new ArtifactProcessQuiescence().IsQuiescent(0));
    }

    [MacArtifactFact]
    public async Task Actual_running_designated_helper_blocks_recovery_even_after_old_PID_is_gone()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Kordra.Api");
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, ArgumentList = { "--internal-keychain-read" }
        }) ?? throw new InvalidOperationException("Synthetic helper could not start.");
        try
        {
            await Task.Delay(100);
            Assert.False(process.HasExited);
            var check = new ArtifactProcessQuiescence();
            Assert.False(check.IsQuiescent(process.Id));
            Assert.False(check.IsQuiescent(int.MaxValue));
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            // Only this test-owned synthetic child may be stopped on test failure.
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
        }
    }

    [MacArtifactFact]
    public void Offline_installation_custody_uses_existing_lock_without_loading_runtime_configuration_or_TLS_key()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var product = new DirectoryInfo(AppContext.BaseDirectory);
        while (product is not null && !File.Exists(Path.Combine(product.FullName, "Kordra.slnx"))) product = product.Parent;
        Assert.NotNull(product);
        var root = Path.Combine(product!.FullName, ".amr-local", "t10-implementation", "custody-" + Guid.NewGuid().ToString("N"));
        var installation = Guid.NewGuid(); var instance = Path.Combine(root, installation.ToString("D"));
        Directory.CreateDirectory(instance);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(instance, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var id = Path.Combine(root, "installation.id"); File.WriteAllText(id, installation.ToString("D"));
        File.SetUnixFileMode(id, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var type = typeof(LocalArtifactRecoveryCommand).Assembly.GetType("Kordra.Api.OfflineArtifactInstallation", throwOnError: true)!;
        var acquire = type.GetMethod("AcquireFromRoot", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            using (var held = Assert.IsAssignableFrom<IDisposable>(acquire.Invoke(null, [root])))
            {
                Assert.Equal(installation, type.GetProperty("InstallationId")!.GetValue(held));
                Assert.IsAssignableFrom<IOfflineProvisioningLease>(held).RequireHeld();
                var conflict = Assert.Throws<TargetInvocationException>(() => acquire.Invoke(null, [root]));
                Assert.IsType<InvalidOperationException>(conflict.InnerException);
                Assert.True(File.Exists(Path.Combine(instance, "process.lock")));
                Assert.False(File.Exists(Path.Combine(instance, "local-pilot.json")));
            }
            using var reacquired = Assert.IsAssignableFrom<IDisposable>(acquire.Invoke(null, [root]));
            Assert.IsAssignableFrom<IOfflineProvisioningLease>(reacquired).RequireHeld();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Plan_rejects_duplicate_unknown_fields_noncanonical_ids_and_wrong_types()
    {
        var bytes = _plan.CanonicalBytes(); Assert.Equal(_plan, ArtifactRecoveryPlan.Parse(bytes));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Null(ArtifactRecoveryPlan.Parse(Encoding.UTF8.GetBytes(text[..^1] + ",\"schemaVersion\":1}")));
        Assert.Null(ArtifactRecoveryPlan.Parse(Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":1", "\"schemaVersion\":null"))));
        Assert.Null(ArtifactRecoveryPlan.Parse(Encoding.UTF8.GetBytes(text.Replace(_installation.ToString("D"), _installation.ToString("D").ToUpperInvariant()))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_distinguishes_no_commit_from_real_lost_ACK(bool afterCommit)
    {
        var plan = await Inspect();
        var factory = new FaultAuditFactory(afterCommit);
        var coordinator = new OfflineArtifactRecoveryCoordinator(_installation, _iam, _gate, _clock, _processes, factory);
        var result = await coordinator.ExecuteAsync(_lease, plan.CanonicalBytes(), plan.Hash());
        Assert.Equal(afterCommit ? "unknown" : "unavailable", result.Status);
        await using var art = await _art.CreateDbContextAsync();
        Assert.Equal(afterCommit ? "Abandoned" : "Pending", (await art.Registrations.SingleAsync(r => r.Id == _receipt.Id)).State);
        if (afterCommit)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var closed = await _gate.AcquireReadAsync(); });
            var stoppedRestart = new OfflineArtifactRecoveryCoordinator(_installation, _iam, new(), _clock, _processes);
            Assert.Equal("success", (await stoppedRestart.ExecuteAsync(_lease, plan.CanonicalBytes(), plan.Hash())).Status);
        }
        else { await using var open = await _gate.AcquireReadAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Registered_result_is_preserved_only_with_the_matching_atomic_mutation_receipt(bool hasMutationAudit)
    {
        var consumption = Use() with { SafeReason = "artifact-c-" + new string('b', 64) };
        Assert.Equal(AuditAppendStatus.Durable, (await new AuditAppendStore(_aud).AppendAsync(consumption)).Status);
        var plan = await Inspect();
        var identity = _receipt.Request.Identity;
        var facts = new ArtifactVerificationFacts(identity, ArtifactVerificationFacts.OciManifest, 100,
            identity.RootDigest, ArtifactVerificationFacts.OciManifest, 100,
            OCIImageDigest.Parse("sha256:" + new string('c', 64)), 50, identity.Platform, "none", _clock.Now);
        if (hasMutationAudit)
        {
            var commit = await new ArtifactOwnerTransaction(_installation, _iam, new ArtifactAuditTransactionFactory(), _gate, _clock).RunAsync(
                prepared => Use() with { Stage = AuditIntentStage.Mutation, ResourceRef = prepared!.ArtifactId, SafeReason = "artifact-m-" + new string('c', 64) },
                async (store, ct) => await store.PrepareRegisteredAsync(_receipt, facts, _clock.Now, ct), _ => Task.FromResult(true), default);
            Assert.Equal("durable", commit.Status);
        }
        else
        {
            // Explicit inconsistent synthetic history: ART was committed without its required M audit.
            await using var db = await _art.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync();
            Assert.Equal("staged_registered", (await new ArtifactRegistrationStore(db).PrepareRegisteredAsync(_receipt, facts, _clock.Now)).Status);
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        var result = await Coordinator.ExecuteAsync(_lease, plan.CanonicalBytes(), plan.Hash());
        Assert.Equal(hasMutationAudit ? "unchanged" : "unknown", result.Status);
        if (hasMutationAudit)
        {
            // Inspection after an unknown M outcome can confirm the durable registered result.
            var afterCommit = await Coordinator.InspectAsync(_lease, (_plan with { PlanId = Guid.NewGuid() }).CanonicalBytes());
            Assert.NotNull(afterCommit);
            Assert.Equal("unchanged", (await Coordinator.ExecuteAsync(_lease, afterCommit!.CanonicalBytes(), afterCommit.Hash())).Status);
        }
        await using var verify = await _art.CreateDbContextAsync();
        Assert.Equal("Registered", (await verify.Registrations.SingleAsync(r => r.Id == _receipt.Id)).State);
        if (!hasMutationAudit)
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var closed = await _gate.AcquireReadAsync(); });
    }

    private sealed class FaultAuditFactory(bool afterCommit) : IArtifactAuditTransactionFactory
    {
        public async ValueTask<IArtifactAuditTransaction> EnlistAsync(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction t, CancellationToken ct)
        {
            if (!afterCommit) throw new IOException("Synthetic pre-commit setup failure.");
            return new LostAck(await AuditTransactionContext.CreateOfflineAsync(c, t, new AuditLease(), ct));
        }
        private sealed class AuditLease : IOfflineAuditLease { public void RequireHeld() { } }
        private sealed class LostAck(AuditTransactionContext inner) : IArtifactAuditTransaction
        {
            public bool CommitAttempted => inner.CommitAttempted;
            public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct) => inner.StageAsync(intent, ct);
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct)
            { var result = await inner.CommitAsync(ct); Assert.Equal(AuditAppendStatus.Durable, result.Status); throw new IOException("Synthetic lost commit ACK."); }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
    private sealed class Quiescence : IArtifactProcessQuiescence { public bool Stopped = true; public bool IsQuiescent(int pid) => Stopped; }
    private sealed class Lease : IOfflineProvisioningLease { public bool Held = true; public void RequireHeld() { if (!Held) throw new InvalidOperationException("Custody not held."); } }
    private sealed class Clock : IAuthorityClock
    {
        public DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public DateTimeOffset GetTrustedUtcNow() => Now;
    }
}
