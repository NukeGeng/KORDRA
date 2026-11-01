using System.Data;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowPersistenceModelTests
{
    private const string Locator = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC";

    [Fact]
    public void OfflineDdlContainsAllNineOwnerTablesAndNeverConnects()
    {
        using var context = WorkflowDbContext.CreateOfflineModel();
        var sql = context.Database.GenerateCreateScript();
        foreach (var table in new[] { "instances", "steps", "transitions", "timers", "operation_custody", "recovery_decisions", "request_receipts", "clock_floor", "authority_bindings" })
            Assert.Contains("CREATE TABLE wfl." + table, sql);
        Assert.Equal(9, context.Model.GetEntityTypes().Count());
        Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.Contains("version > 0", sql);
        Assert.DoesNotContain("DROP ", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_SQL_ARTIFACT_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "wfl-revision-1.sql"), sql);
            using var connection = new NpgsqlConnection(Locator);
            var caseId = Guid.ParseExact(Environment.GetEnvironmentVariable("KORDRA_WFL_SQL_ARTIFACT_CASE_ID")!, "D");
            using var fixture = WorkflowDbContext.CreateFixture(connection, caseId);
            File.WriteAllText(Path.Combine(directory, "fixture-schema.sql"), fixture.Database.GenerateCreateScript());
        }
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void CommittedOfflineMigrationMatchesCurrentModelAndImmutableManifest()
    {
        using var context = WorkflowDbContext.CreateOfflineModel();
        var sql = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v1.sql"));
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v1.manifest.json")));
        Assert.Equal(context.Database.GenerateCreateScript(), System.Text.Encoding.UTF8.GetString(sql));
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(sql), Convert.FromHexString(manifest.RootElement.GetProperty("sha256").GetString()!));
        Assert.Equal(WorkflowDbContext.SchemaRevision, manifest.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(context.Model.GetEntityTypes().Select(x => x.GetTableName()).Order(),
            manifest.RootElement.GetProperty("tables").EnumerateArray().Select(x => x.GetString()).Order());
    }

    [Fact]
    public void EveryKeyAndAssociationContainsInstallationAndTenantWithoutCascade()
    {
        using var context = WorkflowDbContext.CreateOfflineModel();
        foreach (var entity in context.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey()!.Properties.Select(x => x.Name).ToArray();
            Assert.Contains("InstallationId", key); Assert.Contains("TenantId", key);
            foreach (var fk in entity.GetForeignKeys())
            {
                Assert.Contains(fk.Properties, x => x.Name == "InstallationId");
                Assert.Contains(fk.Properties, x => x.Name == "TenantId");
                Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
            }
            if (entity.FindProperty("Version") is { } version) Assert.True(version.IsConcurrencyToken);
        }
    }

    [Fact]
    public void IndependentFixtureSchemasDoNotShareCachedModelOrTouchDefaultSchema()
    {
        using var aConnection = new NpgsqlConnection(Locator);
        using var bConnection = new NpgsqlConnection(Locator);
        using var a = WorkflowDbContext.CreateFixture(aConnection, Guid.NewGuid());
        using var b = WorkflowDbContext.CreateFixture(bConnection, Guid.NewGuid());
        var first = a.Database.GenerateCreateScript(); var second = b.Database.GenerateCreateScript();
        Assert.NotEqual(a.SchemaName, b.SchemaName);
        Assert.Contains(a.SchemaName + ".instances", first);
        Assert.DoesNotContain(b.SchemaName, first);
        Assert.Contains(b.SchemaName + ".instances", second);
        Assert.DoesNotContain("wfl.", first); Assert.DoesNotContain("wfl.", second);
        Assert.Equal(ConnectionState.Closed, aConnection.State); Assert.Equal(ConnectionState.Closed, bConnection.State);
    }

    [Fact]
    public void HistoryAndBusinessSpecCannotBeRewrittenOrDeletedBeforeAnyIO()
    {
        using var context = WorkflowDbContext.CreateOfflineModel();
        var receipt = new WorkflowRequestReceiptRecord { InstallationId = Guid.NewGuid(), TenantId = Guid.NewGuid(), RequestId = Guid.NewGuid() };
        context.Attach(receipt); receipt.ContentHash = new string('a', 64);
        Assert.Contains("append-only", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        context.ChangeTracker.Clear();
        var instance = NewInstance(); context.Attach(instance); instance.SpecificationHash = new string('b', 64);
        Assert.Contains("immutable", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        context.ChangeTracker.Clear(); context.Remove(instance);
        Assert.Contains("retention", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(1, 1)][InlineData(1, 3)][InlineData(long.MaxValue, long.MaxValue)]
    public void MissingOrSkippedVersionAdvancementRejectsWithoutIO(long original, long next)
    {
        using var context = WorkflowDbContext.CreateOfflineModel();
        var instance = NewInstance(); instance.Version = original; context.Attach(instance);
        instance.DeadlineExceeded = true; instance.Version = next;
        Assert.Contains("version", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task PersistenceNeverFallsBackToAutocommit()
    {
        using var context = WorkflowDbContext.CreateOfflineModel(); context.Add(NewInstance());
        Assert.Contains("caller-owned", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        Assert.Contains("caller-owned", (await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync())).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private static WorkflowInstanceRecord NewInstance()
    {
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var spec = new WorkflowSpecification(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            1, new string('a', 64), WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(1));
        return new() { InstallationId = spec.InstallationId, TenantId = spec.TenantId, InstanceId = spec.InstanceId,
            RequestId = spec.RequestId, Version = 1, State = 1, SpecificationHash = spec.SpecificationHash,
            Specification = spec.WriteCanonical(), CreatedAtUtc = spec.CreatedAtUtc, DeadlineUtc = spec.DeadlineUtc };
    }
}
