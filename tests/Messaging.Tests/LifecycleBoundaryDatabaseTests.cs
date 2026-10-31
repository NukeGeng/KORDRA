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
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Postgresql;
using Wolverine.Postgresql.Transport;
using Wolverine.RDBMS;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class LifecycleBoundaryDatabaseTests
{
    public static string CleanupSql(Guid caseId)
    {
        if (caseId == Guid.Empty) throw new InvalidOperationException("Explicit lifecycle case required.");
        using var host = Host.CreateDefaultBuilder().ConfigureLogging(logging => logging.ClearProviders()).UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.UsePostgresqlPersistenceAndTransport("Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_test",
                "t12_" + caseId.ToString("N"), "t12_" + caseId.ToString("N"));
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        }).Build();
        return Assert.IsAssignableFrom<IMessageDatabase>(host.Services.GetRequiredService<IMessageStore>())
            .BatchedDeleteExpiredHandledEnvelopesSql(100) ?? throw new InvalidOperationException("Native cleanup SQL unavailable.");
    }

    [Fact]
    public async Task CancellationRequiresActualRollbackAndNativeCleanupPreservesLiveWorkAndTerminalReceipts()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("KORDRA_MSG_LIFECYCLE_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact lifecycle fixture operation approval required.")));
        var plan = manifest.RootElement;
        if (plan.GetProperty("status").GetString() != "approved_for_execution" || plan.GetProperty("purpose").GetString() != "T12.1e-lifecycle")
            throw new InvalidOperationException("Exact lifecycle fixture scope required.");
        var caseId = plan.GetProperty("caseId").GetGuid(); var schema = "t12_" + caseId.ToString("N");
        var ddl = NativeIngressDatabaseTests.CreationSql(caseId);
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ddl))) != plan.GetProperty("creationSha256").GetString())
            throw new InvalidOperationException("Lifecycle DDL differs from reviewed artifact.");
        var reviewedCleanup = CleanupSql(caseId);
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reviewedCleanup))) != plan.GetProperty("nativeCleanupSha256").GetString())
            throw new InvalidOperationException("Native cleanup SQL differs from reviewed artifact.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private kordra_test locator required.");
        AuditDbContextFactory.ValidateTestTarget(locator);
        var target = new NpgsqlConnectionStringBuilder(locator);
        if (target.Multiplexing || !string.IsNullOrEmpty(target.Options) || !string.IsNullOrEmpty(target.SearchPath))
            throw new InvalidOperationException("Lifecycle fixture rejects session overrides.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(65)); var ct = budget.Token;
        await using var observer = new NpgsqlConnection(locator); await observer.OpenAsync(ct);
        await using var writer = new NpgsqlConnection(locator); await writer.OpenAsync(ct);
        Assert.NotEqual(observer.ProcessID, writer.ProcessID);
        using var host = Builder(false).Build(); using var receiver = Builder(true).Build();
        var created = false;
        try
        {
            await Sql($"CREATE SCHEMA {schema}"); created = true;
            await Sql(ddl[($"CREATE SCHEMA {schema};\n").Length..]);
            await host.StartAsync(ct);
            var native = Assert.IsAssignableFrom<IMessageDatabase>(host.Services.GetRequiredService<IMessageStore>());
            var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var message = NativeWireFixture.Create(caseId, Guid.NewGuid(), now);
            var inspector = MessageReceiptInspector.CreateIsolatedTestInspector(observer, caseId);
            var pending = Seed(EnvelopeStatus.Incoming);
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                var stager = new CallerOwnedMessageStager(writer, root, native, schema);
                await stager.StageIncomingAsync([pending]);
                await CallerOwnedReceiptStore.CreateIsolatedTestStore(writer, root, caseId).StagePublicationAsync(message, now, ct);
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    CallerOwnedReceiptStore.CreateIsolatedTestStore(writer, root, caseId)
                        .StageLogicalAsync(message, LogicalReceiptOutcome.Denied, now, cancelled.Token));
                await root.RollbackAsync(ct); // Owner-confirmed rollback, not an inference from the cancellation signal.
            }
            Assert.Equal(0, await Count("wolverine_incoming_envelopes"));
            Assert.Equal(ReceiptInspectionStatus.StillUnknown, (await inspector.InspectPublicationAsync(message.Binding.Identity, message.PayloadHash, ct)).Status);
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                await new CallerOwnedMessageStager(writer, root, native, schema).StageIncomingAsync([pending]);
                var receipts = CallerOwnedReceiptStore.CreateIsolatedTestStore(writer, root, caseId);
                await receipts.StagePublicationAsync(message, now, ct);
                await receipts.StageLogicalAsync(message, LogicalReceiptOutcome.Denied, now, ct);
                await root.CommitAsync(ct);
            }
            using (var cancelledAfterCommit = new CancellationTokenSource())
            {
                cancelledAfterCommit.Cancel();
                Assert.Throws<OperationCanceledException>(() => cancelledAfterCommit.Token.ThrowIfCancellationRequested());
            }
            Assert.Equal(1, await Count("wolverine_incoming_envelopes"));
            Assert.Equal(ReceiptInspectionStatus.MatchingObserved, (await inspector.InspectPublicationAsync(message.Binding.Identity, message.PayloadHash, ct)).Status);

            var oldReceiptTime = now.AddDays(-91);
            var oldReceipt = NativeWireFixture.Create(caseId, Guid.NewGuid(), oldReceiptTime);
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                var receipts = CallerOwnedReceiptStore.CreateIsolatedTestStore(writer, root, caseId);
                await receipts.StagePublicationAsync(oldReceipt, oldReceiptTime, ct);
                await receipts.StageLogicalAsync(oldReceipt, LogicalReceiptOutcome.Denied, oldReceiptTime, ct);
                await root.CommitAsync(ct);
            }

            // Historical synthetic timestamps only: OS time is never modified. Native public staging
            // generates the real handled expiry; native public cleanup SQL is executed unchanged.
            for (var offset = 0; offset < 103; offset += 10)
            {
                await using var root = await writer.BeginTransactionAsync(ct);
                var stager = new CallerOwnedMessageStager(writer, root, native, schema);
                for (var index = offset; index < Math.Min(offset + 10, 103); index++)
                {
                    var envelope = Seed(EnvelopeStatus.Incoming);
                    await stager.StageIncomingAsync([envelope]);
                    var handledAt = index < 101 ? now.AddDays(-7).AddMilliseconds(-1) : index == 101 ? now.AddDays(-7) : now;
                    await stager.StageHandledAsync(envelope, handledAt, ct);
                }
                await root.CommitAsync(ct);
            }
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                await new CallerOwnedMessageStager(writer, root, native, schema).StageIncomingAsync(
                    [Seed(EnvelopeStatus.Incoming), Seed(EnvelopeStatus.Scheduled)]);
                await root.CommitAsync(ct);
            }
            Assert.Equal(106, await Count("wolverine_incoming_envelopes"));
            var cleanupSql = native.BatchedDeleteExpiredHandledEnvelopesSql(100)
                ?? throw new InvalidOperationException("Pinned native bounded cleanup SQL unavailable.");
            Assert.Equal(reviewedCleanup, cleanupSql);
            Assert.Equal(100, await Cleanup(now.AddMilliseconds(-1)));
            Assert.Equal(1, await Cleanup(now.AddMilliseconds(-1)));
            Assert.Equal(5, await Count("wolverine_incoming_envelopes"));
            Assert.Equal(1, await Cleanup(now)); // Expiry is inclusive, but future handled/active/scheduled survive.
            Assert.Equal(4, await Count("wolverine_incoming_envelopes"));
            Assert.Equal(0, await Cleanup(now));
            Assert.Equal(2, await Count("publication_receipts")); Assert.Equal(2, await Count("logical_receipts"));
            Assert.Equal(ReceiptInspectionStatus.MatchingObserved, (await inspector.InspectLogicalAsync(message.Binding.Identity, message.PayloadHash, ct)).Status);
            var retained = await inspector.InspectLogicalAsync(oldReceipt.Binding.Identity, oldReceipt.PayloadHash, ct);
            Assert.Equal(ReceiptInspectionStatus.MatchingObserved, retained.Status);
            Assert.Equal(LogicalReceiptOutcome.Denied, retained.OriginalOutcome);
            using var protectedQuery = new NpgsqlCommand($"SELECT id FROM {schema}.wolverine_incoming_envelopes", observer);
            var protectedIds = new List<Guid>();
            await using (var rows = await protectedQuery.ExecuteReaderAsync(ct))
                while (await rows.ReadAsync(ct)) protectedIds.Add(rows.GetGuid(0));
            Assert.Equal(4, protectedIds.Count);
            await host.StopAsync(ct);

            // A separate native receiver receives expired/originally >24h old wire. No deadline or
            // delivery identity is refreshed, and no business handler may produce an effect.
            await receiver.StartAsync(ct);
            var sender = receiver.Services.GetRequiredService<IMessageBus>().EndpointFor(new Uri("postgresql://nativecompatibility"));
            var rejectedIds = new List<Guid>();
            foreach (var age in new[] { TimeSpan.FromSeconds(121), TimeSpan.FromHours(25) })
            {
                var old = NativeWireFixture.Create(caseId, Guid.NewGuid(), DateTimeOffset.UtcNow - age);
                rejectedIds.Add(old.Delivery.MessageId);
                var wire = new MessageEnvelopeCodec(NativeWireFixture.Registry()).Write(old.Binding, old.Delivery, old.CopyPayload());
                await sender.SendRawMessageAsync(wire, configure: e =>
                { e.Id = old.Delivery.MessageId; e.Destination = new Uri("postgresql://nativecompatibility"); e.MessageType = NativeWireFixture.TypeId; e.ContentType = MessageEnvelopeCodec.ContentType; });
            }
            while (await Count("wolverine_dead_letters") != 2) await Task.Delay(50, ct);
            Assert.Equal(0, await Count("fixture_effect")); Assert.Equal(0, await Count("aud_intents"));
            Assert.Equal(2, await Count("publication_receipts")); Assert.Equal(2, await Count("logical_receipts"));
            using (var preserved = new NpgsqlCommand($"SELECT count(*) FROM {schema}.wolverine_incoming_envelopes WHERE id=ANY(@ids)", observer))
            {
                preserved.Parameters.AddWithValue("ids", protectedIds.ToArray());
                Assert.Equal(4L, await preserved.ExecuteScalarAsync(ct));
            }
            using (var rejected = new NpgsqlCommand($"SELECT id,body FROM {schema}.wolverine_dead_letters", observer))
            await using (var rows = await rejected.ExecuteReaderAsync(ct))
            {
                var seen = 0;
                while (await rows.ReadAsync(ct))
                {
                    Assert.Contains(rows.GetGuid(0), rejectedIds); seen++;
                    var diagnostic = Wolverine.Runtime.Serialization.EnvelopeSerializer.Deserialize((byte[])rows.GetValue(1));
                    Assert.Equal(RejectedEnvelopeDiagnostic.TypeId, diagnostic.MessageType);
                    Assert.Equal(RejectedEnvelopeDiagnostic.ContentType, diagnostic.ContentType);
                }
                Assert.Equal(2, seen);
            }

            async Task<int> Cleanup(DateTimeOffset time)
            { using var command = new NpgsqlCommand(cleanupSql, observer) { CommandTimeout = 5 }; command.Parameters.AddWithValue("now", time); return await command.ExecuteNonQueryAsync(ct); }
            Envelope Seed(EnvelopeStatus status) => new()
            {
                Id = Guid.NewGuid(), Destination = new Uri("postgresql://nativecompatibility"),
                Data = "{}"u8.ToArray(), ContentType = MessageEnvelopeCodec.ContentType, MessageType = NativeWireFixture.TypeId,
                Status = status, WasPersistedInInbox = true, ScheduledTime = status == EnvelopeStatus.Scheduled ? now.AddHours(1) : null
            };
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await host.StopAsync(cleanup.Token); await receiver.StopAsync(cleanup.Token); }
            finally { if (created) await Sql($"DROP SCHEMA {schema} CASCADE", cleanup.Token); }
        }
        IHostBuilder Builder(bool listen) => Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSingleton(new NativeFixtureConfiguration(locator, caseId, "consumer")))
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery(); options.Discovery.IncludeType<NativeCompatibilityHandler>();
                options.AddSerializer(new NativeWireFixture.Serializer(caseId));
                options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
                options.UsePostgresqlPersistenceAndTransport(locator, schema, schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
                options.Durability.MessageIdentity = Wolverine.MessageIdentity.IdAndDestination;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromDays(7);
                options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromMinutes(5);
                options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMinutes(5);
                options.PublishMessage<NativeCompatibilityMessage>().ToPostgresqlQueue("nativecompatibility");
                options.PublishMessage<NativeCompatibilityResult>().ToPostgresqlQueue("native_result");
                if (listen)
                {
                    options.ListenToPostgresqlQueue("nativecompatibility").Sequential();
                    options.UnknownMessageBehavior = UnknownMessageBehavior.DeadLetterQueue;
                    options.Transports.GetOrCreate<PostgresqlTransport>().Queues["nativecompatibility"].IncomingRules.Add(
                        new NativeMessageIngressRule(NativeWireFixture.Registry(), caseId,
                            Kordra.Modules.Messaging.Contracts.MessageScope.Tenant(caseId), new Uri("postgresql://nativecompatibility"), TimeProvider.System));
                }
            });
        async Task Sql(string sql, CancellationToken? cancellation = null)
        { using var command = new NpgsqlCommand(sql, observer) { CommandTimeout = 5 }; await command.ExecuteNonQueryAsync(cancellation ?? ct); }
        async Task<long> Count(string table)
        { using var command = new NpgsqlCommand($"SELECT count(*) FROM {schema}.{table}", observer) { CommandTimeout = 5 }; return (long)(await command.ExecuteScalarAsync(ct))!; }
    }
}
