using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Messaging.Tests;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Weasel.Core.Migrations;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.RDBMS;
using Wolverine.Runtime;
using Wolverine.Runtime.Serialization;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

[Collection(LocalHostEnvironmentCollection.Name)]
public sealed class WorkflowReconciliationRollbackDatabaseTests
{
    public static readonly Guid CaseId = WorkflowOperatorAuthorityFixture<RolledBackTimerAuthorityCase>.CaseId;
    private static string Schema => "t12_" + CaseId.ToString("N");
    private static string DirectoryPath => Path.Combine(Root(), ".amr-local", "t12-remaining-design", "t12-2e-reconcile-rollback-schema-review");

    [Fact]
    public void ExportReceiveFaultSqlMetadataWithoutStartingHostsOrOpeningConnections()
    {
        const string locator = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC";
        using var connection = new NpgsqlConnection(locator);
        using var workflow = WorkflowDbContext.CreateReconciliationFixture(connection, CaseId);
        using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId);
        using var iam = WorkflowOperatorAuthorityFixture<RolledBackTimerAuthorityCase>.Identity(connection);
        using var ten = WorkflowOperatorAuthorityFixture<RolledBackTimerAuthorityCase>.Tenants(connection);
        using var prj = WorkflowOperatorAuthorityFixture<RolledBackTimerAuthorityCase>.Projects(connection);
        using var host = WorkflowWakeFixtureProfile.Builder("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1",
            Schema, new(CaseId, ""), false).Build();
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(host);
        var native = Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>());
        var nativeSql = WorkflowTransactionSchemaTests.StableNativeScript(native, Schema);
        using var listener = WorkflowWakeFixtureProfile.Builder("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1",
            Schema, new(CaseId, ""), true).Build();
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(listener);
        Assert.Equal(nativeSql, WorkflowTransactionSchemaTests.StableNativeScript(
            Assert.IsAssignableFrom<IDatabase>(listener.Services.GetRequiredService<IMessageStore>()), Schema));
        var scripts = new Dictionary<string, string> {
            ["create.sql"] = "CREATE SCHEMA " + Schema + ";\n", ["wfl.sql"] = workflow.Database.GenerateCreateScript(), ["aud.sql"] = audit.Database.GenerateCreateScript(),
            ["iam.sql"] = iam.Database.GenerateCreateScript(), ["ten.sql"] = ten.Database.GenerateCreateScript(), ["prj.sql"] = prj.Database.GenerateCreateScript(),
            ["msg-owned.sql"] = OwnedMessagingSchema.TablesSql.Replace("msg.", Schema + ".", StringComparison.Ordinal),
            ["msg-native.sql"] = nativeSql, ["cleanup.sql"] = "DROP SCHEMA " + Schema + " CASCADE;\n"
        };
        var principal = WorkflowOperatorAuthorityFixture<RolledBackTimerAuthorityCase>.Principal(Guid.NewGuid(), "synthetic-offline-required-fields");
        foreach (var field in iam.Model.FindEntityType(typeof(Kordra.Modules.IdentityAccess.Infrastructure.KordraIdentityUser))!.GetProperties().Where(x => !x.IsNullable))
        {
            var value = field.PropertyInfo!.GetValue(principal);
            Assert.True(value is not null, "Missing synthetic principal field: " + field.Name);
            if (value is string text && field.GetMaxLength() is { } limit) Assert.InRange(text.Length, 1, limit);
        }
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        foreach (var sql in scripts.Values) { Assert.Contains(Schema, sql); Assert.DoesNotContain("wolverine_queues", sql); }
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_RECONCILE_ROLLBACK_ARTIFACT_DIRECTORY") is not { } directory) return;
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

    [WorkflowReconciliationRollbackDatabaseFact]
    public async Task OperatorRecordsKnownReceiverRollbackOnlyAfterBackendTerminationWithoutReplay()
    {
        var scripts = ReadApprovedScripts();
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_RECONCILE_ROLLBACK_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved private rollback-origin locator required.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var ct = timeout.Token;
        var state = new WorkflowWakeFixtureState(CaseId, locator);
        await using var setup = state.NewOwnedConnection(); await setup.OpenAsync(ct);
        Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", ct, ("schema", Schema)));
        using var producer = WorkflowWakeFixtureProfile.Builder(locator, Schema, state, false).Build();
        using var receiver = WorkflowWakeFixtureProfile.Builder(locator, Schema, state, true).Build();
        var created = false; var producerStarted = false; var receiverStarted = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                await Execute(setup, scripts["create.sql"], ct, ddl);
                foreach (var name in new[] { "wfl.sql", "aud.sql", "iam.sql", "ten.sql", "prj.sql", "msg-owned.sql", "msg-native.sql" })
                    await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            var (spec, timer) = await WorkflowWakeDatabaseTests.SeedWaitingTimer(setup, ct, CaseId);
            var now = DateTimeOffset.UtcNow;
            var wake = new WorkflowWakeOperation(spec, timer.StepId, timer.TimerId, 1, 1, 1,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 3, now, now.AddSeconds(120));
            var schedulerAudit = Audit(spec, now); var completionAudit = Audit(spec, now);
            var requests = WorkflowWakePublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseRequest());
            var input = WorkflowWakePublication.Prepare(spec, wake, requests, now);
            var results = WorkflowTransitionResultPublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseResult());
            var resultId = Guid.NewGuid();
            var expectedOutput = WorkflowTransitionResultPublication.PrepareTimerDue(wake, 6, resultId, results, now);
            WorkflowTransactionOrigin? origin = null;
            var commitOutcomes = 0;
            await using var recovery = IsolatedWorkflowFixture.CancellationRecovery(setup, CaseId);
            var invocation = new WorkflowWakeFixtureInvocation(spec, wake, completionAudit, Guid.NewGuid(), Guid.NewGuid(), resultId)
            {
                CommitOutcomeObserved = _ => Interlocked.Increment(ref commitOutcomes),
                BeforeCommitFault = async (_, captured, token) =>
                {
                    origin = captured;
                    Assert.Equal(WorkflowTransactionProbeStatus.Unresolved,
                        (await IsolatedWorkflowFixture.Probe(captured, setup, token)).Status);
                    Assert.False(await recovery.ObserveAbortedCompletionAsync(captured, spec, wake, input,
                        schedulerAudit, expectedOutput, completionAudit, token));
                    // Test-owned failure before the actual COMMIT path; disposal must roll back all staged effects.
                    throw new InvalidOperationException("Synthetic receiver failure before commit.");
                }
            };
            state.Add(invocation);
            producerStarted = true; await producer.StartAsync(ct);
            await using (var connection = state.NewOwnedConnection())
            {
                await connection.OpenAsync(ct); await using var rootTransaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, rootTransaction,
                    CaseId, new MessageContext(producer.Services.GetRequiredService<IWolverineRuntime>()), state.Gate, ct);
                await root.StageInitialTimerDispatchAsync(spec, wake, requests, 4, 1, Guid.NewGuid(), Guid.NewGuid(), now, ct);
                await root.StageAuditAsync(schedulerAudit, ct); await root.StageClockFloorAsync(now, ct);
                Assert.Equal(WorkflowCommitDisposition.Committed, await root.CommitAsync(ct)); await root.FlushConfirmedAsync();
            }
            await producer.StopAsync(ct); producerStarted = false;
            receiverStarted = true; await receiver.StartAsync(ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.Completion.Task.WaitAsync(ct));
            await receiver.StopAsync(ct); receiverStarted = false;
            Assert.NotNull(origin); Assert.Equal(0, commitOutcomes); Assert.Equal(1, invocation.HandlerCalls);
            using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                bounded.CancelAfter(TimeSpan.FromSeconds(2));
                while (true)
                {
                    var observation = await IsolatedWorkflowFixture.Probe(origin, setup, bounded.Token);
                    Assert.Equal(WorkflowTransactionProbeStatus.Aborted, observation.Status);
                    if (observation.OriginalBackendPresent == false) break;
                    await Task.Delay(20, bounded.Token);
                }
            }
            Assert.True(await recovery.ObserveAbortedCompletionAsync(origin, spec, wake, input, schedulerAudit,
                expectedOutput, completionAudit, ct));
            await AssertOriginalEffectsAbsent(5);
            var publications = await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts", ct);
            var adminGate = new FairAsyncReaderWriterAdmissionGate();
            var currentOperator = await WorkflowOperatorAuthorityFixture<RolledBackTimerAuthorityCase>.SeedAsync(setup, locator, spec, adminGate, ct);
            var scope = new WorkflowReadScope(CaseId, spec.ProjectId, spec.EnvironmentId, spec.InstanceId);
            await using var custody = await IsolatedWorkflowFixture.Custody(locator, CaseId,
                WorkflowFixtureCustodyLease.LockKey(CaseId), ct);
            Assert.NotNull(custody);
            var selected = new WorkflowReconciliationSelection(Guid.NewGuid(), wake.WakeOperationId, 5,
                WorkflowReconciliationKind.ConfirmKnownRollback,
                WorkflowReconciliationEvidence.ComputeHash(origin, wake, input, schedulerAudit, expectedOutput, completionAudit));
            var source = new WorkflowReconciliationSource(spec, wake, origin, input, schedulerAudit, expectedOutput, completionAudit);
            await using var writer = state.NewOwnedConnection(); await writer.OpenAsync(ct);
            foreach (var fault in new[] { "evidence", "result", "commit-claim" })
            {
                var changedSource = source;
                var changed = fault == "evidence" ? selected with { EvidenceHash = new string('e', 64) }
                    : fault == "commit-claim" ? selected with { Kind = WorkflowReconciliationKind.BindMatchingCommittedReceipt, ExpectedVersion = 6 }
                    : selected;
                if (fault == "result")
                {
                    var substituted = WorkflowTransitionResultPublication.PrepareTimerDue(wake, 6, Guid.NewGuid(), results, now);
                    changedSource = source with { Result = substituted };
                    changed = selected with { EvidenceHash = WorkflowReconciliationEvidence.ComputeHash(origin, wake, input, schedulerAudit, substituted, completionAudit) };
                }
                await Assert.ThrowsAsync<InvalidOperationException>(() => Reconcile(changed, changedSource));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".operator_decisions", ct));
                await AssertOriginalEffectsAbsent(5);
            }
            var committed = await Reconcile(selected, source);
            Assert.Equal(WorkflowCommitDisposition.Committed, committed.Disposition);
            Assert.NotNull(committed.Receipt); Assert.False(committed.Receipt.Duplicate); Assert.Equal(6L, committed.Receipt.Version);
            var duplicate = await Reconcile(selected, source);
            Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, duplicate.Disposition);
            Assert.NotNull(duplicate.Receipt); Assert.True(duplicate.Receipt.Duplicate); Assert.Equal(6L, duplicate.Receipt.Version);
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".operator_decisions", ct));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE permission='workflow.reconcile'", ct));
            await AssertOriginalEffectsAbsent(6);
            Assert.Equal(publications, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts", ct));
            Assert.Equal(1, invocation.HandlerCalls); Assert.False(state.Gate.IsClosed); Assert.False(adminGate.IsClosed);
            await currentOperator.RevokeSessionAsync(setup, ct);
            var denied = await Reconcile(selected, source);
            Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, denied.Disposition); Assert.Null(denied.Receipt);
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE permission='workflow.reconcile'", ct));

            async Task AssertOriginalEffectsAbsent(long version)
            {
                Assert.Equal(version, await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".transitions WHERE operation_id=@id", ct, ("id", wake.WakeOperationId)));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE intent_id=@id", ct, ("id", completionAudit.IntentId)));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".logical_receipts WHERE operation_id=@id", ct, ("id", wake.WakeOperationId)));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts WHERE operation_id=@id AND type_id=@type", ct,
                    ("id", wake.WakeOperationId), ("type", WorkflowTransitionResultPublication.TypeId)));
            }
            async Task<WorkflowReconciliationTransactionResult> Reconcile(WorkflowReconciliationSelection selection, WorkflowReconciliationSource supplied)
            {
                await using var root = await writer.BeginTransactionAsync(ct);
                return await IsolatedWorkflowFixture.ReconcileAdmitted(writer, root, CaseId,
                    selection, scope, currentOperator.Session, currentOperator.Commands, custody, recovery, supplied, adminGate, ct);
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (producerStarted) await producer.StopAsync(cleanup.Token);
            if (receiverStarted) await receiver.StopAsync(cleanup.Token);
            if (created)
            {
                await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", cleanup.Token, ("schema", Schema)));
                await using var released = await IsolatedWorkflowFixture.Custody(locator, CaseId,
                    WorkflowFixtureCustodyLease.LockKey(CaseId), cleanup.Token);
                Assert.NotNull(released);
            }
        }
    }

    private static Dictionary<string, string> ReadApprovedScripts()
    {
        var file = Environment.GetEnvironmentVariable("KORDRA_WFL_RECONCILE_ROLLBACK_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact receive-fault fixture approval required.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(file)); var root = manifest.RootElement;
        Assert.Equal("approved_for_execution", root.GetProperty("status").GetString()); Assert.Equal(CaseId, root.GetProperty("caseId").GetGuid());
        Assert.Equal(Schema, root.GetProperty("schema").GetString());
        var scripts = new Dictionary<string, string>();
        foreach (var name in new[] { "create.sql", "wfl.sql", "aud.sql", "iam.sql", "ten.sql", "prj.sql", "msg-owned.sql", "msg-native.sql", "cleanup.sql" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, name)); Assert.Equal(root.GetProperty("sha256").GetProperty(name).GetString(), Hash(bytes));
            scripts.Add(name, Encoding.UTF8.GetString(bytes));
        }
        Assert.Equal("DROP SCHEMA " + Schema + " CASCADE;\n", scripts["cleanup.sql"]); return scripts;
    }
    private static AuditIntent Audit(WorkflowSpecification spec, DateTimeOffset now) => new(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation,
        Guid.NewGuid(), CaseId, spec.InstanceId, "workflow.start", "environment", spec.EnvironmentId, 1, 1, "synthetic_fixture", now, spec.RequestId);
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

public sealed class WorkflowReconciliationRollbackDatabaseFactAttribute : FactAttribute
{
    public WorkflowReconciliationRollbackDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_RECONCILE_ROLLBACK_FIXTURE_MANIFEST")))
            Skip = "Requires exact aborted-origin reconciliation SQL/schema/native hosts/rollback/custody/authority fixture approval.";
    }
}
