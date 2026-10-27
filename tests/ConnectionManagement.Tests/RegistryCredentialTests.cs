using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Commands;
using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.ConnectionManagement.Keychain;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace Kordra.Credentials.Tests;

public sealed class RegistryCredentialTests : IAsyncLifetime
{
    private readonly Guid _installation = Guid.NewGuid(), _tenant = Guid.NewGuid(), _project = Guid.NewGuid(),
        _registry = Guid.NewGuid(), _locator = Guid.NewGuid();
    private readonly Lease _lease = new();
    private readonly Clock _clock = new();
    private readonly FairAsyncReaderWriterAdmissionGate _gate = new();
    private CredentialDbContext _db = null!;
    private IDbContextTransaction? _tx;
    private RegistryCredentialBindingStore Store => new(_db);

    public async Task InitializeAsync()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private isolated kordra_test locator required.");
        _db = new(CredentialDbContextFactory.CreateTestOptions(locator));
        await _db.Database.MigrateAsync();
        _tx = await _db.Database.BeginTransactionAsync();
    }
    public async Task DisposeAsync()
    {
        if (_tx is not null) await _tx.DisposeAsync();
        if (_db is not null) await _db.DisposeAsync();
    }
    private RegistryCredentialManifest Create(Guid? reference = null) => new(1, Guid.NewGuid(), _installation, _tenant,
        _project, _registry, "team/frontend", "artifact.registry.read.v1", "artifact.register", "create", null, 0,
        Guid.NewGuid(), Guid.NewGuid(), reference ?? Guid.NewGuid(), _locator, _clock.Now.AddMinutes(-1), _clock.Now.AddHours(1));
    private async Task<RegistryCredentialBindingSnapshotV1> Apply(RegistryCredentialManifest manifest)
    {
        var row = await Store.StageAsync(_lease, manifest, _clock.Now, default);
        Assert.NotNull(row); await _db.SaveChangesAsync(); return row;
    }
    private async Task<CredentialBindingSnapshotV1> Legacy(Guid reference)
    {
        var manifest = new CredentialManifest(1, Guid.NewGuid(), _installation, _tenant, Guid.NewGuid(),
            "inventory.readonly.v1", "target.read", "create", null, 0, Guid.NewGuid(), Guid.NewGuid(), reference,
            _locator, _clock.Now.AddMinutes(-1), _clock.Now.AddHours(1));
        var result = await new CredentialBindingStore(_db).StageAsync(_lease, manifest, _clock.Now, default);
        Assert.NotNull(result); await _db.SaveChangesAsync(); return result;
    }

    [Fact]
    public async Task Exact_registry_scope_create_rotate_revoke_and_recreate_keep_monotonic_history()
    {
        Assert.False(_db.Database.HasPendingModelChanges());
        var original = Create(); var first = await Apply(original);
        Assert.Equal(original.Selection, first.Selection);
        Assert.Equal(1, first.Revision); Assert.True(first.IsValidAt(_clock.Now));
        var rotate = original with
        {
            ManifestId = Guid.NewGuid(),
            Action = "rotate",
            ExpectedBindingId = first.BindingId,
            ExpectedRevision = first.Revision,
            ReferenceId = Guid.NewGuid()
        };
        var second = await Apply(rotate);
        Assert.Equal(2, second.Revision); Assert.NotEqual(first.Digest(), second.Digest());
        Assert.Null(await Store.ReadLocatorUnderAdmissionAsync(first, default));
        Assert.Equal(_locator, await Store.ReadLocatorUnderAdmissionAsync(second, default));
        var revoke = rotate with
        {
            ManifestId = Guid.NewGuid(),
            Action = "revoke",
            ExpectedRevision = 2,
            ProfileId = null,
            ReferenceId = null,
            LocatorId = null,
            NotBeforeUtc = null,
            ExpiresAtUtc = null
        };
        var third = await Apply(revoke); Assert.Equal(3, third.Revision); Assert.False(third.IsValidAt(_clock.Now));
        Assert.Equal(2, await _db.ReferenceAllocations.CountAsync(a => a.InstallationId == _installation));
        var fresh = Create() with { ExpectedBindingId = third.BindingId, ExpectedRevision = 3 };
        var fourth = await Apply(fresh); Assert.Equal(4, fourth.Revision); Assert.NotEqual(third.BindingId, fourth.BindingId);
        Assert.Equal(3, await _db.ReferenceAllocations.CountAsync(a => a.InstallationId == _installation));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("project")]
    [InlineData("registry")]
    [InlineData("repository")]
    [InlineData("consumer")]
    [InlineData("operation")]
    public async Task Foreign_or_legacy_selection_does_not_resolve_registry_metadata(string fault)
    {
        var row = await Apply(Create());
        var wrong = fault switch
        {
            "tenant" => row.Selection with { TenantId = Guid.NewGuid() },
            "project" => row.Selection with { ProjectId = Guid.NewGuid() },
            "registry" => row.Selection with { RegistryId = Guid.NewGuid() },
            "repository" => row.Selection with { Repository = "team/other" },
            "consumer" => row.Selection with { ConsumerId = "inventory.readonly.v1" },
            _ => row.Selection with { Operation = "target.read" }
        };
        Assert.Null(await Store.ReadCurrentUnderAdmissionAsync(wrong));
    }

    [Fact]
    public async Task Global_native_reference_cannot_cross_kind_or_be_reused_after_revoke()
    {
        var reference = Guid.NewGuid(); await Legacy(reference);
        Assert.Null(await Store.StageAsync(_lease, Create(reference), _clock.Now, default));
        var manifest = Create(); var current = await Apply(manifest);
        var legacy = new CredentialManifest(1, Guid.NewGuid(), _installation, _tenant, Guid.NewGuid(), "inventory.readonly.v1", "target.read",
            "create", null, 0, Guid.NewGuid(), Guid.NewGuid(), current.ReferenceId, _locator, _clock.Now.AddMinutes(-1), _clock.Now.AddHours(1));
        Assert.Null(await new CredentialBindingStore(_db).StageAsync(_lease, legacy, _clock.Now, default));
        var revoked = await Apply(manifest with
        {
            ManifestId = Guid.NewGuid(),
            Action = "revoke",
            ExpectedBindingId = current.BindingId,
            ExpectedRevision = 1,
            ProfileId = null,
            ReferenceId = null,
            LocatorId = null,
            NotBeforeUtc = null,
            ExpiresAtUtc = null
        });
        Assert.Null(await Store.StageAsync(_lease, Create(current.ReferenceId) with { ExpectedBindingId = revoked.BindingId, ExpectedRevision = 2 }, _clock.Now, default));
    }

    [Fact]
    public async Task SQL_reference_owner_FK_rejects_cross_kind_even_when_global_ID_exists()
    {
        var legacy = await Legacy(Guid.NewGuid());
        var current = await Apply(Create());
        _db.RegistryReferences.Add(new()
        {
            InstallationId = _installation,
            TenantId = _tenant,
            ProfileId = current.ProfileId,
            ReferenceId = legacy.ReferenceId,
            LocatorId = _locator
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Manifest_roundtrip_is_closed_and_legacy_bytes_do_not_gain_registry_fields()
    {
        var manifest = Create(); Assert.Equal(manifest, RegistryCredentialManifest.Parse(manifest.CanonicalBytes()));
        var json = Encoding.UTF8.GetString(manifest.CanonicalBytes());
        Assert.DoesNotContain("targetId", json);
        Assert.Null(RegistryCredentialManifest.Parse(Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"targetId\":null"))));
        Assert.Null(RegistryCredentialManifest.Parse(Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"))));
        var legacy = await Legacy(Guid.NewGuid());
        var before = legacy.Digest();
        await Apply(Create());
        Assert.Equal(before, (await new CredentialBindingStore(_db).ReadCurrentUnderAdmissionAsync(legacy.Selection))!.Digest());
    }

    [Fact]
    public async Task Broker_uses_exact_typed_reference_exits_helper_before_C_and_clears_owned_bytes()
    {
        var snapshot = await Apply(Create()); var lookup = new Lookup(); var broker = Broker(lookup);
        using var reservation = broker.TryReserve(); Assert.NotNull(reservation);
        var c = false;
        var consumer = new Consumer((bytes, context) =>
        {
            Assert.True(c); Assert.Equal(snapshot.Selection, context.Selection);
            Assert.Equal(lookup.Original, bytes.ToArray());
            return true;
        });
        var result = await reservation.ReadRegistryAndUseAsync(Context(snapshot), snapshot.Digest(), async (operation, token) =>
        {
            using var one = broker.TryReserve(); using var two = broker.TryReserve();
            Assert.NotNull(one); Assert.NotNull(two); // helper slot has already been returned before C
            c = true; return await operation(token);
        }, consumer, default);
        Assert.True(result); Assert.Single(lookup.Requests);
        Assert.Equal(snapshot.ReferenceId, lookup.Requests[0].ReferenceId);
        Assert.Equal(_installation, lookup.Requests[0].InstallationId);
        Assert.All(lookup.LastBytes!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Consumer_failure_does_not_expose_plaintext_and_releases_shared_slots()
    {
        var snapshot = await Apply(Create()); var lookup = new Lookup(); var broker = Broker(lookup);
        using var reservation = broker.TryReserve(); Assert.NotNull(reservation);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => reservation.ReadRegistryAndUseAsync(Context(snapshot), snapshot.Digest(),
            (operation, token) => operation(token), new Consumer((_, _) => throw new InvalidOperationException("synthetic-secret-canary")), default));
        Assert.DoesNotContain("synthetic-secret-canary", failure.Message); Assert.Null(failure.InnerException);
        Assert.All(lookup.LastBytes!, value => Assert.Equal(0, value));
        using var first = broker.TryReserve(); using var second = broker.TryReserve(); Assert.NotNull(first); Assert.NotNull(second);
    }

    [Fact]
    public async Task Stale_metadata_and_wrong_consumer_never_start_lookup()
    {
        var snapshot = await Apply(Create()); var lookup = new Lookup(); var broker = Broker(lookup);
        using (var reservation = broker.TryReserve())
        {
            Assert.NotNull(reservation);
            await Assert.ThrowsAsync<InvalidOperationException>(() => reservation.ReadRegistryAndUseAsync(Context(snapshot), new string('0', 64),
                (operation, token) => operation(token), new Consumer((_, _) => true), default));
        }
        using (var reservation = broker.TryReserve())
        {
            Assert.NotNull(reservation);
            await Assert.ThrowsAsync<InvalidOperationException>(() => reservation.ReadRegistryAndUseAsync(Context(snapshot), snapshot.Digest(),
                (operation, token) => operation(token), new Consumer((_, _) => true, "inventory.readonly.v1"), default));
        }
        Assert.Empty(lookup.Requests);
    }

    [Fact]
    public async Task Unknown_helper_quiescence_closes_gate_before_C_and_never_releases_uncertain_slot()
    {
        var snapshot = await Apply(Create()); var lookup = new Lookup { Quiescent = false }; var broker = Broker(lookup);
        using var reservation = broker.TryReserve(); Assert.NotNull(reservation); var c = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => reservation.ReadRegistryAndUseAsync(Context(snapshot), snapshot.Digest(),
            (operation, token) => { c = true; return operation(token); }, new Consumer((_, _) => true), default));
        Assert.False(c); Assert.All(lookup.LastBytes!, value => Assert.Equal(0, value));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
        using var onlyOtherSlot = broker.TryReserve(); Assert.NotNull(onlyOtherSlot); Assert.Null(broker.TryReserve());
    }

    [Fact]
    public void Brokers_and_consumer_kinds_share_exactly_two_slots_per_gate()
    {
        var oneBroker = Broker(new Lookup()); var otherBroker = Broker(new Lookup());
        using var legacy = oneBroker.TryReserve(); using var registry = otherBroker.TryReserve();
        Assert.NotNull(legacy); Assert.NotNull(registry); Assert.Null(oneBroker.TryReserve());
        legacy.Dispose(); legacy.Dispose();
        using var available = otherBroker.TryReserve(); Assert.NotNull(available); Assert.Null(oneBroker.TryReserve());
    }

    private RegistryCredentialUseContextV1 Context(RegistryCredentialBindingSnapshotV1 snapshot) => new(Guid.NewGuid(), snapshot.Selection, new string('e', 64));
    private CredentialBroker Broker(Lookup lookup)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kordra.slnx"))) root = root.Parent;
        var executable = Path.Combine(root!.FullName, "src/Host/Kordra.Api/bin/Release/net10.0/Kordra.Api");
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable)));
        var config = new KeychainAccessConfiguration(_installation, executable, hash,
            new Dictionary<Guid, string> { [_locator] = Path.Combine(Path.GetTempPath(), "not-an-opened-keychain-" + _installation) });
        return new(new EnlistedViews(_db.Database.GetDbConnection(), _tx!.GetDbTransaction()), _gate, _clock, config, lookup);
    }

    private sealed class Lease : IOfflineProvisioningLease { public void RequireHeld() { } }
    private sealed class Clock : IAuthorityClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 25, 14, 0, 0, TimeSpan.Zero);
        public DateTimeOffset GetTrustedUtcNow() => Now;
    }
    private sealed class Consumer(Func<ReadOnlyMemory<byte>, RegistryCredentialUseContextV1, bool> read,
        string id = "artifact.registry.read.v1") : RegistryCredentialConsumer<bool>
    {
        public override string Id => id;
        public override Task<bool> ReadAsync(ReadOnlyMemory<byte> credential, RegistryCredentialUseContextV1 context, CancellationToken ct) =>
            Task.FromResult(read(credential, context));
    }
    private sealed class Lookup : ISecretLookup
    {
        public readonly byte[] Original = [0, 255, 17, 42, 10, 13];
        public byte[]? LastBytes;
        public bool Quiescent = true;
        public List<KeychainRequest> Requests { get; } = [];
        public Task<SecretLookup> ReadAsync(KeychainRequest request, CancellationToken ct)
        { Requests.Add(request); LastBytes = Original.ToArray(); return Task.FromResult(new SecretLookup(LastBytes, Quiescent)); }
    }
    // Synthetic read views share only this test's rollback transaction, avoiding committed fixture
    // references or a production metadata-cleanup bypass. No real Keychain is opened by this fake.
    private sealed class EnlistedViews(DbConnection connection, DbTransaction transaction) : IDbContextFactory<CredentialDbContext>
    {
        public CredentialDbContext CreateDbContext()
        {
            var db = new CredentialDbContext(CredentialDbContextFactory.CreateOfflineEnlistedOptions(connection));
            db.Database.UseTransaction(transaction); return db;
        }
        public Task<CredentialDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }
}
