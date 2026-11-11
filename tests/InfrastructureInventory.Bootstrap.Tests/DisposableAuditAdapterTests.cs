using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.ReleaseManagement.Contracts;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using System.Data;
using System.Data.Common;
using Kordra.Api;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Binding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class DisposableAuditAdapterTests
{
    private static readonly Guid Run = Guid.NewGuid();
    private const string Secret = "synthetic-do-not-echo";
    private static Binding Profile() => (Run, new string('a', 64), new string('b', 64), DateTimeOffset.UtcNow.AddMinutes(10));
    private static string Locator() => $"Host=127.0.0.1;Database=kordra_test_{Run:N};Username=kordra_test;Password={Secret};Pooling=false";

    [Fact]
    public void ExplicitIsolatedAuditKeepsCaseModelsDistinctWithoutOpeningConnection()
    {
        using var connection = new NpgsqlConnection(Locator());
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var profile = Profile();
        using var one = AuditDbContextFactory.CreateDisposableIsolatedTestContext(connection, first, profile);
        using var two = AuditDbContextFactory.CreateDisposableIsolatedTestContext(connection, second, profile);
        Assert.Same(connection, one.Database.GetDbConnection()); Assert.Same(connection, two.Database.GetDbConnection());
        Assert.Equal("t12_" + first.ToString("N"), one.Model.GetDefaultSchema());
        Assert.Equal("t12_" + second.ToString("N"), two.Model.GetDefaultSchema());
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateIsolatedTestContext(connection, first));
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateDisposableIsolatedTestContext(connection, Guid.Empty, profile));
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData("identity")][InlineData("tenant")][InlineData("offline")][InlineData("artifact")][InlineData("catalog")][InlineData("offline-artifact")]
    public async Task ProfiledAdaptersRejectExpiredSelectionBeforeAnyDatabaseOrTransactionEffect(string kind)
    {
        var profile = Profile(); profile.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var connection = new NpgsqlConnection(Locator()); using var root = new ClosedRoot(connection);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Enlist(kind, connection, root, profile));
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.Equal(ConnectionState.Closed, connection.State); Assert.Equal(0, root.CommitCalls); Assert.Equal(0, root.RollbackCalls);
    }

    [Theory]
    [InlineData("identity")][InlineData("tenant")][InlineData("offline")][InlineData("artifact")][InlineData("catalog")][InlineData("offline-artifact")]
    public async Task ProfiledAdaptersStillRequireAnOpenExactCallerRoot(string kind)
    {
        using var connection = new NpgsqlConnection(Locator()); using var root = new ClosedRoot(connection);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Enlist(kind, connection, root, Profile()));
        Assert.Equal(ConnectionState.Closed, connection.State); Assert.Equal(0, root.CommitCalls); Assert.Equal(0, root.RollbackCalls);
    }

    [Fact]
    public async Task OfflineProfileCannotReplaceCustody()
    {
        using var connection = new NpgsqlConnection(Locator()); using var root = new ClosedRoot(connection);
        var factory = OfflinePolicyAuditTransactionFactory.CreateDisposableTestFactory(Profile());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await factory.EnlistAsync(connection, root, new Lease(false)));
        Assert.Equal(ConnectionState.Closed, connection.State); Assert.Equal(0, root.CommitCalls);
    }

    [Fact]
    public void OfflineReceiptReadSelectionStillRequiresHeldCustodyAndDoesNotOpenConnection()
    {
        using var connection = new NpgsqlConnection(Locator());
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateDisposableOfflineReceiptReadOptions(connection, new Lease(false), Profile()));
        using var context = new AuditDbContext(AuditDbContextFactory.CreateDisposableOfflineReceiptReadOptions(connection, new Lease(true), Profile()));
        Assert.Same(connection, context.Database.GetDbConnection());
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateOfflineReceiptReadOptions(connection, new Lease(true)));
    }

    [Fact]
    public void PoolRegressionSelectionPreservesPoolAndTargetWithoutWeakeningOrdinaryGuard()
    {
        var profile = Profile();
        var parsed = new NpgsqlConnectionStringBuilder(Locator()) { Pooling = true, MaxPoolSize = 1, Timeout = 15 };
        var locator = parsed.ConnectionString;
        using var context = new AuditDbContext(AuditDbContextFactory.CreateDisposableSingleConnectionPoolTestOptions(locator, profile));
        Assert.Equal(locator, context.Database.GetConnectionString());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateDisposableTestOptions(locator, profile));
        parsed.MaxPoolSize = 2;
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateDisposableSingleConnectionPoolTestOptions(parsed.ConnectionString, profile));
        parsed.MaxPoolSize = 1; parsed.Database = "kordra_test";
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateDisposableSingleConnectionPoolTestOptions(parsed.ConnectionString, profile));
        parsed.Database = "kordra_test_" + Run.ToString("N"); parsed.Options = "-c search_path=app";
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateDisposableSingleConnectionPoolTestOptions(parsed.ConnectionString, profile));
    }

    [Fact]
    public void ReleaseAndApprovalRecoveryReadersRequireCustodyAndTheSelectedDatabase()
    {
        using var selected = new NpgsqlConnection(Locator());
        using var legacy = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Pooling=false");
        var profile = Profile();
        var held = new Lease(true);
        var released = new Lease(false);
        using var approvals = new ApprovalDbContext(ApprovalDbContextFactory.CreateDisposableOfflineReceiptReadOptions(selected, held, profile));
        using var releases = new ReleaseDbContext(ReleaseDbContextFactory.CreateDisposableOfflineReceiptReadOptions(selected, held, profile));
        Assert.Same(selected, approvals.Database.GetDbConnection());
        Assert.Same(selected, releases.Database.GetDbConnection());
        Assert.Throws<InvalidOperationException>(() => ApprovalDbContextFactory.CreateDisposableOfflineReceiptReadOptions(selected, released, profile));
        Assert.Throws<InvalidOperationException>(() => ReleaseDbContextFactory.CreateDisposableOfflineReceiptReadOptions(selected, released, profile));
        Assert.Throws<InvalidOperationException>(() => ApprovalDbContextFactory.CreateDisposableOfflineReceiptReadOptions(legacy, held, profile));
        Assert.Throws<InvalidOperationException>(() => ReleaseDbContextFactory.CreateDisposableOfflineReceiptReadOptions(legacy, held, profile));
        Assert.Throws<InvalidOperationException>(() => ApprovalDbContextFactory.CreateOfflineReceiptReadOptions(selected, held));
        Assert.Throws<InvalidOperationException>(() => ReleaseDbContextFactory.CreateOfflineReceiptReadOptions(selected, held));
        Assert.Equal(ConnectionState.Closed, selected.State);
        Assert.Equal(ConnectionState.Closed, legacy.State);
    }

    private static async Task Enlist(string kind, NpgsqlConnection connection, DbTransaction root, Binding profile)
    {
        switch (kind)
        {
            case "offline-artifact": await using (var enlisted = await OfflineArtifactAuditTransactionFactory.CreateDisposableTestFactory(new Lease(true), profile).EnlistAsync(connection, root, default)) { } break;
            case "catalog": await using (var enlisted = await CatalogAuditTransactionFactory.CreateDisposableTestFactory(profile).EnlistAsync(connection, root, default)) { } break;
            case "artifact": await using (var enlisted = await ArtifactAuditTransactionFactory.CreateDisposableTestFactory(profile).EnlistAsync(connection, root, default)) { } break;
            case "identity": await using (var enlisted = await IdentityAccessAuditTransactionAdapter.CreateDisposableTestAdapter(profile).EnlistAsync(connection, root)) { } break;
            case "tenant": await using (var enlisted = await TenantAuditTransactionAdapter.CreateDisposableTestAdapter(profile).EnlistAsync(connection, root)) { } break;
            case "offline": await using (var enlisted = await OfflinePolicyAuditTransactionFactory.CreateDisposableTestFactory(profile).EnlistAsync(connection, root, new Lease(true))) { } break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
    private sealed class Lease(bool held) : IOfflineAuditLease, IOfflineApprovalCustody, IReleaseOfflineCustody
    { public void RequireHeld() { if (!held) throw new InvalidOperationException("Fixture custody is not held."); } }
    private sealed class ClosedRoot(DbConnection connection) : DbTransaction
    {
        public int CommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        public override void Commit() { CommitCalls++; throw new InvalidOperationException("No unit fixture commit allowed."); }
        public override void Rollback() { RollbackCalls++; throw new InvalidOperationException("No unit fixture rollback allowed."); }
    }
}
