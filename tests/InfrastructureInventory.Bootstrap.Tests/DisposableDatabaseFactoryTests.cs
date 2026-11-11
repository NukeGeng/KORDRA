using System.Text.Json;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.TenantManagement.Infrastructure;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Xunit;
using Binding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class DisposableDatabaseFactoryTests
{
    private static readonly Guid Run = Guid.NewGuid();
    private const string Source = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Manifest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Secret = "synthetic-do-not-echo";
    private static readonly string[] Owners = ["IAM", "AUD", "TEN", "PRJ", "INV", "SEC", "ART", "REL", "APR"];

    [Theory]
    [InlineData("IAM")][InlineData("AUD")][InlineData("TEN")][InlineData("PRJ")][InlineData("INV")]
    [InlineData("SEC")][InlineData("ART")][InlineData("REL")][InlineData("APR")]
    public void ExplicitSelectionKeepsExactConnectionAndDoesNotOpenIt(string owner)
    {
        var profile = Profile();
        var locator = Locator(); profile.ValidateLocator(locator, DateTimeOffset.UtcNow);
        using var optionsContext = Create(owner, locator, profile.OwnerBinding);
        Assert.Equal(System.Data.ConnectionState.Closed, optionsContext.Database.GetDbConnection().State);
        Assert.Equal("app", optionsContext.Model.GetDefaultSchema());
        using var callerConnection = new NpgsqlConnection(locator);
        using var enlisted = CreateEnlisted(owner, callerConnection, profile.OwnerBinding);
        Assert.Same(callerConnection, enlisted.Database.GetDbConnection());
        Assert.Equal(System.Data.ConnectionState.Closed, callerConnection.State);
        Assert.Null(enlisted.Database.CurrentTransaction);
        Assert.False(enlisted.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single().IsConnectionOwned);
        // Options factories have no commit/DDL/connection ownership, even when the caller disposes the context.
        enlisted.Dispose(); Assert.Equal(System.Data.ConnectionState.Closed, callerConnection.State);
    }

    [Fact]
    public void DefaultAndRuntimeEntriesCannotAcquireDisposableSelectionImplicitly()
    {
        foreach (var owner in Owners)
        {
            var locator = Locator();
            Assert.Throws<InvalidOperationException>(() => DefaultTest(owner, locator));
            Assert.Throws<InvalidOperationException>(() => Runtime(owner, locator));
            Assert.Throws<InvalidOperationException>(() => Offline(owner, locator));
            // The unchanged legacy test boundary remains usable only when selected explicitly by its original entry point.
            DefaultTest(owner, "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        }
    }

    [Theory]
    [InlineData("Host=localhost")][InlineData("Port=5433")]
    [InlineData("Database=kordra")][InlineData("Database=kordra_test")]
    [InlineData("Database=kordra_test_11111111111111111111111111111111")]
    [InlineData("Username=postgres")][InlineData("Username=kordra_admin")][InlineData("Username=kordra_app")]
    [InlineData("Pooling=true")][InlineData("Multiplexing=true")][InlineData("Include Error Detail=true")]
    [InlineData("Persist Security Info=true")][InlineData("Search Path=app")][InlineData("Options=-c search_path=app")]
    [InlineData("Passfile=/tmp/synthetic-do-not-echo")][InlineData("Root Certificate=/tmp/synthetic-do-not-echo")]
    public void EveryOwnerRejectsSubstitutedTargetRoleOrSessionWithoutEcho(string change)
    {
        var binding = Profile().OwnerBinding;
        var parsed = new NpgsqlConnectionStringBuilder(Locator()); var pair = change.Split('=', 2); parsed[pair[0]] = pair[1];
        // Npgsql rejects multiplexing with pooling disabled at construction, before our enlisted entry can run.
        if (parsed.Multiplexing) parsed.Pooling = true;
        foreach (var owner in Owners)
        {
            var error = Assert.Throws<InvalidOperationException>(() => Create(owner, parsed.ConnectionString, binding));
            Assert.DoesNotContain(Secret, error.ToString());
            using var connection = new NpgsqlConnection(parsed.ConnectionString);
            Assert.Throws<InvalidOperationException>(() => CreateEnlisted(owner, connection, binding));
            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        }
    }

    [Theory]
    [InlineData("run")][InlineData("source")][InlineData("manifest")][InlineData("expiry")][InlineData("offset")][InlineData("infinite")]
    public void EveryOwnerRequiresCompleteCurrentBinding(string fault)
    {
        var binding = Profile().OwnerBinding;
        binding = fault switch
        {
            "run" => (Guid.Empty, binding.SourceSha256, binding.OperationManifestSha256, binding.ExpiresAtUtc),
            "source" => (binding.RunId, Source.ToUpperInvariant(), binding.OperationManifestSha256, binding.ExpiresAtUtc),
            "manifest" => (binding.RunId, binding.SourceSha256, "invalid", binding.ExpiresAtUtc),
            "expiry" => (binding.RunId, binding.SourceSha256, binding.OperationManifestSha256, DateTimeOffset.UtcNow.AddMinutes(-1)),
            "offset" => (binding.RunId, binding.SourceSha256, binding.OperationManifestSha256, binding.ExpiresAtUtc.ToOffset(TimeSpan.FromHours(7))),
            "infinite" => (binding.RunId, binding.SourceSha256, binding.OperationManifestSha256, DateTimeOffset.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        foreach (var owner in Owners)
        {
            Assert.Throws<InvalidOperationException>(() => Create(owner, Locator(), binding));
            using var connection = new NpgsqlConnection(Locator());
            Assert.Throws<InvalidOperationException>(() => CreateEnlisted(owner, connection, binding));
            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        }
    }

    private static DisposablePostgreSqlProfile Profile()
    {
        var now = DateTimeOffset.UtcNow;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, kind = "disposable-postgresql-regression",
            runId = Run.ToString("D"), host = "127.0.0.1", port = 5432, database = "kordra_test_" + Run.ToString("N"),
            role = "kordra_test", schema = "app", sourceSha256 = Source, operationManifestSha256 = Manifest,
            expiresAtUtc = now.AddMinutes(10).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'") });
        return DisposablePostgreSqlProfile.Read(bytes, Run, Source, Manifest, now);
    }
    private static string Locator() => $"Host=127.0.0.1;Database=kordra_test_{Run:N};Username=kordra_test;Password={Secret};Pooling=false;Multiplexing=false;Include Error Detail=false";
    private static DbContext Create(string owner, string locator, Binding profile) => owner switch
    {
        "IAM" => new KordraIdentityDbContext(IdentityAccessDbContextFactory.CreateDisposableTestOptions(locator, profile)),
        "AUD" => new AuditDbContext(AuditDbContextFactory.CreateDisposableTestOptions(locator, profile)),
        "TEN" => new TenantDbContext(TenantDbContextFactory.CreateDisposableTestOptions(locator, profile)),
        "PRJ" => new ProjectDbContext(ProjectDbContextFactory.CreateDisposableTestOptions(locator, profile)),
        "INV" => new InventoryDbContext(InventoryDbContextFactory.CreateDisposableTestOptions(locator, profile)),
        "SEC" => new CredentialDbContext(CredentialDbContextFactory.CreateDisposableTestOptions(locator, profile)),
        "ART" => new ArtifactDbContext(ArtifactDbContextFactory.CreateDisposableTestOptions(locator, profile)),
        "REL" => new ReleaseDbContext(ReleaseDbContextFactory.CreateDisposableTestOptions(locator, profile)),
        "APR" => new ApprovalDbContext(ApprovalDbContextFactory.CreateDisposableTestOptions(locator, profile)),
        _ => throw new ArgumentOutOfRangeException(nameof(owner))
    };
    private static DbContext CreateEnlisted(string owner, NpgsqlConnection connection, Binding profile) => owner switch
    {
        "IAM" => new KordraIdentityDbContext(IdentityAccessDbContextFactory.CreateDisposableEnlistedTestOptions(connection, profile)),
        "AUD" => new AuditDbContext(AuditDbContextFactory.CreateDisposableEnlistedTestOptions(connection, profile)),
        "TEN" => new TenantDbContext(TenantDbContextFactory.CreateDisposableEnlistedTestOptions(connection, profile)),
        "PRJ" => new ProjectDbContext(ProjectDbContextFactory.CreateDisposableEnlistedTestOptions(connection, profile)),
        "INV" => new InventoryDbContext(InventoryDbContextFactory.CreateDisposableEnlistedTestOptions(connection, profile)),
        "SEC" => new CredentialDbContext(CredentialDbContextFactory.CreateDisposableEnlistedTestOptions(connection, profile)),
        "ART" => new ArtifactDbContext(ArtifactDbContextFactory.CreateDisposableEnlistedTestOptions(connection, profile)),
        "REL" => new ReleaseDbContext(ReleaseDbContextFactory.CreateDisposableEnlistedTestOptions(connection, profile)),
        "APR" => new ApprovalDbContext(ApprovalDbContextFactory.CreateDisposableEnlistedTestOptions(connection, profile)),
        _ => throw new ArgumentOutOfRangeException(nameof(owner))
    };
    private static void DefaultTest(string owner, string locator)
    {
        switch (owner)
        {
            case "IAM": IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(locator); break;
            case "AUD": _ = AuditDbContextFactory.CreateTestOptions(locator); break;
            case "TEN": _ = TenantDbContextFactory.CreateTestOptions(locator); break;
            case "PRJ": _ = ProjectDbContextFactory.CreateTestOptions(locator); break;
            case "INV": _ = InventoryDbContextFactory.CreateTestOptions(locator); break;
            case "SEC": _ = CredentialDbContextFactory.CreateTestOptions(locator); break;
            case "ART": _ = ArtifactDbContextFactory.CreateTestOptions(locator); break;
            case "REL": _ = ReleaseDbContextFactory.CreateTestOptions(locator); break;
            case "APR": _ = ApprovalDbContextFactory.CreateTestOptions(locator); break;
            default: throw new ArgumentOutOfRangeException(nameof(owner));
        }
    }
    private static void Runtime(string owner, string locator)
    {
        switch (owner)
        {
            case "IAM": _ = IdentityAccessDbContextFactory.CreateRuntimeOptions(locator); break;
            case "AUD": _ = AuditDbContextFactory.CreateRuntimeOptions(locator); break;
            case "TEN": _ = TenantDbContextFactory.CreateRuntimeOptions(locator); break;
            case "PRJ": _ = ProjectDbContextFactory.CreateRuntimeOptions(locator); break;
            case "INV": _ = InventoryDbContextFactory.CreateRuntimeOptions(locator); break;
            case "SEC": _ = CredentialDbContextFactory.CreateRuntimeOptions(locator); break;
            case "ART": _ = ArtifactDbContextFactory.CreateRuntimeOptions(locator); break;
            case "REL": _ = ReleaseDbContextFactory.CreateRuntimeOptions(locator); break;
            case "APR": _ = ApprovalDbContextFactory.CreateRuntimeOptions(locator); break;
            default: throw new ArgumentOutOfRangeException(nameof(owner));
        }
    }
    private static void Offline(string owner, string locator)
    {
        switch (owner)
        {
            case "IAM": _ = IdentityAccessDbContextFactory.CreateOfflineMigrationOptions(locator); break;
            case "AUD": _ = AuditDbContextFactory.CreateOfflineMigrationOptions(locator); break;
            case "TEN": _ = TenantDbContextFactory.CreateOfflineMigrationOptions(locator); break;
            case "PRJ": _ = ProjectDbContextFactory.CreateOfflineMigrationOptions(locator); break;
            case "INV": _ = InventoryDbContextFactory.CreateOfflineMigrationOptions(locator); break;
            case "SEC": _ = CredentialDbContextFactory.CreateOfflineMigrationOptions(locator); break;
            case "ART": _ = ArtifactDbContextFactory.CreateOfflineMigrationOptions(locator); break;
            case "REL": _ = ReleaseDbContextFactory.CreateOfflineMigrationOptions(locator); break;
            case "APR": _ = ApprovalDbContextFactory.CreateOfflineMigrationOptions(locator); break;
            default: throw new ArgumentOutOfRangeException(nameof(owner));
        }
    }
}
