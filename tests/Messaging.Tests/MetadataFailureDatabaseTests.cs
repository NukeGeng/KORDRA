using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JasperFx;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Postgresql;
using Wolverine.Postgresql.Transport;
using Wolverine.RDBMS;
using Wolverine.Runtime;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class MetadataFailureDatabaseTests
{
    [Fact]
    public async Task IndependentReceiptInspectionAndActualHighWatermarkFailuresNeverDropCommittedWork()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("KORDRA_MSG_METADATA_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact metadata failure fixture approval required before database access.")));
        var plan = manifest.RootElement;
        if (plan.GetProperty("status").GetString() != "approved_for_execution" || plan.GetProperty("purpose").GetString() != "T12.1e-metadata")
            throw new InvalidOperationException("Exact metadata failure fixture scope required.");
        var caseId = plan.GetProperty("caseId").GetGuid(); var schema = "t12_" + caseId.ToString("N");
        var ddl = NativeIngressDatabaseTests.CreationSql(caseId);
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ddl))) != plan.GetProperty("creationSha256").GetString())
            throw new InvalidOperationException("Metadata fixture DDL differs from approved artifact.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private metadata fixture locator required.");
        AuditDbContextFactory.ValidateTestTarget(locator);
        var target = new NpgsqlConnectionStringBuilder(locator);
        if (target.Multiplexing || !string.IsNullOrEmpty(target.Options) || !string.IsNullOrEmpty(target.SearchPath))
            throw new InvalidOperationException("Metadata fixture rejects session overrides.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var ct = budget.Token;
        await using var observer = new NpgsqlConnection(locator); await observer.OpenAsync(ct);
        await using var writer = new NpgsqlConnection(locator); await writer.OpenAsync(ct);
        Assert.NotEqual(observer.ProcessID, writer.ProcessID);
        var created = false;
        using var host = Host.CreateDefaultBuilder().UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
            options.UsePostgresqlPersistenceAndTransport(locator, schema, schema);
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
            options.Durability.MessageIdentity = Wolverine.MessageIdentity.IdAndDestination;
            options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromMinutes(5);
            options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMinutes(5);
            options.PublishMessage<NativeCompatibilityMessage>().ToPostgresqlQueue("nativecompatibility");
            options.PublishMessage<NativeCompatibilityResult>().ToPostgresqlQueue("native_result");
            // No listener. Recovery polls are delayed beyond this bounded fixture; committed seed rows must stay put.
        }).Build();
        try
        {
            await Sql($"CREATE SCHEMA {schema}"); created = true;
            await Sql(ddl[($"CREATE SCHEMA {schema};\n").Length..]);
            await host.StartAsync(ct);
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            var native = Assert.IsAssignableFrom<IMessageDatabase>(runtime.Storage);
            var inspector = MessageReceiptInspector.CreateIsolatedTestInspector(observer, caseId);
            var message = NativeWireFixture.Create(caseId, Guid.NewGuid(), DateTimeOffset.UtcNow);
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                var receipts = CallerOwnedReceiptStore.CreateIsolatedTestStore(writer, root, caseId);
                Assert.Equal(ReceiptStageDisposition.Staged, (await receipts.StagePublicationAsync(message, DateTimeOffset.UtcNow, ct)).Disposition);
                Assert.Equal(ReceiptInspectionStatus.StillUnknown, (await inspector.InspectPublicationAsync(message.Binding.Identity, message.PayloadHash, ct)).Status);
                await root.RollbackAsync(ct);
            }
            Assert.Equal(ReceiptInspectionStatus.StillUnknown, (await inspector.InspectPublicationAsync(message.Binding.Identity, message.PayloadHash, ct)).Status);
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                var receipts = CallerOwnedReceiptStore.CreateIsolatedTestStore(writer, root, caseId);
                await receipts.StagePublicationAsync(message, DateTimeOffset.UtcNow, ct);
                await receipts.StageLogicalAsync(message, LogicalReceiptOutcome.Denied, DateTimeOffset.UtcNow, ct);
                await root.CommitAsync(ct);
            }
            var publication = await inspector.InspectPublicationAsync(message.Binding.Identity, message.PayloadHash, ct);
            Assert.Equal(ReceiptInspectionStatus.MatchingObserved, publication.Status); Assert.Equal(message.Delivery.MessageId, publication.OriginalMessageId);
            var denial = await inspector.InspectLogicalAsync(message.Binding.Identity, message.PayloadHash, ct);
            Assert.Equal(ReceiptInspectionStatus.MatchingObserved, denial.Status); Assert.Equal(LogicalReceiptOutcome.Denied, denial.OriginalOutcome);
            Assert.Equal(ReceiptInspectionStatus.Conflict, (await inspector.InspectLogicalAsync(message.Binding.Identity, new string('0', 64), ct)).Status);
            var wrongScope = new Kordra.Modules.Messaging.Contracts.MessageIdentity(caseId,
                Kordra.Modules.Messaging.Contracts.MessageScope.Tenant(Guid.NewGuid()), message.Binding.Identity.OperationId,
                message.Binding.Identity.StepId, 1, NativeWireFixture.TypeId);
            Assert.Equal(ReceiptInspectionStatus.StillUnknown, (await inspector.InspectLogicalAsync(wrongScope, message.PayloadHash, ct)).Status);

            // Synthetic setup only, via the package public API; not one oversized product transaction.
            // Each seed transaction carries at most ten envelopes, matching the bounded kernel profile.
            var seedIds = new List<Guid>();
            for (var offset = 0; offset < 10001; offset += 10)
            {
                var batch = Enumerable.Range(offset, Math.Min(10, 10001 - offset)).Select(_ =>
                {
                    var id = Guid.NewGuid(); seedIds.Add(id);
                    return new Envelope { Id = id, Destination = new Uri("postgresql://nativecompatibility"),
                        ContentType = MessageEnvelopeCodec.ContentType, MessageType = NativeWireFixture.TypeId,
                        Data = "{}"u8.ToArray(), Status = EnvelopeStatus.Outgoing };
                }).ToArray();
                await using var root = await writer.BeginTransactionAsync(ct);
                await new CallerOwnedMessageStager(writer, root, native, schema).StageOutgoingAsync(batch);
                await root.CommitAsync(ct);
            }
            Assert.Equal(10001, await Count("wolverine_outgoing_envelopes"));
            var transport = runtime.Options.Transports.GetOrCreate<PostgresqlTransport>();
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                var guard = PublicationCapacityGuard.CreateIsolatedTestGuard(writer, root, caseId);
                Assert.Equal(PublicationCapacityStatus.AboveWatermark, await guard.CheckAsync(NativeWireFixture.Registry(), transport, ct));
                var stager = new CallerOwnedMessageStager(writer, root, native, schema);
                var context = new MessageContext(runtime);
                await context.EnlistInOutboxAsync(new CallerOwnedEnvelopeEnlistment(stager, async () => await root.RollbackAsync()));
                var fresh = NativeWireFixture.Create(caseId, Guid.NewGuid(), DateTimeOffset.UtcNow);
                var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => stager.StagePublicationAsync(
                    CallerOwnedReceiptStore.CreateIsolatedTestStore(writer, root, caseId), NativeWireFixture.Registry(), context, fresh, DateTimeOffset.UtcNow, ct));
                Assert.Equal("Publication capacity is unavailable or above its admission watermark.", rejected.Message);
                Assert.Empty(context.Outstanding); await root.RollbackAsync(ct);
            }
            Assert.Equal(1, await Count("publication_receipts")); Assert.Equal(10001, await Count("wolverine_outgoing_envelopes"));
            // Fixture setup only: remove exactly one known synthetic seed to exercise the boundary.
            using (var removeSeed = new NpgsqlCommand($"DELETE FROM {schema}.wolverine_outgoing_envelopes WHERE id = @id", observer) { CommandTimeout = 5 })
            {
                removeSeed.Parameters.AddWithValue("id", seedIds[0]);
                Assert.Equal(1, await removeSeed.ExecuteNonQueryAsync(ct));
            }
            Assert.Equal(10000, await Count("wolverine_outgoing_envelopes"));
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                Assert.Equal(PublicationCapacityStatus.WithinWatermark, await PublicationCapacityGuard.CreateIsolatedTestGuard(writer, root, caseId)
                    .CheckAsync(NativeWireFixture.Registry(), transport, ct));
                await root.RollbackAsync(ct);
            }
            await Sql($"DROP TABLE {schema}.wolverine_queue_nativecompatibility"); // Own empty fixture queue only; simulate missing storage.
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                Assert.Equal(PublicationCapacityStatus.Unavailable, await PublicationCapacityGuard.CreateIsolatedTestGuard(writer, root, caseId)
                    .CheckAsync(NativeWireFixture.Registry(), transport, ct));
                await root.RollbackAsync(ct);
            }
            Assert.Equal(10000, await Count("wolverine_outgoing_envelopes")); Assert.Equal(0, await Count("fixture_effect"));
            await observer.CloseAsync();
            Assert.Equal(ReceiptInspectionStatus.Unavailable, (await inspector.InspectLogicalAsync(message.Binding.Identity, message.PayloadHash, ct)).Status);
            await observer.OpenAsync(ct);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await host.StopAsync(cleanup.Token); }
            finally
            {
                if (observer.State != System.Data.ConnectionState.Open) await observer.OpenAsync(cleanup.Token);
                if (created) await Sql($"DROP SCHEMA {schema} CASCADE", cleanup.Token);
            }
        }
        async Task Sql(string sql, CancellationToken? token = null)
        { using var command = new NpgsqlCommand(sql, observer) { CommandTimeout = 5 }; await command.ExecuteNonQueryAsync(token ?? ct); }
        async Task<long> Count(string table)
        { using var command = new NpgsqlCommand($"SELECT count(*) FROM {schema}.{table}", observer) { CommandTimeout = 5 }; return (long)(await command.ExecuteScalarAsync(ct))!; }
    }
}
