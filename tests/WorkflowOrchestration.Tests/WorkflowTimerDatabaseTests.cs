using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowTimerDatabaseTests
{
    public const string CaseId = "3061a02c-5571-442e-be7a-b1c5418f21c7";
    public const string Schema = "t12_3061a02c5571442ebe7ab1c5418f21c7";
    private static readonly Guid Case = Guid.ParseExact(CaseId, "D");
    private static readonly DateTimeOffset Created = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TimerFixtureSqlIsOfflineBoundedAndDoesNotGrantExecution()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        using var context = WorkflowDbContext.CreateFixture(connection, Case);
        var scripts = new Dictionary<string, string> {
            ["wfl.sql"] = "CREATE SCHEMA " + Schema + ";\n" + context.Database.GenerateCreateScript(),
            ["cleanup.sql"] = "DROP SCHEMA " + Schema + " CASCADE;\n"
        };
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Contains("CREATE TABLE " + Schema + ".timers", scripts["wfl.sql"]);
        Assert.DoesNotContain("SYNTHETIC", scripts["wfl.sql"]);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_TIMER_ARTIFACT_DIRECTORY") is not { Length: > 0 } directory) return;
        var root = Root(); var allowed = Path.Combine(root, ".amr-local", "t12-remaining-design", "t12-2d-timer-schema-review");
        Assert.Equal(Path.GetFullPath(allowed), Path.GetFullPath(directory)); Directory.CreateDirectory(allowed);
        foreach (var (name, sql) in scripts)
        {
            var file = Path.Combine(allowed, name);
            if (File.Exists(file)) Assert.Equal(sql, File.ReadAllText(file)); else File.WriteAllText(file, sql);
        }
        var manifest = JsonSerializer.Serialize(new { status = "proposal_not_operation_authorization", caseId = CaseId, schema = Schema,
            sha256 = scripts.ToDictionary(x => x.Key, x => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(x.Value)))) },
            new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(allowed, "manifest.json");
        if (File.Exists(path)) Assert.Equal(manifest, File.ReadAllText(path)); else File.WriteAllText(path, manifest);
    }

    [WorkflowTimerDatabaseFact]
    public async Task ActualWaitingTimerIsScopedIdempotentRootBoundAndDiscoveryIsLimited()
    {
        var approvalFile = Environment.GetEnvironmentVariable("KORDRA_WFL_TIMER_FIXTURE_MANIFEST")!;
        using var approval = JsonDocument.Parse(File.ReadAllBytes(approvalFile));
        Assert.Equal("approved_for_execution", approval.RootElement.GetProperty("status").GetString());
        Assert.Equal(CaseId, approval.RootElement.GetProperty("caseId").GetString());
        Assert.Equal(Schema, approval.RootElement.GetProperty("schema").GetString());
        var artifactDirectory = Path.Combine(Root(), ".amr-local", "t12-remaining-design", "t12-2d-timer-schema-review");
        var scripts = new Dictionary<string, string>();
        foreach (var name in new[] { "wfl.sql", "cleanup.sql" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(artifactDirectory, name));
            Assert.Equal(approval.RootElement.GetProperty("sha256").GetProperty(name).GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            scripts.Add(name, System.Text.Encoding.UTF8.GetString(bytes));
        }
        Assert.Equal("DROP SCHEMA " + Schema + " CASCADE;\n", scripts["cleanup.sql"]);
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_TIMER_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private separately approved timer fixture locator required.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var ct = budget.Token;
        await using var connection = Openable(); await connection.OpenAsync(ct);
        Assert.Equal(0L, await Count("SELECT count(*) FROM pg_namespace WHERE nspname='" + Schema + "'"));
        var created = false;
        var canonicalSpecifications = new Dictionary<Guid, byte[]>();
        try
        {
            await using (var setup = await connection.BeginTransactionAsync(ct))
            { await Execute(connection, scripts["wfl.sql"], ct, setup); await setup.CommitAsync(ct); created = true; }
            var request = await Prepare();
            WorkflowTimerScheduleResult? original = null;
            await WithRoot(connection, async (context, transaction) => {
                var provisional = await new WorkflowTimerStore(context).StageWaitingAsync(request, ct);
                Assert.False(provisional.Duplicate); Assert.Equal(4, provisional.WorkflowVersion);
                Assert.Equal(0L, await TimerCount());
                await transaction.RollbackAsync(ct);
            });
            Assert.Equal(0L, await TimerCount());
            await WithRoot(connection, async (context, transaction) => {
                original = await new WorkflowTimerStore(context).StageWaitingAsync(request, ct);
                await transaction.CommitAsync(ct);
            });
            Assert.Equal(1L, await TimerCount()); Assert.NotNull(original);
            await WithRoot(connection, async (context, transaction) => {
                var duplicate = await new WorkflowTimerStore(context).StageWaitingAsync(request, ct);
                Assert.True(duplicate.Duplicate); Assert.Equal(original.TimerId, duplicate.TimerId);
                Assert.Equal(original.WorkflowVersion, duplicate.WorkflowVersion); await transaction.CommitAsync(ct);
            });
            foreach (var wrong in new[] {
                Change(request, identity: new(Case, Guid.NewGuid(), request.Identity.InstanceId)),
                Change(request, hash: new string('b', 64)), Change(request, operation: Guid.NewGuid()),
                Change(request, custody: new(request.Custody.WorkerId, Guid.NewGuid(), 1, 1)),
                Change(request, workflowVersion: 4)
            })
                await WithRoot(connection, async (context, transaction) => {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowTimerStore(context).StageWaitingAsync(wrong, ct));
                    await transaction.RollbackAsync(ct);
                });
            Assert.Equal(1L, await TimerCount());
            var reordered = await Prepare(completeEarlier: false);
            await WithRoot(connection, async (context, transaction) => {
                await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowTimerStore(context).StageWaitingAsync(reordered, ct));
                await transaction.RollbackAsync(ct);
            });
            // A stale tracked Running snapshot must not schedule after a current cancellation intent.
            var staleRequest = await Prepare();
            await using (var staleConnection = Openable())
            {
                await staleConnection.OpenAsync(ct);
                await using var staleContext = WorkflowModelFixture.CreateFixture(staleConnection, Case);
                _ = await staleContext.Set<WorkflowInstanceRecord>().SingleAsync(x => x.InstanceId == staleRequest.Identity.InstanceId, ct);
                await WithRoot(connection, async (context, transaction) => {
                    await new WorkflowStore(context).StageTransitionAsync(staleRequest.Identity, staleRequest.StepId, Guid.NewGuid(),
                        staleRequest.SpecificationHash, 3, WorkflowState.CancelRequested, Created.AddMinutes(6), ct);
                    await transaction.CommitAsync(ct);
                });
                await using var staleRoot = await staleConnection.BeginTransactionAsync(ct); await staleContext.Database.UseTransactionAsync(staleRoot, ct);
                await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowTimerStore(staleContext).StageWaitingAsync(staleRequest, ct));
                await staleRoot.RollbackAsync(ct);
            }
            var contested = await Prepare();
            await WithRoot(connection, async (context, transaction) => {
                await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowTimerStore(context).StageWaitingAsync(Change(contested, workflowVersion: 2), ct));
                await transaction.RollbackAsync(ct);
            });
            await using (var b = Openable())
            {
                await b.OpenAsync(ct); await using var bContext = WorkflowModelFixture.CreateFixture(b, Case);
                _ = await bContext.Set<WorkflowInstanceRecord>().SingleAsync(x => x.InstanceId == contested.Identity.InstanceId, ct);
                await WithRoot(connection, async (context, transaction) => {
                    await new WorkflowTimerStore(context).StageWaitingAsync(contested, ct); await transaction.CommitAsync(ct);
                });
                await using var bRoot = await b.BeginTransactionAsync(ct); await bContext.Database.UseTransactionAsync(bRoot, ct);
                Assert.True((await new WorkflowTimerStore(bContext).StageWaitingAsync(contested, ct)).Duplicate);
                await bRoot.RollbackAsync(ct);
            }
            // 102 legitimate scheduled workflows, rather than corrupt duplicate timer rows, prove the 100-row query bound.
            for (var i = 0; i < 100; i++)
            {
                var extra = await Prepare();
                await WithRoot(connection, async (context, transaction) => {
                    await new WorkflowTimerStore(context).StageWaitingAsync(extra, ct); await transaction.CommitAsync(ct);
                });
            }
            Assert.Equal(102L, await TimerCount());
            await using (var read = Openable())
            {
                await read.OpenAsync(ct); await using var context = WorkflowModelFixture.CreateFixture(read, Case); var store = new WorkflowTimerStore(context);
                Assert.Empty(await store.DiscoverDueAsync(Case, Case, Created.AddMinutes(59), ct: ct));
                Assert.Empty(await store.DiscoverDueAsync(Case, Guid.NewGuid(), Created.AddHours(1), ct: ct));
                var due = await store.DiscoverDueAsync(Case, Case, Created.AddHours(1), ct: ct);
                Assert.Equal(100, due.Count); Assert.Equal(100, due.Select(x => x.TimerId).Distinct().Count());
                Assert.All(due, x => { Assert.Equal(Case, x.Identity.TenantId); Assert.Equal(1, x.Generation); Assert.Equal(0, x.SchedulerAttempts); });
                Assert.Equal(due.Select(x => x.TimerId.ToString("D")).Order(StringComparer.Ordinal), due.Select(x => x.TimerId.ToString("D")));
                Assert.Single(await store.DiscoverDueAsync(Case, Case, Created.AddHours(1), 1, ct));
            }
            // Canonical bytes keep 100ns ticks; provider timestamp columns cannot represent them.
            foreach (var precision in new[] {
                (Origin: Created, CreatedTicks: 1, DeadlineTicks: 1, DueTicks: 0),
                (Origin: Created, CreatedTicks: 0, DeadlineTicks: 0, DueTicks: 7),
                (Origin: Created, CreatedTicks: 9, DeadlineTicks: 2, DueTicks: 3),
                (Origin: new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero), CreatedTicks: 1, DeadlineTicks: 9, DueTicks: 7)
            })
            {
                var fractional = await Prepare(origin: precision.Origin, createdTicks: precision.CreatedTicks,
                    deadlineTicks: precision.DeadlineTicks, dueTicks: precision.DueTicks);
                WorkflowTimerScheduleResult? committed = null;
                await WithRoot(connection, async (context, transaction) => {
                    committed = await new WorkflowTimerStore(context).StageWaitingAsync(fractional, ct);
                    Assert.False(committed.Duplicate); await transaction.CommitAsync(ct);
                });
                await WithRoot(connection, async (context, transaction) => {
                    var duplicate = await new WorkflowTimerStore(context).StageWaitingAsync(fractional, ct);
                    Assert.True(duplicate.Duplicate); Assert.Equal(committed!.TimerId, duplicate.TimerId);
                    Assert.Equal(committed.WorkflowVersion, duplicate.WorkflowVersion); await transaction.RollbackAsync(ct);
                });
                await using var read = Openable(); await read.OpenAsync(ct);
                await using var observed = WorkflowModelFixture.CreateFixture(read, Case);
                var stored = await observed.Set<WorkflowInstanceRecord>().AsNoTracking()
                    .SingleAsync(x => x.InstanceId == fractional.Identity.InstanceId, ct);
                Assert.Equal(canonicalSpecifications[fractional.Identity.InstanceId], stored.Specification);
                var canonical = WorkflowSpecification.ReadCanonical(stored.Specification);
                Assert.Equal(fractional.SpecificationHash, canonical.SpecificationHash);
                if (precision.CreatedTicks != 0) Assert.NotEqual(canonical.CreatedAtUtc, stored.CreatedAtUtc);
                if (precision.DeadlineTicks != 0) Assert.NotEqual(canonical.DeadlineUtc, stored.DeadlineUtc);
                var storedTimer = await observed.Set<WorkflowTimerRecord>().AsNoTracking()
                    .SingleAsync(x => x.InstanceId == fractional.Identity.InstanceId, ct);
                if (precision.DueTicks != 0)
                {
                    Assert.NotEqual(canonical.DueAtUtc, storedTimer.DueAtUtc);
                    await using (var corrupt = new NpgsqlCommand("UPDATE " + Schema +
                        ".timers SET due_at_utc=due_at_utc+interval '1 microsecond' WHERE instance_id=@instance", connection))
                    { corrupt.Parameters.AddWithValue("instance", fractional.Identity.InstanceId); await corrupt.ExecuteNonQueryAsync(ct); }
                    await WithRoot(connection, async (context, transaction) => {
                        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowTimerStore(context).StageWaitingAsync(fractional, ct));
                        await transaction.RollbackAsync(ct);
                    });
                }
            }
            // Adjacent persisted microseconds are real binding changes, even when <1us from canonical.
            foreach (var column in new[] { "created_at_utc", "deadline_utc" })
            {
                var damaged = await Prepare(origin: Created, createdTicks: 7, deadlineTicks: 7);
                await using (var corrupt = new NpgsqlCommand("UPDATE " + Schema + ".instances SET " + column + "=" +
                    column + "+interval '1 microsecond' WHERE instance_id=@instance", connection))
                { corrupt.Parameters.AddWithValue("instance", damaged.Identity.InstanceId); await corrupt.ExecuteNonQueryAsync(ct); }
                await WithRoot(connection, async (context, transaction) => {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowTimerStore(context).StageWaitingAsync(damaged, ct));
                    await transaction.RollbackAsync(ct);
                });
            }
            Assert.Equal(106L, await TimerCount());

        }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var cleanupConnection = Openable(); await cleanupConnection.OpenAsync(cleanup.Token);
                await Execute(cleanupConnection, scripts["cleanup.sql"], cleanup.Token);
                await using var check = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", cleanupConnection);
                check.Parameters.AddWithValue("schema", Schema);
                Assert.Equal(0L, (long)(await check.ExecuteScalarAsync(cleanup.Token))!);
            }
        }

        NpgsqlConnection Openable()
        {
            var value = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(locator) { Pooling = false, Multiplexing = false }.ConnectionString);
            WorkflowModelFixture.Validate(value, Case); return value;
        }
        async Task<long> Count(string sql)
        { await using var read = Openable(); await read.OpenAsync(ct); await using var command = new NpgsqlCommand(sql, read); return (long)(await command.ExecuteScalarAsync(ct))!; }
        Task<long> TimerCount() => Count("SELECT count(*) FROM " + Schema + ".timers");
        async Task WithRoot(NpgsqlConnection c, Func<WorkflowDbContext, NpgsqlTransaction, Task> action)
        {
            await using var context = WorkflowModelFixture.CreateFixture(c, Case); await using var transaction = await c.BeginTransactionAsync(ct);
            await context.Database.UseTransactionAsync(transaction, ct); await action(context, transaction);
        }
        async Task<WorkflowTimerScheduleRequest> Prepare(bool completeEarlier = true, DateTimeOffset? origin = null,
            int createdTicks = 0, int deadlineTicks = 0, int dueTicks = 0)
        {
            WorkflowTimerScheduleRequest? result = null;
            await WithRoot(connection, async (context, transaction) => {
                var start = origin ?? Created;
                var spec = new WorkflowSpecification(Case, Case, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test, start.AddTicks(createdTicks),
                    start.AddHours(origin.HasValue ? 23 : 24).AddTicks(deadlineTicks), start.AddHours(1).AddTicks(dueTicks));
                canonicalSpecifications.Add(spec.InstanceId, spec.WriteCanonical());
                var store = new WorkflowStore(context); await store.StageCreationAsync(spec, spec.CreatedAtUtc, ct);
                var identity = new WorkflowIdentity(Case, Case, spec.InstanceId); var worker = Guid.NewGuid(); var boot = Guid.NewGuid();
                var step0 = await context.Set<WorkflowStepRecord>().SingleAsync(x => x.InstanceId == spec.InstanceId && x.Ordinal == 0, ct);
                await store.StageTransitionAsync(identity, step0.StepId, Guid.NewGuid(), spec.SpecificationHash, 1, WorkflowState.Running, spec.CreatedAtUtc, ct);
                var checkpoint = Guid.NewGuid();
                var custody0 = await store.StageInitialCustodyAsync(identity, step0.StepId, checkpoint, spec.SpecificationHash, worker, boot, 1, spec.CreatedAtUtc, ct);
                if (completeEarlier)
                {
                    // Synthetic admitted prior checkpoint facts only, not a production checkpoint executor.
                    var instance = await context.Set<WorkflowInstanceRecord>().SingleAsync(x => x.InstanceId == spec.InstanceId, ct);
                    step0.State = (int)WorkflowStepState.Succeeded; step0.Version++;
                    custody0.Phase = 3; custody0.Version++; custody0.LastCommitReceiptId = checkpoint; instance.Version++;
                    context.Add(new WorkflowTransitionRecord { InstallationId = Case, TenantId = Case, InstanceId = spec.InstanceId,
                        StepId = step0.StepId, OperationId = checkpoint, SpecificationHash = spec.SpecificationHash,
                        ResultingState = (int)WorkflowState.Running, ResultingVersion = instance.Version, RecordedAtUtc = spec.CreatedAtUtc.AddMinutes(1) });
                    await context.SaveChangesAsync(ct);
                }
                var step = await context.Set<WorkflowStepRecord>().SingleAsync(x => x.InstanceId == spec.InstanceId && x.Ordinal == 1, ct);
                var operation = Guid.NewGuid();
                await store.StageInitialCustodyAsync(identity, step.StepId, operation, spec.SpecificationHash, worker, boot, 1, spec.CreatedAtUtc.AddMinutes(2), ct);
                result = new(identity, step.StepId, operation, spec.SpecificationHash, completeEarlier ? 3 : 2, 2,
                    new(worker, boot, 1, 1), spec.CreatedAtUtc.AddMinutes(5));
                await transaction.CommitAsync(ct);
            });
            return result!;
        }
    }

    private static WorkflowTimerScheduleRequest Change(WorkflowTimerScheduleRequest value, WorkflowIdentity? identity = null,
        Guid? operation = null, string? hash = null, long? workflowVersion = null, WorkflowTimerCustodyExpectation? custody = null)
        => new(identity ?? value.Identity, value.StepId, operation ?? value.OperationId, hash ?? value.SpecificationHash,
            workflowVersion ?? value.ExpectedWorkflowVersion, value.ExpectedStepVersion, custody ?? value.Custody, value.RecordedAtUtc);
    private static string Root()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "Kordra.slnx"))) path = path.Parent;
        return path?.FullName ?? throw new InvalidOperationException("Product root missing.");
    }
    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? transaction = null)
    { await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 3 }; await command.ExecuteNonQueryAsync(ct); }
}

public sealed class WorkflowTimerDatabaseFactAttribute : FactAttribute
{
    public WorkflowTimerDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_TIMER_FIXTURE_MANIFEST")))
            Skip = "Requires separately approved exact T12.2d timer schema, source/SQL manifest and cleanup; no IO performed.";
    }
}
