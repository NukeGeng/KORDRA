using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
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

public sealed class WorkflowWakeLifecycleDatabaseTests
{
    public static readonly Guid CaseId = Guid.Parse("c94a33e9-22c9-4a78-8e33-a8e5b3ae8c9f");
    private static string Schema => "t12_" + CaseId.ToString("N");
    private static string DirectoryPath => Path.Combine(Root(), ".amr-local", "t12-remaining-design", "t12-2d-wake-lifecycle-schema-review");

    [Fact]
    public void ExportLifecycleSqlMetadataWithoutStartingHostsOrOpeningConnections()
    {
        const string locator = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC";
        using var connection = new NpgsqlConnection(locator);
        using var workflow = WorkflowDbContext.CreateWakeFixture(connection, CaseId);
        using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId);
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
            ["wfl.sql"] = workflow.Database.GenerateCreateScript(), ["aud.sql"] = audit.Database.GenerateCreateScript(),
            ["msg-owned.sql"] = OwnedMessagingSchema.TablesSql.Replace("msg.", Schema + ".", StringComparison.Ordinal),
            ["msg-native.sql"] = nativeSql, ["cleanup.sql"] = "DROP SCHEMA " + Schema + " CASCADE;\n"
        };
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        foreach (var sql in scripts.Values) { Assert.Contains(Schema, sql); Assert.DoesNotContain("wolverine_queues", sql); }
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_LIFECYCLE_ARTIFACT_DIRECTORY") is not { } directory) return;
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

    [WorkflowWakeLifecycleDatabaseFact]
    public async Task NativeDueReceiveCommitsInboxOwnerAuditResultOnceAcrossHostReplacementAndRedelivery()
    {
        var scripts = ReadApprovedScripts();
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_LIFECYCLE_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved private lifecycle fixture locator required.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var ct = budget.Token;
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
                await Execute(setup, "CREATE SCHEMA " + Schema, ct, ddl);
                foreach (var name in new[] { "wfl.sql", "aud.sql", "msg-owned.sql", "msg-native.sql" }) await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            var (spec, schedule) = await WorkflowWakeDatabaseTests.SeedWaitingTimer(setup, ct, CaseId);
            var now = DateTimeOffset.UtcNow;
            var wake = new WorkflowWakeOperation(spec, schedule.StepId, schedule.TimerId, 1, 1, 1,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 3, now, now.AddSeconds(120));
            var receiveAudit = Audit(spec, now);
            var invocation = new WorkflowWakeFixtureInvocation(spec, wake, receiveAudit, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) {
                BeforeCommitOracle = async observed => {
                    await using (var owner = IsolatedWorkflowFixture.WorkflowReceipt(setup, CaseId))
                        Assert.Equal(WorkflowReceiptInspectionStatus.StillUnknown, await owner.InspectAsync(new(
                            new(CaseId, CaseId, spec.InstanceId), spec.RequestId, spec.SpecificationHash, 6,
                            wake.WakeOperationId, wake.StepId), observed));
                    Assert.Equal(5L, await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", observed, ("id", spec.InstanceId)));
                    Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".transitions WHERE operation_id=@id", observed, ("id", wake.WakeOperationId)));
                    Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE intent_id=@id", observed, ("id", receiveAudit.IntentId)));
                    Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".logical_receipts WHERE operation_id=@id", observed, ("id", wake.WakeOperationId)));
                    Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts WHERE operation_id=@id AND type_id=@type", observed,
                        ("id", wake.WakeOperationId), ("type", WorkflowTransitionResultPublication.TypeId)));
                    Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".wolverine_incoming_envelopes WHERE id=@id AND status='Handled'", observed,
                        ("id", wake.MessageId)));
                }
            };
            state.Add(invocation);
            producerStarted = true; await producer.StartAsync(ct);
            var requests = WorkflowWakePublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseRequest());
            var input = WorkflowWakePublication.Prepare(spec, wake, requests, now);
            await using (var connection = state.NewOwnedConnection())
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction, CaseId,
                    new MessageContext(producer.Services.GetRequiredService<IWolverineRuntime>()), state.Gate, ct);
                await root.StageInitialTimerDispatchAsync(spec, wake, requests, 4, 1, Guid.NewGuid(), Guid.NewGuid(), now, ct);
                await root.StageAuditAsync(Audit(spec, now), ct); await root.StageClockFloorAsync(now, ct);
                Assert.Equal(WorkflowCommitDisposition.Committed, await root.CommitAsync(ct)); await root.FlushConfirmedAsync();
            }
            await producer.StopAsync(ct); producerStarted = false;
            receiverStarted = true; await receiver.StartAsync(ct);
            Assert.Equal(WorkflowCommitDisposition.Committed, await invocation.Completion.Task.WaitAsync(ct));
            await AssertCommitted();
            var results = WorkflowTransitionResultPublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseResult());
            await using (var query = new NpgsqlCommand("SELECT body FROM " + Schema + ".wolverine_queue_workflow_results_v1 WHERE id=@id", setup))
            {
                query.Parameters.AddWithValue("id", invocation.ResultMessageId);
                var native = EnvelopeSerializer.Deserialize(Assert.IsType<byte[]>(await query.ExecuteScalarAsync(ct)));
                Assert.NotNull(native.Data);
                var output = new MessageEnvelopeCodec(results).Read(native.Data, MessageEnvelopeCodec.ContentType, CaseId, MessageScope.Tenant(CaseId), DateTimeOffset.UtcNow);
                WorkflowTransitionResultPublication.RequireTimerDueMatch(output, wake, 6);
                var receipt = await IsolatedWorkflowFixture.MessageReceipt(setup, CaseId)
                    .InspectPublicationAsync(output.Binding.Identity, output.PayloadHash, ct);
                Assert.Equal(ReceiptInspectionStatus.MatchingObserved, receipt.Status); Assert.Equal(invocation.ResultMessageId, receipt.OriginalMessageId);
            }
            await using (var owner = IsolatedWorkflowFixture.WakeReceipt(setup, CaseId))
                Assert.Equal(WorkflowReceiptInspectionStatus.MatchingObserved, await owner.InspectAsync(new(
                    new(new(CaseId, CaseId, spec.InstanceId), spec.RequestId, spec.SpecificationHash, 5, wake.SchedulerOperationId, wake.StepId),
                    wake.TimerId, wake.Generation, wake.DescriptorHash), ct));
            Assert.Equal(ReceiptInspectionStatus.MatchingObserved, (await IsolatedWorkflowFixture.MessageReceipt(setup, CaseId)
                .InspectLogicalAsync(input.Binding.Identity, input.PayloadHash, ct)).Status);
            await Until(() => Scalar(setup, "SELECT count(*) FROM " + Schema + ".wolverine_queue_workflow_transitions_v1", ct), ct);
            // Re-deliver the identical physical envelope after its original atomic inbox disposition.
            // This represents lost transport ACK; it must not execute the owner handler again.
            var runtime = receiver.Services.GetRequiredService<IWolverineRuntime>();
            var sender = new MessageContext(runtime);
            await using (var transaction = await setup.BeginTransactionAsync(ct))
            {
                var stager = new CallerOwnedMessageStager(setup, transaction, Assert.IsAssignableFrom<IMessageDatabase>(runtime.Storage), Schema);
                await sender.EnlistInOutboxAsync(new CallerOwnedEnvelopeEnlistment(stager, async () => await transaction.RollbackAsync(CancellationToken.None)));
                var bytes = new MessageEnvelopeCodec(requests).Write(input.Binding, input.Delivery, input.CopyPayload());
                await sender.EndpointFor(new Uri("postgresql://workflow-transitions-v1")).SendRawMessageAsync(bytes, configure: envelope => {
                    envelope.Id = input.Delivery.MessageId; envelope.Destination = new Uri("postgresql://workflow_transitions_v1");
                    envelope.ContentType = MessageEnvelopeCodec.ContentType; envelope.MessageType = WorkflowWakePublication.TypeId;
                    envelope.Status = EnvelopeStatus.Outgoing;
                });
                await transaction.CommitAsync(ct);
            }
            await sender.FlushOutgoingMessagesAsync();
            await Until(() => Scalar(setup, "SELECT count(*) FROM " + Schema + ".wolverine_queue_workflow_transitions_v1", ct), ct);
            await receiver.StopAsync(ct); receiverStarted = false;
            Assert.Equal(1, invocation.HandlerCalls); await AssertCommitted();

            async Task AssertCommitted()
            {
                Assert.Equal(6L, await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
                Assert.Equal(5L, await Scalar(setup, "SELECT version FROM " + Schema + ".steps WHERE step_id=@id", ct, ("id", wake.StepId)));
                Assert.Equal(4L, await Scalar(setup, "SELECT state::bigint FROM " + Schema + ".steps WHERE step_id=@id", ct, ("id", wake.StepId)));
                Assert.Equal(3L, await Scalar(setup, "SELECT status::bigint FROM " + Schema + ".timers WHERE timer_id=@id", ct, ("id", wake.TimerId)));
                Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".transitions WHERE operation_id=@id", ct, ("id", wake.WakeOperationId)));
                Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE intent_id=@id", ct, ("id", receiveAudit.IntentId)));
                Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".logical_receipts WHERE operation_id=@id", ct, ("id", wake.WakeOperationId)));
                Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts WHERE operation_id=@id AND type_id=@type", ct,
                    ("id", wake.WakeOperationId), ("type", WorkflowTransitionResultPublication.TypeId)));
                Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".wolverine_incoming_envelopes WHERE id=@id AND status='Handled'", ct, ("id", wake.MessageId)));
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
            }
        }
    }

    private static async Task Until(Func<Task<long>> count, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct); bounded.CancelAfter(TimeSpan.FromSeconds(5));
        while (await count() != 0) await Task.Delay(20, bounded.Token);
    }
    private static Dictionary<string, string> ReadApprovedScripts()
    {
        var file = Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_LIFECYCLE_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact lifecycle fixture approval required.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(file)); var root = manifest.RootElement;
        Assert.Equal("approved_for_execution", root.GetProperty("status").GetString()); Assert.Equal(CaseId, root.GetProperty("caseId").GetGuid());
        Assert.Equal(Schema, root.GetProperty("schema").GetString());
        var scripts = new Dictionary<string, string>();
        foreach (var name in new[] { "wfl.sql", "aud.sql", "msg-owned.sql", "msg-native.sql", "cleanup.sql" })
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

public sealed class WorkflowWakeLifecycleDatabaseFactAttribute : FactAttribute
{
    public WorkflowWakeLifecycleDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_LIFECYCLE_FIXTURE_MANIFEST")))
            Skip = "Requires exact lifecycle SQL/schema/native host replacement/redelivery approval; no DB or listener started.";
    }
}
