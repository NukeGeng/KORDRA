using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.ArtifactManagement.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ArtifactManagement.Contracts;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.ArtifactManagement.Infrastructure.Registry;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Commands;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Kordra.Artifacts.Tests;

// Real PostgreSQL commits and synthetic TLS, with controlled current-authority ports.
// This does not replace the later real login/owner HTTP conformance cases.
public sealed class ArtifactRegistrationCoordinatorTests : IAsyncLifetime
{
    private readonly Facts _facts = new();
    private readonly FairAsyncReaderWriterAdmissionGate _gate = new();
    private PooledDbContextFactory<KordraIdentityDbContext> _iam = null!;
    private PooledDbContextFactory<ArtifactDbContext> _art = null!;
    private PooledDbContextFactory<CredentialDbContext> _sec = null!;
    private PooledDbContextFactory<AuditDbContext> _aud = null!;
    private RegistryTransportTests.Fixture _server = null!;
    private RegistryTrustConfiguration _configuration = null!;
    private ArtifactRegistrationRequestV1 _command = null!;
    private const string Key = "synthetic-registration-key-0001";
    private byte[] _config = null!, _root = null!;
    public async Task InitializeAsync()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private isolated kordra_test locator required.");
        _iam = new(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(locator,
            p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
        _art = new(ArtifactDbContextFactory.CreateTestOptions(locator)); _sec = new(CredentialDbContextFactory.CreateTestOptions(locator));
        _aud = new(AuditDbContextFactory.CreateTestOptions(locator));
        await using (var db = await _iam.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _art.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _sec.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _aud.CreateDbContextAsync()) await db.Database.MigrateAsync();
        _server = new();
        var trust = _server.Trust("anonymous", installation: _facts.Installation, tenant: _facts.Project.TenantId,
            project: _facts.Project.ProjectId, registry: _facts.Registry);
        _configuration = new(_facts.Installation, new string('f', 64), [trust]);
        _config = "{\"os\":\"linux\",\"architecture\":\"amd64\"}"u8.ToArray();
        _root = Image(_config);
        _server.Handler = Response;
        _command = new(1, Guid.NewGuid(), _facts.Registry, "team/frontend", Hash(_root), new("linux", "amd64", null), null);
    }
    public async Task DisposeAsync() { if (_server is not null) await _server.DisposeAsync(); }
    // All committed rows are fresh, bounded, synthetic append-only transaction evidence; guards are never bypassed.
    private ArtifactRegistrationCoordinator Coordinator(IArtifactAuditTransactionFactory? hooks = null, bool historicalOnly = false)
    {
        var authority = new ArtifactAuthorityResolver(_facts.Installation, new(_facts, _facts, _facts, _facts, _facts), _sec, _facts,
            historicalOnly ? null : _configuration);
        var transactions = new ArtifactOwnerTransaction(_facts.Installation, _iam, hooks ?? new ArtifactAuditTransactionFactory(), _gate, _facts);
        return new(_facts.Installation, authority, _art, new(_art, _sec), transactions,
            new(_facts.Installation, authority, _art, transactions, _gate, _facts), _gate, _facts, null);
    }
    private Task<ArtifactExecutionStatus> Register(Capture response, IArtifactAuditTransactionFactory? hooks = null,
        ArtifactRegistrationRequestV1? command = null, string key = Key, bool historicalOnly = false) =>
        Coordinator(hooks, historicalOnly).ExecuteAsync(_facts.Selection, _facts.Project.ProjectId, command ?? _command, key, response);
    private static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static byte[] Image(byte[] config) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = 2,
        mediaType = "application/vnd.oci.image.manifest.v1+json",
        config = new { mediaType = "application/vnd.oci.image.config.v1+json", digest = Hash(config), size = config.Length },
        layers = Array.Empty<object>()
    });
    private string Response(string request)
    {
        var blob = request.StartsWith("GET /v2/team/frontend/blobs/", StringComparison.Ordinal);
        var bytes = blob ? _config : _root;
        return "HTTP/1.1 200 OK\r\nContent-Type: " + (blob ? "application/octet-stream" : "application/vnd.oci.image.manifest.v1+json") +
            "\r\nContent-Length: " + bytes.Length + "\r\n\r\n" + Encoding.UTF8.GetString(bytes);
    }

    [Fact]
    public async Task Registration_has_durable_U_C_M_D_and_same_key_replay_never_calls_provider()
    {
        var first = new Capture(); Assert.Equal(ArtifactExecutionStatus.Completed, await Register(first)); Assert.Equal(201, first.Status);
        Assert.Equal(2, _server.Requests.Count);
        using var json = JsonDocument.Parse(first.Bytes!); var registration = json.RootElement.GetProperty("registration");
        Assert.Equal("Registered", registration.GetProperty("status").GetString());
        var id = registration.GetProperty("registrationId").GetGuid();
        await using var aud = await _aud.CreateDbContextAsync();
        var entries = await aud.Entries.Where(e => e.InvocationId == first.Selection!.InvocationId).ToArrayAsync();
        Assert.Equal(4, entries.Length); Assert.Equal(2, entries.Count(e => e.Stage == "U"));
        Assert.Single(entries, e => e.Stage == "mutation"); Assert.Single(entries, e => e.Stage == "D");
        var replay = new Capture(); Assert.Equal(ArtifactExecutionStatus.Completed, await Register(replay, historicalOnly: true));
        Assert.Equal(200, replay.Status); Assert.Equal(2, _server.Requests.Count);
        using var replayJson = JsonDocument.Parse(replay.Bytes!);
        Assert.Equal(id, replayJson.RootElement.GetProperty("registration").GetProperty("registrationId").GetGuid());
        var conflict = new Capture();
        Assert.Equal(ArtifactExecutionStatus.Completed, await Register(conflict, command: _command with { SourceClaim = new("team/frontend", new string('a', 40), null) }));
        Assert.Equal(409, conflict.Status); Assert.Equal(2, _server.Requests.Count);
        using var conflictJson = JsonDocument.Parse(conflict.Bytes!); Assert.Equal(JsonValueKind.Null, conflictJson.RootElement.GetProperty("registration").ValueKind);
    }

    [Fact]
    public async Task Distinct_keys_share_artifact_but_keep_first_timestamp_and_independent_claims()
    {
        var command = _command with { SourceClaim = new("team/frontend", new string('a', 40), "build-1") };
        var first = new Capture(); Assert.Equal(ArtifactExecutionStatus.Completed, await Register(first, command: command));
        _facts.Now = _facts.Now.AddSeconds(1);
        var second = new Capture(); Assert.Equal(ArtifactExecutionStatus.Completed, await Register(second,
            command: command with { SourceClaim = new("team/frontend", new string('b', 40), "build-2") }, key: Key + "-second"));
        Assert.Equal(201, second.Status); Assert.Equal(4, _server.Requests.Count);
        await using var db = await _art.CreateDbContextAsync();
        Assert.Equal(1, await db.Artifacts.CountAsync(a => a.ProjectId == _facts.Project.ProjectId));
        Assert.Equal(2, await db.ProvenanceClaims.CountAsync(a => a.ProjectId == _facts.Project.ProjectId));
        using var one = JsonDocument.Parse(first.Bytes!); using var two = JsonDocument.Parse(second.Bytes!);
        var a = one.RootElement.GetProperty("registration"); var b = two.RootElement.GetProperty("registration");
        Assert.Equal(a.GetProperty("artifact").GetProperty("verifiedAtUtc").GetString(), b.GetProperty("artifact").GetProperty("verifiedAtUtc").GetString());
        Assert.NotEqual(a.GetProperty("attemptVerifiedAtUtc").GetString(), b.GetProperty("attemptVerifiedAtUtc").GetString());
        Assert.Equal(new string('b', 40), b.GetProperty("sourceClaim").GetProperty("sourceCommit").GetString());
    }

    [Fact]
    public async Task Concurrent_distinct_keys_produce_one_artifact_and_independent_receipts_and_claims()
    {
        var one = new Capture(); var two = new Capture();
        var first = _command with { SourceClaim = new("team/frontend", new string('a', 40), "build-1") };
        var second = _command with { CorrelationId = Guid.NewGuid(), SourceClaim = new("team/frontend", new string('b', 40), "build-2") };
        var results = await Task.WhenAll(Register(one, command: first, key: Key + "-one"), Register(two, command: second, key: Key + "-two"));
        Assert.All(results, x => Assert.Equal(ArtifactExecutionStatus.Completed, x));
        Assert.Equal(201, one.Status); Assert.Equal(201, two.Status); Assert.Equal(4, _server.Requests.Count);
        using var firstBody = JsonDocument.Parse(one.Bytes!); using var secondBody = JsonDocument.Parse(two.Bytes!);
        var firstReceipt = firstBody.RootElement.GetProperty("registration"); var secondReceipt = secondBody.RootElement.GetProperty("registration");
        Assert.NotEqual(firstReceipt.GetProperty("registrationId").GetGuid(), secondReceipt.GetProperty("registrationId").GetGuid());
        Assert.Equal(firstReceipt.GetProperty("artifact").GetProperty("artifactId").GetGuid(), secondReceipt.GetProperty("artifact").GetProperty("artifactId").GetGuid());
        Assert.Equal(new string('a', 40), firstReceipt.GetProperty("sourceClaim").GetProperty("sourceCommit").GetString());
        Assert.Equal(new string('b', 40), secondReceipt.GetProperty("sourceClaim").GetProperty("sourceCommit").GetString());
        await using var db = await _art.CreateDbContextAsync();
        Assert.Equal(1, await db.Artifacts.CountAsync(a => a.ProjectId == _facts.Project.ProjectId));
        Assert.Equal(2, await db.ProvenanceClaims.CountAsync(a => a.ProjectId == _facts.Project.ProjectId));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Changed_authority_during_each_audit_stage_never_discloses_and_preserves_confirmed_M(int stage)
    {
        var response = new Capture(); var hooks = new Hooks(count => { if (count == stage) _facts.Grant = _facts.Grant with { AssignmentRevision = 2 }; });
        Assert.Equal(ArtifactExecutionStatus.Denied, await Register(response, hooks)); Assert.Null(response.Bytes);
        await using var db = await _art.CreateDbContextAsync();
        var rows = await db.Registrations.Where(r => r.InstallationId == _facts.Installation).ToArrayAsync();
        if (stage == 1) Assert.Empty(rows);
        else Assert.Equal(stage == 4 ? "Registered" : "Failed", Assert.Single(rows).State);
        Assert.Equal(stage == 4 ? 1 : 0, await db.Artifacts.CountAsync(a => a.ProjectId == _facts.Project.ProjectId));
        await using var held = await _gate.AcquireReadAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Lost_commit_ACK_at_each_stage_fences_without_guessed_failure_or_body(int stage)
    {
        var response = new Capture(); Assert.Equal(ArtifactExecutionStatus.Unknown, await Register(response, new Hooks(_ => { }, stage)));
        Assert.True(response.Aborted); Assert.Null(response.Bytes); Assert.Equal(stage <= 2 ? 0 : 2, _server.Requests.Count);
        await using var db = await _art.CreateDbContextAsync();
        var row = await db.Registrations.SingleAsync(r => r.InstallationId == _facts.Installation);
        Assert.Equal(stage <= 2 ? "Pending" : "Registered", row.State);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
    }

    [Theory]
    [InlineData("provider", 503)]
    [InlineData("platform", 422)]
    [InlineData("hash", 422)]
    public async Task Known_provider_or_content_failures_have_safe_audited_outcomes(string fault, int expected)
    {
        if (fault == "provider") _server.Handler = _ => "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\n\r\n";
        if (fault == "hash") _root = "{\"changed\":true}"u8.ToArray();
        if (fault == "platform")
        { _config = "{\"os\":\"windows\",\"architecture\":\"amd64\"}"u8.ToArray(); _root = Image(_config); _command = _command with { Digest = Hash(_root) }; }
        var response = new Capture(); Assert.Equal(ArtifactExecutionStatus.Completed, await Register(response)); Assert.Equal(expected, response.Status);
        using var json = JsonDocument.Parse(response.Bytes!); Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("registration").ValueKind);
        await using var db = await _art.CreateDbContextAsync();
        Assert.Equal("Failed", (await db.Registrations.SingleAsync(r => r.InstallationId == _facts.Installation)).State);
        Assert.False(await db.Artifacts.AnyAsync(a => a.ProjectId == _facts.Project.ProjectId));
    }

    [Fact]
    public async Task Provider_IO_is_outside_the_common_gate_and_revocation_before_M_prevents_artifact()
    {
        _server.AsyncHandler = async request =>
        { await using var held = await _gate.AcquireWriteAsync(); _facts.Grant = _facts.Grant with { AssignmentRevision = 2 }; return Response(request); };
        var response = new Capture(); Assert.Equal(ArtifactExecutionStatus.Denied, await Register(response)); Assert.Null(response.Bytes);
        await using var db = await _art.CreateDbContextAsync(); Assert.False(await db.Artifacts.AnyAsync(a => a.ProjectId == _facts.Project.ProjectId));
    }

    [Fact]
    public async Task Minimum_schema_checks_and_non_destructive_downgrade_are_explicit()
    {
        Assert.True(await new ArtifactWriteReadiness(_art, _sec).CheckUnderAdmissionAsync(default));
        await using var db = await _art.CreateDbContextAsync();
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript("20261025124612_InitialArtifactRegistration", "0"));
    }

    [Fact]
    public async Task Concurrent_same_key_observes_Pending_without_a_second_provider_attempt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.AsyncHandler = async request =>
        {
            if (request.Contains("/manifests/", StringComparison.Ordinal))
            { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
            return Response(request);
        };
        var first = new Capture(); var active = Register(first);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var replay = new Capture(); Assert.Equal(ArtifactExecutionStatus.Completed, await Register(replay));
            Assert.Equal(409, replay.Status); Assert.Single(_server.Requests);
            using var body = JsonDocument.Parse(replay.Bytes!);
            Assert.Equal("registration_in_progress", body.RootElement.GetProperty("code").GetString());
        }
        finally { release.TrySetResult(); }
        Assert.Equal(ArtifactExecutionStatus.Completed, await active); Assert.Equal(201, first.Status);
        Assert.Equal(2, _server.Requests.Count);
        await using var db = await _art.CreateDbContextAsync();
        Assert.Equal(1, await db.Registrations.CountAsync(r => r.InstallationId == _facts.Installation));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Original_use_expiry_at_M_or_D_is_not_renewed(int stage)
    {
        var response = new Capture(); var hooks = new Hooks(count => { if (count == stage) _facts.Now = _facts.Now.AddSeconds(5); });
        Assert.Equal(ArtifactExecutionStatus.Denied, await Register(response, hooks)); Assert.Null(response.Bytes);
        await using var db = await _art.CreateDbContextAsync();
        Assert.Equal(stage == 4 ? "Registered" : "Failed", (await db.Registrations.SingleAsync(r => r.InstallationId == _facts.Installation)).State);
    }

    [Fact]
    public async Task Shortened_expiry_seen_at_U_is_not_restored_at_C()
    {
        var hooks = new Hooks(count =>
        {
            if (count == 1) _facts.Grant = _facts.Grant with { ExpiresAtUtc = _facts.Now.AddSeconds(1) };
            if (count == 2) { _facts.Now = _facts.Now.AddSeconds(1); _facts.Grant = _facts.Grant with { ExpiresAtUtc = _facts.Now.AddMinutes(20) }; }
        });
        var response = new Capture(); Assert.Equal(ArtifactExecutionStatus.Denied, await Register(response, hooks));
        Assert.Empty(_server.Requests); Assert.Null(response.Bytes);
    }

    [Fact]
    public async Task Cancellation_after_U_drains_known_work_and_records_only_exact_failed_attempt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.AsyncHandler = async request =>
        { entered.TrySetResult(); await Task.Delay(TimeSpan.FromSeconds(2)); return Response(request); };
        using var cancellation = new CancellationTokenSource(); var response = new Capture();
        var running = Coordinator().ExecuteAsync(_facts.Selection, _facts.Project.ProjectId, _command, Key, response, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); await cancellation.CancelAsync();
        Assert.Equal(ArtifactExecutionStatus.Unavailable, await running.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(response.Aborted); Assert.Null(response.Bytes);
        await using var db = await _art.CreateDbContextAsync();
        var row = await db.Registrations.SingleAsync(r => r.InstallationId == _facts.Installation);
        Assert.Equal("Failed", row.State); Assert.Equal("cancelled", row.SafeReason);
        Assert.False(await db.Artifacts.AnyAsync(a => a.ProjectId == _facts.Project.ProjectId));
        await using var held = await _gate.AcquireReadAsync();
        using var capacity = ProtectedInvocationCapacity.TryReserve(_gate); Assert.NotNull(capacity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authenticated_connection_without_binding_or_helper_never_falls_back_to_anonymous(bool binding)
    {
        var trust = _server.Trust("static-bearer", installation: _facts.Installation, tenant: _facts.Project.TenantId,
            project: _facts.Project.ProjectId, registry: _facts.Registry);
        _configuration = new(_facts.Installation, new string('f', 64), [trust]);
        if (binding)
        {
            await using var db = await _sec.CreateDbContextAsync(); await using var transaction = await db.Database.BeginTransactionAsync();
            var manifest = new RegistryCredentialManifest(1, Guid.NewGuid(), _facts.Installation, _facts.Project.TenantId, _facts.Project.ProjectId,
                _facts.Registry, "team/frontend", "artifact.registry.read.v1", "artifact.register", "create", null, 0, Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _facts.Now.AddMinutes(-1), _facts.Now.AddHours(1));
            Assert.NotNull(await new RegistryCredentialBindingStore(db).StageAsync(new SyntheticCustody(), manifest, _facts.Now));
            await db.SaveChangesAsync(); await transaction.CommitAsync(); // Metadata only: no credential or Keychain item exists.
        }
        var response = new Capture(); Assert.Equal(binding ? ArtifactExecutionStatus.Unavailable : ArtifactExecutionStatus.Denied, await Register(response));
        Assert.Null(response.Bytes); Assert.Empty(_server.Requests);
        await using var art = await _art.CreateDbContextAsync();
        Assert.False(await art.Registrations.AnyAsync(r => r.InstallationId == _facts.Installation));
    }

    private sealed class SyntheticCustody : IOfflineProvisioningLease { public void RequireHeld() { } }

    private sealed class Capture : IArtifactResponseCommitter
    {
        public byte[]? Bytes; public int Status; public ArtifactResponseCommitment? Selection; public bool Aborted;
        public Task CommitAsync(ArtifactResponseCommitment s, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Selection = s; Status = s.StatusCode; Bytes = bytes.ToArray(); return Task.CompletedTask; }
        public void Abort() => Aborted = true;
    }
    private sealed class Facts : ICurrentSubjectReader, IScopedGrantReader, IRoleDefinitionReader, IArtifactProjectAuthorityReader, IAuthorityClock
    {
        public Guid Installation = Guid.NewGuid(), Registry = Guid.NewGuid();
        public DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public readonly ArtifactProjectAuthority Project = new(Guid.NewGuid(), Guid.NewGuid(), 1);
        public readonly CurrentSubject Subject;
        public ScopedGrant Grant;
        public SessionSelection Selection { get; }
        public Facts()
        {
            Subject = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Now.AddHours(1), Now.AddMinutes(30));
            Grant = new(Subject.PrincipalId, Project.TenantId, Guid.NewGuid(), 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(), 1, "artifact.register", "project", Project.ProjectId, Now.AddMinutes(20));
            Selection = new(Subject.PrincipalId, new string('a', 43), Subject.BootGeneration);
        }
        public DateTimeOffset GetTrustedUtcNow() => Now;
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection s, CancellationToken ct = default) => Task.FromResult<CurrentSubject?>(Subject);
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid p, Guid t, string a, string k, Guid id, CancellationToken ct = default) => Task.FromResult<ScopedGrant?>(Grant);
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken ct = default) => Task.FromResult<RoleDefinitionSnapshot?>(new(Grant.DefinitionId, Grant.DefinitionRevision, "Label", [new("artifact.register", "project")]));
        public Task<bool> IsActivePrincipalAsync(Guid p, CancellationToken ct = default) => Task.FromResult(p == Subject.PrincipalId);
        public Task<ArtifactProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid project, CancellationToken ct = default) => Task.FromResult<ArtifactProjectAuthority?>(Project);
    }
    private sealed class Hooks(Action<int> staged, int loseAck = 0) : IArtifactAuditTransactionFactory
    {
        private int _count;
        public async ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection c, DbTransaction t, CancellationToken ct) =>
            new Enlisted(await new ArtifactAuditTransactionFactory().EnlistAsync(c, t, ct), staged, Interlocked.Increment(ref _count), loseAck);
        private sealed class Enlisted(IArtifactAuditTransaction inner, Action<int> hook, int count, int lose) : IArtifactAuditTransaction
        {
            public bool CommitAttempted => inner.CommitAttempted;
            public async Task<AuditTransactionStageOutcome> StageAsync(AuditIntent i, CancellationToken ct) { var r = await inner.StageAsync(i, ct); hook(count); return r; }
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct) { var r = await inner.CommitAsync(ct); if (count == lose) throw new IOException("Synthetic lost ACK"); return r; }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
