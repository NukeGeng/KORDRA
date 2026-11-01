using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Messaging.Tests;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;
using Wolverine.RDBMS;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Wolverine.Runtime;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowRootDatabaseTests
{
    [WorkflowTransactionDatabaseTheory]
    [InlineData(0)][InlineData(1)][InlineData(2)]
    public async Task ActualOwnersAndNativeTransportShareExactlyOneRoot(int slot)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_TRANSACTION_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved private workflow transaction locator required.");
        var caseId = Guid.ParseExact(WorkflowTransactionSchemaTests.ProposedCases[slot], "D"); var schema = "t12_" + caseId.ToString("N");
        var scripts = ReadApprovedScripts(caseId);
        await using var setup = new NpgsqlConnection(locator); _ = WorkflowFixtureTarget.Validate(setup, caseId);
        await setup.OpenAsync(ct); Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", ct, ("schema", schema)));
        var created = false; var state = new WorkflowFixtureState(caseId, locator);
        using var host = WorkflowTransactionSchemaTests.NativeBuilder(locator, schema, state, slot == 2).Build();
        var started = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                await Execute(setup, "CREATE SCHEMA " + schema, ct, ddl);
                foreach (var name in new[] { "wfl.sql", "aud.sql", "msg-owned.sql", "msg-native.sql" })
                    await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            await host.StartAsync(ct); started = true;
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            if (slot == 0)
            {
                await RejectSecondCoordinator();
                for (var boundary = 0; boundary < 6; boundary++) await FailureAtBoundary(boundary);
                await SharedNativeRootBudget(bytes: false);
                await SharedNativeRootBudget(bytes: true);
                await Producer(commit: false, lateAuditReplay: false, conflict: false, loseReply: false);
                await Producer(commit: true, lateAuditReplay: false, conflict: false, loseReply: false);
                await Producer(commit: false, lateAuditReplay: false, conflict: true, loseReply: false);
                await Producer(commit: false, lateAuditReplay: false, conflict: false, loseReply: false, orphan: true);
            }
            else if (slot == 1) await Producer(commit: true, lateAuditReplay: false, conflict: false, loseReply: true);
            else
            {
                await Producer(commit: true, lateAuditReplay: true, conflict: false, loseReply: false);
                var invocation = NewInvocation(caseId); state.Add(invocation);
                var input = Message(invocation.Specification, WorkflowFixtureWire.InputType);
                var receiverInvocation = new WorkflowFixtureInvocation(invocation.Specification, invocation.Audit, invocation.Output) {
                    BeforeCommitOracle = async observed => {
                        var view = await Observe(invocation, input, observed);
                        Assert.Equal(new Observation(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), view);
                    }
                };
                // The reserved server-side facts are replaced before any message is seeded, never derived from wire authority.
                var receiverState = state;
                receiverState.ReplaceForOracle(receiverInvocation);
                var native = Assert.IsAssignableFrom<IMessageDatabase>(runtime.Storage);
                // Use the actual native transport. Seeding Inbox with Incoming and owner_id=0
                // waits for recovery; it does not exercise this queue's listener delivery path.
                var sender = new MessageContext(runtime);
                await using (var seed = await setup.BeginTransactionAsync(ct))
                {
                    var stager = new CallerOwnedMessageStager(setup, seed, native, schema);
                    await sender.EnlistInOutboxAsync(new CallerOwnedEnvelopeEnlistment(stager,
                        async () => await seed.RollbackAsync(CancellationToken.None)));
                    await sender.EndpointFor(new Uri("postgresql://workflow-transitions-v1"))
                        .SendRawMessageAsync(Wire(input), configure: envelope => {
                            envelope.Id = input.Delivery.MessageId;
                            envelope.Destination = new Uri("postgresql://workflow_transitions_v1");
                            envelope.ContentType = MessageEnvelopeCodec.ContentType;
                            envelope.MessageType = WorkflowFixtureWire.InputType;
                            envelope.Status = EnvelopeStatus.Outgoing;
                        });
                    await seed.CommitAsync(ct);
                }
                await sender.FlushOutgoingMessagesAsync();
                Assert.Equal(WorkflowCommitDisposition.Committed, await receiverInvocation.Completion.Task.WaitAsync(ct));
                var after = await Observe(invocation, input, ct);
                Assert.Equal(new Observation(1, 4, 1, 1, 1, 1, 1, 1, 1, 0), after);
            }

            async Task RejectSecondCoordinator()
            {
                await using var connection = state.NewOwnedConnection(); await connection.OpenAsync(ct);
                await using (var transaction = await connection.BeginTransactionAsync(ct))
                {
                    await using var first = await WorkflowRootTransaction.CreateIsolatedTestAsync(connection, transaction,
                        caseId, new MessageContext(runtime), state.Gate, ct);
                    await Assert.ThrowsAsync<InvalidOperationException>(() => WorkflowRootTransaction.CreateIsolatedTestAsync(
                        connection, transaction, caseId, new MessageContext(runtime), state.Gate, ct));
                    await first.RollbackAsync();
                }
                // A later actual PostgreSQL transaction remains eligible even if Npgsql recycles its wrapper.
                await using var next = await connection.BeginTransactionAsync(ct);
                await using var successor = await WorkflowRootTransaction.CreateIsolatedTestAsync(connection, next,
                    caseId, new MessageContext(runtime), state.Gate, ct);
                await successor.RollbackAsync();
            }

            async Task SharedNativeRootBudget(bool bytes)
            {
                var invocation = NewInvocation(caseId);
                await using var connection = state.NewOwnedConnection(); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await WorkflowRootTransaction.CreateIsolatedTestAsync(connection, transaction,
                    caseId, new MessageContext(runtime), state.Gate, ct);
                await root.StageCreationAsync(invocation.Specification, ct);
                await root.StageAuditAsync(invocation.Audit, ct);
                await root.StageClockFloorAsync(DateTimeOffset.UtcNow, ct);
                await root.StagePublicationAsync(WorkflowFixtureWire.Registry(), invocation.Output, DateTimeOffset.UtcNow, ct);
                var native = Assert.IsAssignableFrom<IMessageDatabase>(runtime.Storage);
                var first = new CallerOwnedMessageStager(connection, transaction, native, schema);
                var second = new CallerOwnedMessageStager(connection, transaction, native, schema);
                Envelope Item(int size) => new() {
                    Id = Guid.NewGuid(), Destination = new Uri("postgresql://workflow_results_v1"),
                    ContentType = MessageEnvelopeCodec.ContentType, MessageType = WorkflowFixtureWire.OutputType,
                    Data = new byte[size], Status = EnvelopeStatus.Outgoing
                };
                if (bytes)
                    // Six total envelopes fit the count budget but exceed 160KiB including the real paired message.
                    await Assert.ThrowsAsync<InvalidOperationException>(() => first.StageOutgoingAsync(
                        Enumerable.Range(0, 5).Select(_ => Item(32 * 1024)).ToArray()));
                else
                {
                    await first.StageOutgoingAsync(Enumerable.Range(0, 9).Select(_ => Item(1)).ToArray());
                    Assert.Equal(10L, await Scalar(connection, "SELECT count(*) FROM " + schema + ".wolverine_outgoing_envelopes", ct));
                    await Assert.ThrowsAsync<InvalidOperationException>(() => second.StageOutgoingAsync([Item(1)]));
                }
                // A rejected low-level staging reservation never commits any WFL/AUD/floor/receipt/outbox row.
                await root.RollbackAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
                Assert.Equal(new Observation(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), await Observe(invocation, null, ct));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + schema + ".wolverine_outgoing_envelopes", ct));
            }

            async Task FailureAtBoundary(int boundary)
            {
                var invocation = NewInvocation(caseId); var spec = invocation.Specification;
                await using var connection = state.NewOwnedConnection(); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                var messages = new MessageContext(runtime);
                await using var root = await WorkflowRootTransaction.CreateIsolatedTestAsync(connection, transaction, caseId, messages, state.Gate, ct);
                if (boundary == 0)
                {
                    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => root.StageCreationAsync(spec, cancelled.Token));
                }
                else
                {
                    await root.StageCreationAsync(spec, ct);
                    if (boundary == 1)
                        await Assert.ThrowsAsync<InvalidOperationException>(() => root.StageAuditAsync(invocation.Audit with { TenantRef = Guid.NewGuid() }, ct));
                    else
                    {
                        await root.StageAuditAsync(invocation.Audit, ct);
                        if (boundary == 2)
                            await Assert.ThrowsAsync<InvalidOperationException>(() => root.StageClockFloorAsync(default, ct));
                        else
                        {
                            await root.StageClockFloorAsync(DateTimeOffset.UtcNow, ct);
                            if (boundary == 3)
                            {
                                // Correct tenant/instance but a different logical operation must not pair with this owner.
                                var wrongOperation = Message(spec, WorkflowFixtureWire.OutputType, operation: Guid.NewGuid());
                                await Assert.ThrowsAsync<InvalidOperationException>(() => root.StagePublicationAsync(
                                    WorkflowFixtureWire.Registry(), wrongOperation, DateTimeOffset.UtcNow, ct));
                            }
                            else
                            {
                                await root.StagePublicationAsync(WorkflowFixtureWire.Registry(), invocation.Output, DateTimeOffset.UtcNow, ct);
                                if (boundary == 4) await root.RollbackAsync();
                                else
                                {
                                    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                                    Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, await root.CommitAsync(cancelled.Token));
                                }
                            }
                        }
                    }
                }
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
                Assert.Equal(new Observation(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), await Observe(invocation, null, ct));
                await using var stillOpen = await state.Gate.AcquireReadAsync(ct);
            }

            async Task Producer(bool commit, bool lateAuditReplay, bool conflict, bool loseReply, bool orphan = false)
            {
                var invocation = NewInvocation(caseId); var spec = invocation.Specification;
                if (orphan)
                {
                    await using var seedConnection = state.NewOwnedConnection(); await seedConnection.OpenAsync(ct);
                    await using var seedRoot = await seedConnection.BeginTransactionAsync(ct);
                    var receipts = CallerOwnedReceiptStore.CreateIsolatedTestStore(seedConnection, seedRoot, caseId);
                    Assert.Equal(ReceiptStageDisposition.Staged, (await receipts.StagePublicationAsync(invocation.Output, DateTimeOffset.UtcNow, ct)).Disposition);
                    await seedRoot.CommitAsync(ct);
                }
                await using var connection = state.NewOwnedConnection(); await connection.OpenAsync(ct);
                var predecessor = connection.ProcessID;
                await using var transaction = await connection.BeginTransactionAsync(ct);
                var messages = new MessageContext(runtime);
                await using var root = await WorkflowRootTransaction.CreateIsolatedTestAsync(connection, transaction, caseId, messages, state.Gate, ct);
                await root.StageCreationAsync(spec, ct); await root.StageAuditAsync(invocation.Audit, ct);
                await root.StageClockFloorAsync(DateTimeOffset.UtcNow, ct);
                if (orphan)
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => root.StagePublicationAsync(WorkflowFixtureWire.Registry(), invocation.Output, DateTimeOffset.UtcNow, ct));
                    Assert.Equal(0L, await Scoped("instances", "instance_id", spec.InstanceId, ct));
                    Assert.Equal(0L, await Scoped("aud_intents", "intent_id", invocation.Audit.IntentId, ct));
                    Assert.Equal(1L, await Scoped("publication_receipts", "operation_id", spec.RequestId, ct));
                    Assert.Equal(0L, await Scoped("wolverine_outgoing_envelopes", "id", invocation.Output.Delivery.MessageId, ct));
                    await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
                    await using var held = await state.Gate.AcquireReadAsync(ct); return;
                }
                await root.StagePublicationAsync(WorkflowFixtureWire.Registry(), invocation.Output, DateTimeOffset.UtcNow, ct);
                Assert.Equal(0L, await Scoped("instances", "instance_id", spec.InstanceId, ct));
                Assert.Equal(0L, await Scoped("aud_intents", "intent_id", invocation.Audit.IntentId, ct));
                Assert.Equal(0L, await Scoped("publication_receipts", "operation_id", spec.RequestId, ct));
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
                if (conflict)
                {
                    var changed = Message(spec, WorkflowFixtureWire.OutputType, Guid.NewGuid());
                    await Assert.ThrowsAsync<InvalidOperationException>(() => root.StagePublicationAsync(WorkflowFixtureWire.Registry(), changed, DateTimeOffset.UtcNow, ct));
                    Assert.Equal(0L, await Scoped("instances", "instance_id", spec.InstanceId, ct));
                    Assert.Equal(0L, await Scoped("publication_receipts", "operation_id", spec.RequestId, ct));
                    await using var held = await state.Gate.AcquireReadAsync(ct); return;
                }
                if (lateAuditReplay)
                {
                    await using var competing = state.NewOwnedConnection(); await competing.OpenAsync(ct);
                    await using var competingRoot = await competing.BeginTransactionAsync(ct);
                    await using var other = await AuditTransactionContext.CreateIsolatedTestAsync(competing, competingRoot, caseId, ct);
                    Assert.Equal(AuditTransactionStageStatus.Staged, (await other.StageAsync(invocation.Audit, ct)).Status);
                    Assert.Equal(AuditAppendStatus.Durable, (await other.CommitAsync(ct)).Status);
                    Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, await root.CommitAsync(ct));
                    await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
                    Assert.Equal(0L, await Scoped("instances", "instance_id", spec.InstanceId, ct));
                    Assert.Equal(0L, await Scoped("publication_receipts", "operation_id", spec.RequestId, ct));
                    Assert.Equal(1L, await Scoped("aud_intents", "intent_id", invocation.Audit.IntentId, ct));
                    await using var held = await state.Gate.AcquireReadAsync(ct); return;
                }
                if (!commit)
                {
                    await root.RollbackAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
                    Assert.Equal(new Observation(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), await Observe(invocation, null, ct));
                    await using var held = await state.Gate.AcquireReadAsync(ct); return;
                }
                var fault = loseReply ? CommitReplyFault.Install(connection) : null;
                var result = await root.CommitAsync(ct);
                Assert.Equal(loseReply ? WorkflowCommitDisposition.Unknown : WorkflowCommitDisposition.Committed, result);
                if (loseReply)
                {
                    Assert.True(fault!.Dropped); await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
                    await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var denied = await state.Gate.AcquireReadAsync(ct); });
                    await connection.CloseAsync();
                    for (var i = 0; i < 100 && await BackendAlive(predecessor, ct); i++) await Task.Delay(10, ct);
                    Assert.False(await BackendAlive(predecessor, ct));
                    Assert.Equal(new Observation(1, 4, 1, 1, 1, 1, 1, 0, 0, 1), await Observe(invocation, null, ct));
                    await using var reader = state.NewOwnedConnection(); await reader.OpenAsync(ct);
                    Assert.NotEqual(predecessor, reader.ProcessID);
                    await using var wfl = WorkflowReceiptInspector.CreateIsolatedTestInspector(reader, caseId);
                    var expected = new WorkflowReceiptExpectation(new(spec.InstallationId, spec.TenantId, spec.InstanceId),
                        spec.RequestId, spec.SpecificationHash, 1);
                    Assert.Equal(WorkflowReceiptInspectionStatus.MatchingObserved, await wfl.InspectAsync(expected, ct));
                    Assert.Equal(WorkflowReceiptInspectionStatus.Conflict, await wfl.InspectAsync(new(expected.Identity,
                        expected.RequestId, new string('f', 64), 1), ct));
                    Assert.Equal(WorkflowReceiptInspectionStatus.StillUnknown, await wfl.InspectAsync(new(
                        new(spec.InstallationId, Guid.NewGuid(), spec.InstanceId), spec.RequestId, spec.SpecificationHash, 1), ct));
                    var msg = MessageReceiptInspector.CreateIsolatedTestInspector(reader, caseId);
                    var publication = await msg.InspectPublicationAsync(invocation.Output.Binding.Identity, invocation.Output.PayloadHash, ct);
                    Assert.Equal(ReceiptInspectionStatus.MatchingObserved, publication.Status);
                    Assert.Equal(invocation.Output.Delivery.MessageId, publication.OriginalMessageId);
                    var audit = new AuditMutationEvidenceReader(new FixtureAuditReaderFactory(reader, caseId));
                    Assert.Equal(AuditEvidenceStatus.Matched, await audit.MatchUnderAdmissionAsync(new(invocation.Audit.IntentId,
                        AuditIntentCanonicalizer.ComputeHash(invocation.Audit), invocation.Audit.SubjectRef!.Value, spec.TenantId,
                        spec.InstanceId, invocation.Audit.InvocationId, invocation.Audit.Permission, invocation.Audit.ScopeKind,
                        invocation.Audit.ScopeId!.Value, invocation.Audit.SafeReason), ct));
                }
                else
                {
                    Assert.Equal(new Observation(1, 4, 1, 1, 1, 1, 1, 0, 0, 1), await Observe(invocation, null, ct));
                    await root.FlushConfirmedAsync();
                    Assert.Equal(0L, await Scoped("wolverine_outgoing_envelopes", "id", invocation.Output.Delivery.MessageId, ct));
                }
            }

            async Task<Observation> Observe(WorkflowFixtureInvocation invocation, VerifiedMessageEnvelope? input, CancellationToken observed)
            {
                var spec = invocation.Specification;
                await using var read = state.NewOwnedConnection(); await read.OpenAsync(observed);
                var filters = "installation_id=@installation AND tenant_id=@tenant AND instance_id=@instance";
                var sql = "SELECT " +
                    "(SELECT count(*) FROM " + schema + ".instances WHERE " + filters + ")," +
                    "(SELECT count(*) FROM " + schema + ".steps WHERE " + filters + ")," +
                    "(SELECT count(*) FROM " + schema + ".request_receipts WHERE " + filters + " AND request_id=@request)," +
                    "(SELECT count(*) FROM " + schema + ".aud_intents WHERE intent_id=@intent)," +
                    "(SELECT count(*) FROM " + schema + ".publication_receipts WHERE installation_id=@installation AND tenant_key=@tenant AND operation_id=@request AND type_id=@output_type)," +
                    "(SELECT count(*) FROM " + schema + ".instances i JOIN " + schema +
                    ".request_receipts r ON r.installation_id=i.installation_id AND r.tenant_id=i.tenant_id AND r.instance_id=i.instance_id AND r.request_id=i.request_id JOIN " + schema +
                    ".publication_receipts p ON p.installation_id=i.installation_id AND p.tenant_key=i.tenant_id AND p.operation_id=i.request_id JOIN " + schema +
                    ".aud_intents a ON a.intent_id=@intent WHERE i.installation_id=@installation AND i.tenant_id=@tenant AND i.instance_id=@instance " +
                    "AND i.specification_hash=@spec AND i.specification=@canonical AND i.version=1 AND i.state=1 AND r.content_hash=@spec " +
                    "AND p.binding_hash=@publication AND a.canonical_intent_hash=@audit)," +
                    "(SELECT count(*) FROM " + schema + ".clock_floor WHERE installation_id=@installation AND tenant_id=@tenant AND floor_utc>=@created)," +
                    "(SELECT count(*) FROM " + schema + ".logical_receipts WHERE installation_id=@installation AND tenant_key=@tenant AND operation_id=@request AND type_id=@input_type)," +
                    "(SELECT count(*) FROM " + schema + ".wolverine_incoming_envelopes WHERE id=@input AND status='Handled')," +
                    "(SELECT count(*) FROM " + schema + ".wolverine_outgoing_envelopes WHERE id=@output)";
                await using var query = new NpgsqlCommand(sql, read) { CommandTimeout = 2 };
                foreach (var (name, value) in new (string, object)[] { ("installation", spec.InstallationId), ("tenant", spec.TenantId),
                    ("instance", spec.InstanceId), ("request", spec.RequestId), ("intent", invocation.Audit.IntentId),
                    ("spec", spec.SpecificationHash), ("canonical", spec.WriteCanonical()), ("publication", invocation.Output.PayloadHash),
                    ("audit", Convert.FromHexString(AuditIntentCanonicalizer.ComputeHash(invocation.Audit))), ("created", spec.CreatedAtUtc),
                    ("input_type", WorkflowFixtureWire.InputType), ("output_type", WorkflowFixtureWire.OutputType),
                    ("input", input?.Delivery.MessageId ?? Guid.Empty), ("output", invocation.Output.Delivery.MessageId) })
                    query.Parameters.AddWithValue(name, value);
                await using var rows = await query.ExecuteReaderAsync(observed); Assert.True(await rows.ReadAsync(observed));
                return new(rows.GetInt64(0), rows.GetInt64(1), rows.GetInt64(2), rows.GetInt64(3), rows.GetInt64(4),
                    rows.GetInt64(5), rows.GetInt64(6), rows.GetInt64(7), rows.GetInt64(8), rows.GetInt64(9));
            }
            async Task<long> Scoped(string table, string key, Guid id, CancellationToken observed)
            {
                Assert.Contains((table, key), new[] { ("instances", "instance_id"), ("steps", "instance_id"), ("request_receipts", "request_id"),
                    ("aud_intents", "intent_id"), ("publication_receipts", "operation_id"), ("wolverine_outgoing_envelopes", "id") });
                await using var read = state.NewOwnedConnection(); await read.OpenAsync(observed);
                return await Scalar(read, "SELECT count(*) FROM " + schema + "." + table + " WHERE " + key + "=@id", observed, ("id", id));
            }
            async Task<bool> BackendAlive(int pid, CancellationToken observed)
            {
                await using var read = state.NewOwnedConnection(); await read.OpenAsync(observed);
                return await Scalar(read, "SELECT count(*) FROM pg_stat_activity WHERE pid=@pid", observed, ("pid", pid)) != 0;
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (started) await host.StopAsync(cleanup.Token);
            if (created)
            {
                await using var connection = state.NewOwnedConnection(); await connection.OpenAsync(cleanup.Token);
                await Execute(connection, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0L, await Scalar(connection, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", cleanup.Token, ("schema", schema)));
            }
        }
    }

    private sealed class FixtureAuditReaderFactory(NpgsqlConnection reader, Guid caseId) : IDbContextFactory<AuditDbContext>
    {
        public AuditDbContext CreateDbContext() => AuditDbContextFactory.CreateIsolatedTestContext(reader, caseId);
    }

    private sealed record Observation(long Instances, long Steps, long Requests, long Audit, long Publications, long Matching, long Floor, long Logical, long Handled, long Outgoing);
    private static Dictionary<string, string> ReadApprovedScripts(Guid caseId)
    {
        var file = Environment.GetEnvironmentVariable("KORDRA_WFL_TRANSACTION_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Explicit C operation approval required.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(file));
        Assert.Equal("approved_for_execution", manifest.RootElement.GetProperty("status").GetString());
        var declared = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("caseId").GetGuid() == caseId);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kordra.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root.FullName, ".amr-local", "t12-remaining-design", "t12-2c-schema-review", caseId.ToString("D"));
        var scripts = new Dictionary<string, string>();
        foreach (var name in new[] { "wfl.sql", "aud.sql", "msg-owned.sql", "msg-native.sql", "cleanup.sql" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(directory, name));
            Assert.Equal(declared.GetProperty("sha256").GetProperty(name).GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            scripts.Add(name, Encoding.UTF8.GetString(bytes));
        }
        Assert.Equal("DROP SCHEMA t12_" + caseId.ToString("N") + " CASCADE;\n", scripts["cleanup.sql"]);
        return scripts;
    }
    private static WorkflowFixtureInvocation NewInvocation(Guid caseId)
    {
        var now = DateTimeOffset.UtcNow;
        var spec = new WorkflowSpecification(caseId, caseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(1));
        return new(spec, new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation, Guid.NewGuid(), caseId, spec.InstanceId,
            "workflow.start", "environment", spec.EnvironmentId, 1, 1, "synthetic_fixture", now, Guid.NewGuid()), Message(spec, WorkflowFixtureWire.OutputType));
    }
    private static byte[] Wire(VerifiedMessageEnvelope value) => new MessageEnvelopeCodec(WorkflowFixtureWire.Registry())
        .Write(value.Binding, value.Delivery, value.CopyPayload());

    private static VerifiedMessageEnvelope Message(WorkflowSpecification spec, string type, Guid? payload = null, Guid? operation = null)
    {
        var registry = WorkflowFixtureWire.Registry(); var codec = new MessageEnvelopeCodec(registry);
        var binding = new MessageBinding(new(spec.InstallationId, MessageScope.Tenant(spec.TenantId), operation ?? spec.RequestId, spec.RequestId, 1, type),
            1, new("workflow", spec.InstanceId), spec.CreatedAtUtc, spec.CreatedAtUtc.AddSeconds(120));
        using var output = new MemoryStream(); using (var writer = new Utf8JsonWriter(output)) { new WorkflowFixtureWire.Codec().Write(writer, payload ?? spec.InstanceId); writer.Flush(); }
        var delivery = new DeliveryIdentity(Guid.NewGuid(), spec.RequestId, spec.RequestId);
        return codec.Read(codec.Write(binding, delivery, output.ToArray()), MessageEnvelopeCodec.ContentType, spec.InstallationId, binding.Identity.Scope, DateTimeOffset.UtcNow);
    }
    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? root = null)
    { await using var command = new NpgsqlCommand(sql, connection, root) { CommandTimeout = 3 }; await command.ExecuteNonQueryAsync(ct); }
    private static async Task<long> Scalar(NpgsqlConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 2 };
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}

public sealed class WorkflowTransactionDatabaseTheoryAttribute : TheoryAttribute
{
    public WorkflowTransactionDatabaseTheoryAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_TRANSACTION_FIXTURE_MANIFEST")))
            Skip = "Pending separately approved exact T12.2c schemas/SQL/cleanup; no database operation executed.";
    }
}
