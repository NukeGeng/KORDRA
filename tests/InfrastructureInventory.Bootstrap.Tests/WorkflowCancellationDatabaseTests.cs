using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowCancellationDatabaseTests
{
    public static readonly Guid CaseId = Guid.Parse("64dc981e-baae-4552-b38f-ee738154da30");
    private static string Schema => "t12_" + CaseId.ToString("N");
    private static string DirectoryPath => Path.Combine(Root(), ".amr-local", "t12-remaining-design", "t12-2e-cancellation-schema-review");

    [Fact]
    public void ExportCancellationFixtureMetadataWithoutOpeningConnection()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        using var workflow = WorkflowDbContext.CreateWakeFixture(connection, CaseId);
        using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId);
        var scripts = new Dictionary<string, string> {
            ["wfl.sql"] = workflow.Database.GenerateCreateScript(), ["aud.sql"] = audit.Database.GenerateCreateScript(),
            ["cancellation-expansion.sql"] = File.ReadAllText(Path.Combine(Root(), "src/Modules/WorkflowOrchestration/Infrastructure/Migrations/wfl-v3-cancellation.sql"))
                .Replace("wfl.", Schema + ".", StringComparison.Ordinal),
            ["cleanup.sql"] = "DROP SCHEMA " + Schema + " CASCADE;\n"
        };
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        foreach (var sql in scripts.Values) { Assert.Contains(Schema, sql); Assert.DoesNotContain("wolverine_queues", sql); }
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_CANCELLATION_ARTIFACT_DIRECTORY") is not { } directory) return;
        Assert.Equal(DirectoryPath, Path.GetFullPath(directory)); Directory.CreateDirectory(directory);
        foreach (var (name, sql) in scripts)
        {
            var file = Path.Combine(directory, name);
            if (File.Exists(file)) Assert.Equal(sql, File.ReadAllText(file)); else File.WriteAllText(file, sql);
        }
        var manifest = JsonSerializer.Serialize(new { status = "proposal_not_operation_authorization", caseId = CaseId, schema = Schema,
            sha256 = scripts.ToDictionary(x => x.Key, x => Hash(Encoding.UTF8.GetBytes(x.Value))) }, new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(directory, "manifest.json");
        if (File.Exists(path)) Assert.Equal(manifest, File.ReadAllText(path)); else File.WriteAllText(path, manifest);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("tenant")]
    [InlineData("environment")]
    [InlineData("request")]
    [InlineData("hash")]
    [InlineData("stage")]
    public async Task CancellationRejectsSubstitutedAuditBeforeTouchingDatabase(string changed)
    {
        await using var connection = new NpgsqlConnection(
            "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        var environment = Guid.NewGuid();
        var intent = new WorkflowCancellationIntent(CaseId, CaseId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 1, new string('a', 64));
        var mutation = new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation,
            intent.ActorId, intent.TenantId, intent.InstanceId, "workflow.cancel", "environment", environment,
            1, 1, "workflow-cancel-" + intent.CommandHash, DateTimeOffset.UtcNow, intent.RequestId);
        mutation = changed switch
        {
            "actor" => mutation with { SubjectRef = Guid.NewGuid() },
            "tenant" => mutation with { TenantRef = Guid.NewGuid() },
            "environment" => mutation with { ScopeId = Guid.NewGuid() },
            "request" => mutation with { CorrelationId = Guid.NewGuid() },
            "hash" => mutation with { SafeReason = "workflow-cancel-" + new string('b', 64) },
            "stage" => mutation with { Stage = AuditIntentStage.Use },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowCancellationTransaction.ExecuteIsolatedAsync(connection, null!, CaseId, intent, Guid.NewGuid(),
                environment, mutation, gate, _ => throw new InvalidOperationException("Unexpected admission callback.")));
        Assert.Contains("exact trusted scope and mutation audit", error.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.False(gate.IsClosed);
    }

    [WorkflowCancellationDatabaseFact]
    public async Task CancellationIntentAuditAndFloorAreAtomicStickyScopedAndSerializeBeforeEffects()
    {
        var scripts = ReadApprovedScripts();
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_CANCELLATION_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved private cancellation fixture locator required.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var ct = timeout.Token;
        await using var setup = Connection(locator); await setup.OpenAsync(ct);
        Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", ct, ("schema", Schema)));
        var created = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                await Execute(setup, "CREATE SCHEMA " + Schema, ct, ddl);
                foreach (var name in new[] { "wfl.sql", "aud.sql" }) await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            // Populate revision2 before additive expansion. No native host or production command runs.
            var original = await SeedPending();
            await Execute(setup, scripts["cancellation-expansion.sql"], ct);
            Assert.Equal(1L, await Version(original));
            var actor = Guid.NewGuid();
            var intent = new WorkflowCancellationIntent(CaseId, CaseId, original.InstanceId, Guid.NewGuid(), actor, 1, original.SpecificationHash);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Cancel(original, intent, true, project: Guid.NewGuid()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Cancel(original, intent, true, environment: Guid.NewGuid()));
            var floorBefore = await Floor();
            var rolledBack = await Cancel(original, intent, false);
            Assert.Equal(1L, await Version(original)); Assert.Equal(0L, await IntentCount(original));
            Assert.Equal(floorBefore, await Floor());
            Assert.Null(rolledBack.Result);
            Assert.NotNull(rolledBack.Audit);
            Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE intent_id=@id", ct, ("id", rolledBack.Audit.IntentId)));
            var committed = await Cancel(original, intent, true);
            Assert.NotNull(committed.Result); Assert.False(committed.Result.Duplicate); Assert.Equal(2L, committed.Result.Version);
            Assert.Equal(2L, await Version(original)); Assert.Equal(1L, await IntentCount(original));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".clock_floor f JOIN " + Schema +
                ".cancellation_intents c ON c.installation_id=f.installation_id AND c.tenant_id=f.tenant_id WHERE c.request_id=@id AND f.floor_utc>=c.recorded_at_utc", ct, ("id", intent.RequestId)));
            Assert.Equal((long)WorkflowState.Pending, await State(original)); // intent is not terminal cancellation
            Assert.NotNull(committed.Audit);
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE intent_id=@id", ct, ("id", committed.Audit.IntentId)));
            var replay = await Cancel(original, intent, true);
            Assert.NotNull(replay.Result); Assert.True(replay.Result.Duplicate); Assert.Equal(2L, replay.Result.Version); Assert.Null(replay.Audit);
            Assert.Equal(2L, await Version(original)); Assert.Equal(1L, await IntentCount(original));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Cancel(original, intent, true, environment: Guid.NewGuid()));
            foreach (var changed in new[] {
                new WorkflowCancellationIntent(CaseId, CaseId, original.InstanceId, intent.RequestId, Guid.NewGuid(), 1, original.SpecificationHash),
                new WorkflowCancellationIntent(CaseId, CaseId, original.InstanceId, intent.RequestId, actor, 2, original.SpecificationHash),
                new WorkflowCancellationIntent(CaseId, Guid.NewGuid(), original.InstanceId, Guid.NewGuid(), actor, 1, original.SpecificationHash),
                new WorkflowCancellationIntent(CaseId, CaseId, original.InstanceId, Guid.NewGuid(), actor, 2, new string('b', 64)) })
                await Assert.ThrowsAsync<InvalidOperationException>(() => Cancel(original, changed, true));
            await using (var connection = Connection(locator))
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var context = IsolatedWorkflowFixture.CreateCancellationFixture(connection, CaseId);
                await context.Database.UseTransactionAsync(transaction, ct);
                var step = await context.Set<WorkflowStepRecord>().FirstAsync(x => x.InstanceId == original.InstanceId, ct);
                await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowStore(context).StageTransitionAsync(
                    new(CaseId, CaseId, original.InstanceId), step.StepId, Guid.NewGuid(), original.SpecificationHash, 2, WorkflowState.Running, DateTimeOffset.UtcNow, ct));
                await transaction.RollbackAsync(ct);
            }
            // A Pending intent with no unresolved work can later finish through the existing accepted graph.
            await using (var connection = Connection(locator))
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var context = IsolatedWorkflowFixture.CreateCancellationFixture(connection, CaseId);
                await context.Database.UseTransactionAsync(transaction, ct);
                var step = await context.Set<WorkflowStepRecord>().FirstAsync(x => x.InstanceId == original.InstanceId, ct);
                await new WorkflowStore(context).StageTransitionAsync(new(CaseId, CaseId, original.InstanceId), step.StepId,
                    Guid.NewGuid(), original.SpecificationHash, 2, WorkflowState.Cancelled, DateTimeOffset.UtcNow, ct);
                await Finish(context, transaction, original, actor, true);
            }
            Assert.Equal((long)WorkflowState.Cancelled, await State(original));
            var terminalDuplicate = await Cancel(original, intent, true); Assert.NotNull(terminalDuplicate.Result); Assert.True(terminalDuplicate.Result.Duplicate);
            Assert.Equal(2L, terminalDuplicate.Result.Version); Assert.Equal(3L, await Version(original));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Cancel(original,
                new(CaseId, CaseId, original.InstanceId, Guid.NewGuid(), actor, 3, original.SpecificationHash), true));

            var race = await SeedPending();
            await using (var canceller = Connection(locator))
            await using (var competitor = Connection(locator))
            {
                await canceller.OpenAsync(ct); await competitor.OpenAsync(ct);
                await using var cancelRoot = await canceller.BeginTransactionAsync(ct);
                await using var effectRoot = await competitor.BeginTransactionAsync(ct);
                await using var cancelContext = IsolatedWorkflowFixture.CreateCancellationFixture(canceller, CaseId);
                await using var effectContext = IsolatedWorkflowFixture.CreateCancellationFixture(competitor, CaseId);
                await cancelContext.Database.UseTransactionAsync(cancelRoot, ct); await effectContext.Database.UseTransactionAsync(effectRoot, ct);
                var stale = await effectContext.Set<WorkflowInstanceRecord>().SingleAsync(x => x.InstanceId == race.InstanceId, ct);
                var step = await effectContext.Set<WorkflowStepRecord>().FirstAsync(x => x.InstanceId == race.InstanceId, ct);
                Assert.Equal(1, stale.Version);
                await new WorkflowCancellationStore(cancelContext).StageIntentAsync(new(CaseId, CaseId, race.InstanceId,
                    Guid.NewGuid(), actor, 1, race.SpecificationHash), DateTimeOffset.UtcNow, ct);
                var operation = Guid.NewGuid();
                var competing = new WorkflowStore(effectContext).StageTransitionAsync(new(CaseId, CaseId, race.InstanceId),
                    step.StepId, operation, race.SpecificationHash, 1, WorkflowState.Running, DateTimeOffset.UtcNow, ct);
                try
                {
                    await Task.Delay(50, ct); Assert.False(competing.IsCompleted);
                    await Finish(cancelContext, cancelRoot, race, actor, true);
                    var denial = await Assert.ThrowsAsync<InvalidOperationException>(() => competing);
                    Assert.Equal("Cancellation intent forbids a fresh workflow effect.", denial.Message);
                }
                finally
                {
                    // Release the instance lock on failure. Disposal also handles a committed transaction.
                    await cancelRoot.DisposeAsync();
                    try { await competing; } catch (InvalidOperationException) { }
                }
                Assert.Equal(2L, await Version(race)); Assert.Equal((long)WorkflowState.Pending, await State(race));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".transitions WHERE operation_id=@id", ct, ("id", operation)));
            }

            var (waiting, timer) = await WorkflowWakeDatabaseTests.SeedWaitingTimer(setup, ct, CaseId);
            // No cancellation intent exists yet: Failed/Expired must still preserve an unresolved published timer.
            await using (var connection = Connection(locator))
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var context = IsolatedWorkflowFixture.CreateCancellationFixture(connection, CaseId);
                await context.Database.UseTransactionAsync(transaction, ct);
                var pending = await context.Set<WorkflowTimerRecord>().SingleAsync(x => x.TimerId == timer.TimerId, ct);
                pending.Status = 2; pending.SchedulerAttempts = 1; pending.Version++;
                await context.SaveChangesAsync(ct);
                foreach (var terminal in new[] { WorkflowState.Failed, WorkflowState.Expired })
                {
                    var denial = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        new WorkflowStore(context).StageTransitionAsync(new(CaseId, CaseId, waiting.InstanceId), timer.StepId,
                            Guid.NewGuid(), waiting.SpecificationHash, 4, terminal, DateTimeOffset.UtcNow, ct));
                    Assert.Contains("reconciliation", denial.Message);
                }
                await transaction.RollbackAsync(ct);
            }
            await Cancel(waiting, new(CaseId, CaseId, waiting.InstanceId, Guid.NewGuid(), actor, 4, waiting.SpecificationHash), true);
            Assert.Equal((long)WorkflowState.CancelRequested, await State(waiting));
            await using (var connection = Connection(locator))
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var context = IsolatedWorkflowFixture.CreateCancellationFixture(connection, CaseId);
                await context.Database.UseTransactionAsync(transaction, ct);
                var now = DateTimeOffset.UtcNow;
                var wake = new WorkflowWakeOperation(waiting, timer.StepId, timer.TimerId, 1, 1, 1,
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 6, 3, now, now.AddSeconds(120));
                await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowWakeStore(context).StageInitialDispatchAsync(wake, 5, 1,
                    Guid.NewGuid(), Guid.NewGuid(), now, ct));
                await transaction.RollbackAsync(ct);
            }
            Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".wake_operations WHERE instance_id=@id", ct, ("id", waiting.InstanceId)));
            Assert.Equal(1L, await Scalar(setup, "SELECT status::bigint FROM " + Schema + ".timers WHERE timer_id=@id", ct, ("id", timer.TimerId)));

            // Synthetic unresolved owner marker only; this is not a native publication or authority proof.
            await using (var connection = Connection(locator))
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var context = IsolatedWorkflowFixture.CreateCancellationFixture(connection, CaseId);
                await context.Database.UseTransactionAsync(transaction, ct);
                var marker = await context.Set<WorkflowTimerRecord>().SingleAsync(x => x.TimerId == timer.TimerId, ct);
                marker.Status = 2; marker.SchedulerAttempts = 1; marker.Version++;
                await context.SaveChangesAsync(ct); await Finish(context, transaction, waiting, actor, true);
            }
            await using (var connection = Connection(locator))
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var context = IsolatedWorkflowFixture.CreateCancellationFixture(connection, CaseId);
                await context.Database.UseTransactionAsync(transaction, ct);
                foreach (var terminal in new[] { WorkflowState.Cancelled, WorkflowState.Failed, WorkflowState.Expired, WorkflowState.Succeeded })
                    await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowStore(context).StageTransitionAsync(
                        new(CaseId, CaseId, waiting.InstanceId), timer.StepId, Guid.NewGuid(), waiting.SpecificationHash, 5,
                        terminal, DateTimeOffset.UtcNow, ct));
                await transaction.RollbackAsync(ct);
            }
            Assert.Equal((long)WorkflowState.CancelRequested, await State(waiting)); Assert.Equal(5L, await Version(waiting));

            var reconciling = await SeedPending(true);
            await Cancel(reconciling, new(CaseId, CaseId, reconciling.InstanceId, Guid.NewGuid(), actor, 3, reconciling.SpecificationHash), true);
            Assert.Equal((long)WorkflowState.Reconciling, await State(reconciling));
            Assert.Equal(4L, await Version(reconciling));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".steps WHERE instance_id=@id AND state=@state", ct,
                ("id", reconciling.InstanceId), ("state", (int)WorkflowStepState.Unknown)));

            async Task<WorkflowSpecification> SeedPending(bool reconcilingState = false)
            {
                await using var connection = Connection(locator); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                // Revision2 remains usable for setup before/after additive expansion; it cannot write cancellation rows.
                await using var context = IsolatedWorkflowFixture.CreateWakeFixture(connection, CaseId);
                await context.Database.UseTransactionAsync(transaction, ct);
                var start = DateTimeOffset.UtcNow.AddMinutes(-10);
                var spec = new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test,
                    start, start.AddHours(2), start.AddHours(1));
                var store = new WorkflowStore(context); await store.StageCreationAsync(spec, spec.CreatedAtUtc, ct);
                if (reconcilingState)
                {
                    var step = await context.Set<WorkflowStepRecord>().FirstAsync(x => x.InstanceId == spec.InstanceId, ct);
                    await store.StageTransitionAsync(new(CaseId, CaseId, spec.InstanceId), step.StepId, Guid.NewGuid(), spec.SpecificationHash, 1, WorkflowState.Running, DateTimeOffset.UtcNow, ct);
                    step.State = (int)WorkflowStepState.Running; step.Version++; await context.SaveChangesAsync(ct);
                    step.State = (int)WorkflowStepState.Unknown; step.Version++; await context.SaveChangesAsync(ct);
                    await store.StageTransitionAsync(new(CaseId, CaseId, spec.InstanceId), step.StepId, Guid.NewGuid(), spec.SpecificationHash, 2, WorkflowState.Reconciling, DateTimeOffset.UtcNow, ct);
                }
                await Finish(context, transaction, spec, Guid.NewGuid(), true); return spec;
            }
            async Task<(WorkflowStoredResult? Result, AuditIntent? Audit)> Cancel(WorkflowSpecification spec,
                WorkflowCancellationIntent command, bool commit, Guid? project = null, Guid? environment = null)
            {
                await using var connection = Connection(locator);
                await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                var mutation = new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation,
                    command.ActorId, command.TenantId, command.InstanceId, "workflow.cancel", "environment",
                    environment ?? spec.EnvironmentId, 1, 1, "workflow-cancel-" + command.CommandHash,
                    DateTimeOffset.UtcNow, command.RequestId);
                var gate = new FairAsyncReaderWriterAdmissionGate();
                await using var held = await gate.AcquireWriteAsync(ct);
                var checks = 0;
                var result = await IsolatedWorkflowFixture.Cancel(connection, transaction, CaseId,
                    command, project ?? spec.ProjectId, environment ?? spec.EnvironmentId, mutation, gate,
                    _ => Task.FromResult(++checks == 1 || commit), ct);
                Assert.Equal(2, checks);
                Assert.False(gate.IsClosed);
                Assert.Equal(commit && result.Receipt?.Duplicate == false
                    ? WorkflowCommitDisposition.Committed : WorkflowCommitDisposition.NoCommitAttempt, result.Disposition);
                return (result.Receipt, result.Receipt?.Duplicate == true ? null : mutation);
            }
            async Task<AuditIntent> Finish(WorkflowDbContext context, NpgsqlTransaction transaction, WorkflowSpecification spec, Guid subject, bool commit)
            {
                var now = DateTimeOffset.UtcNow;
                var audit = new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation, subject, CaseId, spec.InstanceId,
                    "workflow.cancel", "environment", spec.EnvironmentId, 1, 1, "synthetic_fixture", now, spec.RequestId);
                await using var append = await IsolatedWorkflowFixture.AuditTransaction((NpgsqlConnection)context.Database.GetDbConnection(), transaction, CaseId, ct);
                Assert.Equal(AuditTransactionStageStatus.Staged, (await append.StageAsync(audit, ct)).Status);
                var floor = await context.Set<WorkflowClockFloorRecord>().SingleOrDefaultAsync(x => x.InstallationId == CaseId && x.TenantId == CaseId, ct);
                if (floor is null) context.Add(new WorkflowClockFloorRecord { InstallationId = CaseId, TenantId = CaseId, FloorUtc = now });
                else if (now > floor.FloorUtc) floor.FloorUtc = now;
                await context.SaveChangesAsync(ct);
                if (commit) Assert.Equal(AuditAppendStatus.Durable, (await append.CommitAsync(ct)).Status);
                else await transaction.RollbackAsync(ct);
                return audit;
            }
            async Task<DateTime> Floor()
            {
                await using var command = new NpgsqlCommand("SELECT floor_utc FROM " + Schema + ".clock_floor WHERE installation_id=@id AND tenant_id=@id", setup);
                command.Parameters.AddWithValue("id", CaseId); return (DateTime)(await command.ExecuteScalarAsync(ct))!;
            }
            Task<long> Version(WorkflowSpecification spec) => Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId));
            Task<long> State(WorkflowSpecification spec) => Scalar(setup, "SELECT state::bigint FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId));
            Task<long> IntentCount(WorkflowSpecification spec) => Scalar(setup, "SELECT count(*) FROM " + Schema + ".cancellation_intents WHERE instance_id=@id", ct, ("id", spec.InstanceId));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (created)
            {
                await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", cleanup.Token, ("schema", Schema)));
            }
        }
    }

    private static Dictionary<string, string> ReadApprovedScripts()
    {
        var file = Environment.GetEnvironmentVariable("KORDRA_WFL_CANCELLATION_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact cancellation fixture approval required.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(file)); var root = manifest.RootElement;
        Assert.Equal("approved_for_execution", root.GetProperty("status").GetString()); Assert.Equal(CaseId, root.GetProperty("caseId").GetGuid());
        Assert.Equal(Schema, root.GetProperty("schema").GetString());
        var scripts = new Dictionary<string, string>();
        foreach (var name in new[] { "wfl.sql", "aud.sql", "cancellation-expansion.sql", "cleanup.sql" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, name)); Assert.Equal(root.GetProperty("sha256").GetProperty(name).GetString(), Hash(bytes));
            scripts.Add(name, Encoding.UTF8.GetString(bytes));
        }
        Assert.Equal("DROP SCHEMA " + Schema + " CASCADE;\n", scripts["cleanup.sql"]); return scripts;
    }
    private static NpgsqlConnection Connection(string locator)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(locator) { Pooling = false, Multiplexing = false }.ConnectionString);
        _ = IsolatedWorkflowFixture.Validate(connection, CaseId); return connection;
    }
    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? transaction = null)
    { await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 3 }; await command.ExecuteNonQueryAsync(ct); }
    private static async Task<long> Scalar(NpgsqlConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 2 };
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kordra.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Product root missing.");
    }
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}

public sealed class WorkflowCancellationDatabaseFactAttribute : FactAttribute
{
    public WorkflowCancellationDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_CANCELLATION_FIXTURE_MANIFEST")))
            Skip = "Requires exact cancellation SQL/schema operation approval; no DB or host started.";
    }
}
