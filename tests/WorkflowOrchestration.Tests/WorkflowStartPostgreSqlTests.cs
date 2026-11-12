using System.Security.Cryptography;
using System.Text;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowStartPostgreSqlTests
{
    private static readonly Guid CaseId = Guid.Parse("b5ce6e04-6190-4d89-8227-1d92b8a30f78");
    private const string Schema = "t12_b5ce6e0461904d8982271d92b8a30f78";
    private static readonly Guid Actor = Guid.Parse("f95ba093-53ee-40f6-9473-6773c75fb8d4");

    [Fact]
    public void ExportReviewedSqlWithoutDatabaseAccess()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        var sql = Scripts(connection, offlineMetadata: true);
        Assert.All(sql.Values, value => Assert.Contains(Schema, value));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_START_FIXTURE_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, value) in sql)
            File.WriteAllText(Path.Combine(directory, name), value);
    }

    [WorkflowStartDatabaseFact]
    public async Task StartReceiptRollsBackAndConcurrentRetryReturnsOriginal()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_START_FIXTURE_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved start fixture locator required.");
        var connectionString = new NpgsqlConnectionStringBuilder(locator) { Pooling = false, Multiplexing = false }.ConnectionString;
        await using var setup = WorkflowModelFixture.Connection(connectionString, CaseId);
        var scripts = Scripts(setup);
        var approval = Environment.GetEnvironmentVariable("KORDRA_WFL_START_FIXTURE_MANIFEST")!;
        using var approved = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(approval));
        Assert.Equal("approved_for_execution", approved.RootElement.GetProperty("status").GetString());
        Assert.Equal(CaseId, approved.RootElement.GetProperty("caseId").GetGuid());
        Assert.Equal(Schema, approved.RootElement.GetProperty("schema").GetString());
        foreach (var (name, sql) in scripts)
            Assert.Equal(approved.RootElement.GetProperty("sha256").GetProperty(name).GetString(),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));

        await setup.OpenAsync(ct);
        Assert.Equal(0, await NamespaceCount(setup, ct));
        var created = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                foreach (var name in new[] { "create.sql", "baseline.sql", "expand.sql" })
                    await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct);
                created = true;
            }

            var now = DateTimeOffset.UtcNow;
            var spec = new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64),
                WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
            var start = Command(spec, spec.ReleaseRevision);
            await using var first = WorkflowModelFixture.Connection(connectionString, CaseId);
            await first.OpenAsync(ct);
            await using (var context = WorkflowModelFixture.CreateStartFixture(first, CaseId))
            await using (var root = await first.BeginTransactionAsync(ct))
            {
                await context.Database.UseTransactionAsync(root, ct);
                Assert.False((await new WorkflowStartStore(context).StageAsync(start, () => spec, now, ct)).Duplicate);
                await AssertCounts(0);
                await root.RollbackAsync(ct);
            }
            await AssertCounts(0);

            await using var second = WorkflowModelFixture.Connection(connectionString, CaseId);
            await second.OpenAsync(ct);
            await using (var winner = WorkflowModelFixture.CreateStartFixture(first, CaseId))
            await using (var loser = WorkflowModelFixture.CreateStartFixture(second, CaseId))
            await using (var rootA = await first.BeginTransactionAsync(ct))
            await using (var rootB = await second.BeginTransactionAsync(ct))
            {
                await winner.Database.UseTransactionAsync(rootA, ct);
                await loser.Database.UseTransactionAsync(rootB, ct);
                Assert.False((await new WorkflowStartStore(winner).StageAsync(start, () => spec, now, ct)).Duplicate);
                var allocation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var different = new WorkflowSpecification(spec.InstallationId, spec.TenantId, Guid.NewGuid(), Guid.NewGuid(),
                    spec.ProjectId, spec.ApplicationId, spec.EnvironmentId, spec.TargetId, spec.ReleaseId,
                    spec.ReleaseRevision, spec.ReleaseHash, spec.Environment, now, spec.DeadlineUtc, spec.DueAtUtc);
                var competing = new WorkflowStartStore(loser).StageAsync(Command(different, different.ReleaseRevision), () =>
                {
                    allocation.SetResult();
                    return different;
                }, now, ct);
                await allocation.Task.WaitAsync(ct);
                await rootA.CommitAsync(ct);
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => competing);
                var violation = Assert.IsType<PostgresException>(error.InnerException);
                Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
                Assert.Equal("PK_start_receipts", violation.ConstraintName);
                await rootB.RollbackAsync(ct);
            }

            await using (var context = WorkflowModelFixture.CreateStartFixture(first, CaseId))
            await using (var root = await first.BeginTransactionAsync(ct))
            {
                await context.Database.UseTransactionAsync(root, ct);
                var store = new WorkflowStartStore(context);
                var retry = await store.StageAsync(start, NeverAllocate, now.AddDays(2), ct);
                Assert.True(retry.Duplicate);
                Assert.Equal(spec.InstanceId, retry.InstanceId);
                Assert.Equal(1, retry.Version);
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.StageAsync(
                    Command(spec, spec.ReleaseRevision + 1), NeverAllocate, now, ct));
                await root.RollbackAsync(ct);
            }
            await AssertCounts(1);
        }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0, await NamespaceCount(setup, cleanup.Token));
            }
        }

        async Task AssertCounts(int expected)
        {
            foreach (var table in new[] { "instances", "request_receipts", "start_receipts" })
            {
                await using var count = new NpgsqlCommand($"SELECT count(*) FROM {Schema}.{table}", setup);
                Assert.Equal((long)expected, await count.ExecuteScalarAsync(ct));
            }
        }
    }

    private static WorkflowSpecification NeverAllocate() => throw new InvalidOperationException("Retry allocated a new specification.");

    private static WorkflowStartCommand Command(WorkflowSpecification spec, long revision) =>
        new(spec.InstallationId, spec.TenantId, Actor, spec.ProjectId, spec.EnvironmentId, spec.RequestId, spec.ReleaseId,
            revision, spec.ReleaseHash, WorkflowSpecification.TemplateId, WorkflowSpecification.TemplateVersion, spec.DeadlineUtc, "start-fixture");

    internal static Dictionary<string, string> Scripts(NpgsqlConnection connection, bool offlineMetadata = false)
    {
        using var baseline = offlineMetadata ? WorkflowDbContext.CreateCancellationFixture(connection, CaseId)
            : WorkflowModelFixture.CreateCancellationFixture(connection, CaseId);
        return new()
        {
            ["create.sql"] = $"CREATE SCHEMA {Schema};\n",
            ["baseline.sql"] = baseline.Database.GenerateCreateScript(),
            ["expand.sql"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v4-start.sql"))
                .Replace("wfl.", Schema + ".", StringComparison.Ordinal),
            ["cleanup.sql"] = $"DROP SCHEMA {Schema} CASCADE;\n"
        };
    }

    private static async Task<long> NamespaceCount(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", connection);
        command.Parameters.AddWithValue("schema", Schema);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? root = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, root) { CommandTimeout = 3 };
        await command.ExecuteNonQueryAsync(ct);
    }
}

public sealed class WorkflowStartDatabaseFactAttribute : FactAttribute
{
    public WorkflowStartDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_START_FIXTURE_MANIFEST")))
            Skip = "Requires exact approval for the start receipt PostgreSQL fixture.";
    }
}
