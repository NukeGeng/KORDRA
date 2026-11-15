using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

/// <summary>12.3b (D3) schema revision 7: the start root may record authority generation 1 (0 → 1). Offline only.</summary>
public sealed class WorkflowOriginalBindingModelTests
{
    internal static string RevisionSevenSql()
    {
        using var previous = WorkflowDbContext.CreateOfflineAuthorizationModel();
        using var current = WorkflowDbContext.CreateOfflineOriginalBindingModel();
        var operations = current.GetService<IMigrationsModelDiffer>().GetDifferences(
            previous.GetService<IDesignTimeModel>().Model.GetRelationalModel(), current.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        return string.Join("\n", current.GetService<IMigrationsSqlGenerator>().Generate(operations, current.GetService<IDesignTimeModel>().Model)
            .Select(x => x.CommandText));
    }

    [Fact]
    public void RevisionSevenOnlyReplacesTheAuthorityBindingVersionCheck()
    {
        using var previous = WorkflowDbContext.CreateOfflineAuthorizationModel();
        using var current = WorkflowDbContext.CreateOfflineOriginalBindingModel();
        var operations = current.GetService<IMigrationsModelDiffer>().GetDifferences(
            previous.GetService<IDesignTimeModel>().Model.GetRelationalModel(), current.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        Assert.Equal(2, operations.Count);
        var drop = Assert.IsType<DropCheckConstraintOperation>(operations[0]);
        var add = Assert.IsType<AddCheckConstraintOperation>(operations[1]);
        Assert.Equal(("authority_bindings", "ck_authority_bindings_versions"), (drop.Table, drop.Name));
        Assert.Equal(("authority_bindings", "ck_authority_bindings_versions"), (add.Table, add.Name));
        Assert.Contains("generation = 1 AND expected_version = 0 AND resulting_version = 1", add.Sql);
        Assert.Contains("resulting_version = expected_version + 1", add.Sql);
        Assert.False(previous.IncludesOriginalBinding); Assert.True(current.IncludesOriginalBinding);
        Assert.Equal(ConnectionState.Closed, current.Database.GetDbConnection().State);
    }

    [Fact]
    public void RevisionSevenArtifactReplacesOneConstraintWithoutDataChangesAndIsPinned()
    {
        var sql = RevisionSevenSql();
        Assert.DoesNotContain("DROP TABLE", sql); Assert.DoesNotContain("DROP COLUMN", sql); Assert.DoesNotContain("DELETE", sql);
        Assert.DoesNotContain("UPDATE ", sql); Assert.DoesNotContain("CREATE TABLE", sql);
        Assert.Equal(1, sql.Split("DROP CONSTRAINT").Length - 1);
        Assert.Contains("ALTER TABLE wfl.authority_bindings DROP CONSTRAINT ck_authority_bindings_versions", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_ORIGINAL_BINDING_SQL_ARTIFACT_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory); var file = Path.Combine(directory, "wfl-v7-original-binding.sql");
            if (File.Exists(file) && File.ReadAllText(file).Length > 0) Assert.Equal(sql, File.ReadAllText(file)); else File.WriteAllText(file, sql);
        }
        var artifact = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v7-original-binding.sql"));
        Assert.Equal(sql, Encoding.UTF8.GetString(artifact));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v7-original-binding.manifest.json")));
        Assert.Equal(WorkflowDbContext.OriginalBindingSchemaRevision, manifest.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(artifact)), manifest.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v6-authorization.sql")))),
            manifest.RootElement.GetProperty("baselineSha256").GetString());
        Assert.False(manifest.RootElement.GetProperty("destructiveOperations").GetBoolean());
        Assert.Equal(["ck_authority_bindings_versions"], manifest.RootElement.GetProperty("constraintsReplaced").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task OriginalBindingNeedsRevisionSevenCallerRootAndFreshOwnerAndNeverOpensTheDatabaseOtherwise()
    {
        var now = DateTimeOffset.UtcNow;
        var specification = new WorkflowSpecification(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test,
            now, now.AddHours(1), now.AddMinutes(3));
        var delegation = new WorkflowDelegationReference(Guid.NewGuid(), now.AddMinutes(30));
        foreach (var context in new[] { WorkflowDbContext.CreateOfflineAuthorizationModel(), WorkflowDbContext.CreateOfflineOriginalBindingModel() })
        await using (context)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowAuthorityBindingStore(context)
                .StageOriginalAsync(specification, Guid.NewGuid(), new string('b', 64), delegation));
            Assert.Contains("caller root", error.Message);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }
    }

    [Fact]
    public void GenerationOneCannotBeInsertedBeforeRevisionSevenOrWithoutAFreshPendingOwner()
    {
        foreach (var context in new[] { WorkflowDbContext.CreateOfflineAuthorizationModel(), WorkflowDbContext.CreateOfflineOriginalBindingModel() })
        using (context)
        {
            context.Add(new WorkflowAuthorityBindingRecord {
                InstallationId = Guid.NewGuid(), TenantId = Guid.NewGuid(), InstanceId = Guid.NewGuid(), BindingId = Guid.NewGuid(),
                DelegationId = Guid.NewGuid(), ActorId = Guid.NewGuid(), Generation = 1, RecordedAtUtc = DateTimeOffset.UtcNow,
                RequestId = Guid.NewGuid(), CommandHash = new string('a', 64), SpecificationHash = new string('b', 64),
                ExpectedVersion = 0, ResultingVersion = 1 });
            var error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
            Assert.Contains(context.IncludesOriginalBinding ? "tracked owner" : "revision 7", error.Message);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }
    }
}
