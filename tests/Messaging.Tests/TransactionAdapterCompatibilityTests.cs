using System.Text;
using System.Text.Json;
using JasperFx;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wolverine;
using Wolverine.Postgresql;
using Wolverine.RDBMS;
using Wolverine.Runtime;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using MsgContracts = Kordra.Modules.Messaging.Contracts;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class TransactionAdapterCompatibilityTests
{
    public sealed record CompatibilityMessage(Guid CaseId);

    [Theory]
    [InlineData(false, 0, false, false)]
    [InlineData(false, 2, true, false)]
    [InlineData(true, 1, false, false)]
    [InlineData(true, 3, true, false)]
    [InlineData(true, 7, false, true)]
    public async Task ActualAuditAndOwnerFloorAndPublicOutboxShareCallerCommit(bool commit, int slot, bool stageInbox, bool loseReply)
    {
        var path = Environment.GetEnvironmentVariable("KORDRA_MSG_COMPAT_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Explicit approved fixture manifest required before database access.");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        if (manifest.RootElement.GetProperty("status").GetString() != "approved_for_execution")
            throw new InvalidOperationException("Fixture operation approval required.");
        var schema = manifest.RootElement.GetProperty("schemas")[slot].GetString()!;
        if (schema.Length != 36 || !schema.StartsWith("t12_", StringComparison.Ordinal) ||
            !Guid.TryParseExact(schema[4..], "N", out var caseId) || caseId == Guid.Empty)
            throw new InvalidOperationException("Exact disposable fixture case required.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private exact test locator required.");
        // Reuse the actual AUD target/session guards before opening any connection.
        await using var setup = new NpgsqlConnection(locator);
        await using var model = AuditDbContextFactory.CreateIsolatedTestContext(setup, caseId);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await setup.OpenAsync(budget.Token);
        var created = false;
        using var host = Host.CreateDefaultBuilder().UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
            options.UsePostgresqlPersistenceAndTransport(locator, schema, schema);
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            options.Durability.MessageIdentity = Wolverine.MessageIdentity.IdAndDestination;
            options.Durability.KeepAfterMessageHandling = TimeSpan.FromDays(7);
            // Probe staging before recovery polling; this is not live handler/ACK evidence.
            options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromMinutes(1);
            options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMinutes(1);
            options.PublishMessage<CompatibilityMessage>().ToPostgresqlQueue("compatibility");
        }).Build();
        try
        {
            await Execute($"CREATE SCHEMA {schema}"); created = true;
            await Execute(model.Database.GenerateCreateScript());
            await Execute($"CREATE TABLE {schema}.fixture_owner(id uuid PRIMARY KEY); CREATE TABLE {schema}.fixture_floor(id uuid PRIMARY KEY)");
            await Execute(OwnedMessagingSchema.TablesSql.Replace("msg.", schema + ".", StringComparison.Ordinal));
            await host.StartAsync(budget.Token);
            var store = Assert.IsAssignableFrom<IMessageDatabase>(host.Services.GetRequiredService<IWolverineRuntime>().Storage);
            var incoming = new Envelope(new CompatibilityMessage(caseId))
            {
                Destination = new Uri("postgresql://compatibility"), Status = EnvelopeStatus.Incoming,
                Data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new CompatibilityMessage(caseId))),
                ContentType = "application/json", MessageType = "kordra.fixture.compatibility.v1"
            };
            if (stageInbox)
            {
                // Seed simulates a transport-persisted inbox row, separate from the subsequent owner transaction.
                await using var seed = await setup.BeginTransactionAsync(budget.Token);
                var sibling = new Envelope(new CompatibilityMessage(caseId))
                {
                    Id = incoming.Id, Destination = new Uri("postgresql://other-destination"), Status = EnvelopeStatus.Incoming,
                    Data = incoming.Data, ContentType = incoming.ContentType, MessageType = incoming.MessageType
                };
                await store.StoreIncomingAsync(seed, [incoming, sibling]); await seed.CommitAsync(budget.Token);
                incoming.WasPersistedInInbox = true;
            }
            var ownerLocator = new NpgsqlConnectionStringBuilder(locator) { Pooling = false, Multiplexing = false }.ConnectionString;
            await using var connection = new NpgsqlConnection(ownerLocator); await connection.OpenAsync(budget.Token);
            await using var transaction = await connection.BeginTransactionAsync(budget.Token);
            await using var audit = await AuditTransactionContext.CreateIsolatedTestAsync(connection, transaction, caseId, budget.Token);
            var stager = new CallerOwnedMessageStager(connection, transaction, store, schema);
            Assert.Throws<InvalidOperationException>(() => new CallerOwnedMessageStager(connection, transaction, store, "unbound_schema"));
            using (var differentConnection = new NpgsqlConnection(locator))
                Assert.Throws<InvalidOperationException>(() => new CallerOwnedMessageStager(differentConnection, transaction, store, schema));
            var intent = new AuditIntent(1, Guid.NewGuid(), caseId, AuditIntentStage.Mutation,
                Guid.NewGuid(), caseId, caseId, "target.read", "target", caseId, 1, 1, "synthetic_fixture",
                DateTimeOffset.UtcNow, caseId);
            Assert.Equal(AuditTransactionStageStatus.Staged, (await audit.StageAsync(intent, budget.Token)).Status);
            await using (var owner = new NpgsqlCommand($"INSERT INTO {schema}.fixture_owner VALUES (@id); INSERT INTO {schema}.fixture_floor VALUES (@id)", connection, transaction))
            {
                owner.Parameters.AddWithValue("id", caseId); await owner.ExecuteNonQueryAsync(budget.Token);
            }
            var envelope = new Envelope(new CompatibilityMessage(caseId))
            {
                Destination = new Uri("postgresql://compatibility"),
                Data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new CompatibilityMessage(caseId))),
                ContentType = "application/json", MessageType = "kordra.fixture.compatibility.v1",
                Status = EnvelopeStatus.Outgoing
            };
            var receipts = CallerOwnedReceiptStore.CreateIsolatedTestStore(connection, transaction, caseId);
            var publicationContext = new MessageContext(host.Services.GetRequiredService<IWolverineRuntime>());
            await publicationContext.EnlistInOutboxAsync(new CallerOwnedEnvelopeEnlistment(stager, async () => await audit.RollbackAsync()));
            var registry = new CompiledMessageRegistry([new CompiledMessageRegistration<Guid>(
                new("fixture.compatibility", 1, MsgContracts.MessageScopeKind.Tenant, false), new CaseCodec(), new RefuseConsumer(), "compatibility", new())]);
            var codec = new MessageEnvelopeCodec(registry); var createdAt = DateTimeOffset.UtcNow;
            var binding = new MsgContracts.MessageBinding(new(caseId, MsgContracts.MessageScope.Tenant(caseId), caseId, caseId, 1, "fixture.compatibility"),
                1, null, createdAt, createdAt.AddSeconds(120));
            var payload = Encoding.UTF8.GetBytes("{\"caseId\":\"" + caseId.ToString("D") + "\"}");
            var delivery = new DeliveryIdentity(envelope.Id, caseId, caseId);
            var verified = codec.Read(codec.Write(binding, delivery, payload), MessageEnvelopeCodec.ContentType, caseId, binding.Identity.Scope, createdAt);
            Assert.Equal(ReceiptStageDisposition.Staged, (await stager.StagePublicationAsync(receipts, registry, publicationContext, verified, createdAt, budget.Token)).Disposition);
            var repeated = codec.Read(codec.Write(binding, new(Guid.NewGuid(), caseId, caseId), payload), MessageEnvelopeCodec.ContentType, caseId, binding.Identity.Scope, createdAt);
            var duplicate = await stager.StagePublicationAsync(receipts, registry, publicationContext, repeated, createdAt, budget.Token);
            Assert.Equal(ReceiptStageDisposition.ExistingInTransaction, duplicate.Disposition);
            Assert.Equal(delivery.MessageId, duplicate.OriginalMessageId);
            var stagedNative = Assert.Single(publicationContext.Outstanding);
            Assert.Equal(delivery.MessageId, stagedNative.Id);
            Assert.Equal(binding.Identity.TypeId, stagedNative.MessageType);
            Assert.Equal(MessageEnvelopeCodec.ContentType, stagedNative.ContentType);
            Assert.Equal(verified.PayloadHash, codec.Read(stagedNative.Data!, MessageEnvelopeCodec.ContentType,
                caseId, binding.Identity.Scope, createdAt).PayloadHash);
            if (stageInbox) await stager.StageHandledAsync(incoming, DateTimeOffset.UtcNow, budget.Token);
            Assert.False(audit.CommitAttempted);
            Assert.Equal(0, await Count("fixture_owner")); Assert.Equal(0, await Count("fixture_floor"));
            Assert.Equal(0, await Count("aud_intents")); Assert.Equal(stageInbox ? 2 : 0, await DeliveryCount());
            Assert.Equal(0, await Count("publication_receipts"));
            Assert.Equal(0, await HandledCount());
            if (commit)
            {
                var fault = loseReply ? CommitReplyFault.Install(connection) : null;
                var result = await audit.CommitAsync(budget.Token);
                Assert.Equal(loseReply ? AuditAppendStatus.Unknown : AuditAppendStatus.Durable, result.Status);
                Assert.True(audit.CommitAttempted);
                if (fault is not null)
                {
                    Assert.True(fault.Dropped);
                    await using var lookupConnection = new NpgsqlConnection(locator);
                    await using var lookup = AuditDbContextFactory.CreateIsolatedTestContext(lookupConnection, caseId);
                    var stored = await lookup.Entries.AsNoTracking().SingleAsync(x => x.IntentId == intent.IntentId, budget.Token);
                    Assert.Equal(AuditIntentCanonicalizer.ComputeHash(intent), Convert.ToHexString(stored.CanonicalIntentHash).ToLowerInvariant());
                }
            }
            else await audit.RollbackAsync(budget.Token);
            if (commit) Assert.Equal(AuditAppendStatus.Rejected, (await audit.CommitAsync(budget.Token)).Status);
            Assert.Equal(commit ? 1 : 0, await Count("fixture_owner"));
            Assert.Equal(commit ? 1 : 0, await Count("fixture_floor"));
            Assert.Equal(commit ? 1 : 0, await Count("aud_intents"));
            Assert.Equal(commit ? 1 : 0, await Count("publication_receipts"));
            Assert.Equal((stageInbox ? 2 : 0) + (commit ? 1 : 0), await DeliveryCount());
            Assert.Equal(stageInbox && commit ? 1 : 0, await HandledCount());
            if (stageInbox)
            {
                await using var observer = new NpgsqlConnection(locator); await observer.OpenAsync(budget.Token);
                await using var siblingCheck = new NpgsqlCommand($"SELECT count(*) FROM {schema}.wolverine_incoming_envelopes WHERE id=@id AND received_at=@destination AND status='Incoming'", observer);
                siblingCheck.Parameters.AddWithValue("id", incoming.Id);
                siblingCheck.Parameters.AddWithValue("destination", new Uri("postgresql://other-destination").ToString());
                Assert.Equal(1L, await siblingCheck.ExecuteScalarAsync(budget.Token));
            }
            if (commit && !loseReply)
            {
                await publicationContext.FlushOutgoingMessagesAsync();
                for (var i = 0; i < 100 && await Count("wolverine_outgoing_envelopes") != 0; i++)
                    await Task.Delay(50, budget.Token);
                Assert.Equal(0, await Count("wolverine_outgoing_envelopes"));
                Assert.Equal(1, await Count("wolverine_queue_compatibility"));
                Assert.Equal(1, await Count("publication_receipts"));
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => stager.StageOutgoingAsync([envelope]));
            async Task<long> HandledCount()
            {
                await using var observer = new NpgsqlConnection(locator); await observer.OpenAsync(budget.Token);
                await using var command = new NpgsqlCommand($"SELECT count(*) FROM {schema}.wolverine_incoming_envelopes WHERE status='Handled'", observer);
                return (long)(await command.ExecuteScalarAsync(budget.Token))!;
            }
            async Task<long> Count(string table)
            {
                await using var observer = new NpgsqlConnection(locator); await observer.OpenAsync(budget.Token);
                await using var command = new NpgsqlCommand($"SELECT count(*) FROM {schema}.{table}", observer);
                return (long)(await command.ExecuteScalarAsync(budget.Token))!;
            }
            async Task<long> DeliveryCount()
            {
                await using var observer = new NpgsqlConnection(locator); await observer.OpenAsync(budget.Token);
                await using var command = new NpgsqlCommand($"SELECT (SELECT count(*) FROM {schema}.wolverine_outgoing_envelopes) + " +
                    $"(SELECT count(*) FROM {schema}.wolverine_incoming_envelopes) + (SELECT count(*) FROM {schema}.wolverine_queue_compatibility)", observer);
                return (long)(await command.ExecuteScalarAsync(budget.Token))!;
            }
        }
        finally
        {
            try { await host.StopAsync(CancellationToken.None); }
            finally
            {
                if (created)
                {
                    await using var cleanup = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", setup) { CommandTimeout = 10 };
                    await cleanup.ExecuteNonQueryAsync();
                }
            }
        }
        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, setup); await command.ExecuteNonQueryAsync(budget.Token);
        }
    }

    private sealed class CaseCodec : MsgContracts.IMessagePayloadCodec<Guid>
    {
        public Guid Read(JsonElement payload)
        {
            if (!payload.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "caseId" }))
                throw new InvalidOperationException("Closed fixture payload required.");
            var text = payload.GetProperty("caseId").GetString();
            if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text)
                throw new InvalidOperationException("Canonical fixture identity required.");
            return id;
        }
        public void Write(Utf8JsonWriter writer, Guid payload)
        { writer.WriteStartObject(); writer.WriteString("caseId", payload.ToString("D")); writer.WriteEndObject(); }
    }
    private sealed class RefuseConsumer : MsgContracts.IMessageConsumer<Guid>
    { public ValueTask HandleAsync(Guid message, CancellationToken cancellationToken) => throw new InvalidOperationException("Producer must not dispatch."); }
}
