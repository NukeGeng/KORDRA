using Kordra.ArtifactManagement.Domain;
using Kordra.DbMigrator;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ArtifactManagement.Tests;

public sealed class ArtifactBoundaryTests
{
    [Theory]
    [InlineData("runtime", "kordra", "kordra_app")]
    [InlineData("offline", "kordra", "kordra_admin")]
    [InlineData("test", "kordra_test", "kordra_test")]
    public void Factory_profiles_accept_only_their_explicit_loopback_identity(string profile, string database, string user)
    {
        var locator = $"Host=127.0.0.1;Port=5432;Database={database};Username={user}";
        using var db = new ArtifactDbContext(Options(profile, locator));
        var target = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.Equal(database, target.Database); Assert.Equal(user, target.Username);
        Assert.Equal("127.0.0.1", target.Host); Assert.Equal(5432, target.Port);
    }

    [Theory]
    [InlineData("Host=localhost;Database=kordra;Username=kordra_app")]
    [InlineData("Host=127.0.0.1;Port=5433;Database=kordra;Username=kordra_app")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=kordra_app")]
    [InlineData("Host=127.0.0.1;Database=kordra;Username=postgres;Password=canary-secret")]
    [InlineData("not-a-locator;canary-secret")]
    [InlineData("")]
    public void Runtime_target_rejection_does_not_echo_a_locator_or_fall_back(string locator)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ArtifactDbContextFactory.CreateRuntimeOptions(locator));
        Assert.DoesNotContain("canary-secret", error.Message);
        Assert.DoesNotContain("Password", error.Message);
    }

    [Fact]
    public void Enlisted_connections_cannot_silently_upgrade_the_runtime_role_to_admin()
    {
        using var admin = new NpgsqlConnection("Host=127.0.0.1;Database=kordra;Username=kordra_admin");
        Assert.Throws<InvalidOperationException>(() => ArtifactDbContextFactory.CreateEnlistedOptions(admin));
        _ = ArtifactDbContextFactory.CreateEnlistedOptions(admin, offline: true);
        using var app = new NpgsqlConnection("Host=127.0.0.1;Database=kordra;Username=kordra_app");
        _ = ArtifactDbContextFactory.CreateEnlistedOptions(app);
        Assert.Throws<InvalidOperationException>(() => ArtifactDbContextFactory.CreateEnlistedOptions(app, offline: true));
    }

    [Fact]
    public async Task Mutation_without_caller_transaction_rejects_before_opening_a_connection()
    {
        using var db = new ArtifactDbContext(ArtifactDbContextFactory.CreateTestOptions(
            "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Timeout=1"));
        var identity = ArtifactIdentity.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "team/frontend",
            OCIImageDigest.Parse("sha256:" + new string('a', 64)), ArtifactPlatform.Create("linux", "amd64", null));
        var pending = ArtifactRegistrationReceipt.Pending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), new(identity, null), "request-00000001", DateTimeOffset.UtcNow);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new ArtifactRegistrationStore(db).PreparePendingAsync(pending));
        Assert.Contains("shared transaction", error.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(typeof(ArtifactVerificationFacts).GetConstructors());
    }

    [Fact]
    public async Task Migration_command_uses_documented_locator_and_rejects_test_database_before_IO()
    {
        const string name = "KORDRA_ARTIFACT_MIGRATION_CONNECTION_STRING";
        Assert.Equal(name, ArtifactManagementMigrationCommand.ConnectionEnvironmentVariable);
        var previous = Environment.GetEnvironmentVariable(name);
        var stderr = Console.Error;
        using var capture = new StringWriter();
        try
        {
            Console.SetError(capture);
            Environment.SetEnvironmentVariable(name,
                "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=canary-secret");
            Assert.Equal(2, await ArtifactManagementMigrationCommand.RunAsync(["artifact-management"]));
            Assert.Contains("not the approved", capture.ToString());
            Assert.DoesNotContain("Set the private", capture.ToString());
            Assert.DoesNotContain("canary-secret", capture.ToString());
        }
        finally { Console.SetError(stderr); Environment.SetEnvironmentVariable(name, previous); }
    }

    private static DbContextOptions<ArtifactDbContext> Options(string profile, string locator) => profile switch
    {
        "runtime" => ArtifactDbContextFactory.CreateRuntimeOptions(locator),
        "offline" => ArtifactDbContextFactory.CreateOfflineMigrationOptions(locator),
        _ => ArtifactDbContextFactory.CreateTestOptions(locator)
    };
}
