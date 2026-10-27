using System.Data.Common;
using System.Text.Json;
using Kordra.Api;
using Kordra.ArtifactManagement.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

// Controlled authority-port interleavings plus real owner/AUD/floor storage, not an HTTPS/login proof.
[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class ArtifactReadCoordinatorTests : IAsyncLifetime
{
    private readonly ArtifactAuthorityTestFacts _facts = new();
    private readonly FairAsyncReaderWriterAdmissionGate _gate = new();
    private PooledDbContextFactory<KordraIdentityDbContext> _iam = null!;
    private PooledDbContextFactory<ArtifactDbContext> _art = null!;
    private PooledDbContextFactory<AuditDbContext> _aud = null!;
    private string _locator = "";
    public async Task InitializeAsync()
    {
        _locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private isolated kordra_test locator required.");
        _art = new(ArtifactDbContextFactory.CreateTestOptions(_locator)); _aud = new(AuditDbContextFactory.CreateTestOptions(_locator));
        _iam = new(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(_locator,
            p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
        await using (var db = await _iam.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _art.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _aud.CreateDbContextAsync()) await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask; // Own committed synthetic append-only receipt/AUD evidence is retained.
    private ArtifactReadCoordinator Coordinator(IArtifactAuditTransactionFactory? audit = null)
    {
        var sec = new PooledDbContextFactory<CredentialDbContext>(CredentialDbContextFactory.CreateTestOptions(_locator));
        var resolver = new ArtifactAuthorityResolver(_facts.Installation, _facts.Reader, sec, _facts, null);
        return new(_facts.Installation, resolver, _art,
            new(_facts.Installation, _iam, audit ?? new ArtifactAuditTransactionFactory(), _gate, _facts), _gate, _facts);
    }
    private Task<ArtifactExecutionStatus> Read(ArtifactReadCommand query, Capture capture, IArtifactAuditTransactionFactory? factory = null) =>
        Coordinator(factory).ExecuteAsync(_facts.Selection, _facts.Project.ProjectId, query, capture);

    [Fact]
    public async Task Empty_historical_list_has_current_U_D_with_result_hash_and_no_provider_configuration()
    {
        _facts.SetAction("artifact.read"); var capture = new Capture();
        var result = await Read(new(ArtifactQueryKind.List, Guid.NewGuid()), capture);
        Assert.Equal(ArtifactExecutionStatus.Completed, result); Assert.Equal(200, capture.Selection!.StatusCode);
        using var json = JsonDocument.Parse(capture.Bytes!);
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextAfterArtifactId").ValueKind);
        await using var audit = await _aud.CreateDbContextAsync();
        var entries = await audit.Entries.Where(x => x.InvocationId == capture.Selection.InvocationId).ToArrayAsync();
        Assert.Equal(2, entries.Length);
        Assert.Contains(entries, x => x.Stage == "U");
        Assert.Contains(entries, x => x.Stage == "D");
        Assert.All(entries, x => Assert.Equal("artifact.read", x.Permission));
        await using var held = await _gate.AcquireReadAsync();
    }

    [Theory]
    [InlineData(ArtifactQueryKind.Detail)]
    [InlineData(ArtifactQueryKind.Registration)]
    public async Task Foreign_and_absent_records_have_the_same_project_authorized_error(ArtifactQueryKind kind)
    {
        _facts.SetAction(kind == ArtifactQueryKind.Detail ? "artifact.read" : "artifact.register");
        var capture = new Capture();
        Assert.Equal(ArtifactExecutionStatus.Completed, await Read(new(kind, Guid.NewGuid(), Guid.NewGuid()), capture));
        Assert.Equal(404, capture.Selection!.StatusCode);
        using var json = JsonDocument.Parse(capture.Bytes!);
        Assert.Equal("not_found", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("registration").ValueKind);
    }

    [Fact]
    public async Task Pending_replay_and_own_status_need_no_registry_SEC_or_second_attempt()
    {
        var identity = ArtifactIdentity.Create(_facts.Project.TenantId, _facts.Project.ProjectId, Guid.NewGuid(), "synthetic/read-test",
            OCIImageDigest.Parse("sha256:" + new string('a', 64)), ArtifactPlatform.Create("linux", "amd64", null));
        var pending = ArtifactRegistrationReceipt.Pending(Guid.NewGuid(), _facts.Installation, _facts.Selection.BootGeneration,
            Guid.NewGuid(), _facts.Subject.PrincipalId, new(identity, null), "synthetic-read-replay-key", _facts.Now);
        var transaction = new ArtifactOwnerTransaction(_facts.Installation, _iam, new ArtifactAuditTransactionFactory(), _gate, _facts);
        await using (var held = await _gate.AcquireWriteAsync())
        {
            var seeded = await transaction.RunAsync(_ => new(1, Guid.NewGuid(), pending.InvocationId, AuditIntentStage.Use,
                _facts.Subject.PrincipalId, _facts.Project.TenantId, pending.Id, "artifact.register", "project", _facts.Project.ProjectId,
                null, 1, "synthetic-test-seed", _facts.Now, Guid.NewGuid()),
                async (store, ct) => await store.PreparePendingAsync(pending, ct), _ => Task.FromResult(true), default);
            Assert.Equal("durable", seeded.Status);
        }
        var replay = new Capture();
        Assert.Equal(ArtifactExecutionStatus.Completed, await Read(new(ArtifactQueryKind.Replay, Guid.NewGuid(), Replay: pending), replay));
        Assert.Equal(409, replay.Selection!.StatusCode);
        using var json = JsonDocument.Parse(replay.Bytes!);
        Assert.Equal("registration_in_progress", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(pending.Id, json.RootElement.GetProperty("registration").GetProperty("registrationId").GetGuid());
        var status = new Capture();
        Assert.Equal(ArtifactExecutionStatus.Completed, await Read(new(ArtifactQueryKind.Registration, Guid.NewGuid(), pending.Id), status));
        Assert.Equal(200, status.Selection!.StatusCode);
        await using var db = await _art.CreateDbContextAsync();
        Assert.Equal(1, await db.Registrations.CountAsync(r => r.InstallationId == _facts.Installation));
        _facts.Subject = _facts.Subject with { PrincipalId = Guid.NewGuid() };
        _facts.Grant = _facts.Grant with { PrincipalId = _facts.Subject.PrincipalId };
        var foreign = new Capture();
        var subject = new Contracts.SessionSelection(_facts.Subject.PrincipalId, "synthetic-other-session", _facts.Subject.BootGeneration);
        Assert.Equal(ArtifactExecutionStatus.Completed, await Coordinator().ExecuteAsync(subject, _facts.Project.ProjectId,
            new(ArtifactQueryKind.Registration, Guid.NewGuid(), pending.Id), foreign));
        Assert.Equal(404, foreign.Selection!.StatusCode);
    }

    [Theory]
    [InlineData("u-revision")]
    [InlineData("d-revision")]
    [InlineData("expiry")]
    [InlineData("shorten-restore")]
    public async Task Authority_change_or_earlier_expiry_before_commit_withholds_body(string fault)
    {
        _facts.SetAction("artifact.read"); var capture = new Capture();
        var hook = new Hooks(stage =>
        {
            if (fault == "u-revision" && stage == 1 || fault == "d-revision" && stage == 2)
                _facts.Grant = _facts.Grant with { AssignmentRevision = _facts.Grant.AssignmentRevision + 1 };
            if (fault == "expiry" && stage == 2) _facts.Now = _facts.Now.AddSeconds(10);
            if (fault == "shorten-restore" && stage == 1) _facts.Grant = _facts.Grant with { ExpiresAtUtc = _facts.Now.AddSeconds(1) };
            if (fault == "shorten-restore" && stage == 2)
            { _facts.Now = _facts.Now.AddSeconds(1); _facts.Grant = _facts.Grant with { ExpiresAtUtc = _facts.Now.AddMinutes(30) }; }
        });
        Assert.Equal(ArtifactExecutionStatus.Denied, await Read(new(ArtifactQueryKind.List, Guid.NewGuid()), capture, hook));
        Assert.Null(capture.Bytes); await using var held = await _gate.AcquireReadAsync();
    }

    [Fact]
    public async Task Unknown_D_commit_aborts_and_closes_without_any_body()
    {
        _facts.SetAction("artifact.read"); var capture = new Capture(); var hook = new Hooks(_ => { }, loseSecondAck: true);
        Assert.Equal(ArtifactExecutionStatus.Unknown, await Read(new(ArtifactQueryKind.List, Guid.NewGuid()), capture, hook));
        Assert.True(capture.Aborted); Assert.Null(capture.Bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
    }

    [Fact]
    public async Task Query_shares_the_existing_sixteen_slot_pool()
    {
        var slots = Enumerable.Range(0, 16).Select(_ => ProtectedInvocationCapacity.TryReserve(_gate)!).ToArray();
        try
        {
            _facts.SetAction("artifact.read"); var response = new Capture();
            Assert.Equal(ArtifactExecutionStatus.Unavailable, await Read(new(ArtifactQueryKind.List, Guid.NewGuid()), response));
            Assert.Null(response.Bytes);
        }
        finally { foreach (var slot in slots) slot.Dispose(); }
    }

    [Fact]
    public async Task Completed_aborted_handoffs_do_not_exhaust_the_shared_pool()
    {
        _facts.SetAction("artifact.read");
        for (var attempt = 0; attempt < 17; attempt++)
        {
            var capture = new Capture { FailHandoff = true };
            Assert.Equal(ArtifactExecutionStatus.Unknown, await Read(new(ArtifactQueryKind.List, Guid.NewGuid()), capture));
            Assert.True(capture.Aborted);
        }
        using var available = ProtectedInvocationCapacity.TryReserve(_gate);
        Assert.NotNull(available);
        await using var healthy = await _gate.AcquireReadAsync();
    }

    private sealed class Capture : IArtifactResponseCommitter
    {
        public byte[]? Bytes;
        public ArtifactResponseCommitment? Selection;
        public bool Aborted;
        public bool FailHandoff;
        public Task CommitAsync(ArtifactResponseCommitment selection, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailHandoff) return Task.FromException(new IOException("Synthetic completed handoff failure"));
            Selection = selection; Bytes = bytes.ToArray(); return Task.CompletedTask;
        }
        public void Abort() => Aborted = true;
    }
    private sealed class Hooks(Action<int> staged, bool loseSecondAck = false) : IArtifactAuditTransactionFactory
    {
        private int _count;
        public async ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection c, DbTransaction t, CancellationToken ct) =>
            new Enlisted(await new ArtifactAuditTransactionFactory().EnlistAsync(c, t, ct), staged, Interlocked.Increment(ref _count), loseSecondAck);
        private sealed class Enlisted(IArtifactAuditTransaction inner, Action<int> hook, int count, bool lose) : IArtifactAuditTransaction
        {
            public bool CommitAttempted => inner.CommitAttempted;
            public async Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct)
            { var result = await inner.StageAsync(intent, ct); hook(count); return result; }
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct)
            { var result = await inner.CommitAsync(ct); if (lose && count == 2) throw new IOException("Synthetic ACK loss"); return result; }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
