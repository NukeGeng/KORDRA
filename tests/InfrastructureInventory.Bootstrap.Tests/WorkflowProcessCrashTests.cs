using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core.Migrations;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowProcessCrashTests
{
    private const string ManifestVariable = "KORDRA_WFL_CRASH_MANIFEST";
    private const string LocatorVariable = "KORDRA_WFL_CRASH_CONNECTION_STRING";
    private const string Ready = "WFL_CRASH_BOUNDARY_READY";
    public static readonly IReadOnlyDictionary<string, Guid> Cases = new Dictionary<string, Guid> {
        ["producer-before-commit"] = Guid.Parse("5c01a417-4389-457d-bd45-e4415f786c71"),
        ["producer-after-commit"] = Guid.Parse("bc549c84-f923-468c-97cf-457c740b934f"),
        ["consumer-before-effect"] = Guid.Parse("da63c925-c53a-4cf7-a96e-c84086bceea8"),
        ["consumer-after-commit"] = Guid.Parse("74a3bdc9-7b81-4bf4-b221-bf5e89eb1f7b")
    };
    private sealed record Data(Guid CaseId, string Phase, byte[] Specification, byte[] Wake,
        AuditIntent SchedulerAudit, AuditIntent CompletionAudit, Guid ResultId);
    private static string Schema(Guid id) => "t12_" + id.ToString("N");
    private static string Application(Guid id) => "kordra-wfl-crash-" + id.ToString("N");

    private static string CreationSql(Guid id)
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test");
        using var workflow = WorkflowDbContext.CreateCancellationFixture(connection, id);
        using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, id);
        using var host = Builder(connection.ConnectionString, id, new(id, ""), false).Build();
        var durability = host.Services.GetRequiredService<IWolverineRuntime>().Options.Durability;
        Assert.InRange(durability.ScheduledJobFirstExecution, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Assert.InRange(durability.ScheduledJobPollingTime, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5));
        Assert.InRange(durability.OrphanedMessageSweepPollingTime, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5));
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(host);
        return "CREATE SCHEMA " + Schema(id) + ";\n" + workflow.Database.GenerateCreateScript() +
            audit.Database.GenerateCreateScript() + OwnedMessagingSchema.TablesSql.Replace("msg.", Schema(id) + ".", StringComparison.Ordinal) +
            WorkflowTransactionSchemaTests.StableNativeScript(
                Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>()), Schema(id));
    }
    private static string CleanupSql(Guid id) => "DROP SCHEMA " + Schema(id) + " CASCADE;\n";

    [Fact]
    public void ExportFourProcessBoundarySchemasWithoutStartingHostsOrOpeningConnections()
    {
        foreach (var (phase, id) in Cases)
        {
            var sql = CreationSql(id);
            Assert.Equal(sql, CreationSql(id));
            Assert.Contains(Schema(id) + ".cancellation_intents", sql);
            if (Environment.GetEnvironmentVariable("KORDRA_WFL_CRASH_EXPORT") is not { } directory) continue;
            Directory.CreateDirectory(directory);
            foreach (var (name, content) in new[] { (phase + ".sql", sql), (phase + "-cleanup.sql", CleanupSql(id)) })
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path)) Assert.Equal(content, File.ReadAllText(path)); else File.WriteAllText(path, content);
            }
        }
    }

    [CrashFixtureFact]
    public async Task FourActualProcessCrashesPreserveAtomicIntentAndRecoverOneLogicalCompletion()
    {
        using var plan = ReadPlan();
        var locator = Environment.GetEnvironmentVariable(LocatorVariable) ?? throw new InvalidOperationException("Private crash fixture locator required.");
        locator = IsolatedWorkflowFixture.BoundedOwnerLocator(locator);
        foreach (var (phase, id) in Cases)
        {
            VerifySql(plan.RootElement, phase, id);
            var state = new WorkflowWakeFixtureState(id, locator);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var ct = budget.Token;
            await using var setup = state.NewOwnedConnection(); await setup.OpenAsync(ct);
            Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", ct, ("schema", Schema(id))));
            var created = false;
            Process? child = null;
            Task<string>? stderr = null;
            try
            {
                await using (var ddl = await setup.BeginTransactionAsync(ct))
                {
                    await using var command = new NpgsqlCommand(CreationSql(id), setup, ddl);
                    await command.ExecuteNonQueryAsync(ct); await ddl.CommitAsync(ct); created = true;
                }
                var (spec, timer) = await WorkflowWakeDatabaseTests.SeedWaitingTimer(setup, ct, id);
                var now = DateTimeOffset.UtcNow;
                var wake = new WorkflowWakeOperation(spec, timer.StepId, timer.TimerId, 1, 1, 1,
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 3, now, now.AddSeconds(120));
                var data = new Data(id, phase, spec.WriteCanonical(), wake.WriteCanonical(), Audit(spec, now), Audit(spec, now), Guid.NewGuid());
                var path = CasePath(plan.RootElement, phase);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(data);
                await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await file.WriteAsync(bytes, ct);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                if (phase.StartsWith("consumer-", StringComparison.Ordinal))
                {
                    using var producer = Builder(locator, id, state, false).Build();
                    await producer.StartAsync(ct);
                    try { await ProduceAsync(data, state, producer.Services.GetRequiredService<IWolverineRuntime>(), false, ct); }
                    finally { await producer.StopAsync(ct); }
                }
                var start = new ProcessStartInfo("dotnet") {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
                };
                start.ArgumentList.Add(typeof(WorkflowFixtureProgram).Assembly.Location);
                start.ArgumentList.Add("wake-crash-fixture"); start.ArgumentList.Add(phase);
                start.Environment["KORDRA_WFL_CRASH_CASE_SHA256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
                child = Process.Start(start) ?? throw new InvalidOperationException("Owned crash child failed to start.");
                stderr = child.StandardError.ReadToEndAsync();
                using (var readyBudget = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    readyBudget.CancelAfter(TimeSpan.FromSeconds(20));
                    Assert.Equal(Ready, await child.StandardOutput.ReadLineAsync(readyBudget.Token));
                }
                child.Kill(); await child.WaitForExitAsync(ct);
                Assert.NotEqual(0, child.ExitCode);
                while ((long)(await Scalar(setup,
                    "SELECT count(*) FROM pg_stat_activity WHERE application_name=@application", ct, ("application", Application(id))))! != 0)
                    await Task.Delay(25, ct);
                var expectedVersion = phase == "producer-before-commit" ? 4L : phase == "consumer-after-commit" ? 6L : 5L;
                Assert.Equal(expectedVersion, await Scalar(setup,
                    $"SELECT version FROM {Schema(id)}.instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
                var logical = phase == "consumer-after-commit" ? 1L : 0L;
                Assert.Equal(logical, await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.logical_receipts", ct));
                Assert.Equal(logical, await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.aud_intents WHERE intent_id=@id", ct,
                    ("id", data.CompletionAudit.IntentId)));
                if (phase != "producer-before-commit")
                {
                    await using var custody = await IsolatedWorkflowFixture.Custody(
                        locator, id, WorkflowFixtureCustodyLease.LockKey(id), ct);
                    Assert.NotNull(custody);
                    var invocation = Invocation(data);
                    state.Add(invocation);
                    using var recovered = Builder(locator, id, state, true).Build();
                    await recovered.StartAsync(ct);
                    try
                    {
                        try
                        {
                            while ((long)(await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.logical_receipts", ct))! != 1 ||
                                (long)(await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.wolverine_queue_workflow_results_v1", ct))! != 1)
                                await Task.Delay(50, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            using var diagnostic = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            var logicalCount = await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.logical_receipts", diagnostic.Token);
                            var resultCount = await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.wolverine_queue_workflow_results_v1", diagnostic.Token);
                            var outgoingCount = await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.wolverine_outgoing_envelopes", diagnostic.Token);
                            throw new InvalidOperationException(
                                $"Crash recovery budget expired for {phase}: logical={logicalCount}, results={resultCount}, outgoing={outgoingCount}.");
                        }
                    }
                    finally { await recovered.StopAsync(ct); }
                    Assert.Equal(phase == "consumer-after-commit" ? 0 : 1, invocation.HandlerCalls);
                    Assert.Equal(6L, await Scalar(setup,
                        $"SELECT version FROM {Schema(id)}.instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
                    Assert.Equal(2L, await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.publication_receipts", ct));
                    Assert.Equal(1L, await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.transitions WHERE operation_id=@id", ct,
                        ("id", wake.WakeOperationId)));
                    Assert.Equal(1L, await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.aud_intents WHERE intent_id=@id", ct,
                        ("id", data.CompletionAudit.IntentId)));
                }
                else
                {
                    Assert.Equal(0L, await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.publication_receipts", ct));
                    Assert.Equal(0L, await Scalar(setup, $"SELECT count(*) FROM {Schema(id)}.aud_intents WHERE intent_id=@id", ct,
                        ("id", data.SchedulerAudit.IntentId)));
                }
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    if (child is not null)
                    {
                        if (!child.HasExited) child.Kill();
                        await child.WaitForExitAsync(cleanup.Token);
                        if (stderr is not null) _ = await stderr.WaitAsync(cleanup.Token);
                        child.Dispose();
                    }
                }
                finally
                {
                    if (created)
                    {
                        await using var drop = new NpgsqlCommand(CleanupSql(id), setup);
                        await drop.ExecuteNonQueryAsync(cleanup.Token);
                    }
                    Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema",
                        cleanup.Token, ("schema", Schema(id))));
                }
            }
        }
    }

    internal static async Task<int> RunChildAsync(string phase)
    {
        if (!Cases.TryGetValue(phase, out var id)) throw new InvalidOperationException("Fixed process crash phase required.");
        using var plan = ReadPlan(); VerifySql(plan.RootElement, phase, id);
        var bytes = await File.ReadAllBytesAsync(CasePath(plan.RootElement, phase));
        if (bytes.Length > 128 * 1024 || Convert.ToHexStringLower(SHA256.HashData(bytes)) !=
            Environment.GetEnvironmentVariable("KORDRA_WFL_CRASH_CASE_SHA256"))
            throw new InvalidOperationException("Owned synthetic crash case hash differs.");
        var data = JsonSerializer.Deserialize<Data>(bytes) ?? throw new InvalidOperationException("Crash case missing.");
        if (data.CaseId != id || data.Phase != phase) throw new InvalidOperationException("Wrong crash case binding.");
        var settings = new NpgsqlConnectionStringBuilder(IsolatedWorkflowFixture.BoundedOwnerLocator(
            Environment.GetEnvironmentVariable(LocatorVariable) ?? throw new InvalidOperationException("Private crash locator required."), Application(id)));
        var state = new WorkflowWakeFixtureState(id, settings.ConnectionString);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await using var custody = await IsolatedWorkflowFixture.Custody(settings.ConnectionString, id,
            WorkflowFixtureCustodyLease.LockKey(id), ct);
        if (custody is null) throw new InvalidOperationException("Crash child custody unavailable.");
        var consumer = phase.StartsWith("consumer-", StringComparison.Ordinal);
        if (consumer)
        {
            var spec = WorkflowSpecification.ReadCanonical(data.Specification);
            var wake = WorkflowWakeOperation.ReadCanonical(data.Wake, spec);
            state.Add(new(spec, wake, data.CompletionAudit, Guid.NewGuid(), Guid.NewGuid(), data.ResultId) {
                BeforeOwnerStage = phase == "consumer-before-effect" ? WaitForKill : null,
                AfterCommitBeforeFlush = phase == "consumer-after-commit" ? WaitForKill : null
            });
        }
        using var host = Builder(settings.ConnectionString, id, state, consumer).Build();
        await host.StartAsync(ct);
        try
        {
            if (consumer) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            else await ProduceAsync(data, state, host.Services.GetRequiredService<IWolverineRuntime>(), true, ct);
        }
        finally { await host.StopAsync(); }
        return 0;
    }

    private static async Task ProduceAsync(Data data, WorkflowWakeFixtureState state, IWolverineRuntime runtime,
        bool crashBoundary, CancellationToken ct)
    {
        var spec = WorkflowSpecification.ReadCanonical(data.Specification);
        var wake = WorkflowWakeOperation.ReadCanonical(data.Wake, spec);
        await using var connection = state.NewOwnedConnection(); await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var root = await IsolatedWorkflowFixture.CreateIsolatedCancellationTestAsync(connection, transaction,
            data.CaseId, new MessageContext(runtime), state.Gate, ct);
        await root.StageInitialTimerDispatchAsync(spec, wake,
            WorkflowWakePublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseRequest()),
            4, 1, Guid.NewGuid(), Guid.NewGuid(), wake.OccurredAtUtc, ct);
        await root.StageAuditAsync(data.SchedulerAudit, ct); await root.StageClockFloorAsync(wake.OccurredAtUtc, ct);
        if (crashBoundary && data.Phase == "producer-before-commit") await WaitForKill(ct);
        Assert.Equal(WorkflowCommitDisposition.Committed, await root.CommitAsync(ct));
        if (crashBoundary) await WaitForKill(ct);
        await root.FlushConfirmedAsync();
    }
    private static async Task WaitForKill(CancellationToken ct)
    {
        await Console.Out.WriteLineAsync(Ready); await Console.Out.FlushAsync(ct);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }
    private static WorkflowWakeFixtureInvocation Invocation(Data data)
    {
        var spec = WorkflowSpecification.ReadCanonical(data.Specification);
        return new(spec, WorkflowWakeOperation.ReadCanonical(data.Wake, spec), data.CompletionAudit,
            Guid.NewGuid(), Guid.NewGuid(), data.ResultId);
    }
    private static IHostBuilder Builder(string locator, Guid id, WorkflowWakeFixtureState state, bool listen)
        => WorkflowWakeFixtureProfile.Builder(locator, Schema(id), state, listen, processRecovery: true)
            .ConfigureLogging(logging => logging.ClearProviders());
    private static AuditIntent Audit(WorkflowSpecification spec, DateTimeOffset at) => new(1, Guid.NewGuid(), Guid.NewGuid(),
        AuditIntentStage.Mutation, Guid.NewGuid(), spec.TenantId, spec.InstanceId, "workflow.reconcile", "environment",
        spec.EnvironmentId, 1, 1, "synthetic_fixture", at, spec.RequestId);
    private static JsonDocument ReadPlan()
    {
        var document = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable(ManifestVariable)
            ?? throw new InvalidOperationException("Exact crash fixture approval required.")));
        if (document.RootElement.GetProperty("status").GetString() == "approved_for_execution") return document;
        document.Dispose(); throw new InvalidOperationException("Crash fixture permission missing.");
    }
    private static string CasePath(JsonElement plan, string phase)
    {
        var directory = Path.GetFullPath(plan.GetProperty("caseDirectory").GetString()!);
        var expected = Path.Combine(Root(), ".amr-local", "t12-remaining-design", "t12-2d-crash-cases");
        if (Path.GetDirectoryName(directory) != expected ||
            !Guid.TryParseExact(Path.GetFileName(directory), "N", out var attempt) || attempt == Guid.Empty ||
            !Cases.ContainsKey(phase))
            throw new InvalidOperationException("Exact crash case directory required.");
        return Path.Combine(directory, phase + ".json");
    }
    private static void VerifySql(JsonElement plan, string phase, Guid id)
    {
        var reviewed = plan.GetProperty("cases").GetProperty(phase);
        if (reviewed.GetProperty("caseId").GetGuid() != id ||
            reviewed.GetProperty("creationSha256").GetString() != Hash(CreationSql(id)) ||
            reviewed.GetProperty("cleanupSha256").GetString() != Hash(CleanupSql(id)))
            throw new InvalidOperationException("Crash fixture SQL differs from the approved manifest.");
    }
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kordra.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Product root required.");
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql, CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 3 };
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync(ct);
    }
    public sealed class CrashFixtureFactAttribute : FactAttribute
    {
        public CrashFixtureFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ManifestVariable)))
                Skip = "Requires exact four-schema OS process crash fixture approval.";
        }
    }
}
