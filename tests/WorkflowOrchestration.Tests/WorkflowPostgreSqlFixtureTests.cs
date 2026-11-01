using System.Security.Cryptography;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowPostgreSqlFixtureTests
{
    public const string CaseId = "9d8750dc-46d7-41e7-8023-37b7b9d09da6";
    private const string Schema = "t12_9d8750dc46d741e7802337b7b9d09da6";

    [WorkflowDatabaseFixtureFact]
    public async Task ActualPostgreSqlCreationRollbackDuplicateScopeAndCas()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_FIXTURE_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved private WFL fixture locator required.");
        await using var connection = new NpgsqlConnection(locator);
        await using var model = WorkflowDbContext.CreateFixture(connection, Guid.Parse(CaseId));
        var ddl = model.Database.GenerateCreateScript();
        var expectedHash = Environment.GetEnvironmentVariable("KORDRA_WFL_FIXTURE_SQL_SHA256");
        Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ddl))));
        await connection.OpenAsync(ct);
        await using (var preflight = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname = @schema", connection))
        {
            preflight.Parameters.AddWithValue("schema", Schema);
            Assert.Equal(0L, (long)(await preflight.ExecuteScalarAsync(ct))!);
        }
        var created = false;
        try
        {
            // Strict creation in a root prevents GenerateCreateScript's IF NOT EXISTS from adopting an existing schema.
            await using (var setup = await connection.BeginTransactionAsync(ct))
            {
                await using var create = new NpgsqlCommand("CREATE SCHEMA " + Schema, connection, setup);
                await create.ExecuteNonQueryAsync(ct);
                await using var schema = new NpgsqlCommand(ddl, connection, setup);
                await schema.ExecuteNonQueryAsync(ct);
                await setup.CommitAsync(ct); created = true;
            }
            var spec = NewSpecification();
            await WithRoot(connection, async (context, root) => {
                await new WorkflowStore(context).StageCreationAsync(spec, ct);
                // Independent reader must not see staged rows; caller rollback discards instance/steps/receipt together.
                Assert.Equal(0L, await Count(locator, "instances", ct));
                await root.RollbackAsync(ct);
            });
            Assert.Equal(0L, await Count(locator, "instances", ct));
            Assert.Equal(0L, await Count(locator, "request_receipts", ct));
            await WithRoot(connection, async (context, root) => {
                var result = await new WorkflowStore(context).StageCreationAsync(spec, ct);
                Assert.False(result.Duplicate); await root.CommitAsync(ct);
            });
            Assert.Equal(1L, await Count(locator, "instances", ct));
            Assert.Equal(4L, await Count(locator, "steps", ct));
            await WithRoot(connection, async (context, root) => {
                var result = await new WorkflowStore(context).StageCreationAsync(spec, ct);
                Assert.True(result.Duplicate); await root.CommitAsync(ct);
            });
            var altered = NewSpecification(spec, new string('b', 64));
            await WithRoot(connection, async (context, root) => {
                await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowStore(context).StageCreationAsync(altered, ct));
                await root.RollbackAsync(ct);
            });
            await WithRoot(connection, async (context, root) => {
                var identity = new WorkflowIdentity(spec.InstallationId, Guid.NewGuid(), spec.InstanceId);
                await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowStore(context).StageTransitionAsync(identity,
                    Guid.NewGuid(), Guid.NewGuid(), spec.SpecificationHash, 1, WorkflowState.Running, spec.CreatedAtUtc, ct));
                await root.RollbackAsync(ct);
            });
            // Both real roots read version 1. First commits, second must lose the database CAS, with no second history row.
            await using var second = new NpgsqlConnection(locator); await second.OpenAsync(ct);
            await using var a = WorkflowDbContext.CreateFixture(connection, Guid.Parse(CaseId));
            await using var b = WorkflowDbContext.CreateFixture(second, Guid.Parse(CaseId));
            await using var txA = await connection.BeginTransactionAsync(ct); await a.Database.UseTransactionAsync(txA, ct);
            await using var txB = await second.BeginTransactionAsync(ct); await b.Database.UseTransactionAsync(txB, ct);
            var rowA = await a.Set<WorkflowInstanceRecord>().SingleAsync(ct);
            var rowB = await b.Set<WorkflowInstanceRecord>().SingleAsync(ct);
            Assert.Equal(1L, rowA.Version); Assert.Equal(1L, rowB.Version);
            var stepId = await a.Set<WorkflowStepRecord>().Where(x => x.Ordinal == 0).Select(x => x.StepId).SingleAsync(ct);
            var identityA = new WorkflowIdentity(spec.InstallationId, spec.TenantId, spec.InstanceId);
            await new WorkflowStore(a).StageTransitionAsync(identityA, stepId, Guid.NewGuid(), spec.SpecificationHash, 1,
                WorkflowState.Running, spec.CreatedAtUtc, ct);
            await txA.CommitAsync(ct);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new WorkflowStore(b).StageTransitionAsync(identityA,
                stepId, Guid.NewGuid(), spec.SpecificationHash, 1, WorkflowState.Running, spec.CreatedAtUtc, ct));
            await txB.RollbackAsync(ct);
            await WithRoot(connection, async (context, root) => {
                var custody = await new WorkflowStore(context).StageInitialCustodyAsync(identityA, stepId, Guid.NewGuid(),
                    spec.SpecificationHash, Guid.NewGuid(), Guid.NewGuid(), 1, spec.CreatedAtUtc, ct);
                Assert.Equal(1L, custody.Epoch);
                await root.RollbackAsync(ct);
            });
            Assert.Equal(0L, await Count(locator, "operation_custody", ct));
            await WithRoot(connection, async (context, root) => {
                var operation = Guid.NewGuid(); var worker = Guid.NewGuid(); var boot = Guid.NewGuid();
                var store = new WorkflowStore(context);
                var first = await store.StageInitialCustodyAsync(identityA, stepId, operation, spec.SpecificationHash,
                    worker, boot, 1, spec.CreatedAtUtc, ct);
                var duplicate = await store.StageInitialCustodyAsync(identityA, stepId, operation, spec.SpecificationHash,
                    worker, boot, 1, spec.CreatedAtUtc, ct);
                Assert.Same(first, duplicate);
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.StageInitialCustodyAsync(identityA, stepId,
                    operation, spec.SpecificationHash, worker, Guid.NewGuid(), 1, spec.CreatedAtUtc.AddSeconds(20), ct));
                await root.CommitAsync(ct);
            });
            Assert.Equal(1L, await Count(locator, "operation_custody", ct));
            await using (var negative = await connection.BeginTransactionAsync(ct))
            {
                await using var invalid = new NpgsqlCommand("INSERT INTO " + Schema +
                    ".steps (installation_id, tenant_id, instance_id, step_id, ordinal, state, version) VALUES (@i,@t,@w,@s,0,1,1)", connection, negative);
                invalid.Parameters.AddWithValue("i", spec.InstallationId); invalid.Parameters.AddWithValue("t", Guid.NewGuid());
                invalid.Parameters.AddWithValue("w", spec.InstanceId); invalid.Parameters.AddWithValue("s", Guid.NewGuid());
                var error = await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync(ct));
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState); await negative.RollbackAsync(ct);
            }
            Assert.Equal(1L, await Count(locator, "transitions", ct));
            Assert.Equal(1L, await Count(locator, "request_receipts", ct));
        }
        finally
        {
            if (created)
            {
                using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                // Fresh connection; all failed transaction roots have been disposed before cleanup.
                await using var cleanup = new NpgsqlConnection(locator); await cleanup.OpenAsync(cleanupBudget.Token);
                await using var drop = new NpgsqlCommand("DROP SCHEMA " + Schema + " CASCADE", cleanup);
                await drop.ExecuteNonQueryAsync(cleanupBudget.Token);
                await using var verify = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname = @schema", cleanup);
                verify.Parameters.AddWithValue("schema", Schema);
                Assert.Equal(0L, (long)(await verify.ExecuteScalarAsync(cleanupBudget.Token))!);
            }
        }
    }

    private static async Task WithRoot(NpgsqlConnection connection, Func<WorkflowDbContext, NpgsqlTransaction, Task> body)
    {
        await using var context = WorkflowDbContext.CreateFixture(connection, Guid.Parse(CaseId));
        await using var root = await connection.BeginTransactionAsync(); await context.Database.UseTransactionAsync(root);
        await body(context, root);
    }
    private static async Task<long> Count(string locator, string table, CancellationToken ct)
    {
        Assert.Contains(table, new[] { "instances", "steps", "transitions", "request_receipts", "operation_custody" });
        await using var connection = new NpgsqlConnection(locator); await connection.OpenAsync(ct);
        await using var query = new NpgsqlCommand("SELECT count(*) FROM " + Schema + "." + table, connection);
        return (long)(await query.ExecuteScalarAsync(ct))!;
    }
    private static WorkflowSpecification NewSpecification(WorkflowSpecification? existing = null, string? releaseHash = null)
    {
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        return new(existing?.InstallationId ?? Guid.NewGuid(), existing?.TenantId ?? Guid.NewGuid(), existing?.InstanceId ?? Guid.NewGuid(),
            existing?.RequestId ?? Guid.NewGuid(), existing?.ProjectId ?? Guid.NewGuid(), existing?.ApplicationId ?? Guid.NewGuid(),
            existing?.EnvironmentId ?? Guid.NewGuid(), existing?.TargetId ?? Guid.NewGuid(), existing?.ReleaseId ?? Guid.NewGuid(),
            1, releaseHash ?? new string('a', 64), WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(2));
    }
}

public sealed class WorkflowDatabaseFixtureFactAttribute : FactAttribute
{
    public WorkflowDatabaseFixtureFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_FIXTURE_APPROVED_CASE") != WorkflowPostgreSqlFixtureTests.CaseId)
            Skip = "Pending exact WFL PostgreSQL fixture operation approval; no database IO performed.";
    }
}
