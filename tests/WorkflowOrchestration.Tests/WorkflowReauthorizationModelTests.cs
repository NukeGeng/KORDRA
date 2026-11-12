using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

/// <summary>12.2f schema revision 6: additive receipt columns on authority_bindings; offline only, no connection.</summary>
public sealed class WorkflowReauthorizationModelTests
{
    internal static string RevisionSixSql()
    {
        using var previous = WorkflowDbContext.CreateOfflineReconciliationModel();
        using var current = WorkflowDbContext.CreateOfflineAuthorizationModel();
        var oldModel = previous.GetService<IDesignTimeModel>().Model; var newModel = current.GetService<IDesignTimeModel>().Model;
        var operations = current.GetService<IMigrationsModelDiffer>().GetDifferences(oldModel.GetRelationalModel(), newModel.GetRelationalModel());
        // The table never had a writer. No defaults: PostgreSQL itself refuses NOT NULL columns on a non-empty table.
        foreach (var column in operations.OfType<AddColumnOperation>()) { column.DefaultValue = null; column.DefaultValueSql = null; }
        return string.Join("\n", current.GetService<IMigrationsSqlGenerator>().Generate(operations, newModel).Select(x => x.CommandText));
    }

    [Fact]
    public void RevisionSixOnlyAddsReceiptColumnsIndexAndConstraintsToAuthorityBindings()
    {
        using var previous = WorkflowDbContext.CreateOfflineReconciliationModel();
        using var current = WorkflowDbContext.CreateOfflineAuthorizationModel();
        var operations = current.GetService<IMigrationsModelDiffer>().GetDifferences(
            previous.GetService<IDesignTimeModel>().Model.GetRelationalModel(), current.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        Assert.All(operations, item => Assert.True(item is AddColumnOperation or CreateIndexOperation or AddCheckConstraintOperation, item.GetType().Name));
        Assert.All(operations.OfType<AddColumnOperation>(), item => { Assert.Equal("authority_bindings", item.Table); Assert.False(item.IsNullable); });
        Assert.Equal(["command_hash", "expected_version", "request_id", "resulting_version", "specification_hash"],
            operations.OfType<AddColumnOperation>().Select(x => x.Name).Order(StringComparer.Ordinal));
        var index = Assert.Single(operations.OfType<CreateIndexOperation>());
        Assert.Equal("authority_bindings", index.Table); Assert.True(index.IsUnique);
        Assert.Equal(["installation_id", "tenant_id", "instance_id", "request_id"], index.Columns);
        Assert.Equal(["ck_authority_bindings_command_hash", "ck_authority_bindings_request_id", "ck_authority_bindings_resulting_version",
            "ck_authority_bindings_specification_hash", "ck_authority_bindings_versions"],
            operations.OfType<AddCheckConstraintOperation>().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.All(operations.OfType<AddCheckConstraintOperation>(), item => Assert.Equal("authority_bindings", item.Table));
        Assert.False(previous.IncludesAuthorizationReceipts); Assert.True(current.IncludesAuthorizationReceipts);
        Assert.Null(previous.Model.FindEntityType(typeof(WorkflowAuthorityBindingRecord))!.FindProperty(nameof(WorkflowAuthorityBindingRecord.RequestId)));
        Assert.Equal(ConnectionState.Closed, current.Database.GetDbConnection().State);
    }

    [Fact]
    public void RevisionSixArtifactIsNonDestructiveWithoutDefaultsAndPinned()
    {
        var sql = RevisionSixSql();
        Assert.DoesNotContain("DROP ", sql); Assert.DoesNotContain("DEFAULT", sql); Assert.DoesNotContain("CREATE TABLE", sql);
        Assert.Contains("ALTER TABLE wfl.authority_bindings ADD request_id uuid NOT NULL", sql);
        Assert.Contains("resulting_version = expected_version + 1", sql);
        Assert.Contains("request_id <> '00000000-0000-0000-0000-000000000000'::uuid", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_AUTHORIZATION_SQL_ARTIFACT_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory); var file = Path.Combine(directory, "wfl-v6-authorization.sql");
            if (File.Exists(file)) Assert.Equal(sql, File.ReadAllText(file)); else File.WriteAllText(file, sql);
        }
        var artifact = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v6-authorization.sql"));
        Assert.Equal(sql, Encoding.UTF8.GetString(artifact));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v6-authorization.manifest.json")));
        Assert.Equal(WorkflowDbContext.AuthorizationSchemaRevision, manifest.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(artifact)), manifest.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v5-operator-decisions.sql")))),
            manifest.RootElement.GetProperty("baselineSha256").GetString());
        Assert.False(manifest.RootElement.GetProperty("destructiveOperations").GetBoolean());
    }

    [Fact]
    public async Task StoreRequiresRevisionSixCallerRootAndNeverOpensTheDatabaseOtherwise()
    {
        var command = new Kordra.Modules.WorkflowOrchestration.Domain.WorkflowReauthorizationCommand(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, new string('a', 64));
        var delegation = new Kordra.Modules.WorkflowOrchestration.Contracts.WorkflowDelegationReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        foreach (var context in new[] { WorkflowDbContext.CreateOfflineReconciliationModel(), WorkflowDbContext.CreateOfflineAuthorizationModel() })
        await using (context)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new WorkflowAuthorityBindingStore(context).StageReauthorizationAsync(command, delegation, DateTimeOffset.UtcNow));
            Assert.Contains("caller root", error.Message);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        await using var current = WorkflowDbContext.CreateOfflineAuthorizationModel();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WorkflowAuthorityBindingStore(current).StageReauthorizationAsync(command, delegation, DateTimeOffset.UtcNow, cancelled.Token));
        Assert.Equal(ConnectionState.Closed, current.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task OwnerLockRequiresRevisionSixCallerRootAndNeverOpensTheDatabaseOtherwise()
    {
        // PR32 review: the coordinator serializes on this lock before receipt lookup and delegation reissue.
        var command = new Kordra.Modules.WorkflowOrchestration.Domain.WorkflowReauthorizationCommand(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, new string('a', 64));
        foreach (var context in new[] { WorkflowDbContext.CreateOfflineReconciliationModel(), WorkflowDbContext.CreateOfflineAuthorizationModel() })
        await using (context)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowAuthorityBindingStore(context).LockOwnerAsync(command));
            Assert.Contains("caller root", error.Message);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }
        await using var current = WorkflowDbContext.CreateOfflineAuthorizationModel();
        await Assert.ThrowsAsync<ArgumentNullException>(() => new WorkflowAuthorityBindingStore(current).LockOwnerAsync(null!));
    }

    [Fact]
    public void AuthorityBindingCannotBeInsertedBeforeRevisionSixOrWithoutTrackedOwnerAndIsAppendOnly()
    {
        foreach (var context in new[] { WorkflowDbContext.CreateOfflineReconciliationModel(), WorkflowDbContext.CreateOfflineAuthorizationModel() })
        using (context)
        {
            context.Add(new WorkflowAuthorityBindingRecord {
                InstallationId = Guid.NewGuid(), TenantId = Guid.NewGuid(), InstanceId = Guid.NewGuid(), BindingId = Guid.NewGuid(),
                DelegationId = Guid.NewGuid(), ActorId = Guid.NewGuid(), Generation = 2, RecordedAtUtc = DateTimeOffset.UtcNow,
                RequestId = Guid.NewGuid(), CommandHash = new string('a', 64), SpecificationHash = new string('b', 64),
                ExpectedVersion = 3, ResultingVersion = 4 });
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }
        using var current = WorkflowDbContext.CreateOfflineAuthorizationModel();
        var row = new WorkflowAuthorityBindingRecord { InstallationId = Guid.NewGuid(), TenantId = Guid.NewGuid(), InstanceId = Guid.NewGuid(),
            BindingId = Guid.NewGuid(), Generation = 2, CommandHash = new string('a', 64), SpecificationHash = new string('b', 64) };
        current.Attach(row); row.Generation = 3;
        Assert.Contains("append-only", Assert.Throws<InvalidOperationException>(() => current.SaveChanges()).Message);
        current.ChangeTracker.Clear(); current.Attach(row); current.Remove(row);
        Assert.Contains("retention", Assert.Throws<InvalidOperationException>(() => current.SaveChanges()).Message);
    }
}
