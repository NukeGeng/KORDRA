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

public sealed class WorkflowWakeSuccessorDatabaseTests
{
    public static readonly Guid CaseId = Guid.Parse("e362ad0b-6647-4564-93a3-2ab78b3fc8e5");
    private static string Schema => "t12_" + CaseId.ToString("N");
    private const string ManifestVariable = "KORDRA_WFL_SUCCESSOR_MANIFEST";

    private static string CreationSql()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test");
        using var workflow = WorkflowDbContext.CreateCancellationFixture(connection, CaseId);
        using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId);
        using var host = Builder(connection.ConnectionString, new(CaseId, ""), false).Build();
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(host);
        var database = Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>());
        var nativeSql = WorkflowTransactionSchemaTests.StableNativeScript(database, Schema);
        using var receiver = Builder(connection.ConnectionString, new(CaseId, ""), true).Build();
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(receiver);
        Assert.Equal(nativeSql, WorkflowTransactionSchemaTests.StableNativeScript(
            Assert.IsAssignableFrom<IDatabase>(receiver.Services.GetRequiredService<IMessageStore>()), Schema));
        return "CREATE SCHEMA " + Schema + ";\n" + workflow.Database.GenerateCreateScript() +
            audit.Database.GenerateCreateScript() + OwnedMessagingSchema.TablesSql.Replace("msg.", Schema + ".", StringComparison.Ordinal) +
            nativeSql;
    }
    private static string CleanupSql => "DROP SCHEMA " + Schema + " CASCADE;\n";

    [Fact]
    public void ExportSuccessorSqlWithoutDatabaseOrHostStartup()
    {
        var sql = CreationSql();
        Assert.Equal(sql, CreationSql());
        Assert.Contains(Schema + ".cancellation_intents", sql);
        Assert.Contains(Schema + ".recovery_decisions", sql);
        Assert.DoesNotContain("CREATE ROLE", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_SUCCESSOR_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, value) in new[] { ("creation.sql", sql), ("cleanup.sql", CleanupSql) })
        {
            var file = Path.Combine(directory, name);
            if (File.Exists(file)) Assert.Equal(value, File.ReadAllText(file)); else File.WriteAllText(file, value);
        }
    }

    [SuccessorFact]
    public async Task ExpiredNativeWakeReconcilesAndStagesOneAtomicSuccessorWithoutReplay()
    {
        await RunScenarioAsync(receiverAbort: false);
        await RunScenarioAsync(receiverAbort: true);
    }

    private async Task RunScenarioAsync(bool receiverAbort)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Environment.GetEnvironmentVariable(ManifestVariable) ?? throw new InvalidOperationException("Exact successor fixture approval required.")));
        var plan = document.RootElement;
        var creation = CreationSql();
        if (plan.GetProperty("status").GetString() != "approved_for_execution" || plan.GetProperty("caseId").GetGuid() != CaseId ||
            plan.GetProperty("creationSha256").GetString() != Hash(creation) || plan.GetProperty("cleanupSha256").GetString() != Hash(CleanupSql))
            throw new InvalidOperationException("Exact successor fixture SQL required.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_SUCCESSOR_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private isolated successor locator required.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = budget.Token;
        locator = IsolatedWorkflowFixture.BoundedOwnerLocator(locator);
        var state = new WorkflowWakeFixtureState(CaseId, locator);
        await using var writer = state.NewOwnedConnection();
        await using var observer = state.NewOwnedConnection();
        await writer.OpenAsync(ct); await observer.OpenAsync(ct);
        await using var recovery = IsolatedWorkflowFixture.CancellationRecovery(observer, CaseId);
        Assert.Equal(0L, await NamespaceCount(ct));
        var producerStarted = false; var receiverStarted = false; var successorStarted = false; var created = false;
        using var producer = Builder(locator, state, false).Build();
        using var receiver = Builder(locator, state, true).Build();
        using var nextProducer = Builder(locator, state, false).Build();
        try
        {
            await using (var ddl = await writer.BeginTransactionAsync(ct))
            {
                await Sql(creation, ddl, ct); await ddl.CommitAsync(ct); created = true;
            }
            var (spec, timer) = await WorkflowWakeDatabaseTests.SeedWaitingTimer(writer, ct, CaseId);
            await using (var historyConnection = state.NewOwnedConnection())
            {
                await historyConnection.OpenAsync(ct);
                await using var historyRoot = await historyConnection.BeginTransactionAsync(ct);
                await using var history = IsolatedWorkflowFixture.CreateCancellationFixture(historyConnection, CaseId);
                await history.Database.UseTransactionAsync(historyRoot, ct);
                var duplicate = await new WorkflowStore(history).StageCreationAsync(spec, spec.DeadlineUtc.AddDays(90), ct);
                Assert.True(duplicate.Duplicate); Assert.Equal(1, duplicate.Version);
                var expired = new WorkflowSpecification(spec.InstallationId, spec.TenantId, Guid.NewGuid(), Guid.NewGuid(),
                    spec.ProjectId, spec.ApplicationId, spec.EnvironmentId, spec.TargetId, spec.ReleaseId, spec.ReleaseRevision,
                    spec.ReleaseHash, spec.Environment, spec.CreatedAtUtc, spec.DeadlineUtc, spec.DueAtUtc);
                Assert.Equal("RecoveryHorizonExceeded", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new WorkflowStore(history).StageCreationAsync(expired, spec.DeadlineUtc.AddDays(90), ct))).Message);
                Assert.False(history.ChangeTracker.HasChanges());
                await historyRoot.RollbackAsync(ct);
            }
            await using var custody = await IsolatedWorkflowFixture.Custody(locator, CaseId,
                WorkflowFixtureCustodyLease.LockKey(CaseId), ct);
            Assert.NotNull(custody);
            await producer.StartAsync(ct); producerStarted = true;
            var runtime = producer.Services.GetRequiredService<IWolverineRuntime>();
            var registry = WorkflowWakePublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseRequest());
            var now = DateTimeOffset.UtcNow;
            var previous = new WorkflowWakeOperation(spec, timer.StepId, timer.TimerId, 1, 1, 1,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 3, now, now.AddSeconds(5));
            var request = WorkflowWakePublication.Prepare(spec, previous, registry, now);
            var schedulerAudit = Audit(spec, now);
            var completionAudit = Audit(spec, now);
            var resultId = Guid.NewGuid();
            var result = WorkflowTransitionResultPublication.PrepareTimerDue(previous, 6, resultId,
                WorkflowTransitionResultPublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseResult()), previous.OccurredAtUtc);
            WorkflowTransactionOrigin origin;
            WorkflowTransactionOrigin? receiverOrigin = null;
            WorkflowWakeFixtureInvocation? receiverInvocation = null;
            if (receiverAbort)
            {
                receiverInvocation = new(spec, previous, completionAudit, Guid.NewGuid(), Guid.NewGuid(), resultId) {
                    BeforeCommitFault = (_, captured, _) => {
                        receiverOrigin = captured;
                        return Task.FromException(new InvalidOperationException("Synthetic precommit receiver abort."));
                    }
                };
                state.Add(receiverInvocation);
            }
            await using (var connection = state.NewOwnedConnection())
            {
                await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedCancellationTestAsync(connection, transaction,
                    CaseId, new MessageContext(runtime), state.Gate, ct);
                await root.StageInitialTimerDispatchAsync(spec, previous, registry, 4, 1, Guid.NewGuid(), Guid.NewGuid(), now, ct);
                await root.StageAuditAsync(schedulerAudit, ct); await root.StageClockFloorAsync(now, ct);
                origin = await root.CaptureTimerCommitOriginAsync(ct);
                Assert.Equal(WorkflowCommitDisposition.Committed, await root.CommitAsync(ct));
                await root.FlushConfirmedAsync();
            }
            await producer.StopAsync(ct); producerStarted = false;
            await using (var terminalConnection = state.NewOwnedConnection())
            {
                await terminalConnection.OpenAsync(ct);
                await using var terminalRoot = await terminalConnection.BeginTransactionAsync(ct);
                await using var context = IsolatedWorkflowFixture.CreateCancellationFixture(terminalConnection, CaseId);
                await context.Database.UseTransactionAsync(terminalRoot, ct);
                foreach (var terminal in new[] { WorkflowState.Failed, WorkflowState.Expired })
                    Assert.Contains("reconciliation", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        new WorkflowStore(context).StageTransitionAsync(new(CaseId, CaseId, spec.InstanceId),
                            previous.StepId, Guid.NewGuid(), spec.SpecificationHash, 5, terminal, now, ct))).Message);
                await terminalRoot.RollbackAsync(ct);
            }
            if (!receiverAbort)
                while (DateTimeOffset.UtcNow <= previous.DeadlineUtc) await Task.Delay(25, ct);
            await receiver.StartAsync(ct); receiverStarted = true;
            if (receiverInvocation is not null)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => receiverInvocation.Completion.Task.WaitAsync(ct));
                Assert.NotNull(receiverOrigin);
                Assert.Equal(1, receiverInvocation.HandlerCalls);
            }
            using (var deliveryBudget = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deliveryBudget.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    while (receiverAbort
                        ? (long)(await Scalar($"SELECT count(*) FROM {Schema}.wolverine_incoming_envelopes WHERE id=@id AND status='Handled'",
                            deliveryBudget.Token, ("id", previous.MessageId)))! != 1
                        : await Count("wolverine_dead_letters", deliveryBudget.Token) != 1)
                        await Task.Delay(25, deliveryBudget.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new InvalidOperationException("Native expiry did not reach DLQ within its delivery budget. " +
                        $"Pending queue={await Count("wolverine_queue_workflow_transitions_v1", ct)}, " +
                        $"inbox={await Count("wolverine_incoming_envelopes", ct)}, outbox={await Count("wolverine_outgoing_envelopes", ct)}.");
                }
            }
            await receiver.StopAsync(ct); receiverStarted = false;
            if (receiverOrigin is { } aborted)
            {
                var scope = new WorkflowReadScope(spec.TenantId, spec.ProjectId, spec.EnvironmentId, spec.InstanceId);
                var selection = new WorkflowReconciliationSelection(Guid.NewGuid(), previous.WakeOperationId,
                    previous.ExpectedWorkflowVersion, WorkflowReconciliationKind.ConfirmKnownRollback,
                    WorkflowReconciliationEvidence.ComputeHash(aborted, previous, request, schedulerAudit, result, completionAudit));
                Assert.True(await recovery.ObserveReconciliationSelectionAsync(selection, scope, custody, aborted,
                    spec, previous, request, schedulerAudit, result, completionAudit, ct));
                Assert.False(await recovery.ObserveReconciliationSelectionAsync(selection, scope with { TenantId = Guid.NewGuid() },
                    custody, aborted, spec, previous, request, schedulerAudit, result, completionAudit, ct));
                Assert.False(await recovery.ObserveReconciliationSelectionAsync(selection with { EvidenceHash = new string('0', 64) },
                    scope, custody, aborted, spec, previous, request, schedulerAudit, result, completionAudit, ct));
                Assert.False(await recovery.ObserveReconciliationSelectionAsync(selection with
                {
                    Kind = WorkflowReconciliationKind.BindMatchingCommittedReceipt,
                    ExpectedVersion = previous.ExpectedWorkflowVersion + 1
                }, scope, custody, aborted, spec, previous, request, schedulerAudit, result, completionAudit, ct));
            }
            while (DateTimeOffset.UtcNow <= previous.DeadlineUtc) await Task.Delay(25, ct);
            await using (var journalRoot = await writer.BeginTransactionAsync(ct))
            {
                await IsolatedWorkflowFixture.Journal(writer, journalRoot, CaseId).StageQuarantineAsync(
                    previous.MessageId, CaseId, MessageQuarantineReason.Expired, request.PayloadHash, DateTimeOffset.UtcNow, ct);
                await journalRoot.CommitAsync(ct);
            }
            await nextProducer.StartAsync(ct); successorStarted = true;
            var messages = new MessageContext(nextProducer.Services.GetRequiredService<IWolverineRuntime>());
            var evidence = await recovery.ObserveSuccessorEvidenceAsync(custody, receiverOrigin ?? origin, spec, previous, request,
                schedulerAudit, result, completionAudit, registry, messages, DateTimeOffset.UtcNow, ct);
            Assert.NotNull(evidence);
            var at = DateTimeOffset.UtcNow;
            var successor = new WorkflowWakeOperation(spec, previous.StepId, previous.TimerId, 2, 2, 2,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 6, 3, at, at.AddSeconds(120));
            var worker = Guid.NewGuid(); var boot = Guid.NewGuid(); var decision = Guid.NewGuid();
            foreach (var failure in new[] { "stale-timer", "cancellation" })
            {
                await using var connection = state.NewOwnedConnection(); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                if (failure == "cancellation")
                {
                    await using var cancelling = IsolatedWorkflowFixture.CreateCancellationFixture(connection, CaseId);
                    await cancelling.Database.UseTransactionAsync(transaction, ct);
                    await new WorkflowCancellationStore(cancelling).StageIntentAsync(new(
                        CaseId, CaseId, spec.InstanceId, Guid.NewGuid(), Guid.NewGuid(), 5, spec.SpecificationHash), at, ct);
                }
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedCancellationTestAsync(connection, transaction, CaseId,
                    new MessageContext(nextProducer.Services.GetRequiredService<IWolverineRuntime>()), state.Gate, ct);
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.StageTimerSuccessorAsync(
                    evidence, successor, failure == "stale-timer" ? 3 : 2, worker, boot, decision, at, ct));
                Assert.Equal(1L, await Count("wake_operations", ct));
                Assert.Equal(0L, await Count("recovery_decisions", ct));
            }
            foreach (var commit in new[] { false, true })
            {
                await using var connection = state.NewOwnedConnection(); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedCancellationTestAsync(connection, transaction, CaseId,
                    new MessageContext(nextProducer.Services.GetRequiredService<IWolverineRuntime>()), state.Gate, ct);
                var staged = await root.StageTimerSuccessorAsync(evidence, successor, 2, worker, boot, decision, at, ct);
                Assert.False(staged.Duplicate);
                Assert.Equal(1L, await Count("wake_operations", ct));
                Assert.Equal(0L, await Count("recovery_decisions", ct));
                await root.StageAuditAsync(Audit(spec, at), ct); await root.StageClockFloorAsync(at, ct);
                if (commit)
                {
                    Assert.Equal(WorkflowCommitDisposition.Committed, await root.CommitAsync(ct));
                    await root.FlushConfirmedAsync();
                }
                else await root.RollbackAsync();
                Assert.Equal(commit ? 2L : 1L, await Count("wake_operations", ct));
            }
            await using (var connection = state.NewOwnedConnection())
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedCancellationTestAsync(connection, transaction,
                    CaseId, new MessageContext(nextProducer.Services.GetRequiredService<IWolverineRuntime>()), state.Gate, ct);
                Assert.True((await root.StageTimerSuccessorAsync(evidence, successor, 2, worker, boot, decision, at.AddSeconds(1), ct)).Duplicate);
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
            }
            Assert.Equal(1L, await Count("recovery_decisions", ct));
            Assert.Equal(0L, await Count("logical_receipts", ct));
            Assert.Equal(2L, await Count("publication_receipts", ct));
            Assert.Equal(0L, await Scalar($"SELECT count(*) FROM {Schema}.transitions WHERE operation_id=@id", ct, ("id", previous.WakeOperationId)));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                if (producerStarted) await producer.StopAsync(cleanup.Token);
                if (receiverStarted) await receiver.StopAsync(cleanup.Token);
                if (successorStarted) await nextProducer.StopAsync(cleanup.Token);
            }
            finally
            {
                if (created) await Sql(CleanupSql, null, cleanup.Token);
                Assert.Equal(0L, await NamespaceCount(cleanup.Token));
            }
        }
        async Task Sql(string sql, NpgsqlTransaction? transaction, CancellationToken token)
        {
            await using var command = new NpgsqlCommand(sql, writer, transaction) { CommandTimeout = 5 };
            await command.ExecuteNonQueryAsync(token);
        }
        async Task<object?> Scalar(string sql, CancellationToken token, params (string Name, object Value)[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, observer) { CommandTimeout = 3 };
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return await command.ExecuteScalarAsync(token);
        }
        Task<object?> NamespaceCount(CancellationToken token)
            => Scalar("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", token, ("schema", Schema));
        async Task<long> Count(string table, CancellationToken token)
            => (long)(await Scalar($"SELECT count(*) FROM {Schema}.{table}", token))!;
    }

    private static IHostBuilder Builder(string locator, WorkflowWakeFixtureState state, bool listen)
        => WorkflowWakeFixtureProfile.Builder(locator, Schema, state, listen, guardedIngress: true)
            .ConfigureLogging(logging => logging.ClearProviders());
    private static AuditIntent Audit(WorkflowSpecification specification, DateTimeOffset at) => new(
        1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation, Guid.NewGuid(), specification.TenantId,
        specification.InstanceId, "workflow.reconcile", "environment", specification.EnvironmentId,
        1, 1, "synthetic_fixture", at, specification.RequestId);
    private static string Hash(string sql) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
    public sealed class SuccessorFactAttribute : FactAttribute
    {
        public SuccessorFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ManifestVariable)))
                Skip = "Requires exact native successor fixture operation approval.";
        }
    }
}
