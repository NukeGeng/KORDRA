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
using Wolverine.Runtime.Serialization;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeExhaustionDatabaseTests
{
    public static string CreationSql(Guid caseId)
    {
        var schema = "t12_" + caseId.ToString("N");
        return NativeIngressDatabaseTests.CreationSql(caseId) + $$"""

            CREATE SEQUENCE {{schema}}.fixture_terminal_attempt;
            CREATE FUNCTION {{schema}}.fixture_terminal_failure() RETURNS trigger LANGUAGE plpgsql AS $fixture$
            BEGIN
                PERFORM nextval('{{schema}}.fixture_terminal_attempt');
                RAISE EXCEPTION 'synthetic precommit failure' USING ERRCODE = '40001';
                RETURN NEW;
            END;
            $fixture$;
            CREATE TRIGGER fixture_terminal_failure BEFORE INSERT ON {{schema}}.fixture_effect
                FOR EACH ROW EXECUTE FUNCTION {{schema}}.fixture_terminal_failure();
            """;
    }

    [Fact]
    public async Task FiveActualPrecommitFailuresEndInOneSanitizedNativeDeadLetterWithoutOwnerEffects()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("KORDRA_MSG_EXHAUSTION_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact exhaustion fixture operation approval required.")));
        var plan = manifest.RootElement;
        if (plan.GetProperty("status").GetString() != "approved_for_execution" || plan.GetProperty("purpose").GetString() != "T12.1e-exhaustion")
            throw new InvalidOperationException("Exact exhaustion fixture scope required.");
        var caseId = plan.GetProperty("caseId").GetGuid(); var schema = "t12_" + caseId.ToString("N");
        var ddl = CreationSql(caseId);
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ddl))) != plan.GetProperty("creationSha256").GetString())
            throw new InvalidOperationException("Exhaustion fixture DDL differs from reviewed artifact.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private kordra_test locator required.");
        AuditDbContextFactory.ValidateTestTarget(locator);
        var target = new NpgsqlConnectionStringBuilder(locator);
        if (target.Multiplexing || !string.IsNullOrEmpty(target.Options) || !string.IsNullOrEmpty(target.SearchPath))
            throw new InvalidOperationException("Exhaustion fixture rejects session overrides.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(140)); var ct = budget.Token;
        await using var observer = new NpgsqlConnection(locator); await observer.OpenAsync(ct);
        var destination = new Uri("postgresql://nativecompatibility");
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSingleton(new NativeFixtureConfiguration(locator, caseId, "retry-fixture")))
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery(); options.Discovery.IncludeType<NativeCompatibilityHandler>();
                options.AddSerializer(new NativeWireFixture.Serializer(caseId));
                options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
                options.UsePostgresqlPersistenceAndTransport(locator, schema, schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
                options.Durability.MessageIdentity = Wolverine.MessageIdentity.IdAndDestination;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromDays(7);
                options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMilliseconds(200);
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(200);
                options.PublishMessage<NativeCompatibilityMessage>().ToPostgresqlQueue("nativecompatibility");
                options.PublishMessage<NativeCompatibilityResult>().ToPostgresqlQueue("native_result");
                options.ListenToPostgresqlQueue("nativecompatibility").Sequential();
                options.OnException<PostgresException>(error => error.SqlState == "40001")
                    .ScheduleRetry(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60))
                    .Then.ContinueWith(new ExhaustedRetryFixtureContinuation(caseId, destination));
            }).Build();
        var created = false;
        try
        {
            await Sql($"CREATE SCHEMA {schema}"); created = true;
            await Sql(ddl[($"CREATE SCHEMA {schema};\n").Length..]);
            await host.StartAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var message = NativeWireFixture.Create(caseId, Guid.NewGuid(), now);
            var wire = new MessageEnvelopeCodec(NativeWireFixture.Registry()).Write(message.Binding, message.Delivery, message.CopyPayload());
            const string sentinel = "synthetic-terminal-header-token";
            await host.Services.GetRequiredService<IMessageBus>().EndpointFor(destination).SendRawMessageAsync(wire, configure: envelope =>
            {
                envelope.Id = message.Delivery.MessageId; envelope.Destination = destination;
                envelope.ContentType = MessageEnvelopeCodec.ContentType; envelope.MessageType = NativeWireFixture.TypeId;
                envelope.Headers["fixture-private-token"] = sentinel;
            });
            while (await Count("wolverine_dead_letters") != 1) await Task.Delay(100, ct);
            // Allow a mistaken deferred/sixth execution to become observable; timers cannot be silently left behind.
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            using (var attempts = new NpgsqlCommand($"SELECT last_value FROM {schema}.fixture_terminal_attempt", observer))
                Assert.Equal(5L, await attempts.ExecuteScalarAsync(ct));
            Assert.Equal(0, await Count("fixture_effect")); Assert.Equal(0, await Count("logical_receipts"));
            Assert.Equal(0, await Count("aud_intents"));
            Assert.Equal(0, await Count("wolverine_queue_nativecompatibility"));
            Assert.Equal(0, await Count("wolverine_queue_nativecompatibility_scheduled"));
            using (var pending = new NpgsqlCommand($"SELECT count(*) FROM {schema}.wolverine_incoming_envelopes WHERE status IN ('Incoming','Scheduled')", observer))
                Assert.Equal(0L, await pending.ExecuteScalarAsync(ct));
            Assert.True(DateTimeOffset.UtcNow >= now.AddSeconds(81));
            using var query = new NpgsqlCommand($"SELECT id,body,exception_message FROM {schema}.wolverine_dead_letters", observer) { CommandTimeout = 5 };
            await using var row = await query.ExecuteReaderAsync(ct);
            Assert.True(await row.ReadAsync(ct)); Assert.Equal(message.Delivery.MessageId, row.GetGuid(0));
            var bytes = (byte[])row.GetValue(1); Assert.DoesNotContain(sentinel, Encoding.UTF8.GetString(bytes));
            var diagnostic = EnvelopeSerializer.Deserialize(bytes);
            Assert.Equal(RejectedEnvelopeDiagnostic.TypeId, diagnostic.MessageType);
            using var content = JsonDocument.Parse(diagnostic.Data!);
            Assert.Equal("RetryExhausted", content.RootElement.GetProperty("reason").GetString());
            Assert.Equal("Synthetic precommit retry attempts exhausted.", row.GetString(2));
            Assert.False(await row.ReadAsync(ct));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await host.StopAsync(cleanup.Token); }
            finally { if (created) await Sql($"DROP SCHEMA {schema} CASCADE", cleanup.Token); }
        }
        async Task Sql(string sql, CancellationToken? cancellation = null)
        { using var command = new NpgsqlCommand(sql, observer) { CommandTimeout = 5 }; await command.ExecuteNonQueryAsync(cancellation ?? ct); }
        async Task<long> Count(string table)
        { using var command = new NpgsqlCommand($"SELECT count(*) FROM {schema}.{table}", observer) { CommandTimeout = 5 }; return (long)(await command.ExecuteScalarAsync(ct))!; }
    }
}
