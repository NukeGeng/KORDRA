using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.Modules.ReleaseManagement;
using Kordra.ReleaseManagement.Domain;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Kordra.ReleaseManagement.Tests;

public sealed class ReleasePersistenceTests : IAsyncLifetime
{
    private ReleaseDbContext _db = null!;
    private IDbContextTransaction _tx = null!;
    private readonly Guid _installation = Guid.NewGuid(), _tenant = Guid.NewGuid(), _project = Guid.NewGuid();
    private readonly Guid _release = Guid.NewGuid(), _creator = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING") ??
            throw new InvalidOperationException("Private isolated kordra_test locator required.");
        _db = new ReleaseDbContext(ReleaseDbContextFactory.CreateTestOptions(locator));
        await _db.Database.MigrateAsync();
        _tx = await _db.Database.BeginTransactionAsync();
        _db.Lineages.Add(new() { Id = _release, InstallationId = _installation, TenantId = _tenant, ProjectId = _project, CreatedAtUtc = _now });
        await _db.SaveChangesAsync();
    }
    private ReleaseRevisionRecord Draft(long number = 1) => new()
    {
        Id = Guid.NewGuid(),
        InstallationId = _installation,
        TenantId = _tenant,
        ProjectId = _project,
        ReleaseId = _release,
        RevisionNumber = number,
        Version = 1,
        CreatorPrincipalId = _creator,
        Label = "Synthetic",
        ApplicationId = Guid.NewGuid(),
        EnvironmentId = Guid.NewGuid(),
        TargetId = Guid.NewGuid(),
        ArtifactId = Guid.NewGuid(),
        State = "Draft",
        CreatedAtUtc = _now,
        UpdatedAtUtc = _now
    };
    private async Task<ReleaseRevisionRecord> AddDraft()
    {
        var row = Draft(); _db.Revisions.Add(row); await _db.SaveChangesAsync(); return row;
    }
    private async Task Freeze(ReleaseRevisionRecord row)
    {
        row.State = "Frozen"; row.Version++; row.SpecBytes = [1, 2, 3]; row.SpecHash = new byte[32];
        row.FrozenAtUtc = _now; await _db.SaveChangesAsync();
    }
    private static void AssertPostgres(DbUpdateException exception) => Assert.IsType<PostgresException>(exception.InnerException);

    [Fact]
    public async Task ValidDraftFreezeRetireAndNextRevisionKeepImmutableHistory()
    {
        var first = await AddDraft(); await Freeze(first);
        first.State = "Retired"; first.RetiredAtUtc = _now; first.RetiredReasonCode = "obsolete"; first.Version++;
        await _db.SaveChangesAsync();
        var next = Draft(2); _db.Revisions.Add(next); await _db.SaveChangesAsync();
        Assert.Equal(2, await _db.Revisions.CountAsync(x => x.ReleaseId == _release));
        Assert.Equal(new byte[] { 1, 2, 3 }, first.SpecBytes);
    }

    [Fact]
    public async Task FrozenReaderValidatesExactBytesHashAndOwnerScope()
    {
        var row = await AddDraft(); var intent = IntentFor(row);
        row.State = "Frozen"; row.Version++; row.SpecBytes = intent.GetCanonicalBytes();
        row.SpecHash = Convert.FromHexString(intent.SpecHash); row.FrozenAtUtc = _now; await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear(); var reader = new ReleaseStore(_db);
        var found = await reader.ReadFrozenUnderAdmissionAsync(_installation, _tenant, _project, row.Id);
        Assert.NotNull(found); Assert.Equal(intent.SpecHash, found.SpecHash);
        var bytes = found.GetCanonicalSubject(); bytes[0] = 0;
        Assert.Equal((byte)'{', found.GetCanonicalSubject()[0]);
        Assert.Equal(_installation, found.InstallationId);
        Assert.Null(await reader.ReadFrozenUnderAdmissionAsync(Guid.NewGuid(), _tenant, _project, row.Id));
        Assert.Null(await reader.ReadFrozenUnderAdmissionAsync(_installation, Guid.NewGuid(), _project, row.Id));
        Assert.Null(await reader.ReadFrozenUnderAdmissionAsync(_installation, _tenant, Guid.NewGuid(), row.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrozenReaderFailsClosedOnHashMismatchOrWrongOwnerFacts(bool ownerMismatch)
    {
        var row = await AddDraft();
        var intent = IntentFor(row, ownerMismatch ? Guid.NewGuid() : null);
        row.State = "Frozen"; row.Version++; row.SpecBytes = intent.GetCanonicalBytes();
        row.SpecHash = ownerMismatch ? Convert.FromHexString(intent.SpecHash) : new byte[32];
        row.FrozenAtUtc = _now; await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseStore(_db).ReadFrozenUnderAdmissionAsync(_installation, _tenant, _project, row.Id));
    }

    private ReleaseIntent IntentFor(ReleaseRevisionRecord row, Guid? alternateApplication = null)
    {
        var digest = "sha256:" + new string('a', 64);
        var value = new
        {
            schemaVersion = 1,
            subjectKind = "release-intent-v1",
            tenantId = row.TenantId.ToString("D"),
            project = new { id = row.ProjectId.ToString("D"), revision = 1 },
            application = new { id = (alternateApplication ?? row.ApplicationId).ToString("D"), revision = 1 },
            environment = new { id = row.EnvironmentId.ToString("D"), revision = 1, classification = "Test" },
            target = new { id = row.TargetId.ToString("D"), kind = "kubernetes", revision = 1, associationRevision = 1 },
            artifact = new
            {
                artifactId = row.ArtifactId.ToString("D"),
                projectId = row.ProjectId.ToString("D"),
                registryId = Guid.NewGuid().ToString("D"),
                repository = "team/frontend",
                rootDigest = digest,
                platform = new { os = "linux", architecture = "amd64", variant = (string?)null },
                rootMediaType = "application/vnd.oci.image.manifest.v1+json",
                rootSizeBytes = 100,
                selectedManifestDigest = digest,
                selectedManifestSizeBytes = 100,
                selectedManifestMediaType = "application/vnd.oci.image.manifest.v1+json",
                configDigest = "sha256:" + new string('b', 64),
                configSizeBytes = 50,
                observedPlatform = new { os = "linux", architecture = "amd64", variant = (string?)null },
                variantEvidence = "none",
                verifierProfile = "artifact-registry-v1",
                verifiedAtUtc = "2026-10-26T12:00:01.000000Z",
                createdAtUtc = "2026-10-26T12:00:02.000000Z",
                contentVerification = "manifest_config_hash_verified",
                layerAvailability = "not_checked",
                signatureVerification = "not_checked"
            },
            approvalPolicy = new
            {
                policyId = Guid.NewGuid().ToString("D"),
                policyRevision = 1,
                payloadHash = new string('c', 64),
                bindingId = Guid.NewGuid().ToString("D"),
                bindingRevision = 1
            },
            qualification = new { profile = "pilot", provenanceClass = "unattested", executionAuthorized = false }
        };
        return ReleaseIntent.FromOwnerFacts(JsonSerializer.SerializeToUtf8Bytes(value));
    }

    [Fact]
    public async Task RetireRequiresAnExplicitReason()
    {
        var first = await AddDraft(); await Freeze(first);
        first.State = "Retired"; first.RetiredAtUtc = _now; first.Version++;
        AssertPostgres(await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync()));
    }

    [Fact]
    public async Task FrozenBytesCannotBeChangedWhileRetiring()
    {
        var first = await AddDraft(); await Freeze(first);
        first.State = "Retired"; first.RetiredAtUtc = _now; first.RetiredReasonCode = "obsolete"; first.Version++;
        first.SpecBytes = [9];
        AssertPostgres(await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync()));
    }

    [Fact]
    public async Task FrozenRevisionCannotReturnToDraftOrBeDeleted()
    {
        var first = await AddDraft(); await Freeze(first);
        first.State = "Draft"; first.Version++; first.SpecBytes = null; first.SpecHash = null; first.FrozenAtUtc = null;
        AssertPostgres(await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync()));
    }

    [Fact]
    public async Task SecondDraftAndSkippedRevisionAreDenied()
    {
        await AddDraft(); _db.Revisions.Add(Draft(2));
        AssertPostgres(await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync()));
    }

    [Fact]
    public async Task RevisionCannotReferenceAnotherTenantLineage()
    {
        var row = Draft(); row.TenantId = Guid.NewGuid(); _db.Revisions.Add(row);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task StagedFreezeRequiresCreatorExactScopeAndReviewedSelection()
    {
        var row = await AddDraft(); var intent = IntentFor(row); var store = new ReleaseMutationStore(_db);
        var hash = ReleasePreviewHash.Compute(row.Id, row.Version, intent.SpecHash);
        await Assert.ThrowsAsync<ReleaseMutationConflictException>(() => store.StageFreezeAsync(_installation, _tenant, _project, row.Id, Guid.NewGuid(), 1, hash, intent, _now));
        await Assert.ThrowsAsync<ReleaseMutationConflictException>(() => store.StageFreezeAsync(Guid.NewGuid(), _tenant, _project, row.Id, _creator, 1, hash, intent, _now));
        await Assert.ThrowsAsync<ReleaseMutationConflictException>(() => store.StageFreezeAsync(_installation, _tenant, _project, row.Id, _creator, 1, new string('0', 64), intent, _now));
        await Assert.ThrowsAsync<ReleaseMutationConflictException>(() => store.StageFreezeAsync(_installation, _tenant, _project, row.Id, _creator, 2, hash, intent, _now));
        var foreign = IntentFor(row, Guid.NewGuid());
        await Assert.ThrowsAsync<ReleaseMutationConflictException>(() => store.StageFreezeAsync(_installation, _tenant, _project, row.Id, _creator, 1, ReleasePreviewHash.Compute(row.Id, 1, foreign.SpecHash), foreign, _now));
        await store.StageFreezeAsync(_installation, _tenant, _project, row.Id, _creator, 1, hash, intent, _now);
        Assert.Equal("Frozen", row.State);
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        Assert.NotNull(await new ReleaseStore(_db).ReadFrozenUnderAdmissionAsync(_installation, _tenant, _project, row.Id));
    }

    [Fact]
    public async Task NextDraftNamesAnExactFrozenBaseAndPreservesHistory()
    {
        var first = await AddDraft(); var intent = IntentFor(first); var store = new ReleaseMutationStore(_db);
        await store.StageFreezeAsync(_installation, _tenant, _project, first.Id, _creator, 1,
            ReleasePreviewHash.Compute(first.Id, 1, intent.SpecHash), intent, _now);
        await _db.SaveChangesAsync();
        var row = Draft(2); var next = ReleaseDraft.Create(_tenant, _project, _release, row.Id, 2, _creator, row.Label,
            ReleaseSelection.Create(row.ApplicationId, row.EnvironmentId, row.TargetId, row.ArtifactId));
        await Assert.ThrowsAsync<ReleaseMutationConflictException>(() => store.StageNextDraftAsync(_installation, _tenant, _project, Guid.NewGuid(), next, _now));
        await store.StageNextDraftAsync(_installation, _tenant, _project, first.Id, next, _now);
        await _db.SaveChangesAsync(); Assert.Equal(2, await _db.Revisions.CountAsync(x => x.ReleaseId == _release));
    }

    [Fact]
    public async Task RetireIsVersionCheckedAndRetainsExactFrozenBytes()
    {
        var first = await AddDraft(); var intent = IntentFor(first); var store = new ReleaseMutationStore(_db);
        await store.StageFreezeAsync(_installation, _tenant, _project, first.Id, _creator, 1,
            ReleasePreviewHash.Compute(first.Id, 1, intent.SpecHash), intent, _now);
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ReleaseMutationConflictException>(() => store.StageRetireAsync(_installation, _tenant, _project, first.Id, 1, "obsolete", _now));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StageRetireAsync(_installation, _tenant, _project, first.Id, 2, "arbitrary", _now));
        await store.StageRetireAsync(_installation, _tenant, _project, first.Id, 2, "obsolete", _now);
        await _db.SaveChangesAsync();
        Assert.Equal(intent.GetCanonicalBytes(), first.SpecBytes); Assert.Equal("Retired", first.State);
    }

    [Fact]
    public async Task DraftReplacementRequiresCreatorAndExactVersion()
    {
        var row = await AddDraft(); var store = new ReleaseMutationStore(_db);
        var selection = ReleaseSelection.Create(row.ApplicationId, row.EnvironmentId, row.TargetId, row.ArtifactId);
        await Assert.ThrowsAsync<ReleaseMutationConflictException>(() => store.StageReplaceDraftAsync(_installation, _tenant, _project, row.Id, Guid.NewGuid(), 1, "new", selection, _now));
        await Assert.ThrowsAsync<ReleaseMutationConflictException>(() => store.StageReplaceDraftAsync(_installation, _tenant, _project, row.Id, _creator, 2, "new", selection, _now));
        await store.StageReplaceDraftAsync(_installation, _tenant, _project, row.Id, _creator, 1, "new", selection, _now);
        await _db.SaveChangesAsync(); Assert.Equal(2, row.Version);
    }

    [Fact]
    public async Task MutationWithoutSharedTransactionCannotStageChanges()
    {
        await _tx.RollbackAsync(); await _tx.DisposeAsync();
        var row = Draft(); var draft = ReleaseDraft.Create(_tenant, _project, _release, row.Id, 1, _creator, row.Label,
            ReleaseSelection.Create(row.ApplicationId, row.EnvironmentId, row.TargetId, row.ArtifactId));
        Assert.Throws<InvalidOperationException>(() => new ReleaseMutationStore(_db).StageFirstDraft(_installation, draft, _now));
        _tx = await _db.Database.BeginTransactionAsync();
    }

    [Fact]
    public async Task CompetingEditorsCannotOverwriteAnAlreadyAdvancedVersion()
    {
        var first = await AddDraft();
        await using var competing = new ReleaseDbContext(ReleaseDbContextFactory.CreateEnlistedOptions(_db.Database.GetDbConnection()));
        await competing.Database.UseTransactionAsync(_tx.GetDbTransaction());
        var stale = await competing.Revisions.SingleAsync(x => x.Id == first.Id);
        first.Label = "first"; first.Version++; await _db.SaveChangesAsync();
        stale.Label = "second"; stale.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => competing.SaveChangesAsync());
    }

    [Fact]
    public async Task ReceiptReplayIsScopedAndRemainsOnlyCandidateEvidence()
    {
        var row = await AddDraft(); var store = new ReleaseReceiptStore(_db); var key = new byte[32]; key[0] = 1;
        var body = new byte[32]; body[0] = 2; var auditHash = new byte[32]; auditHash[0] = 3; var auditId = Guid.NewGuid();
        Assert.Equal("absent", (await store.FindUnderAdmissionAsync(_installation, _tenant, _project, _creator, "freeze", key, body)).Status);
        store.Stage(Guid.NewGuid(), _installation, _tenant, _project, _creator, row.Id, Guid.NewGuid(), Guid.NewGuid(), "freeze", key, body, auditId, auditHash, _now);
        await _db.SaveChangesAsync();
        var candidate = await store.FindUnderAdmissionAsync(_installation, _tenant, _project, _creator, "freeze", key, body);
        Assert.Equal("candidate", candidate.Status); Assert.Equal(row.Id, candidate.RevisionId); Assert.Equal(auditId, candidate.AuditIntentId);
        Assert.Equal("conflict", (await store.FindUnderAdmissionAsync(_installation, _tenant, _project, _creator, "freeze", key, new byte[32])).Status);
        Assert.Equal("conflict", (await store.FindUnderAdmissionAsync(_installation, _tenant, Guid.NewGuid(), _creator, "freeze", key, body)).Status);
        Assert.Equal("absent", (await store.FindUnderAdmissionAsync(Guid.NewGuid(), _tenant, _project, _creator, "freeze", key, body)).Status);
    }

    [Fact]
    public async Task QueryRequiresExactScopeAndPreservesRetiredSubjectBytes()
    {
        var row = await AddDraft(); var intent = IntentFor(row); var store = new ReleaseMutationStore(_db);
        await store.StageFreezeAsync(_installation, _tenant, _project, row.Id, _creator, 1,
            ReleasePreviewHash.Compute(row.Id, 1, intent.SpecHash), intent, _now);
        await _db.SaveChangesAsync();
        await store.StageRetireAsync(_installation, _tenant, _project, row.Id, 2, "obsolete", _now);
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var query = new ReleaseQueryStore(_db);
        Assert.Null(await query.ReadRevisionUnderAdmissionAsync(Guid.NewGuid(), _tenant, _project, row.Id));
        Assert.Null(await query.ReadRevisionUnderAdmissionAsync(_installation, Guid.NewGuid(), _project, row.Id));
        Assert.Null(await query.ReadRevisionUnderAdmissionAsync(_installation, _tenant, Guid.NewGuid(), row.Id));
        var snapshot = await query.ReadRevisionUnderAdmissionAsync(_installation, _tenant, _project, row.Id);
        Assert.NotNull(snapshot); Assert.Equal("Retired", snapshot.State); Assert.Equal(intent.SpecHash, snapshot.SpecHash);
        var copy = snapshot.GetCanonicalSubject()!; copy[0] = 0;
        Assert.Equal((byte)'{', snapshot.GetCanonicalSubject()![0]);
    }

    [Fact]
    public async Task QueriesUseBoundedSameScopeCursorsAndNeverGuessForeignResources()
    {
        await AddDraft();
        var second = Guid.NewGuid(); _db.Lineages.Add(new()
        {
            Id = second,
            InstallationId = _installation,
            TenantId = _tenant,
            ProjectId = _project,
            CreatedAtUtc = _now
        });
        await _db.SaveChangesAsync(); var query = new ReleaseQueryStore(_db);
        var first = await query.ReadLineagesUnderAdmissionAsync(_installation, _tenant, _project, 1, null);
        Assert.NotNull(first); Assert.Single(first.Items); Assert.NotNull(first.NextAfterId);
        var next = await query.ReadLineagesUnderAdmissionAsync(_installation, _tenant, _project, 1, first.NextAfterId);
        Assert.NotNull(next); Assert.Single(next.Items); Assert.Null(next.NextAfterId);
        Assert.NotEqual(first.Items[0].ReleaseId, next.Items[0].ReleaseId);
        Assert.Null(await query.ReadLineagesUnderAdmissionAsync(_installation, _tenant, _project, 1, Guid.NewGuid()));
        Assert.Null(await query.ReadLineagesUnderAdmissionAsync(_installation, _tenant, _project, 51, null));
        Assert.Null(await query.ReadRevisionsUnderAdmissionAsync(_installation, _tenant, _project, Guid.NewGuid(), 10, null));
        Assert.Null(await query.ReadRevisionsUnderAdmissionAsync(_installation, _tenant, _project, _release, 10, Guid.NewGuid()));
        var revisions = await query.ReadRevisionsUnderAdmissionAsync(_installation, _tenant, _project, _release, 10, null);
        Assert.NotNull(revisions); Assert.Single(revisions.Items); Assert.Null(revisions.Items[0].SpecHash);
    }

    [Fact]
    public async Task QueriesFailClosedWhenFrozenHashDoesNotMatch()
    {
        var row = await AddDraft(); var intent = IntentFor(row);
        row.State = "Frozen"; row.Version++; row.SpecBytes = intent.GetCanonicalBytes(); row.SpecHash = new byte[32]; row.FrozenAtUtc = _now;
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear(); var query = new ReleaseQueryStore(_db);
        await Assert.ThrowsAsync<InvalidDataException>(() => query.ReadRevisionUnderAdmissionAsync(_installation, _tenant, _project, row.Id));
        await Assert.ThrowsAsync<InvalidDataException>(() => query.ReadRevisionsUnderAdmissionAsync(_installation, _tenant, _project, _release, 10, null));
    }

    [Fact]
    public async Task NewReceiptWithoutBootAndInvocationIdentityIsRejectedByDatabase()
    {
        var row = await AddDraft();
        _db.Commands.Add(new() { Id = Guid.NewGuid(), InstallationId = _installation, TenantId = _tenant, ProjectId = _project,
            PrincipalId = _creator, ReleaseRevisionId = row.Id, Action = "freeze", ScopedKeyHash = new byte[32], BusinessHash = new byte[32],
            AuditIntentId = Guid.NewGuid(), AuditIntentHash = new byte[32], CreatedAtUtc = _now });
        AssertPostgres(await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync()));
    }

    [Fact]
    public async Task ConcurrentNextDraftsCannotBothCommitForOneLineage()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")!;
        var options = ReleaseDbContextFactory.CreateTestOptions(locator);
        var release = Guid.NewGuid(); var installation = Guid.NewGuid(); var tenant = Guid.NewGuid(); var project = Guid.NewGuid();
        ReleaseRevisionRecord NewRow(long number) => new() { Id = Guid.NewGuid(), InstallationId = installation, TenantId = tenant, ProjectId = project,
            ReleaseId = release, RevisionNumber = number, Version = 1, CreatorPrincipalId = _creator, Label = "Synthetic race", ApplicationId = Guid.NewGuid(),
            EnvironmentId = Guid.NewGuid(), TargetId = Guid.NewGuid(), ArtifactId = Guid.NewGuid(), State = "Draft", CreatedAtUtc = _now, UpdatedAtUtc = _now };
        // Fresh synthetic immutable test history remains only in the isolated test database.
        await using (var seed = new ReleaseDbContext(options))
        {
            seed.Lineages.Add(new() { Id = release, InstallationId = installation, TenantId = tenant, ProjectId = project, CreatedAtUtc = _now });
            var first = NewRow(1); seed.Revisions.Add(first); await seed.SaveChangesAsync();
            first.State = "Frozen"; first.Version++; first.SpecBytes = [1, 2, 3]; first.SpecHash = new byte[32]; first.FrozenAtUtc = _now;
            await seed.SaveChangesAsync();
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Attempt()
        {
            await using var db = new ReleaseDbContext(options); db.Revisions.Add(NewRow(2)); await start.Task;
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException error) when (error.InnerException is PostgresException) { return false; }
        }
        var a = Attempt(); var b = Attempt(); start.SetResult(); var outcomes = await Task.WhenAll(a, b);
        Assert.Equal(1, outcomes.Count(x => x));
        await using var check = new ReleaseDbContext(options);
        Assert.Equal(1, await check.Revisions.CountAsync(x => x.ReleaseId == release && x.State == "Draft"));
        Assert.Equal(2, await check.Revisions.CountAsync(x => x.ReleaseId == release));
    }

    [Fact]
    public async Task RuntimeRoleHasNoDeleteOrTruncatePrivilege()
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.Transaction = _tx.GetDbTransaction();
        command.CommandText = "SELECT has_table_privilege('kordra_app', 'app.rel_revisions', 'SELECT'), " +
            "has_table_privilege('kordra_app', 'app.rel_revisions', 'DELETE,TRUNCATE'), " +
            "has_column_privilege('kordra_app', 'app.rel_revisions', 'tenant_id', 'UPDATE'), " +
            "has_column_privilege('kordra_app', 'app.rel_revisions', 'state', 'UPDATE')";
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0)); Assert.False(reader.GetBoolean(1)); Assert.False(reader.GetBoolean(2)); Assert.True(reader.GetBoolean(3));
    }

    [Fact]
    public async Task ImmutableHistoryCannotBeTruncated()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => _db.Database.ExecuteSqlRawAsync("TRUNCATE app.rel_commands"));
        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
    }
    public async Task DisposeAsync() { await _tx.DisposeAsync(); await _db.DisposeAsync(); }
}
