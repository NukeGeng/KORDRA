using Microsoft.Extensions.DependencyInjection;
using Kordra.Api;
using System.Data;
using System.Text.Json;
using Kordra.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ProductPostgreSqlFixtureSelectionTests
{
    [Fact]
    public void CapturedProfileReachesAllActualOwnerFactoriesWithoutOpeningAConnection()
    {
        var run = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var source = new string('a', 64); var manifest = new string('b', 64);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new {
            schemaVersion = 1, kind = "disposable-postgresql-regression", runId = run.ToString("D"),
            host = "127.0.0.1", port = 5432, database = "kordra_test_" + run.ToString("N"), role = "kordra_test", schema = "app",
            sourceSha256 = source, operationManifestSha256 = manifest,
            expiresAtUtc = now.AddMinutes(10).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'") });
        var profile = DisposablePostgreSqlProfile.Read(bytes, run, source, manifest, now);
        var locator = $"Host=127.0.0.1;Database={profile.Database};Username=kordra_test;Pooling=false";
        var selected = new ProductPostgreSqlFixtureSelection(locator, profile);
        Assert.Same(profile, selected.Profile);
        var services = new ServiceCollection();
        selected.BindRegisteredOwnerTransactions(services);
        Assert.Empty(services); // Do not activate unregistered modules.
        services.AddSingleton<IArtifactAuditTransactionFactory, ArtifactAuditTransactionFactory>();
        selected.BindRegisteredOwnerTransactions(services);
        using (var provider = services.BuildServiceProvider())
            Assert.IsType<ArtifactAuditTransactionFactory>(provider.GetRequiredService<IArtifactAuditTransactionFactory>());
        var custom = new NeverAudit();
        services.AddSingleton<IArtifactAuditTransactionFactory>(custom);
        selected.BindRegisteredOwnerTransactions(services);
        using (var provider = services.BuildServiceProvider())
            Assert.Same(custom, provider.GetRequiredService<IArtifactAuditTransactionFactory>());

        DbContext[] owners = [new Kordra.Modules.IdentityAccess.Infrastructure.KordraIdentityDbContext(selected.Identity()),
            new Kordra.Modules.AuditCompliance.Infrastructure.AuditDbContext(selected.Audit()),
            new Kordra.Modules.TenantManagement.Infrastructure.TenantDbContext(selected.Tenant()),
            new Kordra.Modules.ProjectEnvironment.Infrastructure.ProjectDbContext(selected.Project()),
            new Kordra.Modules.InfrastructureInventory.Infrastructure.InventoryDbContext(selected.Inventory()),
            new Kordra.Modules.ConnectionManagement.Infrastructure.CredentialDbContext(selected.Credential()),
            new Kordra.Modules.ArtifactManagement.Infrastructure.ArtifactDbContext(selected.Artifact()),
            new Kordra.Modules.ReleaseManagement.Infrastructure.ReleaseDbContext(selected.Release()),
            new Kordra.Modules.ApprovalGovernance.Infrastructure.ApprovalDbContext(selected.Approval())];
        try
        {
            Assert.All(owners, db => {
                var connection = db.Database.GetDbConnection();
                Assert.Equal(ConnectionState.Closed, connection.State);
                Assert.Equal(profile.Database, new NpgsqlConnectionStringBuilder(connection.ConnectionString).Database);
                Assert.Null(db.Database.CurrentTransaction);
            });
        }
        finally { foreach (var owner in owners) owner.Dispose(); }
        var legacy = new ProductPostgreSqlFixtureSelection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test", null);
        var legacyServices = new ServiceCollection(); legacyServices.AddSingleton<IArtifactAuditTransactionFactory, ArtifactAuditTransactionFactory>();
        var before = legacyServices.ToArray(); legacy.BindRegisteredOwnerTransactions(legacyServices);
        Assert.Equal(before, legacyServices.ToArray());
        Assert.Throws<InvalidOperationException>(() => new ProductPostgreSqlFixtureSelection(locator, null));
        Assert.Throws<InvalidOperationException>(() => new ProductPostgreSqlFixtureSelection(
            "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Pooling=false", profile));
    }
    private sealed class NeverAudit : IArtifactAuditTransactionFactory
    {
        public ValueTask<IArtifactAuditTransaction> EnlistAsync(System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction transaction, CancellationToken ct) => throw new InvalidOperationException("No unit-test DB effect permitted.");
    }
}
