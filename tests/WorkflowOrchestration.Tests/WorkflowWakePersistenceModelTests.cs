using System.Data;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowWakePersistenceModelTests
{
    [Fact]
    public void RevisionTwoIsStrictlyAdditiveWithoutRewritingExistingOwnerTables()
    {
        using var baseline = WorkflowDbContext.CreateOfflineModel();
        using var expanded = WorkflowDbContext.CreateOfflineWakeModel();
        var previous = baseline.GetService<IDesignTimeModel>().Model;
        var current = expanded.GetService<IDesignTimeModel>().Model;
        var operations = expanded.GetService<IMigrationsModelDiffer>().GetDifferences(
            previous.GetRelationalModel(), current.GetRelationalModel());
        var create = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("wake_operations", create.Name); Assert.Equal("wfl", create.Schema);
        Assert.All(operations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation));
        Assert.All(operations.OfType<CreateIndexOperation>(), index => Assert.Equal("wake_operations", index.Table));
        var commands = expanded.GetService<IMigrationsSqlGenerator>().Generate(operations, current);
        var sql = string.Join("\n", commands.Select(x => x.CommandText));
        var artifact = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v2-wake.sql"));
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v2-wake.manifest.json")));
        Assert.Equal(sql, System.Text.Encoding.UTF8.GetString(artifact));
        Assert.Equal(WorkflowDbContext.WakeSchemaRevision, manifest.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(artifact)),
            manifest.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v1.sql")))),
            manifest.RootElement.GetProperty("baselineSha256").GetString());
        Assert.DoesNotContain("DROP ", sql); Assert.DoesNotContain("ALTER TABLE", sql);
        Assert.DoesNotContain("CREATE TABLE wfl.instances", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_SQL_ARTIFACT_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "wfl-v2-wake.sql"), sql);
        }
        Assert.Equal(ConnectionState.Closed, expanded.Database.GetDbConnection().State);
    }

    [Fact]
    public void AdditiveModelKeepsRevisionOneIntactAndUsesScopedTimerForeignKey()
    {
        using var baseline = WorkflowDbContext.CreateOfflineModel();
        using var expanded = WorkflowDbContext.CreateOfflineWakeModel();
        Assert.False(baseline.IncludesWakeOperations); Assert.True(expanded.IncludesWakeOperations);
        Assert.Equal(9, baseline.Model.GetEntityTypes().Count()); Assert.Equal(10, expanded.Model.GetEntityTypes().Count());
        Assert.Null(baseline.Model.FindEntityType(typeof(WorkflowWakeOperationRecord)));
        var wake = expanded.Model.FindEntityType(typeof(WorkflowWakeOperationRecord))!;
        Assert.Equal(new[] { "InstallationId", "TenantId", "InstanceId", "StepId", "TimerId", "Generation" },
            wake.FindPrimaryKey()!.Properties.Select(p => p.Name));
        var fk = Assert.Single(wake.GetForeignKeys());
        Assert.Equal(typeof(WorkflowTimerRecord), fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        var sql = expanded.Database.GenerateCreateScript();
        Assert.Contains("CREATE TABLE wfl.wake_operations", sql);
        Assert.Contains("scheduler_operation_id <> wake_operation_id", sql);
        Assert.Contains("scheduler_attempt BETWEEN 1 AND 10", sql);
        Assert.Contains("octet_length(descriptor) BETWEEN 2 AND 16384", sql);
        Assert.Equal(3, wake.GetIndexes().Count(x => x.IsUnique));
        Assert.Equal(ConnectionState.Closed, expanded.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed, baseline.Database.GetDbConnection().State);
    }

    [Fact]
    public void WakeHistoryCannotBeModifiedOrDeletedBeforeAnyIo()
    {
        using var context = WorkflowDbContext.CreateOfflineWakeModel();
        var row = new WorkflowWakeOperationRecord { InstallationId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            InstanceId = Guid.NewGuid(), StepId = Guid.NewGuid(), TimerId = Guid.NewGuid(), Generation = 1,
            Descriptor = [1], DescriptorHash = new string('a', 64) };
        context.Attach(row); row.Descriptor = [2];
        Assert.Contains("append-only", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        context.ChangeTracker.Clear(); context.Remove(row);
        Assert.Contains("retention", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void DirectWakeInsertWithoutCanonicalOwnerIsRejectedBeforeIo()
    {
        using var context = WorkflowDbContext.CreateOfflineWakeModel();
        context.Add(new WorkflowWakeOperationRecord { InstallationId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            InstanceId = Guid.NewGuid(), StepId = Guid.NewGuid(), TimerId = Guid.NewGuid(), Generation = 1 });
        Assert.Contains("tracked owner", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
