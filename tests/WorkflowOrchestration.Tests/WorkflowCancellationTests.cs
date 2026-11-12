using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowCancellationTests
{
    private static readonly Guid Installation = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Instance = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Request = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Actor = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static WorkflowCancellationIntent Intent() => new(Installation, Tenant, Instance, Request, Actor, 5, new string('a', 64));

    [Fact]
    public void CommandHashMatchesIndependentClosedVectorAndDoesNotRevealAuthority()
    {
        var intent = Intent();
        const string vector = "{\"schemaVersion\":1,\"kind\":\"workflow.cancel\",\"installationId\":\"11111111-1111-4111-8111-111111111111\",\"tenantId\":\"22222222-2222-4222-8222-222222222222\",\"instanceId\":\"33333333-3333-4333-8333-333333333333\",\"requestId\":\"44444444-4444-4444-8444-444444444444\",\"actorId\":\"55555555-5555-4555-8555-555555555555\",\"expectedVersion\":5,\"specificationHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"reasonCode\":\"OperatorRequested\"}";
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(vector))), intent.CommandHash);
        Assert.DoesNotContain(intent.SpecificationHash, intent.ToString()); Assert.DoesNotContain(Actor.ToString(), intent.ToString());
        Assert.Equal(intent.CommandHash, Intent().CommandHash);
    }

    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)]
    public void CommandIdentityCannotBeReboundAcrossScopeActorVersionOrSpecification(int field)
    {
        var altered = new WorkflowCancellationIntent(field == 0 ? Guid.NewGuid() : Installation, field == 1 ? Guid.NewGuid() : Tenant,
            field == 2 ? Guid.NewGuid() : Instance, field == 3 ? Guid.NewGuid() : Request, field == 4 ? Guid.NewGuid() : Actor,
            field == 5 ? 6 : 5, new string(field == 6 ? 'b' : 'a', 64));
        Assert.NotEqual(Intent().CommandHash, altered.CommandHash);
    }

    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)]
    public void EmptyOwnerActorOrRequestIdentityIsRejected(int empty)
    {
        var ids = new[] { Installation, Tenant, Instance, Request, Actor }; ids[empty] = Guid.Empty;
        Assert.Throws<ArgumentException>(() => new WorkflowCancellationIntent(ids[0], ids[1], ids[2], ids[3], ids[4], 5, new string('a', 64)));
    }

    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(long.MaxValue)]
    public void VersionExhaustionCannotBecomeCancellationMutation(long version)
        => Assert.Throws<ArgumentException>(() => new WorkflowCancellationIntent(Installation, Tenant, Instance, Request, Actor, version, new string('a', 64)));

    [Theory]
    [InlineData(null)][InlineData("")][InlineData("a")][InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void NonCanonicalSpecificationHashIsRejected(string? hash)
        => Assert.Throws<ArgumentException>(() => new WorkflowCancellationIntent(Installation, Tenant, Instance, Request, Actor, 5, hash!));

    [Fact]
    public void RevisionThreeAddsOnlyCancellationHistoryWithoutRewritingPriorModels()
    {
        using var previous = WorkflowDbContext.CreateOfflineWakeModel();
        using var current = WorkflowDbContext.CreateOfflineCancellationModel();
        var oldModel = previous.GetService<IDesignTimeModel>().Model; var newModel = current.GetService<IDesignTimeModel>().Model;
        var operations = current.GetService<IMigrationsModelDiffer>().GetDifferences(oldModel.GetRelationalModel(), newModel.GetRelationalModel());
        Assert.Equal("cancellation_intents", Assert.Single(operations.OfType<CreateTableOperation>()).Name);
        Assert.All(operations, item => Assert.True(item is CreateTableOperation or CreateIndexOperation));
        Assert.All(operations.OfType<CreateIndexOperation>(), item => Assert.Equal("cancellation_intents", item.Table));
        Assert.False(previous.IncludesCancellationIntents); Assert.True(current.IncludesWakeOperations); Assert.True(current.IncludesCancellationIntents);
        Assert.Equal(10, previous.Model.GetEntityTypes().Count()); Assert.Equal(11, current.Model.GetEntityTypes().Count());
        var entity = current.Model.FindEntityType(typeof(WorkflowCancellationIntentRecord))!;
        Assert.Equal(typeof(WorkflowInstanceRecord), Assert.Single(entity.GetForeignKeys()).PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(entity.GetForeignKeys()).DeleteBehavior);
        Assert.True(Assert.Single(entity.GetIndexes()).IsUnique);
        var sql = string.Join("\n", current.GetService<IMigrationsSqlGenerator>().Generate(operations, newModel).Select(x => x.CommandText));
        Assert.DoesNotContain("DROP ", sql); Assert.DoesNotContain("ALTER TABLE", sql);
        Assert.Contains("resulting_version = expected_version + 1", sql);
        var artifact = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v3-cancellation.sql"));
        Assert.Equal(sql, Encoding.UTF8.GetString(artifact));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v3-cancellation.manifest.json")));
        Assert.Equal(WorkflowDbContext.CancellationSchemaRevision, manifest.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(artifact)), manifest.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v2-wake.sql")))),
            manifest.RootElement.GetProperty("baselineSha256").GetString());
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_CANCELLATION_SQL_ARTIFACT_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory); var file = Path.Combine(directory, "wfl-v3-cancellation.sql");
            if (File.Exists(file)) Assert.Equal(sql, File.ReadAllText(file)); else File.WriteAllText(file, sql);
        }
        Assert.Equal(ConnectionState.Closed, current.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed, previous.Database.GetDbConnection().State);
    }

    [Fact]
    public void CancellationCannotBeInsertedWithoutItsActualTrackedOwnerOrEditedOrDeleted()
    {
        using var context = WorkflowDbContext.CreateOfflineCancellationModel();
        var intent = Intent(); var row = new WorkflowCancellationIntentRecord { InstallationId = Installation, TenantId = Tenant,
            InstanceId = Instance, RequestId = Request, ActorId = Actor, ExpectedVersion = 5, ResultingVersion = 6,
            SpecificationHash = intent.SpecificationHash, CommandHash = intent.CommandHash, RecordedAtUtc = DateTimeOffset.UtcNow };
        context.Add(row); Assert.Contains("tracked owner", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        context.ChangeTracker.Clear(); context.Attach(row); row.ActorId = Guid.NewGuid();
        Assert.Contains("append-only", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        context.ChangeTracker.Clear(); context.Remove(row);
        Assert.Contains("retention", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData("hash")][InlineData("version")][InlineData("terminal")][InlineData("time")]
    public void DirectCancellationRowCannotBypassCanonicalOwnerAndReceiptBinding(string fault)
    {
        using var context = WorkflowDbContext.CreateOfflineCancellationModel();
        var start = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var spec = new WorkflowSpecification(Installation, Tenant, Instance, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test,
            start, start.AddHours(2), start.AddHours(1));
        var intent = new WorkflowCancellationIntent(Installation, Tenant, Instance, Request, Actor, 1, spec.SpecificationHash);
        context.Attach(new WorkflowInstanceRecord { InstallationId = Installation, TenantId = Tenant, InstanceId = Instance,
            RequestId = spec.RequestId, Specification = spec.WriteCanonical(), SpecificationHash = spec.SpecificationHash,
            CreatedAtUtc = spec.CreatedAtUtc, DeadlineUtc = spec.DeadlineUtc, Version = fault == "version" ? 3 : 2,
            State = (int)(fault == "terminal" ? WorkflowState.Cancelled : WorkflowState.Pending) });
        context.Add(new WorkflowCancellationIntentRecord { InstallationId = Installation, TenantId = Tenant, InstanceId = Instance,
            RequestId = Request, ActorId = Actor, ExpectedVersion = 1, ResultingVersion = 2, SpecificationHash = spec.SpecificationHash,
            CommandHash = fault == "hash" ? new string('b', 64) : intent.CommandHash,
            RecordedAtUtc = fault == "time" ? start.AddTicks(-1) : start });
        Assert.Contains("immutable command", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task OwnerStagingCannotOpenConnectionOrBypassExpandedCallerRoot()
    {
        using var old = WorkflowDbContext.CreateOfflineWakeModel(); using var current = WorkflowDbContext.CreateOfflineCancellationModel();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowCancellationStore(old).StageIntentAsync(Intent(), DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowCancellationStore(current).StageIntentAsync(Intent(), DateTimeOffset.UtcNow));
        var scoped = new WorkflowCancellationStore(current);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scoped.StageScopedIntentAsync(Intent(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scoped.StageScopedIntentAsync(Intent(), Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scoped.StageScopedIntentAsync(Intent(), Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WorkflowCancellationStore(current).StageIntentAsync(Intent(), DateTimeOffset.UtcNow, cancellation.Token));
        Assert.Equal(ConnectionState.Closed, old.Database.GetDbConnection().State); Assert.Equal(ConnectionState.Closed, current.Database.GetDbConnection().State);
    }
}
