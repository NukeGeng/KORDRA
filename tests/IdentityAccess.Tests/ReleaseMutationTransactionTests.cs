using System.Data.Common;
using System.Reflection;
using Kordra.Api;
using Kordra.ApprovalGovernance.Domain;
using System.Text;
using System.Globalization;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.ArtifactManagement.Contracts;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.ReleaseManagement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class ReleaseMutationTransactionTests : IAsyncLifetime
{
    private readonly Facts _facts = new();
    private readonly FairAsyncReaderWriterAdmissionGate _gate = new();
    private PooledDbContextFactory<KordraIdentityDbContext> _iam = null!;
    private PooledDbContextFactory<ReleaseDbContext> _rel = null!;
    private PooledDbContextFactory<AuditDbContext> _aud = null!;
    public async Task InitializeAsync()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Private isolated test locator required.");
        _iam = new(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(locator,
            p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
        _rel = new(ReleaseDbContextFactory.CreateTestOptions(locator)); _aud = new(AuditDbContextFactory.CreateTestOptions(locator));
        await using (var db = await _iam.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _rel.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _aud.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(locator))) await db.Database.MigrateAsync();
    }
    // Synthetic immutable histories intentionally persist only in isolated kordra_test; no guard is disabled.
    public Task DisposeAsync() => Task.CompletedTask;
    private ReleaseMutationTransaction Runner(IArtifactAuditTransactionFactory factory, IAuditMutationEvidenceReader? evidence = null) => new(_facts.Installation, _iam,
        new(_facts, _facts, _facts, _facts, _facts), new(_facts, _facts, _facts, _facts), factory, evidence ?? new AuditMutationEvidenceReader(_aud), _gate, _facts);
    private ReleaseUseAdmission Uses(IArtifactAuditTransactionFactory? factory = null) => new(_facts.Installation, _iam,
        new(_facts, _facts, _facts, _facts, _facts), factory ?? new ArtifactAuditTransactionFactory(), _gate, _facts);
    private Task<ReleaseMutationResult> Run(IArtifactAuditTransactionFactory factory) => RunCommand(factory,
        ReleaseMutationCommand.Create(_facts.Project, "Synthetic transaction", _facts.Selection), "synthetic-release-key-1234");
    private async Task<ReleaseMutationResult> RunCommand(IArtifactAuditTransactionFactory factory, ReleaseMutationCommand command,
        string key, IAuditMutationEvidenceReader? evidence = null)
    {
        var use = await Uses().BeginAsync(_facts.Session, command, key, Guid.NewGuid());
        Assert.Equal("admitted", use.Status); Assert.NotNull(use.Ticket);
        return await Runner(factory, evidence).ExecuteAsync(use.Ticket);
    }

    [Fact]
    public async Task MetadataReadinessChecksRealIsolatedSchemaAndRuntimePrivileges()
    {
        Assert.True(await new ReleaseSchemaReadiness(_facts.Installation, _rel).CheckAsync());
    }
    [Fact]
    public async Task ReadinessAcceptsMatchingCommittedOwnerAndAuditHistory()
    {
        Assert.Equal("durable", (await Run(new Faults("none", _facts))).Status);
        Assert.True(await new ReleaseSchemaReadiness(_facts.Installation, _rel).CheckAsync());
    }
    [Fact]
    public async Task ReadinessRejectsPersistedAuditOnlyEvidenceForThisInstallation()
    {
        var plan = RecoveryPlan(); var intent = new AuditIntent(1, ReleaseCommandHash.AuditIntentId(plan.ScopedKeyHash), Guid.NewGuid(),
            AuditIntentStage.Mutation, _facts.Subject.PrincipalId, _facts.Tenant, Guid.NewGuid(), "release.create", "project", _facts.Project,
            null, 1, ReleaseCommandHash.AuditReason(plan.InstallationId, plan.ScopedKeyHash, plan.BusinessHash), _facts.Now, Guid.NewGuid());
        Assert.Equal(AuditAppendStatus.Durable, (await new AuditAppendStore(_aud).AppendAsync(intent)).Status);
        Assert.False(await new ReleaseSchemaReadiness(_facts.Installation, _rel).CheckAsync());
        Assert.True(await new ReleaseSchemaReadiness(Guid.NewGuid(), _rel).CheckAsync());
    }
    [Fact]
    public async Task MissingReadinessPreventsUseAuditAndMutation()
    {
        var coordinator = new ReleaseMutationExecutionCoordinator(Uses(), Runner(new Faults("none", _facts)), Disclosure(new Faults("none", _facts)), _gate, new NotReady());
        var command = ReleaseMutationCommand.Create(_facts.Project, "Synthetic", _facts.Selection);
        Assert.Equal("unavailable", await coordinator.ExecuteAsync(_facts.Session, command, "synthetic-key-1234", Guid.NewGuid(), new Capture()));
        await using var aud = await _aud.CreateDbContextAsync(); Assert.False(await aud.Entries.AnyAsync(x => x.TenantRef == _facts.Tenant));
    }
    private sealed class NotReady : IReleaseSchemaReadiness { public Task<bool> CheckAsync(CancellationToken ct = default) => Task.FromResult(false); }

    [Fact]
    public async Task ProjectReadUsesFreshUseAndDisclosureAuditForDetailAndPages()
    {
        var created = await Run(new Faults("none", _facts)); Assert.Equal("durable", created.Status);
        _facts.Grant = _facts.Grant with { Permission = "release.read" };
        var reader = ReadCoordinator(new ArtifactAuditTransactionFactory()); var capture = new Capture();
        var query = new ReleaseReadQuery(ReleaseReadKind.Detail, Guid.NewGuid(), created.RevisionId);
        Assert.Equal("completed", await reader.ExecuteAsync(_facts.Session, _facts.Project, query, capture));
        Assert.Equal(200, capture.Selection!.StatusCode); Assert.Equal($"\"rel-v1:{created.RevisionId:D}:1\"", capture.Selection.Etag);
        await using var aud = await _aud.CreateDbContextAsync();
        var stages = await aud.Entries.Where(x => x.InvocationId == capture.Selection.InvocationId).Select(x => x.Stage).ToArrayAsync();
        Assert.Equal(new[] { "D", "U" }, stages.Order(StringComparer.Ordinal).ToArray());
        var list = new Capture();
        Assert.Equal("completed", await reader.ExecuteAsync(_facts.Session, _facts.Project, new(ReleaseReadKind.Lineages, Guid.NewGuid()), list));
        Assert.Equal(64 * 1024, list.Selection!.MaximumBytes); Assert.Null(list.Selection.Etag);
        var revisions = new Capture();
        using var body = System.Text.Json.JsonDocument.Parse(capture.Bytes!);
        var release = body.RootElement.GetProperty("revision").GetProperty("releaseId").GetGuid();
        Assert.Equal("completed", await reader.ExecuteAsync(_facts.Session, _facts.Project, new(ReleaseReadKind.Revisions, Guid.NewGuid(), release), revisions));
        using var page = System.Text.Json.JsonDocument.Parse(revisions.Bytes!);
        Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
    }
    [Fact]
    public async Task AuthorizedForeignAndMissingDetailHaveTheSameBoundedError()
    {
        _facts.Grant = _facts.Grant with { Permission = "release.read" };
        var reader = ReadCoordinator(new ArtifactAuditTransactionFactory()); var response = new Capture();
        Assert.Equal("completed", await reader.ExecuteAsync(_facts.Session, _facts.Project, new(ReleaseReadKind.Detail, Guid.NewGuid(), Guid.NewGuid()), response));
        Assert.Equal(404, response.Selection!.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(response.Bytes!); Assert.Equal("not_found", body.RootElement.GetProperty("code").GetString());
    }
    [Theory]
    [InlineData("authority")]
    [InlineData("expiry")]
    [InlineData("stage")]
    [InlineData("ack")]
    [InlineData("owner")]
    public async Task ReadWithholdsBodyIfDisclosureFactsChange(string fault)
    {
        if (fault == "owner") _facts.AllowPilot(TimeSpan.FromMinutes(5));
        var created = await Run(new Faults("none", _facts)); _facts.Grant = _facts.Grant with { Permission = "release.read" };
        var response = new Capture(); var reader = ReadCoordinator(new SecondAuditFault(fault, _facts));
        var result = await reader.ExecuteAsync(_facts.Session, _facts.Project, new(ReleaseReadKind.Detail, Guid.NewGuid(), created.RevisionId), response);
        Assert.Equal(fault == "ack" ? "unknown" : fault is "authority" or "expiry" ? "denied" : "unavailable", result);
        Assert.Null(response.Bytes);
        if (fault == "ack") await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
        else { await using var held = await _gate.AcquireReadAsync(); }
    }
    private ReleaseReadCoordinator ReadCoordinator(IArtifactAuditTransactionFactory factory) => new(_facts.Installation, _iam, _rel,
        new(_facts, _facts, _facts, _facts, _facts), new(_facts, _facts, _facts, _facts), factory, _gate, _facts, new Ready());
    private sealed class Ready : IReleaseSchemaReadiness { public Task<bool> CheckAsync(CancellationToken ct = default) => Task.FromResult(true); }
    private sealed class SecondAuditFault(string fault, Facts facts) : IArtifactAuditTransactionFactory
    {
        private int _count;
        public ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection c, DbTransaction t, CancellationToken ct) =>
            Interlocked.Increment(ref _count) == 1 ? new ArtifactAuditTransactionFactory().EnlistAsync(c, t, ct) : new Faults(fault, facts).EnlistAsync(c, t, ct);
    }

    [Fact]
    public async Task IntegratedUseMutationDisclosureAndReplayShareTheBoundInvocation()
    {
        var coordinator = new ReleaseMutationExecutionCoordinator(Uses(), Runner(new Faults("none", _facts)), Disclosure(new Faults("none", _facts)), _gate, new Ready());
        var command = ReleaseMutationCommand.Create(_facts.Project, "Synthetic transaction", _facts.Selection);
        var first = new Capture();
        Assert.Equal("completed", await coordinator.ExecuteAsync(_facts.Session, command, "synthetic-release-key-1234", Guid.NewGuid(), first));
        Assert.Equal(201, first.Selection!.StatusCode);
        await using var aud = await _aud.CreateDbContextAsync();
        var stages = await aud.Entries.Where(x => x.InvocationId == first.Selection.InvocationId).Select(x => x.Stage).ToArrayAsync();
        Assert.Equal(new[] { "D", "U", "mutation" }, stages.Order(StringComparer.Ordinal).ToArray());
        var replay = new Capture();
        Assert.Equal("completed", await coordinator.ExecuteAsync(_facts.Session, command, "synthetic-release-key-1234", Guid.NewGuid(), replay));
        Assert.Equal(200, replay.Selection!.StatusCode);
        await using var rel = await _rel.CreateDbContextAsync(); Assert.Equal(1, await rel.Commands.CountAsync(x => x.InstallationId == _facts.Installation));
    }
    [Fact]
    public async Task UnresolvedUseTaskAtDeadlineFencesAndNeverMutates()
    {
        var delayed = new DelayedAudit();
        var coordinator = new ReleaseMutationExecutionCoordinator(Uses(delayed), Runner(new Faults("none", _facts)), Disclosure(new Faults("none", _facts)), _gate, new Ready());
        var response = new Capture(); var command = ReleaseMutationCommand.Create(_facts.Project, "Synthetic", _facts.Selection);
        var result = await coordinator.ExecuteAsync(_facts.Session, command, "synthetic-use-key-1234", Guid.NewGuid(), response);
        Assert.Equal("unknown", result); Assert.True(response.Aborted);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
        delayed.Release();
        await using var rel = await _rel.CreateDbContextAsync(); Assert.False(await rel.Commands.AnyAsync(x => x.InstallationId == _facts.Installation));
    }
    private sealed class DelayedAudit : IArtifactAuditTransactionFactory
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _release.TrySetResult();
        public async ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction tx, CancellationToken ct)
        { await _release.Task; return await new ArtifactAuditTransactionFactory().EnlistAsync(connection, tx, ct); }
    }

    [Fact]
    public async Task FreshDisclosureAuditsExactBodyAndAllowsOnlyOneHandoff()
    {
        var mutation = await Run(new Faults("none", _facts)); Assert.Equal("durable", mutation.Status);
        var response = new Capture(); var disclosure = Disclosure(new Faults("none", _facts));
        Assert.Equal("completed", await disclosure.DiscloseAsync(mutation, response));
        Assert.Equal(201, response.Selection!.StatusCode); Assert.Equal($"\"rel-v1:{mutation.RevisionId:D}:1\"", response.Selection.Etag);
        Assert.Contains("\"executionAuthorized\":false", Encoding.UTF8.GetString(response.Bytes!));
        await using var aud = await _aud.CreateDbContextAsync();
        var entry = await aud.Entries.SingleAsync(x => x.TenantRef == _facts.Tenant && x.Stage == "D");
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(response.Bytes!)), entry.ResultDigest);
        Assert.Equal("invalid", await disclosure.DiscloseAsync(mutation, new Capture()));
    }
    [Theory]
    [InlineData("authority")]
    [InlineData("expiry")]
    [InlineData("stage")]
    [InlineData("ack")]
    [InlineData("owner")]
    public async Task DisclosureFailureWithholdsBodyAndPreservesCommittedMutation(string fault)
    {
        if (fault == "owner") _facts.AllowPilot(TimeSpan.FromMinutes(5));
        var mutation = await Run(new Faults("none", _facts)); Assert.Equal("durable", mutation.Status);
        var response = new Capture(); var result = await Disclosure(new Faults(fault, _facts)).DiscloseAsync(mutation, response);
        Assert.Equal(fault == "ack" ? "unknown" : fault is "authority" or "expiry" ? "denied" : "unavailable", result);
        Assert.Null(response.Bytes);
        await using var rel = await _rel.CreateDbContextAsync(); Assert.True(await rel.Commands.AnyAsync(x => x.InstallationId == _facts.Installation));
        if (fault == "ack") await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
        else { await using var held = await _gate.AcquireReadAsync(); }
    }
    [Fact]
    public async Task FreezeDisclosureRechecksPinnedOwnersAfterMutation()
    {
        var initial = await Run(new Faults("none", _facts)); Assert.Equal("durable", initial.Status); _facts.AllowPilot(TimeSpan.FromMinutes(5));
        var preview = await new ReleaseSelectionReader(_facts, _facts, _facts, _facts).ReadUnderAdmissionAsync(
            _facts.Installation, _facts.Tenant, _facts.Project, _facts.Selection, _facts.Now);
        var id = initial.RevisionId!.Value;
        var command = ReleaseMutationCommand.Freeze(_facts.Project, id, 1, $"\"rel-v1:{id:D}:1\"", ReleasePreviewHash.Compute(id, 1, preview.Intent!.SpecHash));
        var frozen = await RunCommand(new Faults("none", _facts), command, "synthetic-freeze-key-1234"); Assert.Equal("durable", frozen.Status);
        _facts.TargetRevision++; var response = new Capture();
        Assert.Equal("denied", await Disclosure(new Faults("none", _facts)).DiscloseAsync(frozen, response));
        Assert.Null(response.Bytes);
        await using var rel = await _rel.CreateDbContextAsync(); Assert.Equal("Frozen", (await rel.Revisions.SingleAsync(x => x.Id == id)).State);
    }

    [Fact]
    public async Task FailedHandoffFencesWithoutReversingMutation()
    {
        var mutation = await Run(new Faults("none", _facts)); var response = new Capture { Fail = true };
        Assert.Equal("unknown", await Disclosure(new Faults("none", _facts)).DiscloseAsync(mutation, response));
        Assert.True(response.Aborted);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
        await using var rel = await _rel.CreateDbContextAsync(); Assert.True(await rel.Commands.AnyAsync(x => x.InstallationId == _facts.Installation));
    }
    private ReleaseMutationDisclosure Disclosure(IArtifactAuditTransactionFactory factory) => new(_facts.Installation, _iam, _rel,
        new(_facts, _facts, _facts, _facts, _facts), new(_facts, _facts, _facts, _facts), factory, _gate, _facts);
    private sealed class Capture : IReleaseResponseCommitter
    {
        public ReleaseResponseCommitment? Selection; public byte[]? Bytes; public bool Aborted; public bool Fail;
        public Task CommitAsync(ReleaseResponseCommitment selection, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        { if (Fail) throw new IOException("Synthetic handoff failure"); Selection = selection; Bytes = bytes.ToArray(); return Task.CompletedTask; }
        public void Abort() => Aborted = true;
    }

    [Theory]
    [InlineData("enlist")]
    [InlineData("stage")]
    [InlineData("authority")]
    [InlineData("expiry")]
    [InlineData("ack")]
    public async Task UseAuditMustCompleteBeforeTicketIsIssued(string fault)
    {
        var hook = new Faults(fault, _facts); var command = ReleaseMutationCommand.Create(_facts.Project, "Synthetic", _facts.Selection);
        var result = await Uses(hook).BeginAsync(_facts.Session, command, "synthetic-use-key-1234", Guid.NewGuid());
        Assert.Equal(fault == "ack" ? "unknown" : fault is "authority" or "expiry" ? "denied" : "unavailable", result.Status);
        Assert.Null(result.Ticket);
        await using var rel = await _rel.CreateDbContextAsync(); Assert.False(await rel.Commands.AnyAsync(x => x.InstallationId == _facts.Installation));
        if (fault == "ack") await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
        else { await using var held = await _gate.AcquireReadAsync(); }
    }
    [Theory]
    [InlineData("assignment")]
    [InlineData("ten-second-expiry")]
    [InlineData("clock-rollback")]
    public async Task ChangedAuthorityOrExpiredUseTicketCannotStartMutation(string fault)
    {
        var command = ReleaseMutationCommand.Create(_facts.Project, "Synthetic", _facts.Selection);
        var use = await Uses().BeginAsync(_facts.Session, command, "synthetic-use-key-1234", Guid.NewGuid());
        Assert.Equal("admitted", use.Status); Assert.NotNull(use.Ticket);
        if (fault == "assignment") _facts.Grant = _facts.Grant with { AssignmentRevision = 2 };
        else if (fault == "clock-rollback") _facts.Now = _facts.Now.AddSeconds(-1);
        else _facts.Now = _facts.Now.AddSeconds(10);
        Assert.Equal("denied", (await Runner(new Faults("none", _facts)).ExecuteAsync(use.Ticket)).Status);
        Assert.Equal("invalid", (await Runner(new Faults("none", _facts)).ExecuteAsync(use.Ticket)).Status);
        await using var rel = await _rel.CreateDbContextAsync(); Assert.False(await rel.Commands.AnyAsync(x => x.InstallationId == _facts.Installation));
    }
    [Fact]
    public async Task UseTicketCannotCrossInstallationOrGate()
    {
        var command = ReleaseMutationCommand.Create(_facts.Project, "Synthetic", _facts.Selection);
        var use = await Uses().BeginAsync(_facts.Session, command, "synthetic-use-key-1234", Guid.NewGuid()); Assert.NotNull(use.Ticket);
        var other = new ReleaseMutationTransaction(Guid.NewGuid(), _iam, new(_facts, _facts, _facts, _facts, _facts),
            new(_facts, _facts, _facts, _facts), new ArtifactAuditTransactionFactory(), new AuditMutationEvidenceReader(_aud), _gate, _facts);
        Assert.Equal("invalid", (await other.ExecuteAsync(use.Ticket)).Status);
        Assert.Equal("durable", (await Runner(new Faults("none", _facts)).ExecuteAsync(use.Ticket)).Status);
    }

    [Theory]
    [InlineData("enlist")]
    [InlineData("stage")]
    [InlineData("authority")]
    [InlineData("expiry")]
    public async Task PrecommitFailureDoesNotPersistOrFence(string fault)
    {
        var hook = new Faults(fault, _facts); var outcome = await Run(hook);
        Assert.Equal(fault is "authority" or "expiry" ? "denied" : "unavailable", outcome.Status);
        Assert.False(hook.Attempted); await AssertRows(false); await using var held = await _gate.AcquireReadAsync();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneCommitPersistsReleaseAuditAndFloorAndLostAcknowledgementFences(bool loseAck)
    {
        var hook = new Faults(loseAck ? "ack" : "none", _facts); var outcome = await Run(hook);
        Assert.True(hook.Attempted); Assert.Equal(loseAck ? "unknown" : "durable", outcome.Status);
        Assert.Equal(!loseAck, outcome.RevisionId.HasValue); await AssertRows(true);
        if (loseAck)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
            var coordinator = new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new Quiescence());
            var inspection = await coordinator.InspectAsync(new Custody(), RecoveryPlan().CanonicalBytes());
            Assert.Equal("confirmed", inspection.Status); Assert.NotNull(inspection.BoundPlan);
            var bound = inspection.BoundPlan; var recovered = await coordinator.ApplyAsync(new Custody(), bound.CanonicalBytes(), bound.Hash());
            Assert.Equal("confirmed", recovered.Status); Assert.NotNull(recovered.CommandId);
        }
        else { await using var held = await _gate.AcquireReadAsync(); }
    }

    [Fact]
    public async Task DroppedPostgresCommitResponseFencesAndStoppedRecoveryFindsDurableMutation()
    {
        var fault = new DroppedCommitResponse();
        var result = await Run(fault);
        Assert.True(fault.CommitAttempted);
        Assert.True(fault.Transport!.SentCommit);
        Assert.True(fault.Transport.DiscardedCommitResponse);
        Assert.Equal("unknown", result.Status);
        Assert.Null(result.RevisionId);
        await AssertRows(true);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
        var recovery = new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new Quiescence());
        var inspected = await recovery.InspectAsync(new Custody(), RecoveryPlan().CanonicalBytes());
        Assert.Equal("confirmed", inspected.Status);
        Assert.NotNull(inspected.BoundPlan);
        var recovered = await recovery.ApplyAsync(new Custody(), inspected.BoundPlan.CanonicalBytes(), inspected.BoundPlan.Hash());
        Assert.Equal("confirmed", recovered.Status);
        Assert.NotNull(recovered.CommandId);
    }

    [Fact]
    public async Task DroppedCommitResponseRecoversUnderActualStoppedCustody()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var fault = new DroppedCommitResponse();
        Assert.Equal("unknown", (await Run(fault)).Status);
        Assert.True(fault.Transport!.DiscardedCommitResponse);
        await AssertRows(true);

        var workspace = new DirectoryInfo(AppContext.BaseDirectory);
        while (workspace is not null && !File.Exists(Path.Combine(workspace.FullName, "Kordra.slnx"))) workspace = workspace.Parent;
        Assert.NotNull(workspace);
        var root = Path.Combine(workspace.FullName, ".amr-local", "t11-design", "recovery-" + Guid.NewGuid().ToString("N"));
        var instance = Path.Combine(root, _facts.Installation.ToString("D"));
        Directory.CreateDirectory(instance);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(instance, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var identity = Path.Combine(root, "installation.id");
        File.WriteAllText(identity, _facts.Installation.ToString("D"));
        File.SetUnixFileMode(identity, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var type = typeof(LocalReleaseRecoveryCommand).Assembly.GetType("Kordra.Api.OfflineArtifactInstallation", true)!;
        var acquire = type.GetMethod("AcquireFromRoot", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            using var held = Assert.IsAssignableFrom<IDisposable>(acquire.Invoke(null, [root]));
            var custody = Assert.IsAssignableFrom<IOfflineProvisioningLease>(held);
            using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/cat")
            { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })
                ?? throw new InvalidOperationException("Synthetic recovery process could not start.");
            try
            {
                var plan = RecoveryPlan() with { OldProcessId = child.Id };
                var recovery = new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new ArtifactProcessQuiescence());
                Assert.Equal("denied", (await recovery.InspectAsync(custody, plan.CanonicalBytes())).Status);
                child.StandardInput.Close();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var inspected = await recovery.InspectAsync(custody, plan.CanonicalBytes());
                Assert.Equal("confirmed", inspected.Status);
                Assert.NotNull(inspected.BoundPlan);
                var recovered = await recovery.ApplyAsync(custody, inspected.BoundPlan.CanonicalBytes(), inspected.BoundPlan.Hash());
                Assert.Equal("confirmed", recovered.Status);
                Assert.NotNull(recovered.CommandId);
                await AssertRows(true);
            }
            finally
            {
                if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public async Task ExactReplayRequiresMatchingAuditAndDoesNotCommitAgain()
    {
        var first = await Run(new Faults("none", _facts)); Assert.Equal("durable", first.Status);
        var hook = new Faults("enlist", _facts); var replay = await Run(hook);
        Assert.Equal("replay", replay.Status); Assert.Equal(first.RevisionId, replay.RevisionId); Assert.False(hook.Attempted);
        await using var rel = await _rel.CreateDbContextAsync();
        Assert.Equal(1, await rel.Commands.CountAsync(x => x.InstallationId == _facts.Installation));
        var changed = await RunCommand(hook,
            ReleaseMutationCommand.Create(_facts.Project, "Different", _facts.Selection), "synthetic-release-key-1234");
        Assert.Equal("conflict", changed.Status);
    }

    [Fact]
    public async Task RecoveryUsesActualIsolatedInstallationLockWithoutRuntimeSecrets()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var workspace = new DirectoryInfo(AppContext.BaseDirectory);
        while (workspace is not null && !File.Exists(Path.Combine(workspace.FullName, "Kordra.slnx"))) workspace = workspace.Parent;
        Assert.NotNull(workspace);
        var root = Path.Combine(workspace.FullName, ".amr-local", "t11-design", "custody-" + Guid.NewGuid().ToString("N"));
        var instance = Path.Combine(root, _facts.Installation.ToString("D")); Directory.CreateDirectory(instance);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(instance, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var id = Path.Combine(root, "installation.id"); File.WriteAllText(id, _facts.Installation.ToString("D"));
        File.SetUnixFileMode(id, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var type = typeof(LocalReleaseRecoveryCommand).Assembly.GetType("Kordra.Api.OfflineArtifactInstallation", true)!;
        var acquire = type.GetMethod("AcquireFromRoot", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            using (var held = Assert.IsAssignableFrom<IDisposable>(acquire.Invoke(null, [root])))
            {
                var custody = Assert.IsAssignableFrom<IOfflineProvisioningLease>(held);
                var livePlan = RecoveryPlan() with { OldProcessId = Environment.ProcessId };
                var liveResult = await new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new ArtifactProcessQuiescence())
                    .InspectAsync(custody, livePlan.CanonicalBytes());
                Assert.Equal("denied", liveResult.Status);
                using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/cat")
                {
                    UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = true
                }) ?? throw new InvalidOperationException("Synthetic recovery process could not start.");
                try
                {
                    var childPlan = RecoveryPlan() with { OldProcessId = child.Id };
                    var realRecovery = new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new ArtifactProcessQuiescence());
                    Assert.Equal("denied", (await realRecovery.InspectAsync(custody, childPlan.CanonicalBytes())).Status);
                    child.StandardInput.Close();
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    var stopped = await realRecovery.InspectAsync(custody, childPlan.CanonicalBytes());
                    Assert.Equal("no_commit", stopped.Status);
                    Assert.NotNull(stopped.BoundPlan);
                    Assert.Equal("no_commit", (await realRecovery.ApplyAsync(custody, stopped.BoundPlan.CanonicalBytes(), stopped.BoundPlan.Hash())).Status);
                }
                finally
                {
                    // Only the synthetic child started by this test may be terminated.
                    if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
                }
                var result = await new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new Quiescence()).InspectAsync(custody, RecoveryPlan().CanonicalBytes());
                Assert.Equal("no_commit", result.Status);
                var conflict = Assert.Throws<TargetInvocationException>(() => acquire.Invoke(null, [root]));
                Assert.IsType<InvalidOperationException>(conflict.InnerException);
                Assert.False(File.Exists(Path.Combine(instance, "local-pilot.json")));
            }
            using var again = Assert.IsAssignableFrom<IDisposable>(acquire.Invoke(null, [root]));
            Assert.IsAssignableFrom<IOfflineProvisioningLease>(again).RequireHeld();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task StoppedRecoveryConfirmsExactCommitWithoutWritingOrGrantingAuthority()
    {
        var committed = await Run(new Faults("none", _facts)); Assert.Equal("durable", committed.Status);
        var recovery = new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new Quiescence());
        var plan = RecoveryPlan(); var custody = new Custody();
        var inspected = await recovery.InspectAsync(custody, plan.CanonicalBytes());
        Assert.Equal("confirmed", inspected.Status); Assert.NotNull(inspected.BoundPlan);
        var bound = inspected.BoundPlan;
        var result = await recovery.ApplyAsync(custody, bound.CanonicalBytes(), bound.Hash());
        Assert.Equal("confirmed", result.Status); Assert.NotNull(result.CommandId); Assert.NotNull(result.IntentId);
        Assert.Equal("denied", (await recovery.ApplyAsync(custody, bound.CanonicalBytes(), new string('0', 64))).Status);
        var changed = bound with { ReceiptHash = new string('a', 64) };
        Assert.Equal("conflict", (await recovery.ApplyAsync(custody, changed.CanonicalBytes(), changed.Hash())).Status);
        await using var rel = await _rel.CreateDbContextAsync(); Assert.Equal(1, await rel.Commands.CountAsync(x => x.InstallationId == _facts.Installation));
    }
    [Fact]
    public async Task StoppedRecoveryDoesNotConfirmADifferentResourceWithTheSamePrivateHashes()
    {
        var first = await Run(new Faults("none", _facts)); Assert.Equal("durable", first.Status);
        var id = first.RevisionId!.Value;
        var replace = ReleaseMutationCommand.Replace(_facts.Project, id, 1, $"\"rel-v1:{id:D}:1\"", "Changed", _facts.Selection);
        Assert.Equal("durable", (await RunCommand(new Faults("none", _facts), replace, "synthetic-replace-key-1234")).Status);
        var plan = new ReleaseRecoveryPlan(1, Guid.NewGuid(), _facts.Installation, _facts.Session.BootGeneration, 12345,
            _facts.Tenant, _facts.Subject.PrincipalId, replace.Action, replace.Route.Replace(id.ToString("D"), Guid.NewGuid().ToString("D")),
            replace.ScopedKeyHash(_facts.Installation, _facts.Tenant, _facts.Subject.PrincipalId, "synthetic-replace-key-1234"), replace.BusinessHash, null, null);
        var inspection = await new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new Quiescence()).InspectAsync(new Custody(), plan.CanonicalBytes());
        Assert.Equal("unknown", inspection.Status); Assert.Null(inspection.BoundPlan);
    }

    [Fact]
    public async Task StoppedRecoveryRejectsOwnerChangesAfterInspection()
    {
        var committed = await Run(new Faults("none", _facts)); Assert.Equal("durable", committed.Status);
        var recovery = new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new Quiescence()); var custody = new Custody();
        var inspected = await recovery.InspectAsync(custody, RecoveryPlan().CanonicalBytes()); Assert.NotNull(inspected.BoundPlan);
        var id = committed.RevisionId!.Value;
        var update = ReleaseMutationCommand.Replace(_facts.Project, id, 1, $"\"rel-v1:{id:D}:1\"", "Changed after inspect", _facts.Selection);
        Assert.Equal("durable", (await RunCommand(new Faults("none", _facts), update, "new-synthetic-key-1234")).Status);
        var plan = inspected.BoundPlan;
        Assert.Equal("conflict", (await recovery.ApplyAsync(custody, plan.CanonicalBytes(), plan.Hash())).Status);
    }

    [Fact]
    public async Task StoppedRecoveryReportsNoCommitOnlyWithBothOwnerAndAuditAbsent()
    {
        var recovery = new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new Quiescence()); var plan = RecoveryPlan(); var custody = new Custody();
        var inspected = await recovery.InspectAsync(custody, plan.CanonicalBytes()); Assert.Equal("no_commit", inspected.Status);
        Assert.NotNull(inspected.BoundPlan);
        Assert.Equal("no_commit", (await recovery.ApplyAsync(custody, inspected.BoundPlan.CanonicalBytes(), inspected.BoundPlan.Hash())).Status);
        custody.Held = false; Assert.Equal("unknown", (await recovery.InspectAsync(custody, plan.CanonicalBytes())).Status);
        Assert.Equal("denied", (await new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new Quiescence(false)).InspectAsync(new Custody(), plan.CanonicalBytes())).Status);
    }
    [Fact]
    public async Task NoCommitEvidenceCannotBeAppliedAfterTheMutationAppears()
    {
        var recovery = new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new Quiescence()); var custody = new Custody();
        var inspected = await recovery.InspectAsync(custody, RecoveryPlan().CanonicalBytes()); Assert.Equal("no_commit", inspected.Status);
        Assert.Equal("durable", (await Run(new Faults("none", _facts))).Status);
        var bound = inspected.BoundPlan!;
        Assert.Equal("conflict", (await recovery.ApplyAsync(custody, bound.CanonicalBytes(), bound.Hash())).Status);
    }

    [Fact]
    public async Task AuditOnlyEvidenceFencesOnlineMutationInsteadOfReplaying()
    {
        var plan = RecoveryPlan(); var intent = new AuditIntent(1, ReleaseCommandHash.AuditIntentId(plan.ScopedKeyHash), Guid.NewGuid(),
            AuditIntentStage.Mutation, _facts.Subject.PrincipalId, _facts.Tenant, Guid.NewGuid(), "release.create", "project", _facts.Project,
            null, 1, ReleaseCommandHash.AuditReason(plan.InstallationId, plan.ScopedKeyHash, plan.BusinessHash), _facts.Now, Guid.NewGuid());
        Assert.Equal(AuditAppendStatus.Durable, (await new AuditAppendStore(_aud).AppendAsync(intent)).Status);
        Assert.Equal("unknown", (await Run(new Faults("none", _facts))).Status);
        await using var rel = await _rel.CreateDbContextAsync(); Assert.False(await rel.Commands.AnyAsync(x => x.InstallationId == _facts.Installation));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
    }

    [Fact]
    public async Task AuditOnlyEvidenceNeverBecomesNoCommitOrAConfirmedMutation()
    {
        var plan = RecoveryPlan(); var intent = new AuditIntent(1, ReleaseCommandHash.AuditIntentId(plan.ScopedKeyHash), Guid.NewGuid(),
            AuditIntentStage.Mutation, _facts.Subject.PrincipalId, _facts.Tenant, Guid.NewGuid(), "release.create", "project", _facts.Project,
            null, 1, ReleaseCommandHash.AuditReason(plan.InstallationId, plan.ScopedKeyHash, plan.BusinessHash), _facts.Now, Guid.NewGuid());
        Assert.Equal(AuditAppendStatus.Durable, (await new AuditAppendStore(_aud).AppendAsync(intent)).Status);
        var result = await new OfflineReleaseRecoveryCoordinator(_facts.Installation, _iam, new Quiescence()).InspectAsync(new Custody(), plan.CanonicalBytes());
        Assert.Equal("unknown", result.Status); Assert.Null(result.BoundPlan);
    }
    private ReleaseRecoveryPlan RecoveryPlan()
    {
        var command = ReleaseMutationCommand.Create(_facts.Project, "Synthetic transaction", _facts.Selection);
        return new(1, Guid.NewGuid(), _facts.Installation, _facts.Session.BootGeneration, 12345, _facts.Tenant,
            _facts.Subject.PrincipalId, command.Action, command.Route,
            command.ScopedKeyHash(_facts.Installation, _facts.Tenant, _facts.Subject.PrincipalId, "synthetic-release-key-1234"),
            command.BusinessHash, null, null);
    }
    private sealed class Custody : IOfflineProvisioningLease
    { public bool Held = true; public void RequireHeld() { if (!Held) throw new InvalidOperationException("Synthetic custody released."); } }
    private sealed class Quiescence(bool stopped = true) : IArtifactProcessQuiescence
    { public bool IsQuiescent(int pid) => stopped && pid == 12345; }

    [Theory]
    [InlineData("none")]
    [InlineData("owner")]
    [InlineData("policy-expiry")]
    public async Task FreezePinsReviewedOwnerFactsAndRechecksThemAtCommit(string fault)
    {
        var initial = await Run(new Faults("none", _facts)); Assert.Equal("durable", initial.Status);
        _facts.AllowPilot(TimeSpan.FromSeconds(2));
        var preview = await new ReleaseSelectionReader(_facts, _facts, _facts, _facts).ReadUnderAdmissionAsync(
            _facts.Installation, _facts.Tenant, _facts.Project, _facts.Selection, _facts.Now);
        Assert.Equal("eligible", preview.Status);
        var id = initial.RevisionId!.Value;
        var command = ReleaseMutationCommand.Freeze(_facts.Project, id, 1, $"\"rel-v1:{id:D}:1\"",
            ReleasePreviewHash.Compute(id, 1, preview.Intent!.SpecHash));
        var result = await RunCommand(new Faults(fault, _facts), command, "synthetic-freeze-key-1234");
        Assert.Equal(fault == "none" ? "durable" : "conflict", result.Status);
        await using var rel = await _rel.CreateDbContextAsync();
        var row = await rel.Revisions.SingleAsync(x => x.Id == id);
        Assert.Equal(fault == "none" ? "Frozen" : "Draft", row.State);
        Assert.Equal(fault == "none" ? preview.Intent.GetCanonicalBytes() : null, row.SpecBytes);
        Assert.Equal(fault == "none" ? 2 : 1, await rel.Commands.CountAsync(x => x.InstallationId == _facts.Installation));
    }

    [Fact]
    public async Task RealAuditReaderChecksTheExactOwnerEvidence()
    {
        var result = await Run(new Faults("none", _facts)); Assert.Equal("durable", result.Status);
        await using var rel = await _rel.CreateDbContextAsync();
        var receipt = await rel.Commands.SingleAsync(x => x.InstallationId == _facts.Installation);
        var expected = new AuditMutationExpectation(receipt.AuditIntentId, Convert.ToHexStringLower(receipt.AuditIntentHash),
            _facts.Subject.PrincipalId, _facts.Tenant, receipt.ReleaseRevisionId, receipt.InvocationId!.Value, "release.create", "project", _facts.Project, ReleaseCommandHash.AuditReason(receipt.InstallationId, Convert.ToHexStringLower(receipt.ScopedKeyHash), Convert.ToHexStringLower(receipt.BusinessHash)));
        var reader = new AuditMutationEvidenceReader(_aud);
        Assert.Equal(AuditEvidenceStatus.Matched, await reader.MatchUnderAdmissionAsync(expected));
        Assert.Equal(AuditEvidenceStatus.Mismatch, await reader.MatchUnderAdmissionAsync(expected with { TenantId = Guid.NewGuid() }));
        Assert.Equal(AuditEvidenceStatus.Mismatch, await reader.MatchUnderAdmissionAsync(expected with { IntentHash = new string('0', 64) }));
        Assert.Equal(AuditEvidenceStatus.Mismatch, await reader.MatchUnderAdmissionAsync(expected with { Permission = "release.retire" }));
        Assert.Equal(AuditEvidenceStatus.Mismatch, await reader.MatchUnderAdmissionAsync(expected with { InvocationId = Guid.NewGuid() }));
        Assert.Equal(AuditEvidenceStatus.Mismatch, await reader.MatchUnderAdmissionAsync(expected with { SafeReason = "release-m-" + new string('0', 64) }));
        Assert.Equal(AuditEvidenceStatus.Missing, await reader.MatchUnderAdmissionAsync(expected with { IntentId = Guid.NewGuid() }));
    }

    [Theory]
    [InlineData(AuditEvidenceStatus.Missing)]
    [InlineData(AuditEvidenceStatus.Mismatch)]
    [InlineData(AuditEvidenceStatus.Unavailable)]
    public async Task ReplayNeverSucceedsWhenAuditEvidenceDoesNotMatch(AuditEvidenceStatus status)
    {
        Assert.Equal("durable", (await Run(new Faults("none", _facts))).Status);
        var result = await RunCommand(new Faults("none", _facts), ReleaseMutationCommand.Create(_facts.Project,
            "Synthetic transaction", _facts.Selection), "synthetic-release-key-1234", new Evidence(status));
        Assert.Equal(status == AuditEvidenceStatus.Unavailable ? "unavailable" : "unknown", result.Status);
        if (status == AuditEvidenceStatus.Unavailable) { await using var held = await _gate.AcquireReadAsync(); }
        else await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
    }
    private sealed class Evidence(AuditEvidenceStatus status) : IAuditMutationEvidenceReader
    {
        public Task<AuditEvidenceStatus> MatchUnderAdmissionAsync(AuditMutationExpectation expected, CancellationToken ct = default) => Task.FromResult(status);
    }

    [Fact]
    public async Task StaleVersionIsAConflictAndDoesNotStageAnotherMutation()
    {
        var first = await Run(new Faults("none", _facts)); Assert.Equal("durable", first.Status);
        var id = first.RevisionId!.Value;
        var command = ReleaseMutationCommand.Replace(_facts.Project, id, 2, $"\"rel-v1:{id:D}:2\"", "Changed", _facts.Selection);
        var outcome = await RunCommand(new Faults("none", _facts), command, "another-synthetic-key-1234");
        Assert.Equal("precondition", outcome.Status);
        await using var rel = await _rel.CreateDbContextAsync();
        Assert.Equal(1, await rel.Revisions.Where(x => x.Id == id).Select(x => x.Version).SingleAsync());
        Assert.Equal(1, await rel.Commands.CountAsync(x => x.InstallationId == _facts.Installation));
    }

    private async Task AssertRows(bool exists)
    {
        await using var rel = await _rel.CreateDbContextAsync(); await using var aud = await _aud.CreateDbContextAsync();
        await using var iam = await _iam.CreateDbContextAsync();
        Assert.Equal(exists, await rel.Revisions.AnyAsync(x => x.InstallationId == _facts.Installation));
        Assert.Equal(exists, await rel.Commands.AnyAsync(x => x.InstallationId == _facts.Installation));
        Assert.Equal(exists, await aud.Entries.AnyAsync(x => x.TenantRef == _facts.Tenant && x.Stage == "mutation"));
        Assert.True(await aud.Entries.AnyAsync(x => x.TenantRef == _facts.Tenant && x.Stage == "U"));
        Assert.True(await iam.AuthorityClockFloors.AnyAsync(x => x.InstallationId == _facts.Installation));
    }
    private sealed class Faults(string fault, Facts facts) : IArtifactAuditTransactionFactory
    {
        public bool Attempted;
        public async ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction tx, CancellationToken ct)
        {
            if (fault == "enlist") throw new OperationCanceledException();
            return new Wrapper(await new ArtifactAuditTransactionFactory().EnlistAsync(connection, tx, ct), this, facts, fault);
        }
        private sealed class Wrapper(IArtifactAuditTransaction inner, Faults owner, Facts facts, string fault) : IArtifactAuditTransaction
        {
            public bool CommitAttempted => inner.CommitAttempted;
            public async Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct)
            {
                if (fault == "stage") throw new OperationCanceledException();
                var result = await inner.StageAsync(intent, ct);
                if (fault == "authority") facts.Grant = facts.Grant with { AssignmentRevision = 2 };
                if (fault == "expiry") facts.Now = facts.Now.AddMinutes(11);
                if (fault == "policy-expiry") facts.Now = facts.Now.AddSeconds(2);
                if (fault == "owner") facts.TargetRevision++;
                return result;
            }
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct)
            {
                var result = await inner.CommitAsync(ct); owner.Attempted = inner.CommitAttempted;
                if (fault == "ack") throw new IOException("Synthetic lost commit acknowledgement");
                return result;
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    // The PostgreSQL test connection is already constrained to kordra_test/loopback.
    // Inject at Npgsql's stream boundary so the actual COMMIT reply is read from the
    // socket but discarded before the driver can acknowledge it to the transaction.
    private sealed class DroppedCommitResponse : IArtifactAuditTransactionFactory
    {
        public CommitReplyStream? Transport;
        public bool CommitAttempted;
        public async ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction tx, CancellationToken ct)
            => new Wrapped(await new ArtifactAuditTransactionFactory().EnlistAsync(connection, tx, ct), connection, this);

        private sealed class Wrapped(IArtifactAuditTransaction inner, DbConnection connection, DroppedCommitResponse owner) : IArtifactAuditTransaction
        {
            public bool CommitAttempted => inner.CommitAttempted;
            public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct) => inner.StageAsync(intent, ct);
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct)
            {
                owner.Transport = CommitReplyStream.Install(connection);
                try { return await inner.CommitAsync(ct); }
                finally { owner.CommitAttempted = inner.CommitAttempted; }
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    internal sealed class CommitReplyStream(Stream inner) : Stream
    {
        private static readonly byte[] Commit = Encoding.ASCII.GetBytes("COMMIT\0");
        private readonly List<byte> _writeTail = [];
        public bool SentCommit { get; private set; }
        public bool DiscardedCommitResponse { get; private set; }
        public static CommitReplyStream Install(DbConnection connection)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var connector = connection.GetType().GetField("<Connector>k__BackingField", flags)?.GetValue(connection)
                ?? throw new InvalidOperationException("Npgsql connector unavailable for the transport fixture.");
            var read = connector.GetType().GetField("<ReadBuffer>k__BackingField", flags)!.GetValue(connector)!;
            var write = connector.GetType().GetField("<WriteBuffer>k__BackingField", flags)!.GetValue(connector)!;
            var readField = read.GetType().GetField("<Underlying>k__BackingField", flags)!;
            var writeField = write.GetType().GetField("<Underlying>k__BackingField", flags)!;
            var stream = (Stream)readField.GetValue(read)!;
            if (!ReferenceEquals(stream, writeField.GetValue(write))) throw new InvalidOperationException("Unexpected Npgsql streams.");
            var result = new CommitReplyStream(stream);
            readField.SetValue(read, result); writeField.SetValue(write, result);
            return result;
        }
        private void ObserveWrite(ReadOnlySpan<byte> bytes)
        {
            foreach (var value in bytes)
            {
                _writeTail.Add(value);
                if (_writeTail.Count > Commit.Length) _writeTail.RemoveAt(0);
                if (_writeTail.Count == Commit.Length && _writeTail.SequenceEqual(Commit)) SentCommit = true;
            }
        }
        private void VerifyAndDiscard(byte[] response)
        {
            byte[] expected = [(byte)'C', 0, 0, 0, 11, .. Commit];
            if (!response.SequenceEqual(expected)) throw new IOException("Unexpected PostgreSQL reply in commit-loss fixture.");
            DiscardedCommitResponse = true;
            throw new IOException("PostgreSQL COMMIT reply discarded by isolated transport fixture.");
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!SentCommit) return await inner.ReadAsync(buffer, ct);
            var reply = new byte[12]; await inner.ReadExactlyAsync(reply, ct); VerifyAndDiscard(reply); return 0;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!SentCommit) return inner.Read(buffer, offset, count);
            var reply = new byte[12]; inner.ReadExactly(reply); VerifyAndDiscard(reply); return 0;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { await inner.WriteAsync(buffer, ct); ObserveWrite(buffer.Span); }
        public override void Write(byte[] buffer, int offset, int count)
        { inner.Write(buffer, offset, count); ObserveWrite(buffer.AsSpan(offset, count)); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    private sealed class Facts : ICurrentSubjectReader, IScopedGrantReader, IRoleDefinitionReader, IReleaseProjectAuthorityReader,
        IAuthorityClock, IReleaseCatalogReader, IReleaseTargetReader, IReleaseArtifactReader, IReleaseApprovalPolicyReader
    {
        public Guid Installation = Guid.NewGuid(), Tenant = Guid.NewGuid(), Project = Guid.NewGuid();
        private readonly Guid _application = Guid.NewGuid(), _environment = Guid.NewGuid(), _target = Guid.NewGuid(), _artifact = Guid.NewGuid(), _registry = Guid.NewGuid();
        private readonly DateTimeOffset _captured;
        public long TargetRevision = 1;
        public ReleaseApprovalPolicyBinding? Policy;
        public DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public CurrentSubject Subject; public ScopedGrant Grant; public SessionSelection Session;
        public ReleaseSelection Selection => ReleaseSelection.Create(_application, _environment, _target, _artifact);
        public Facts()
        {
            _captured = Now;
            Subject = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Now.AddHours(1), Now.AddMinutes(30));
            Grant = new(Subject.PrincipalId, Tenant, Guid.NewGuid(), 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(), 1,
                "release.create", "project", Project, Now.AddMinutes(10));
            Session = new(Subject.PrincipalId, "synthetic-session", Subject.BootGeneration);
        }
        public DateTimeOffset GetTrustedUtcNow() => Now;
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection s, CancellationToken ct = default) => Task.FromResult<CurrentSubject?>(Subject);
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission, string scopeKind, Guid scopeId, CancellationToken ct = default) => Task.FromResult<ScopedGrant?>(Grant);
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken ct = default) => Task.FromResult<RoleDefinitionSnapshot?>(new(Grant.DefinitionId, Grant.DefinitionRevision, "Synthetic", [new(Grant.Permission, "project")]));
        public Task<bool> IsActivePrincipalAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid p, CancellationToken ct = default) => Task.FromResult<ReleaseProjectAuthority?>(new(Tenant, Project, 1));
        public Task<ReleaseCatalogSelection?> ReadUnderAdmissionAsync(Guid t, Guid p, Guid a, Guid e, Guid target, CancellationToken ct = default) => Task.FromResult<ReleaseCatalogSelection?>(new(Tenant, Project, 1, _application, 1, _environment, 1, "Test", _target, 1));
        public Task<ReleaseTargetIdentity?> ReadUnderAdmissionAsync(Guid t, Guid p, Guid e, Guid target, CancellationToken ct = default) => Task.FromResult<ReleaseTargetIdentity?>(new(Tenant, Project, _environment, _target, "kubernetes", TargetRevision, 1));
        public Task<ArtifactV1?> ReadReleaseArtifactUnderAdmissionAsync(Guid t, Guid p, Guid a, CancellationToken ct = default)
        {
            var platform = new ArtifactPlatformV1("linux", "amd64", null); var digest = "sha256:" + new string('a', 64);
            return Task.FromResult<ArtifactV1?>(new(_artifact, Project, _registry, "synthetic/frontend", digest, platform,
                "application/vnd.oci.image.manifest.v1+json", 100, digest, 100, "application/vnd.oci.image.manifest.v1+json",
                "sha256:" + new string('b', 64), 50, platform, "none", "artifact-registry-v1", _captured, _captured,
                "manifest_config_hash_verified", "not_checked", "not_checked"));
        }
        public Task<ReleaseApprovalPolicyBinding?> ReadCurrentUnderAdmissionAsync(Guid i, Guid t, Guid p, Guid e, CancellationToken ct = default) => Task.FromResult(Policy);
        public void AllowPilot(TimeSpan lifetime)
        {
            string Time(DateTimeOffset x) => x.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
            var json = "{\"schemaVersion\":1,\"purpose\":\"release-intent-v1\",\"status\":\"active\",\"effectiveFromUtc\":\"" + Time(Now.AddSeconds(-1)) +
                "\",\"expiresAtUtc\":\"" + Time(Now.Add(lifetime)) + "\",\"rules\":[{\"classification\":\"Test\",\"effect\":\"allow\",\"separationOfDuties\":\"self_allowed\",\"allowUnattestedProvenance\":true,\"pendingLifetimeSeconds\":300,\"grantLifetimeSeconds\":300,\"blockedWindowsUtc\":[]}]}";
            var policy = ReleaseApprovalPolicy.Parse(Encoding.UTF8.GetBytes(json));
            Policy = new(Installation, Tenant, Project, _environment, Guid.NewGuid(), 1, Guid.NewGuid(), 1,
                policy.PayloadHash, new string('c', 64), policy.GetCanonicalBytes());
        }
    }
}
