using Kordra.ArtifactManagement.Domain;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.ArtifactManagement.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace ArtifactManagement.Tests;

public sealed class ArtifactQueryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _installation = Guid.NewGuid(), _tenant = Guid.NewGuid(), _project = Guid.NewGuid(),
        _registry = Guid.NewGuid(), _principal = Guid.NewGuid();
    private ArtifactDbContext _db = null!;
    private IDbContextTransaction? _tx;
    private ArtifactQueryStore Queries => new(_db);
    public async Task InitializeAsync()
    {
        _db = new(ArtifactDbContextFactory.CreateTestOptions(Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private isolated kordra_test locator required.")));
        await _db.Database.MigrateAsync(); _tx = await _db.Database.BeginTransactionAsync();
    }
    public async Task DisposeAsync() { if (_tx is not null) await _tx.DisposeAsync(); if (_db is not null) await _db.DisposeAsync(); }
    private ArtifactRegistrationReceipt Pending(int index = 1, Guid? tenant = null, Guid? project = null, Guid? principal = null,
        string key = "synthetic-query-key-0001", bool declaration = false) =>
        ArtifactRegistrationReceipt.Pending(Guid.NewGuid(), _installation, Guid.NewGuid(), Guid.NewGuid(), principal ?? _principal,
            new(ArtifactIdentity.Create(tenant ?? _tenant, project ?? _project, _registry, "team/frontend",
                OCIImageDigest.Parse("sha256:" + index.ToString("x64")), ArtifactPlatform.Create("linux", "amd64", null)),
                declaration ? ArtifactSourceClaim.Create("team/frontend", new string('a', 40), "build-1") : null), key, Now);
    private async Task<Guid> Register(ArtifactRegistrationReceipt pending)
    {
        var store = new ArtifactRegistrationStore(_db);
        Assert.Equal("staged_pending", (await store.PreparePendingAsync(pending)).Status); await _db.SaveChangesAsync();
        var identity = pending.Request.Identity;
        var facts = new ArtifactVerificationFacts(identity, ArtifactVerificationFacts.OciManifest, 100, identity.RootDigest,
            ArtifactVerificationFacts.OciManifest, 100, OCIImageDigest.Parse("sha256:" + new string('b', 64)), 50,
            identity.Platform, "none", Now.AddSeconds(1));
        var registered = await store.PrepareRegisteredAsync(pending, facts, Now.AddSeconds(2)); await _db.SaveChangesAsync();
        Assert.Equal("staged_registered", registered.Status); return registered.ArtifactId!.Value;
    }

    [Fact]
    public async Task Real_PostgreSQL_cursor_ties_page_53_records_without_duplicates_or_foreign_scope()
    {
        var ids = new HashSet<Guid>();
        for (var i = 1; i <= 53; i++) ids.Add(await Register(Pending(i, key: "synthetic-query-key-" + i.ToString("D4"))));
        var foreign = await Register(Pending(100, tenant: Guid.NewGuid(), project: Guid.NewGuid()));
        var first = await Queries.ReadPageAsync(_tenant, _project, 50, null);
        Assert.NotNull(first); Assert.Equal(50, first.Items.Count); Assert.NotNull(first.NextAfterArtifactId);
        Assert.Equal(first.Items[^1].ArtifactId, first.NextAfterArtifactId);
        var second = await Queries.ReadPageAsync(_tenant, _project, 50, first.NextAfterArtifactId);
        Assert.NotNull(second); Assert.Equal(3, second.Items.Count); Assert.Null(second.NextAfterArtifactId);
        Assert.True(ids.SetEquals(first.Items.Concat(second.Items).Select(i => i.ArtifactId)));
        Assert.Null(await Queries.ReadPageAsync(_tenant, _project, 50, foreign));
        Assert.Null(await Queries.ReadDetailAsync(_tenant, _project, foreign));
        Assert.Null(await Queries.ReadDetailAsync(Guid.NewGuid(), _project, first.Items[0].ArtifactId));
        Assert.Null(await Queries.ReadDetailAsync(_tenant, Guid.NewGuid(), first.Items[0].ArtifactId));
        var detail = await Queries.ReadDetailAsync(_tenant, _project, first.Items[0].ArtifactId);
        Assert.NotNull(detail); Assert.Equal("manifest_config_hash_verified", detail.ContentVerification);
        Assert.Equal("not_checked", detail.LayerAvailability); Assert.Equal("not_checked", detail.SignatureVerification);
    }

    [Fact]
    public async Task Release_reader_only_returns_exact_project_artifact_without_claim_or_registry_access()
    {
        var id = await Register(Pending(declaration: true));
        IReleaseArtifactReader reader = Queries;
        var actual = await reader.ReadReleaseArtifactUnderAdmissionAsync(_tenant, _project, id);
        Assert.NotNull(actual); Assert.Equal(id, actual.ArtifactId);
        Assert.Equal("manifest_config_hash_verified", actual.ContentVerification);
        Assert.Equal("not_checked", actual.SignatureVerification);
        Assert.Null(await reader.ReadReleaseArtifactUnderAdmissionAsync(Guid.NewGuid(), _project, id));
        Assert.Null(await reader.ReadReleaseArtifactUnderAdmissionAsync(_tenant, Guid.NewGuid(), id));
        Assert.Null(await reader.ReadReleaseArtifactUnderAdmissionAsync(_tenant, _project, Guid.Empty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(-1)]
    public async Task Invalid_page_size_is_not_repaired_into_a_query(int limit) =>
        Assert.Null(await Queries.ReadPageAsync(_tenant, _project, limit, null));

    [Fact]
    public async Task Key_lookup_requires_same_installation_principal_project_and_business_hash()
    {
        var original = Pending(); var store = new ArtifactRegistrationStore(_db);
        await store.PreparePendingAsync(original); await _db.SaveChangesAsync();
        var replay = await Queries.FindOwnKeyAsync(Pending()); Assert.NotNull(replay);
        Assert.Equal("in_progress", replay.Status); Assert.Equal(original.Id, replay.RegistrationId);
        Assert.Null(await Queries.FindOwnKeyAsync(Pending(principal: Guid.NewGuid())));
        Assert.Null(await Queries.FindOwnKeyAsync(Pending(project: Guid.NewGuid())));
        Assert.Null(await Queries.FindOwnKeyAsync(Pending(tenant: Guid.NewGuid())));
        Assert.Equal("key_conflict", (await Queries.FindOwnKeyAsync(Pending(declaration: true)))!.Status);
        await store.PrepareFailedAsync(original, ArtifactRegistrationFailure.RegistryUnavailable, Now.AddSeconds(1));
        await _db.SaveChangesAsync();
        Assert.Equal("terminal_replay", (await Queries.FindOwnKeyAsync(Pending()))!.Status);
        Assert.Equal(1, await _db.Registrations.CountAsync(r => r.InstallationId == _installation));
    }
}
