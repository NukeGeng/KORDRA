using System.Security.Cryptography;
using System.Text;
using JasperFx;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Postgresql.Transport;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weasel.Core.Migrations;
using Wolverine.Persistence.Durability;

namespace Kordra.Messaging.Tests;

public sealed class MessagingSchemaArtifactTests
{
    [Fact]
    public void NativeHandledCleanupUsesBoundedExpiredHandledSelectionWithoutOwnedReceiptPurge()
    {
        using var host = Host.CreateDefaultBuilder().UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            MessagingSchemaProfile.Configure(options, "Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1");
        }).Build();
        var database = Assert.IsAssignableFrom<Wolverine.RDBMS.IMessageDatabase>(host.Services.GetRequiredService<IMessageStore>());
        var sql = database.BatchedDeleteExpiredHandledEnvelopesSql(100);
        Assert.Equal("delete from \"msg\".wolverine_incoming_envelopes where status = 'Handled' and ctid in " +
            "(select ctid from \"msg\".wolverine_incoming_envelopes where status = 'Handled' and keep_until <= :now limit 100);", sql);
        // This is package-generated SQL inspection only; executing cleanup needs a separately scoped fixture.
        // Both predicates matter for partition-local ctid identity and keeping active/scheduled work intact.
        var options = new WolverineOptions();
        MessagingSchemaProfile.Configure(options, "Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened");
        Assert.Equal(100, options.Durability.HandledMessageCleanupBatchSize);
        Assert.Equal(10, options.Durability.HandledMessageCleanupMaxBatchesPerCycle);
    }

    [Fact]
    public void NativeSchemaIsGeneratedFromPinnedPublicDatabaseApiWithoutStartingHost()
    {
        using var host = Host.CreateDefaultBuilder().UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            MessagingSchemaProfile.Configure(options, "Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1");
        }).Build();
        var database = Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>());
        var sql = database.ToDatabaseScript();
        Assert.Contains("wolverine_incoming_envelopes", sql);
        Assert.Contains("wolverine_outgoing_envelopes", sql);
        Assert.Contains("msg", sql);
        Assert.DoesNotContain("wolverine_queues", sql);
        Assert.DoesNotContain("not_opened", sql);
        var artifact = new MessagingMigrationArtifact(database, new string('a', 64));
        Assert.False(artifact.ReadyToApply); Assert.True(artifact.NativeAclComplete);
        Assert.Contains("CREATE TABLE msg.logical_receipts", artifact.CreationSql);
        Assert.Contains("CREATE SCHEMA IF NOT EXISTS msg_transport", artifact.CreationSql);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(artifact.CreationSql))), artifact.CreationSqlSha256);
        Assert.Equal(artifact.CreationSqlSha256, new MessagingMigrationArtifact(database, new string('a', 64)).CreationSqlSha256);
        Assert.Throws<ArgumentException>(() => new MessagingMigrationArtifact(database, "missing-fingerprint"));
        Assert.NotEmpty(artifact.NativeInventory.Objects);
        Assert.Contains(artifact.NativeInventory.Objects, o => o.Kind == "table" && o.Name == "wolverine_incoming_envelopes");
        Assert.DoesNotContain("ALL FUNCTIONS", artifact.NativeInventory.ReviewSql);
        Assert.DoesNotContain("ON ALL TABLES", artifact.NativeInventory.ReviewSql);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE \"msg\".\"wolverine_incoming_envelopes\"", artifact.NativeInventory.ReviewSql);
        Assert.False(artifact.NativeInventory.FunctionAndImplicitSequenceReviewRequired);
        Assert.True(artifact.NativeInventory.IsReviewedEmptyCatalogProfile);
        Assert.NotEqual(artifact.ReviewManifestSha256, new MessagingMigrationArtifact(database, new string('b', 64)).ReviewManifestSha256);
        if (Environment.GetEnvironmentVariable("KORDRA_MSG_SCHEMA_ARTIFACT_ROOT") is { } output)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kordra.slnx"))) directory = directory.Parent;
            var root = directory?.FullName ?? throw new InvalidOperationException("Product root missing.");
            var historical = Path.Combine(root, ".amr-local", "t12-1-design", "schema-review");
            var expected = Path.GetFullPath(output);
            Assert.Contains(expected, new[] { historical, Path.Combine(historical, "current-profile") });
            Directory.CreateDirectory(expected);
            Assert.Null(new DirectoryInfo(historical).LinkTarget);
            Assert.Null(new DirectoryInfo(expected).LinkTarget);
            var packageLock = Path.Combine(root, "src/Modules/Messaging/Infrastructure/packages.lock.json");
            var exact = new MessagingMigrationArtifact(database, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(packageLock))));
            WriteExact("creation.sql", exact.CreationSql);
            WriteExact("owned-acl.sql", exact.OwnedAclSql);
            WriteExact("native-acl-review.sql", exact.NativeInventory.ReviewSql);
            WriteExact("manifest.json", System.Text.Json.JsonSerializer.Serialize(new
            {
                exact.SchemaRevision, exact.WolverineVersion, exact.PackageGraphSha256, exact.CreationSqlSha256,
                exact.OwnedAclSha256, exact.NativeAclSha256, exact.ReviewManifestSha256, exact.NativeAclComplete,
                exact.ReadyToApply, Objects = exact.NativeInventory.Objects
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            void WriteExact(string name, string content)
            {
                var path = Path.Combine(expected, name);
                Assert.False(File.Exists(path) && new FileInfo(path).LinkTarget is not null);
                if (File.Exists(path))
                {
                    Assert.Equal(content, File.ReadAllText(path)); // Never replace historical reviewed artifacts.
                    return;
                }
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(content);
            }
        }
    }
    [Fact]
    public void RuntimeProfileIsExplicitStaticNonMigratingAndRegistersNoBusinessEndpoints()
    {
        var options = new WolverineOptions();
        MessagingSchemaProfile.Configure(options, "Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened");
        Assert.Equal(AutoCreate.None, options.AutoBuildMessageStorageOnStartup);
        Assert.Equal(JasperFx.CodeGeneration.TypeLoadMode.Static, options.CodeGeneration.TypeLoadMode);
        Assert.Equal("msg_transport", options.Transports.GetOrCreate<PostgresqlTransport>().TransportSchemaName);
        Assert.Equal(TimeSpan.FromDays(7), options.Durability.KeepAfterMessageHandling);
        Assert.Equal(Wolverine.MessageIdentity.IdAndDestination, options.Durability.MessageIdentity);
        Assert.Equal(32 * 1024, options.MaxIncomingEnvelopeDataSize);
        Assert.Empty(options.Transports.GetOrCreate<PostgresqlTransport>().Queues);
    }

    [Fact]
    public void OwnedDdlAndAclArtifactsAreDeterministicAndDoNotGrantTerminalMutationOrFutureObjects()
    {
        var sql = OwnedMessagingSchema.CreationSql;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))), OwnedMessagingSchema.Sha256);
        Assert.Contains("PRIMARY KEY (installation_id, scope_kind, tenant_key, operation_id, step_id, step_version, type_id)", sql);
        Assert.Contains("binding_hash ~ '^[0-9a-f]{64}$'", sql);
        Assert.DoesNotContain("wolverine_", sql); Assert.DoesNotContain("DROP ", sql);
        var acl = MessagingAclReview.OwnedObjectSql;
        Assert.Contains("GRANT SELECT, INSERT ON TABLE msg.logical_receipts", acl);
        Assert.Contains("GRANT SELECT ON TABLE msg.schema_manifest", acl);
        foreach (var forbidden in new[] { "GRANT ALL", "GRANT UPDATE", "GRANT DELETE", "TRUNCATE", "DEFAULT PRIVILEGES", "ALL FUNCTIONS", "CREATE ROLE", "app." })
            Assert.DoesNotContain(forbidden, acl);
    }
}
