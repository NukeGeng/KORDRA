using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Commands;
using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.Credentials.Tests;

public sealed class CredentialMetadataTests : IAsyncLifetime
{
    private readonly Guid _installation = Guid.NewGuid(), _tenant = Guid.NewGuid(), _target = Guid.NewGuid();
    private readonly Custody _custody = new(); private readonly Clock _clock = new(); private readonly FairAsyncReaderWriterAdmissionGate _gate = new();
    private PooledDbContextFactory<KordraIdentityDbContext> _identity = null!;
    private PooledDbContextFactory<CredentialDbContext> _sec = null!;
    private AuditAppendStore _audit = null!; private string _locator = "";
    private readonly HashSet<Guid> _manifests = [];
    public async Task InitializeAsync()
    {
        _locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Private kordra_test locator required.");
        _sec = new(CredentialDbContextFactory.CreateTestOptions(_locator));
        _identity = new(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(_locator, x => x.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
        await using (var db = await _identity.CreateDbContextAsync()) await db.Database.MigrateAsync();
        var af = new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(_locator));
        await using (var db = await af.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await _sec.CreateDbContextAsync()) await db.Database.MigrateAsync();
        _audit = new(af);
    }
    private OfflineCredentialCoordinator Coordinator(IOfflinePolicyAuditTransactionFactory? audit = null) =>
        new(_installation, _identity, new Tenant(_tenant), new Target(_tenant, _target), _audit, _gate, _clock, audit);
    private CredentialManifest Create() => new(1, Guid.NewGuid(), _installation, _tenant, _target, "inventory.readonly.v1", "target.read", "create", null, 0,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _clock.Now.AddMinutes(-1), _clock.Now.AddHours(1));
    private Task<CredentialOperationResult> Apply(CredentialManifest m, IOfflinePolicyAuditTransactionFactory? factory = null)
    { _manifests.Add(m.ManifestId); return Coordinator(factory).ExecuteAsync(_custody, m.CanonicalBytes(), m.Hash()); }

    [Fact]
    public async Task Create_rotate_revoke_replay_and_new_binding_keep_monotonic_history_and_audit()
    {
        var original = Create(); var a = await Apply(original); Assert.Equal("success", a.Status); Assert.Equal(1, a.Revision);
        Assert.Equal(a, await Apply(original));
        var rotate = original with { ManifestId = Guid.NewGuid(), Action = "rotate", ExpectedBindingId = a.BindingId, ExpectedRevision = a.Revision, ReferenceId = Guid.NewGuid() };
        var b = await Apply(rotate); Assert.Equal("success", b.Status); Assert.Equal(2, b.Revision); Assert.NotEqual(a.ReferenceId, b.ReferenceId);
        Assert.Equal("conflict", (await Apply(rotate with { ManifestId = Guid.NewGuid(), ExpectedRevision = 1 })).Status);
        Assert.Equal("conflict", (await Apply(rotate with { ReferenceId = Guid.NewGuid() })).Status);
        var revoke = rotate with { ManifestId = Guid.NewGuid(), Action = "revoke", ExpectedRevision = 2, ProfileId = null, ReferenceId = null, LocatorId = null, NotBeforeUtc = null, ExpiresAtUtc = null };
        var c = await Apply(revoke); Assert.Equal("success", c.Status); Assert.Equal(3, c.Revision);
        _clock.Now = _clock.Now.AddDays(2); Assert.Equal(a, await Apply(original)); Assert.Equal(c, await Apply(revoke));
        var replacement = Create() with { ExpectedBindingId = c.BindingId, ExpectedRevision = c.Revision };
        var d = await Apply(replacement); Assert.Equal("success", d.Status); Assert.Equal(4, d.Revision); Assert.NotEqual(a.BindingId, d.BindingId);
        await using var db = await _sec.CreateDbContextAsync();
        Assert.Equal(4, await db.Revisions.CountAsync(x => x.InstallationId == _installation));
        Assert.Equal(4, await db.Commands.CountAsync(x => x.InstallationId == _installation));
        var current = await new CredentialBindingStore(db).ReadCurrentUnderAdmissionAsync(replacement.Selection);
        Assert.NotNull(current); Assert.True(current.IsValidAt(_clock.Now));
        Assert.Null(await new CredentialBindingStore(db).ReadCurrentUnderAdmissionAsync(replacement.Selection with { TenantId = Guid.NewGuid() }));
    }
    [Theory]
    [InlineData("tenant")]
    [InlineData("target")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("lease-too-long")]
    [InlineData("reused-reference")]
    public async Task Wrong_scope_validity_and_reference_reuse_cannot_publish(string fault)
    {
        var first = Create(); Assert.Equal("success", (await Apply(first)).Status);
        var m = first with { ManifestId = Guid.NewGuid(), Action = "rotate", ExpectedBindingId = first.BindingId, ExpectedRevision = 1, ReferenceId = Guid.NewGuid() };
        m = fault switch
        {
            "tenant" => m with { TenantId = Guid.NewGuid() },
            "target" => m with { TargetId = Guid.NewGuid() },
            "expired" => m with { ExpiresAtUtc = _clock.Now },
            "future" => m with { NotBeforeUtc = _clock.Now.AddMinutes(1) },
            "lease-too-long" => m with { ExpiresAtUtc = _clock.Now.AddDays(31) },
            _ => m with { ReferenceId = first.ReferenceId }
        };
        Assert.NotEqual("success", (await Apply(m)).Status);
        await using var db = await _sec.CreateDbContextAsync(); Assert.Equal(1, await db.Revisions.CountAsync(x => x.InstallationId == _installation));
        Assert.Equal(1, await db.Commands.CountAsync(x => x.InstallationId == _installation));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Setup_failure_is_available_but_lost_commit_ACK_closes_and_exact_receipt_reconciles(bool lostAck)
    {
        var m = Create();
        var r = await Apply(m, new FaultFactory(lostAck)); Assert.Equal(lostAck ? "unknown" : "unavailable", r.Status);
        await using var db = await _sec.CreateDbContextAsync();
        Assert.Equal(lostAck ? 1 : 0, await db.Revisions.CountAsync(x => x.InstallationId == _installation));
        if (lostAck)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
            var restarted = new OfflineCredentialCoordinator(_installation, _identity, new Tenant(_tenant), new Target(_tenant, _target), _audit, new(), _clock);
            Assert.Equal("success", (await restarted.ExecuteAsync(_custody, m.CanonicalBytes(), m.Hash())).Status);
        }
        else { await using var held = await _gate.AcquireReadAsync(); }
    }
    [Fact]
    public async Task Current_expiry_is_rechecked_after_actual_audit_staging_before_commit()
    {
        var m = Create() with { ExpiresAtUtc = _clock.Now.AddSeconds(1) };
        Assert.Equal("denied", (await Apply(m, new AdvanceFactory(() => _clock.Now = _clock.Now.AddSeconds(1)))).Status);
        await using var db = await _sec.CreateDbContextAsync(); Assert.False(await db.Revisions.AnyAsync(x => x.InstallationId == _installation));
        Assert.False(await db.Commands.AnyAsync(x => x.InstallationId == _installation));
    }
    [Fact]
    public async Task Actual_database_constraints_and_runtime_role_grants_protect_SEC_metadata()
    {
        var m = Create(); Assert.Equal("success", (await Apply(m)).Status);
        await using var db = await _sec.CreateDbContextAsync(); await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT has_table_privilege('kordra_app','app.sec_binding_heads','SELECT') AND NOT has_table_privilege('kordra_app','app.sec_binding_heads','UPDATE') AND NOT has_table_privilege('kordra_app','app.sec_command_receipts','SELECT')";
        Assert.True((bool)(await cmd.ExecuteScalarAsync())!);
        var profile = new ConnectionProfileRecord { InstallationId = _installation, TenantId = Guid.NewGuid(), TargetId = _target, ProfileId = Guid.NewGuid() };
        db.Profiles.Add(profile); await db.SaveChangesAsync();
        db.References.Add(new() { InstallationId = _installation, TenantId = profile.TenantId, ProfileId = profile.ProfileId, ReferenceId = m.ReferenceId!.Value, LocatorId = Guid.NewGuid() });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
    }
    [Fact]
    public async Task Contradictory_stored_result_never_becomes_a_successful_reconciliation()
    {
        var m = Create(); Assert.Equal("success", (await Apply(m)).Status);
        await using (var db = await _sec.CreateDbContextAsync())
        {
            var receipt = await db.Commands.SingleAsync(x => x.InstallationId == _installation && x.ManifestId == m.ManifestId);
            receipt.ResultJson = JsonSerializer.Serialize(new CredentialOperationResult("success", m.BindingId, 1, Guid.NewGuid())); await db.SaveChangesAsync();
        }
        Assert.Equal("unknown", (await Apply(m)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _gate.AcquireReadAsync(); });
    }
    [Fact]
    public void Canonical_manifest_rejects_duplicates_unknown_missing_fields_submicrosecond_and_noncanonical_ids()
    {
        var m = Create(); var json = Encoding.UTF8.GetString(m.CanonicalBytes()); Assert.Equal(m, CredentialManifest.Parse(m.CanonicalBytes()));
        Assert.Equal(m.Hash(), Convert.ToHexStringLower(SHA256.HashData(m.CanonicalBytes())));
        foreach (var bad in new[] { json.Replace("{", "{\"password\":\"SYNTHETIC\",", StringComparison.Ordinal),
            json.Replace("{", "{\"schemaVersion\":1,", StringComparison.Ordinal), json.Replace("\"expectedBindingId\":null,", ""),
            json.Replace(m.TargetId.ToString("D"), m.TargetId.ToString("N")), json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2") })
            Assert.Null(CredentialManifest.Parse(Encoding.UTF8.GetBytes(bad)));
        Assert.False((m with { ExpiresAtUtc = m.ExpiresAtUtc!.Value.AddTicks(1) }).IsValid());
        Assert.Null(CredentialManifest.Parse(new byte[16385]));
    }
    public async Task DisposeAsync()
    {
        await using var db = await _sec.CreateDbContextAsync();
        await db.Heads.Where(x => x.InstallationId == _installation).ExecuteDeleteAsync(); await db.Revisions.Where(x => x.InstallationId == _installation).ExecuteDeleteAsync();
        await db.References.Where(x => x.InstallationId == _installation).ExecuteDeleteAsync(); await db.ReferenceAllocations.Where(x => x.InstallationId == _installation).ExecuteDeleteAsync(); await db.Profiles.Where(x => x.InstallationId == _installation).ExecuteDeleteAsync();
        await db.Commands.Where(x => x.InstallationId == _installation).ExecuteDeleteAsync();
        await using var a = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(_locator));
        await a.Entries.Where(x => _manifests.Contains(x.InvocationId)).ExecuteDeleteAsync();
        await using var i = await _identity.CreateDbContextAsync(); await i.AuthorityClockFloors.Where(x => x.InstallationId == _installation).ExecuteDeleteAsync();
    }
    private sealed class Custody : IOfflineProvisioningLease { public void RequireHeld() { } }
    private sealed class Clock : IAuthorityClock { public DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); public DateTimeOffset GetTrustedUtcNow() => Now; }
    private sealed class Tenant(Guid id) : IPolicyTenantAuthority { public Task<bool> IsActiveTenantUnderAdmissionAsync(Guid tenant, CancellationToken ct = default) => Task.FromResult(tenant == id); }
    private sealed class Target(Guid tenant, Guid id) : ITargetAuthorityReader { public Task<TargetAuthority?> ReadTargetUnderAdmissionAsync(Guid target, CancellationToken ct = default) => Task.FromResult<TargetAuthority?>(target == id ? new(id, tenant, Guid.Parse("c53b430f-3e72-483a-b8f0-22e467ffcf89"), 1, 1, 1, null, null, null) : null); }
    private sealed class FaultFactory(bool commit) : IOfflinePolicyAuditTransactionFactory
    {
        public async ValueTask<IOfflinePolicyAuditTransaction> EnlistAsync(DbConnection c, DbTransaction t, IOfflineAuditLease l, CancellationToken ct = default)
        { if (!commit) throw new OperationCanceledException(); return new LostAck(await new OfflinePolicyAuditTransactionFactory().EnlistAsync(c, t, l, ct)); }
        private sealed class LostAck(IOfflinePolicyAuditTransaction inner) : IOfflinePolicyAuditTransaction
        {
            public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent i, CancellationToken ct = default) => inner.StageAsync(i, ct);
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct = default) { await inner.CommitAsync(ct); throw new IOException("Synthetic lost ACK"); }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
    private sealed class AdvanceFactory(Action change) : IOfflinePolicyAuditTransactionFactory
    {
        public async ValueTask<IOfflinePolicyAuditTransaction> EnlistAsync(DbConnection c, DbTransaction t, IOfflineAuditLease l, CancellationToken ct = default) => new Advanced(await new OfflinePolicyAuditTransactionFactory().EnlistAsync(c, t, l, ct), change);
        private sealed class Advanced(IOfflinePolicyAuditTransaction inner, Action change) : IOfflinePolicyAuditTransaction
        {
            public async Task<AuditTransactionStageOutcome> StageAsync(AuditIntent i, CancellationToken ct = default) { var r = await inner.StageAsync(i, ct); change(); return r; }
            public Task<AuditAppendOutcome> CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
