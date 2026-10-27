using System.Data.Common;
using Kordra.Api;
using Kordra.ArtifactManagement.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class ArtifactOwnerTransactionTests : IAsyncLifetime
{
    private readonly Guid _installation = Guid.NewGuid(), _tenant = Guid.NewGuid(), _project = Guid.NewGuid(), _principal = Guid.NewGuid();
    private readonly Clock _clock = new();
    private readonly FairAsyncReaderWriterAdmissionGate _gate = new();
    private PooledDbContextFactory<KordraIdentityDbContext> _iam = null!;
    private PooledDbContextFactory<ArtifactDbContext> _art = null!;
    private PooledDbContextFactory<AuditDbContext> _aud = null!;
    public async Task InitializeAsync()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private isolated kordra_test locator required.");
        _art = new(ArtifactDbContextFactory.CreateTestOptions(locator));
        _aud = new(AuditDbContextFactory.CreateTestOptions(locator));
        _iam = new(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(locator,
            p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
        await using (var db = await _iam.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _art.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _aud.CreateDbContextAsync()) await db.Database.MigrateAsync();
    }

    // Committed ART rows deliberately remain immutable synthetic test history. Do not disable
    // guards/delete receipts to clean a durability test. Every selection is a fresh UUID scope.
    public Task DisposeAsync() => Task.CompletedTask;
    private ArtifactRegistrationReceipt Pending() => ArtifactRegistrationReceipt.Pending(Guid.NewGuid(), _installation,
        Guid.NewGuid(), Guid.NewGuid(), _principal,
        new(ArtifactIdentity.Create(_tenant, _project, Guid.NewGuid(), "synthetic/transaction-test",
            OCIImageDigest.Parse("sha256:" + new string('a', 64)), ArtifactPlatform.Create("linux", "amd64", null)), null),
        "synthetic-transaction-key-0001", _clock.Now);
    private AuditIntent Intent(ArtifactRegistrationReceipt pending) => new(1, pending.InvocationId, pending.InvocationId,
        AuditIntentStage.Use, _principal, _tenant, pending.Id, "artifact.register", "project", _project,
        null, 1, "artifact-u-" + pending.BusinessHash, _clock.Now, Guid.NewGuid());

    [Theory]
    [InlineData("enlist")]
    [InlineData("stage")]
    [InlineData("authority")]
    [InlineData("save")]
    public async Task Precommit_failure_rolls_back_all_owners_without_closing_admission(string fault)
    {
        var pending = Pending(); var intent = Intent(pending);
        if (fault == "save")
        {
            // An independently committed synthetic receipt owns this ID in another
            // fixture installation. The attempted insert must fail at the real SQL PK.
            var seed = ArtifactRegistrationReceipt.Pending(pending.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                _principal, pending.Request, "synthetic-conflicting-id-seed", _clock.Now);
            var seeder = new ArtifactOwnerTransaction(seed.InstallationId, _iam, new ArtifactAuditTransactionFactory(), _gate, _clock);
            await using var seedHeld = await _gate.AcquireWriteAsync();
            Assert.Equal("durable", (await seeder.RunAsync(_ => Intent(seed),
                async (store, ct) => await store.PreparePendingAsync(seed, ct), _ => Task.FromResult(true), default)).Status);
        }
        var factory = new Faults(fault);
        var runner = new ArtifactOwnerTransaction(_installation, _iam, factory, _gate, _clock);
        ArtifactOwnerCommit result;
        await using (var held = await _gate.AcquireWriteAsync())
            result = await runner.RunAsync(_ => intent, async (store, ct) =>
            {
                var row = await store.PreparePendingAsync(pending, ct);
                return row;
            }, _ => Task.FromResult(fault != "authority"), default);
        Assert.Equal(fault == "authority" ? "denied" : "unavailable", result.Status);
        Assert.False(factory.ActualCommitAttempted);
        await AssertRows(pending, intent, exists: false);
        await using var usable = await _gate.AcquireReadAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_shared_commit_persists_ART_AUD_floor_and_lost_ACK_fences_without_result(bool loseAck)
    {
        var pending = Pending(); var intent = Intent(pending); var factory = new Faults(loseAck ? "ack" : "none");
        var runner = new ArtifactOwnerTransaction(_installation, _iam, factory, _gate, _clock);
        ArtifactOwnerCommit result;
        await using (var held = await _gate.AcquireWriteAsync())
            result = await runner.RunAsync(_ => intent, (store, ct) => store.PreparePendingAsync(pending, ct)!,
                _ => Task.FromResult(true), default);
        Assert.True(factory.ActualCommitAttempted);
        Assert.Equal(loseAck ? "unknown" : "durable", result.Status);
        if (loseAck)
        {
            Assert.Null(result.Prepared);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var denied = await _gate.AcquireReadAsync(); });
        }
        else { Assert.Equal(pending.Id, result.Prepared!.RegistrationId); await using var usable = await _gate.AcquireReadAsync(); }
        await AssertRows(pending, intent, exists: true);
        var lookup = await new AuditAppendStore(_aud).LookupAsync(intent.IntentId);
        Assert.Equal(AuditLookupStatus.Found, lookup.Status);
        Assert.Equal(AuditIntentCanonicalizer.ComputeHash(intent), lookup.Receipt!.IntentHash);
    }

    private async Task AssertRows(ArtifactRegistrationReceipt pending, AuditIntent intent, bool exists)
    {
        await using var art = await _art.CreateDbContextAsync();
        await using var iam = await _iam.CreateDbContextAsync();
        await using var audit = await _aud.CreateDbContextAsync();
        Assert.Equal(exists, await art.Registrations.AnyAsync(r => r.Id == pending.Id && r.InstallationId == _installation));
        Assert.Equal(exists, await iam.AuthorityClockFloors.AnyAsync(r => r.InstallationId == _installation));
        Assert.Equal(exists, await audit.Entries.AnyAsync(r => r.IntentId == intent.IntentId));
    }
    private sealed class Clock : IAuthorityClock
    { public DateTimeOffset Now { get; } = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); public DateTimeOffset GetTrustedUtcNow() => Now; }
    private sealed class Faults(string fault) : IArtifactAuditTransactionFactory
    {
        public bool ActualCommitAttempted;
        public async ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection c, DbTransaction t, CancellationToken ct)
        {
            if (fault == "enlist") throw new OperationCanceledException();
            return new Enlisted(await new ArtifactAuditTransactionFactory().EnlistAsync(c, t, ct), this, fault);
        }
        private sealed class Enlisted(IArtifactAuditTransaction inner, Faults owner, string fault) : IArtifactAuditTransaction
        {
            public bool CommitAttempted => inner.CommitAttempted;
            public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct) =>
                fault == "stage" ? throw new OperationCanceledException() : inner.StageAsync(intent, ct);
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct)
            {
                var result = await inner.CommitAsync(ct); owner.ActualCommitAttempted = inner.CommitAttempted;
                if (fault == "ack") throw new IOException("Synthetic lost commit ACK");
                return result;
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
