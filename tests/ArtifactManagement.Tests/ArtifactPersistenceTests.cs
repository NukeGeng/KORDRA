using Kordra.ArtifactManagement.Domain;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace ArtifactManagement.Tests;

public sealed class ArtifactPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _installation = Guid.NewGuid(), _tenant = Guid.NewGuid(), _project = Guid.NewGuid(),
        _registry = Guid.NewGuid(), _principal = Guid.NewGuid();
    private ArtifactDbContext _db = null!;
    private IDbContextTransaction? _transaction;
    private ArtifactRegistrationStore Store => new(_db);

    public async Task InitializeAsync()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private isolated kordra_test locator required.");
        _db = new(ArtifactDbContextFactory.CreateTestOptions(locator));
        await _db.Database.MigrateAsync();
        _transaction = await _db.Database.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        // Only this test's uncommitted synthetic rows are rolled back. No immutable-record bypass,
        // schema drop, old fixture cleanup or production migration is performed.
        if (_transaction is not null) await _transaction.DisposeAsync();
        if (_db is not null) await _db.DisposeAsync();
    }

    private ArtifactIdentity Identity(Guid? tenant = null, Guid? project = null, string? digest = null) =>
        ArtifactIdentity.Create(tenant ?? _tenant, project ?? _project, _registry, "team/frontend",
            OCIImageDigest.Parse(digest ?? "sha256:" + new string('a', 64)), ArtifactPlatform.Create("linux", "arm64", null));

    private ArtifactRegistrationReceipt Pending(string? key = null, ArtifactIdentity? identity = null, bool claim = false,
        Guid? principal = null) => ArtifactRegistrationReceipt.Pending(Guid.NewGuid(), _installation,
        Guid.NewGuid(), Guid.NewGuid(), principal ?? _principal,
        new(identity ?? Identity(), claim ? ArtifactSourceClaim.Create("team/frontend", new string('b', 40), "build-1") : null),
        key ?? Guid.NewGuid().ToString("N"), Now);

    private static ArtifactVerificationFacts Facts(ArtifactIdentity identity, DateTimeOffset? capture = null, long size = 100) =>
        new(identity, ArtifactVerificationFacts.OciManifest, size, identity.RootDigest, ArtifactVerificationFacts.OciManifest,
            size, OCIImageDigest.Parse("sha256:" + new string('c', 64)), 50, identity.Platform, "none", capture ?? Now.AddSeconds(1));

    private async Task AddPending(ArtifactRegistrationReceipt pending)
    {
        Assert.Equal("staged_pending", (await Store.PreparePendingAsync(pending)).Status);
        await _db.SaveChangesAsync();
    }

    private async Task<Guid> Register(ArtifactRegistrationReceipt pending, ArtifactVerificationFacts? facts = null)
    {
        await AddPending(pending);
        var staged = await Store.PrepareRegisteredAsync(pending, facts ?? Facts(pending.Request.Identity), Now.AddSeconds(3));
        Assert.Equal("staged_registered", staged.Status);
        await _db.SaveChangesAsync();
        // Validate the extra nullable-principal artifact FK within this test transaction.
        await _db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS app.fk_art_claim_registration_artifact IMMEDIATE");
        await _db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS app.fk_art_claim_registration_artifact DEFERRED");
        return staged.ArtifactId!.Value;
    }

    [Fact]
    public async Task Migration_model_history_and_runtime_role_ACLs_are_exact()
    {
        Assert.False(_db.Database.HasPendingModelChanges());
        Assert.Single(await _db.Database.GetAppliedMigrationsAsync());
        var writable = await _db.Database.SqlQueryRaw<bool>("""
            SELECT has_column_privilege('kordra_app','app.art_registration_receipts','state','UPDATE') AS "Value"
            """).SingleAsync();
        Assert.True(writable);
        Assert.False(await _db.Database.SqlQueryRaw<bool>("""
            SELECT (has_table_privilege('kordra_app','app.art_artifacts','UPDATE') OR
              has_table_privilege('kordra_app','app.art_provenance_claims','DELETE') OR
              has_table_privilege('kordra_app','app.art_registration_receipts','DELETE') OR
              has_table_privilege('kordra_app','app.art_artifacts','TRUNCATE') OR
              has_table_privilege('kordra_app','app.art_provenance_claims','TRUNCATE') OR
              has_table_privilege('kordra_app','app.art_registration_receipts','TRUNCATE') OR
              has_column_privilege('kordra_app','app.art_registration_receipts','business_hash','UPDATE') OR
              has_schema_privilege('kordra_app','app','CREATE')) AS "Value"
            """).SingleAsync());
    }

    [Fact]
    public async Task Pending_replay_and_changed_body_do_not_create_another_attempt()
    {
        var first = Pending("request-00000001"); await AddPending(first);
        var replay = await Store.PreparePendingAsync(Pending("request-00000001"));
        Assert.Equal("in_progress", replay.Status); Assert.Equal(first.Id, replay.RegistrationId);
        var conflict = await Store.PreparePendingAsync(Pending("request-00000001", Identity(digest: "sha256:" + new string('d', 64))));
        Assert.Equal("key_conflict", conflict.Status); Assert.Null(conflict.ArtifactId);
        Assert.Single(await _db.Registrations.Where(r => r.InstallationId == _installation).ToArrayAsync());
    }

    [Fact]
    public async Task Distinct_attempts_deduplicate_artifact_and_preserve_first_times_and_own_claims()
    {
        var first = Pending(claim: true); var id = await Register(first);
        var second = Pending(claim: true);
        Assert.Equal(id, await Register(second, Facts(second.Request.Identity, Now.AddSeconds(2))));
        var stored = await _db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Equal(Now.AddSeconds(1), stored.VerifiedAtUtc);
        Assert.Equal(2, await _db.ProvenanceClaims.CountAsync(c => c.TenantId == _tenant));
        var projection = await Store.ReadOwnRegistrationAsync(_installation, _tenant, _project, _principal, second.Id);
        Assert.NotNull(projection); Assert.Equal("Registered", projection.Status);
        Assert.Equal(Now.AddSeconds(2), projection.AttemptVerifiedAtUtc);
        Assert.Equal("declared_unverified", projection.Provenance);
        Assert.Equal("not_checked", projection.Artifact!.LayerAvailability);
        Assert.Equal("not_checked", projection.Artifact.SignatureVerification);
    }

    [Fact]
    public async Task Terminal_replay_keeps_exact_result_and_changed_claim_is_a_conflict()
    {
        var first = Pending("request-00000001", claim: true); var id = await Register(first);
        var same = await Store.PreparePendingAsync(Pending("request-00000001", claim: true));
        Assert.Equal("terminal_replay", same.Status); Assert.Equal(id, same.ArtifactId);
        Assert.Equal("key_conflict", (await Store.PreparePendingAsync(Pending("request-00000001"))).Status);
    }

    [Fact]
    public async Task Foreign_owner_and_principal_never_read_another_receipt()
    {
        var pending = Pending(); await Register(pending);
        Assert.Null(await Store.ReadOwnRegistrationAsync(Guid.NewGuid(), _tenant, _project, _principal, pending.Id));
        Assert.Null(await Store.ReadOwnRegistrationAsync(_installation, Guid.NewGuid(), _project, _principal, pending.Id));
        Assert.Null(await Store.ReadOwnRegistrationAsync(_installation, _tenant, Guid.NewGuid(), _principal, pending.Id));
        Assert.Null(await Store.ReadOwnRegistrationAsync(_installation, _tenant, _project, Guid.NewGuid(), pending.Id));
        var otherTenant = Pending("request-00000001", Identity(tenant: Guid.NewGuid()));
        await AddPending(otherTenant);
        Assert.NotEqual(pending.KeyHash, otherTenant.KeyHash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_abandoned_is_terminal_without_creating_an_artifact(bool abandon)
    {
        var pending = Pending(); await AddPending(pending);
        var result = abandon
            ? await Store.PrepareAbandonedAsync(_installation, pending.Id, pending.BootId, pending.InvocationId, Now.AddSeconds(1))
            : await Store.PrepareFailedAsync(pending, ArtifactRegistrationFailure.RegistryUnavailable, Now.AddSeconds(1));
        Assert.StartsWith("staged_", result.Status);
        await _db.SaveChangesAsync();
        Assert.Equal("attempt_conflict", (await Store.PrepareRegisteredAsync(pending, Facts(pending.Request.Identity), Now.AddSeconds(3))).Status);
        Assert.Empty(await _db.Artifacts.Where(a => a.TenantId == _tenant).ToArrayAsync());
    }

    [Fact]
    public async Task Wrong_attempt_cannot_terminalize_and_inconsistent_content_does_not_overwrite()
    {
        var first = Pending(); var id = await Register(first);
        var second = Pending(); await AddPending(second);
        Assert.Equal("attempt_conflict", (await Store.PrepareAbandonedAsync(_installation, second.Id,
            Guid.NewGuid(), second.InvocationId, Now.AddSeconds(1))).Status);
        Assert.Equal("integrity_conflict", (await Store.PrepareRegisteredAsync(second, Facts(second.Request.Identity, size: 101), Now.AddSeconds(3))).Status);
        Assert.Equal(100, (await _db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == id)).RootSizeBytes);
        Assert.Equal("Pending", (await _db.Registrations.AsNoTracking().SingleAsync(r => r.Id == second.Id)).State);
    }

    [Theory]
    [InlineData("UPDATE app.art_artifacts SET root_size_bytes = root_size_bytes + 1 WHERE tenant_id = {0}")]
    [InlineData("DELETE FROM app.art_artifacts WHERE tenant_id = {0}")]
    [InlineData("UPDATE app.art_provenance_claims SET declaration_json = '{{}}'::jsonb WHERE tenant_id = {0}")]
    [InlineData("DELETE FROM app.art_registration_receipts WHERE tenant_id = {0}")]
    public async Task SQL_prevents_rewriting_or_deleting_immutable_owner_history(string sql)
    {
        await Register(Pending(claim: true));
        var failure = await Assert.ThrowsAsync<PostgresException>(() => _db.Database.ExecuteSqlRawAsync(sql, _tenant));
        Assert.Equal("55000", failure.SqlState);
    }

    [Fact]
    public async Task SQL_prevents_pending_business_retarget()
    {
        var pending = Pending(); await AddPending(pending);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.art_registration_receipts SET business_hash = {new string('f', 64)}, state = 'Failed', safe_reason = 'cancelled', completed_at_utc = {Now.AddSeconds(1)} WHERE id = {pending.Id}"));
        Assert.Equal("55000", failure.SqlState);
    }

    [Fact]
    public async Task SQL_prevents_terminal_state_rewrite()
    {
        var pending = Pending(); await Register(pending);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.art_registration_receipts SET state = 'Pending', artifact_id = NULL, completed_at_utc = NULL, attempt_verified_at_utc = NULL WHERE id = {pending.Id}"));
        Assert.Equal("55000", failure.SqlState);
    }

    [Fact]
    public async Task SQL_unique_key_prevents_two_pending_attempts_for_same_subject_body_key()
    {
        var one = Pending("request-00000001"); await AddPending(one);
        var copy = await _db.Registrations.AsNoTracking().SingleAsync(r => r.Id == one.Id);
        copy.Id = Guid.NewGuid(); copy.InvocationId = Guid.NewGuid(); _db.Registrations.Add(copy);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task SQL_content_identity_is_unique_independently_of_store_lookup()
    {
        var id = await Register(Pending());
        var duplicate = await _db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == id);
        duplicate.Id = Guid.NewGuid(); _db.Artifacts.Add(duplicate);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("uq_art_content_identity", postgres.ConstraintName);
    }

    [Fact]
    public async Task SQL_foreign_project_artifact_binding_is_rejected()
    {
        var artifact = await Register(Pending());
        var foreign = Pending(identity: Identity(project: Guid.NewGuid())); await AddPending(foreign);
        var error = await Assert.ThrowsAsync<PostgresException>(() => _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.art_registration_receipts SET state = 'Registered', artifact_id = {artifact}, completed_at_utc = {Now.AddSeconds(3)}, attempt_verified_at_utc = {Now.AddSeconds(1)} WHERE id = {foreign.Id}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task SQL_claim_must_match_its_registration_artifact_even_within_same_project()
    {
        var first = Pending(); await Register(first);
        var second = Pending(identity: Identity(digest: "sha256:" + new string('d', 64)));
        var otherArtifact = await Register(second);
        _db.ProvenanceClaims.Add(new()
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant,
            ProjectId = _project,
            RegistrationId = first.Id,
            ArtifactId = otherArtifact,
            DeclarationJson = "{}",
            CreatedAtUtc = Now.AddSeconds(3)
        });
        await _db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => _db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS app.fk_art_claim_registration_artifact IMMEDIATE"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task Staging_and_save_do_not_commit_the_callers_transaction()
    {
        var pending = Pending(); await Register(pending);
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")!;
        await using var outside = new ArtifactDbContext(ArtifactDbContextFactory.CreateTestOptions(locator));
        Assert.False(await outside.Registrations.AnyAsync(r => r.InstallationId == _installation));
        await _transaction!.RollbackAsync();
        Assert.False(await outside.Registrations.AnyAsync(r => r.InstallationId == _installation));
        Assert.False(await outside.Artifacts.AnyAsync(a => a.TenantId == _tenant));
    }
}
