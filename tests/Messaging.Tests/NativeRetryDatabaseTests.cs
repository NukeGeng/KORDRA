using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JasperFx;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeRetryDatabaseTests
{
    public static string CreationSql(Guid caseId)
    {
        var schema = "t12_" + caseId.ToString("N");
        return NativeIngressDatabaseTests.CreationSql(caseId) + $$"""

            CREATE SEQUENCE {{schema}}.fixture_retry_attempt;
            CREATE FUNCTION {{schema}}.fixture_transient_failure() RETURNS trigger LANGUAGE plpgsql AS $fixture$
            BEGIN
                IF nextval('{{schema}}.fixture_retry_attempt') <= 4 THEN
                    RAISE EXCEPTION 'synthetic retry fixture' USING ERRCODE = '40001';
                END IF;
                RETURN NEW;
            END;
            $fixture$;
            CREATE TRIGGER fixture_transient_failure BEFORE INSERT ON {{schema}}.fixture_effect
                FOR EACH ROW EXECUTE FUNCTION {{schema}}.fixture_transient_failure();
            """;
    }

    private static IHostBuilder Builder(string locator, Guid caseId) => Host.CreateDefaultBuilder()
        .ConfigureServices(services => services.AddSingleton(new NativeFixtureConfiguration(locator, caseId, "retry-fixture")))
        .UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery(); options.Discovery.IncludeType<NativeCompatibilityHandler>();
            options.AddSerializer(new NativeWireFixture.Serializer(caseId));
            options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
            var schema = "t12_" + caseId.ToString("N");
            options.UsePostgresqlPersistenceAndTransport(locator, schema, schema);
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
            options.Durability.MessageIdentity = Wolverine.MessageIdentity.IdAndDestination;
            options.Durability.KeepAfterMessageHandling = TimeSpan.FromDays(7);
            options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMilliseconds(200);
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(200);
            options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromSeconds(1);
            options.Durability.NodeReassignmentPollingTime = TimeSpan.FromSeconds(1);
            options.Durability.FirstHealthCheckExecution = TimeSpan.FromSeconds(1);
            options.Durability.StaleNodeTimeout = TimeSpan.FromSeconds(3);
            options.PublishMessage<NativeCompatibilityMessage>().ToPostgresqlQueue("nativecompatibility");
            options.PublishMessage<NativeCompatibilityResult>().ToPostgresqlQueue("native_result");
            options.ListenToPostgresqlQueue("nativecompatibility").Sequential();
            // Only a known PostgreSQL pre-commit serialization failure is transient in this fixture.
            // AUD Unknown returns through the handler's owning-process stop, not this exception policy.
            options.OnException<PostgresException>(error => error.SqlState == "40001")
                .ScheduleRetry(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60));
        });

    [Fact]
    public async Task FourPrecommitFailuresPersistRetryAttemptsAndOnlyFifthAttemptCommitsAfterRestart()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("KORDRA_MSG_RETRY_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact native retry operation approval required before database access.")));
        var plan = manifest.RootElement;
        if (plan.GetProperty("status").GetString() != "approved_for_execution" || plan.GetProperty("purpose").GetString() != "T12.1e-retry")
            throw new InvalidOperationException("Exact native retry scope required.");
        var caseId = plan.GetProperty("caseId").GetGuid(); var schema = "t12_" + caseId.ToString("N");
        var ddl = CreationSql(caseId);
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ddl))) != plan.GetProperty("creationSha256").GetString())
            throw new InvalidOperationException("Native retry DDL differs from approved review artifact.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private exact retry fixture locator required.");
        AuditDbContextFactory.ValidateTestTarget(locator);
        var target = new NpgsqlConnectionStringBuilder(locator);
        if (target.Multiplexing || !string.IsNullOrEmpty(target.Options) || !string.IsNullOrEmpty(target.SearchPath))
            throw new InvalidOperationException("Native retry fixture rejects session overrides.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(145)); var ct = budget.Token;
        await using var observer = new NpgsqlConnection(locator); await observer.OpenAsync(ct);
        using var first = Builder(locator, caseId).Build(); using var second = Builder(locator, caseId).Build();
        var created = false;
        try
        {
            await Sql($"CREATE SCHEMA {schema}"); created = true;
            await Sql(ddl[($"CREATE SCHEMA {schema};\n").Length..]);
            await first.StartAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var message = NativeWireFixture.Create(caseId, Guid.NewGuid(), now);
            var wire = new MessageEnvelopeCodec(NativeWireFixture.Registry()).Write(message.Binding, message.Delivery, message.CopyPayload());
            var destination = new Uri("postgresql://nativecompatibility");
            await first.Services.GetRequiredService<IMessageBus>().EndpointFor(destination).SendRawMessageAsync(wire, configure: envelope =>
            { envelope.Id = message.Delivery.MessageId; envelope.Destination = destination; envelope.ContentType = MessageEnvelopeCodec.ContentType; envelope.MessageType = NativeWireFixture.TypeId; });
            DateTimeOffset? fourthDue = null;
            while (fourthDue is null)
            {
                // A scheduled row is observed only after the failing handler's root scope unwound.
                using var query = new NpgsqlCommand($"SELECT execution_time,body,NULL::integer FROM {schema}.wolverine_queue_nativecompatibility_scheduled WHERE id=@id " +
                    $"UNION ALL SELECT execution_time,body,attempts FROM {schema}.wolverine_incoming_envelopes WHERE id=@id AND status='Scheduled'", observer);
                query.Parameters.AddWithValue("id", message.Delivery.MessageId);
                await using (var row = await query.ExecuteReaderAsync(ct))
                {
                    if (await row.ReadAsync(ct))
                    {
                        var bytes = (byte[])row.GetValue(1);
                        if (bytes.Length > 64 * 1024) throw new InvalidOperationException("Synthetic native timer record exceeds fixture bounds.");
                        var native = Wolverine.Runtime.Serialization.EnvelopeSerializer.Deserialize(bytes);
                        Assert.Equal(message.Delivery.MessageId, native.Id);
                        Assert.Equal(NativeWireFixture.TypeId, native.MessageType);
                        var attempt = row.IsDBNull(2) ? native.Attempts : row.GetInt32(2);
                        if (attempt == 4) fourthDue = new(row.GetDateTime(0));
                        Assert.False(await row.ReadAsync(ct), "One logical retry must not be parked in two native scheduling stores.");
                    }
                }
                if (fourthDue is null) await Task.Delay(100, ct);
            }
            Assert.Equal(4, await Scalar($"SELECT last_value FROM {schema}.fixture_retry_attempt"));
            Assert.Equal(0, await Count("fixture_effect")); Assert.Equal(0, await Count("logical_receipts"));
            Assert.Equal(0, await Count("aud_intents"));
            Assert.True(fourthDue > DateTimeOffset.UtcNow);
            Assert.True(fourthDue < message.Binding.DeadlineUtc, "Retry timer must fit inside its original locked deadline.");
            await first.StopAsync(ct);
            // The sequence survives rollback only as a synthetic observation of attempted INSERTs.
            // It is not a product retry counter, idempotency record or business state/authority.
            await second.StartAsync(ct);
            while (await Count("fixture_effect") != 1) await Task.Delay(100, ct);
            Assert.Equal(5, await Scalar($"SELECT last_value FROM {schema}.fixture_retry_attempt"));
            Assert.Equal(1, await Count("logical_receipts")); Assert.Equal(1, await Count("aud_intents"));
            Assert.Equal(0, await Count("wolverine_dead_letters"));
            using var receipt = new NpgsqlCommand($"SELECT binding_hash,recorded_at_utc FROM {schema}.logical_receipts", observer);
            await using (var row = await receipt.ExecuteReaderAsync(ct))
            {
                Assert.True(await row.ReadAsync(ct)); Assert.Equal(message.PayloadHash, row.GetString(0));
                Assert.True(new DateTimeOffset(row.GetDateTime(1)) >= fourthDue); Assert.False(await row.ReadAsync(ct));
            }
            Assert.True(DateTimeOffset.UtcNow >= now.AddSeconds(81));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await first.StopAsync(cleanup.Token); await second.StopAsync(cleanup.Token); }
            finally { if (created) await Sql($"DROP SCHEMA {schema} CASCADE", cleanup.Token); }
        }
        async Task Sql(string sql, CancellationToken? token = null)
        { using var command = new NpgsqlCommand(sql, observer) { CommandTimeout = 5 }; await command.ExecuteNonQueryAsync(token ?? ct); }
        Task<long> Count(string table) => Scalar($"SELECT count(*) FROM {schema}.{table}");
        async Task<long> Scalar(string sql)
        { using var command = new NpgsqlCommand(sql, observer) { CommandTimeout = 5 }; return (long)(await command.ExecuteScalarAsync(ct))!; }
    }
}
