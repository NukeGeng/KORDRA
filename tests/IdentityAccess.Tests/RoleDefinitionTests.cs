using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class DefinitionRuleTests
{
    [Theory]
    [InlineData("target.read", "project", "allow", true)]
    [InlineData("org.manage", "organization", "deny", true)]
    [InlineData("target.read", "organization", "allow", false)]
    [InlineData("platform.system.manage", "organization", "allow", false)]
    [InlineData("deployment.execute", "environment", "allow", false)]
    [InlineData("target.read", "project", "unknown", false)]
    public void Foundation_rules_are_exact_and_reject_unsupported_catalog_entries(string permission, string scope, string effect, bool valid) =>
        Assert.Equal(valid, FoundationPermissions.IsValid(new(permission, scope, effect)));

    [Fact]
    public void Explicit_deny_and_scope_mismatch_override_role_label()
    {
        var definition = new RoleDefinitionSnapshot(Guid.NewGuid(), 1, "Admin", [new("target.read", "project"), new("target.read", "project", "deny")]);
        Assert.False(FoundationPermissions.Allows(definition, "target.read", "project"));
        Assert.False(FoundationPermissions.Allows(definition, "target.read", "environment"));
        Assert.False(FoundationPermissions.Allows(definition with { Rules = [] }, "target.read", "project"));
    }
}

[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class RoleDefinitionTests : IAsyncLifetime
{
    private LocalAuthenticationTestHost _host = null!;
    private PooledDbContextFactory<KordraIdentityDbContext> _contexts = null!;
    private readonly List<Guid> _ids = [];
    public async Task InitializeAsync()
    {
        _host = await LocalAuthenticationTestHost.StartAsync();
        _contexts = new PooledDbContextFactory<KordraIdentityDbContext>(new DbContextOptionsBuilder<KordraIdentityDbContext>()
            .UseNpgsql(TestDatabaseConfiguration.TryGetConnectionString()!, p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
    }

    [Fact]
    public async Task Exact_revision_replay_conflict_update_and_retirement_never_follow_latest_implicitly()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var store = new RoleDefinitionStore(_contexts, gate, new IdentityAccessAuditTransactionAdapter());
        using var offline = new TestOfflineLease();
        var manifest = Guid.NewGuid();
        var first = await store.PublishOfflineAsync(offline, manifest, Guid.Empty, 0, "Viewer", [new("org.read", "organization")]);
        Assert.Equal("success", first.Status); _ids.Add(first.DefinitionId);
        Assert.Equal(first, await store.PublishOfflineAsync(offline, manifest, Guid.Empty, 0, "Viewer", [new("org.read", "organization")]));
        Assert.Equal("conflict", (await store.PublishOfflineAsync(offline, manifest, Guid.Empty, 0, "Changed", [new("org.read", "organization")])).Status);
        Assert.NotNull(await store.GetCurrentAsync(first.DefinitionId, 1));
        Assert.Null(await store.GetCurrentAsync(first.DefinitionId, 2));
        var next = await store.PublishOfflineAsync(offline, Guid.NewGuid(), first.DefinitionId, 1, "ReadOnly", [new("org.read", "organization")]);
        Assert.Equal(2, next.Revision); Assert.Null(await store.GetCurrentAsync(first.DefinitionId, 1));
        Assert.Equal("conflict", (await store.PublishOfflineAsync(offline, Guid.NewGuid(), first.DefinitionId, 1, "stale", [new("org.read", "organization")])).Status);
        Assert.Equal("success", (await store.PublishOfflineAsync(offline, Guid.NewGuid(), first.DefinitionId, 2, "Retired", [new("org.read", "organization")], retire: true)).Status);
        Assert.Null(await store.GetCurrentAsync(first.DefinitionId, 3));
        await using var db = await _contexts.CreateDbContextAsync();
        Assert.Equal(3, await db.RoleDefinitions.CountAsync(x => x.DefinitionId == first.DefinitionId));
    }

    [Fact]
    public async Task Missing_offline_lease_or_unknown_permission_cannot_publish()
    {
        var store = new RoleDefinitionStore(_contexts, new(), new IdentityAccessAuditTransactionAdapter());
        using var offline = new TestOfflineLease();
        Assert.Equal("invalid", (await store.PublishOfflineAsync(offline, Guid.NewGuid(), Guid.Empty, 0, "Admin", [new("platform.system.manage", "organization")])).Status);
        offline.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PublishOfflineAsync(offline, Guid.NewGuid(), Guid.Empty, 0, "Viewer", [new("org.read", "organization")]));
    }

    public async Task DisposeAsync()
    {
        await using var db = await _contexts.CreateDbContextAsync();
        await db.RoleDefinitions.Where(x => _ids.Contains(x.DefinitionId)).ExecuteDeleteAsync();
        await _host.DisposeAsync();
    }
}

internal sealed class TestOfflineLease : IOfflineProvisioningLease, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "kordra-offline-test-" + Guid.NewGuid().ToString("N"));
    private readonly FileStream _file;
    private bool _disposed;
    public TestOfflineLease() => _file = new FileStream(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    public void RequireHeld() { if (_disposed || _file.SafeFileHandle.IsClosed) throw new InvalidOperationException("Offline lease is no longer held."); }
    public void Dispose() { _disposed = true; _file.Dispose(); File.Delete(_path); }
}
