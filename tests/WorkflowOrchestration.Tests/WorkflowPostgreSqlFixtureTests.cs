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
        await using var connection = WorkflowModelFixture.Connection(locator, Guid.Parse(CaseId));
        await using var model = WorkflowModelFixture.CreateFixture(connection, Guid.Parse(CaseId));
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
                await new WorkflowStore(context).StageCreationAsync(spec, spec.CreatedAtUtc, ct);
                // Independent reader must not see staged rows; caller rollback discards instance/steps/receipt together.
                Assert.Equal(0L, await Count(locator, "instances", ct));
                await root.RollbackAsync(ct);
            });
            Assert.Equal(0L, await Count(locator, "instances", ct));
            Assert.Equal(0L, await Count(locator, "request_receipts", ct));
            await WithRoot(connection, async (context, root) => {
                var denial = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new WorkflowStore(context).StageCreationAsync(spec, spec.DeadlineUtc, ct));
                Assert.Equal("RecoveryHorizonExceeded", denial.Message);
                Assert.False(context.ChangeTracker.HasChanges());
                await root.RollbackAsync(ct);
            });
            await WithRoot(connection, async (context, root) => {
                var result = await new WorkflowStore(context).StageCreationAsync(spec, spec.CreatedAtUtc, ct);
                Assert.False(result.Duplicate); await root.CommitAsync(ct);
            });
            Assert.Equal(1L, await Count(locator, "instances", ct));
            Assert.Equal(4L, await Count(locator, "steps", ct));
            await WithRoot(connection, async (context, root) => {
                var result = await new WorkflowStore(context).StageCreationAsync(spec, spec.DeadlineUtc.AddDays(90), ct);
                Assert.True(result.Duplicate);
                Assert.Equal(spec.InstanceId, result.InstanceId);
                Assert.Equal(1L, result.Version); await root.CommitAsync(ct);
            });
            var altered = NewSpecification(spec, new string('b', 64));
            await WithRoot(connection, async (context, root) => {
                await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowStore(context).StageCreationAsync(altered, altered.CreatedAtUtc, ct));
                await root.RollbackAsync(ct);
            });
            await WithRoot(connection, async (context, root) => {
                var identity = new WorkflowIdentity(spec.InstallationId, Guid.NewGuid(), spec.InstanceId);
                await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowStore(context).StageTransitionAsync(identity,
                    Guid.NewGuid(), Guid.NewGuid(), spec.SpecificationHash, 1, WorkflowState.Running, spec.CreatedAtUtc, ct));
                await root.RollbackAsync(ct);
            });
            // Both real roots read version 1. First commits, second must lose the database CAS, with no second history row.
            await using var second = WorkflowModelFixture.Connection(locator, Guid.Parse(CaseId)); await second.OpenAsync(ct);
            await using var a = WorkflowModelFixture.CreateFixture(connection, Guid.Parse(CaseId));
            await using var b = WorkflowModelFixture.CreateFixture(second, Guid.Parse(CaseId));
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
            // Cancellation wins while the claim context still tracks an earlier Running row.
            var cancelled = await CreateRunningWorkflow();
            await using (var claimContext = WorkflowModelFixture.CreateFixture(connection, Guid.Parse(CaseId)))
            await using (var claimRoot = await connection.BeginTransactionAsync(ct))
            {
                await claimContext.Database.UseTransactionAsync(claimRoot, ct);
                var tracked = await claimContext.Set<WorkflowInstanceRecord>().SingleAsync(x => x.InstanceId == cancelled.Spec.InstanceId, ct);
                Assert.Equal((int)WorkflowState.Running, tracked.State);
                await WithRoot(second, async (context, root) => {
                    var store = new WorkflowStore(context);
                    await store.StageTransitionAsync(cancelled.Identity, cancelled.Step, Guid.NewGuid(), cancelled.Spec.SpecificationHash,
                        2, WorkflowState.CancelRequested, cancelled.Spec.CreatedAtUtc, ct);
                    await store.StageTransitionAsync(cancelled.Identity, cancelled.Step, Guid.NewGuid(), cancelled.Spec.SpecificationHash,
                        3, WorkflowState.Cancelled, cancelled.Spec.CreatedAtUtc, ct);
                    await root.CommitAsync(ct);
                });
                await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowStore(claimContext).StageInitialCustodyAsync(
                    cancelled.Identity, cancelled.Step, Guid.NewGuid(), cancelled.Spec.SpecificationHash,
                    Guid.NewGuid(), Guid.NewGuid(), 1, cancelled.Spec.CreatedAtUtc, ct));
                await claimRoot.RollbackAsync(ct);
            }
            // Claim wins: it must lock the instance before the step so cancellation cannot overtake its root.
            var claimed = await CreateRunningWorkflow();
            await using (var claimContext = WorkflowModelFixture.CreateFixture(connection, Guid.Parse(CaseId)))
            await using (var claimRoot = await connection.BeginTransactionAsync(ct))
            {
                await claimContext.Database.UseTransactionAsync(claimRoot, ct);
                await new WorkflowStore(claimContext).StageInitialCustodyAsync(claimed.Identity, claimed.Step, Guid.NewGuid(),
                    claimed.Spec.SpecificationHash, Guid.NewGuid(), Guid.NewGuid(), 1, claimed.Spec.CreatedAtUtc, ct);
                await WithRoot(second, async (context, root) => {
                    await using var limit = new NpgsqlCommand("SET LOCAL lock_timeout = '200ms'", second, root);
                    await limit.ExecuteNonQueryAsync(ct);
                    var failure = await Assert.ThrowsAsync<PostgresException>(() => new WorkflowStore(context).StageTransitionAsync(
                        claimed.Identity, claimed.Step, Guid.NewGuid(), claimed.Spec.SpecificationHash, 2,
                        WorkflowState.CancelRequested, claimed.Spec.CreatedAtUtc, ct));
                    Assert.Equal(PostgresErrorCodes.LockNotAvailable, failure.SqlState);
                    await root.RollbackAsync(ct);
                });
                await claimRoot.CommitAsync(ct);
            }
            await VerifyCancellationCustody(cancelled, WorkflowState.Cancelled, 0);
            await VerifyCancellationCustody(claimed, WorkflowState.Running, 1);
            Assert.Equal(5L, await Count(locator, "transitions", ct));
            Assert.Equal(3L, await Count(locator, "request_receipts", ct));
            async Task VerifyCancellationCustody((WorkflowSpecification Spec, WorkflowIdentity Identity, Guid Step) workflow,
                WorkflowState state, long custodyCount)
            {
                await using var observer = WorkflowModelFixture.Connection(locator, Guid.Parse(CaseId)); await observer.OpenAsync(ct);
                await using var query = new NpgsqlCommand("SELECT i.state, (SELECT count(*) FROM " + Schema +
                    ".operation_custody c WHERE c.installation_id=i.installation_id AND c.tenant_id=i.tenant_id AND c.instance_id=i.instance_id) FROM " +
                    Schema + ".instances i WHERE i.installation_id=@installation AND i.tenant_id=@tenant AND i.instance_id=@instance", observer);
                query.Parameters.AddWithValue("installation", workflow.Identity.InstallationId);
                query.Parameters.AddWithValue("tenant", workflow.Identity.TenantId); query.Parameters.AddWithValue("instance", workflow.Identity.InstanceId);
                await using var rows = await query.ExecuteReaderAsync(ct);
                Assert.True(await rows.ReadAsync(ct)); Assert.Equal((int)state, rows.GetInt32(0)); Assert.Equal(custodyCount, rows.GetInt64(1));
            }
            async Task<(WorkflowSpecification Spec, WorkflowIdentity Identity, Guid Step)> CreateRunningWorkflow()
            {
                var fresh = NewSpecification(); var scope = new WorkflowIdentity(fresh.InstallationId, fresh.TenantId, fresh.InstanceId);
                Guid step = Guid.Empty;
                await WithRoot(connection, async (context, root) => {
                    var store = new WorkflowStore(context); await store.StageCreationAsync(fresh, fresh.CreatedAtUtc, ct);
                    step = await context.Set<WorkflowStepRecord>().Where(x => x.InstanceId == fresh.InstanceId && x.Ordinal == 0)
                        .Select(x => x.StepId).SingleAsync(ct);
                    await store.StageTransitionAsync(scope, step, Guid.NewGuid(), fresh.SpecificationHash, 1,
                        WorkflowState.Running, fresh.CreatedAtUtc, ct); await root.CommitAsync(ct);
                });
                return (fresh, scope, step);
            }
        }
        finally
        {
            if (created)
            {
                using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                // Fresh connection; all failed transaction roots have been disposed before cleanup.
                await using var cleanup = WorkflowModelFixture.Connection(locator, Guid.Parse(CaseId)); await cleanup.OpenAsync(cleanupBudget.Token);
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
        await using var context = WorkflowModelFixture.CreateFixture(connection, Guid.Parse(CaseId));
        await using var root = await connection.BeginTransactionAsync(); await context.Database.UseTransactionAsync(root);
        await body(context, root);
    }
    private static async Task<long> Count(string locator, string table, CancellationToken ct)
    {
        Assert.Contains(table, new[] { "instances", "steps", "transitions", "request_receipts", "operation_custody" });
        await using var connection = WorkflowModelFixture.Connection(locator, Guid.Parse(CaseId)); await connection.OpenAsync(ct);
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
