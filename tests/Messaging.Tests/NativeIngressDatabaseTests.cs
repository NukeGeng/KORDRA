using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core.Migrations;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Wolverine.Postgresql.Transport;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeIngressDatabaseTests
{
    public static string CreationSql(Guid caseId, IReadOnlyList<string>? reviewedQueueOrder = null)
    {
        if (caseId == Guid.Empty) throw new InvalidOperationException("Explicit ingress case required.");
        var schema = "t12_" + caseId.ToString("N");
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_test");
        using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, caseId);
        using var host = CompatibilityProgram.Builder(connection.ConnectionString, caseId, "ingress", true)
            .ConfigureLogging(logging => logging.ClearProviders()).Build();
        var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
        // Public metadata composition only for this single-store, non-tenanted fixture. Never ConnectAsync/StartAsync:
        // ConfigureAsync adds native queue schema objects to the same store before ToDatabaseScript.
        runtime.Options.Transports.GetOrCreate<PostgresqlTransport>().ConfigureAsync(runtime).GetAwaiter().GetResult();
        var database = Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>());
        return $"CREATE SCHEMA {schema};\n" + audit.Database.GenerateCreateScript() + FixtureSchemaScript.Create(database, reviewedQueueOrder) +
            OwnedMessagingSchema.TablesSql.Replace("msg.", schema + ".", StringComparison.Ordinal) +
            $"CREATE TABLE {schema}.fixture_effect(id uuid PRIMARY KEY);\n";
    }

    [Fact]
    public async Task ActualNativeIncomingRuleScrubsUnknownAndInvalidMessagesBeforeDeadLetterPersistence()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("KORDRA_MSG_INGRESS_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact native ingress operation approval required before database access.")));
        var plan = manifest.RootElement;
        if (plan.GetProperty("status").GetString() != "approved_for_execution" || plan.GetProperty("purpose").GetString() != "T12.1e-ingress")
            throw new InvalidOperationException("Exact native ingress scope required.");
        var caseId = plan.GetProperty("caseId").GetGuid(); var schema = "t12_" + caseId.ToString("N");
        var order = plan.TryGetProperty("nativeQueueObjectOrder", out var reviewedOrder)
            ? reviewedOrder.EnumerateArray().Select(item => item.GetString() ?? throw new InvalidOperationException("Invalid reviewed queue order.")).ToArray() : null;
        var ddl = CreationSql(caseId, order);
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ddl))) != plan.GetProperty("creationSha256").GetString())
            throw new InvalidOperationException("Ingress DDL differs from approved review artifact.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private test locator required.");
        AuditDbContextFactory.ValidateTestTarget(locator);
        var target = new NpgsqlConnectionStringBuilder(locator);
        if (target.Multiplexing || !string.IsNullOrEmpty(target.Options) || !string.IsNullOrEmpty(target.SearchPath))
            throw new InvalidOperationException("Native ingress fixture rejects session overrides.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(50)); var ct = budget.Token;
        await using var connection = new NpgsqlConnection(locator); await connection.OpenAsync(ct);
        var created = false; var logs = new CaptureLogs();
        using var host = CompatibilityProgram.Builder(locator, caseId, "ingress", true)
            .ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs)).Build();
        try
        {
            await Sql($"CREATE SCHEMA {schema}"); created = true;
            await Sql(ddl[($"CREATE SCHEMA {schema};\n").Length..]);
            await host.StartAsync(ct);
            var destination = new Uri("postgresql://nativecompatibility");
            var sender = host.Services.GetRequiredService<IMessageBus>().EndpointFor(destination);
            var verified = NativeWireFixture.Create(caseId, Guid.NewGuid(), DateTimeOffset.UtcNow);
            var valid = new MessageEnvelopeCodec(NativeWireFixture.Registry()).Write(verified.Binding, verified.Delivery, verified.CopyPayload());
            await sender.SendRawMessageAsync(valid, configure: e =>
            { e.Id = verified.Delivery.MessageId; e.Destination = destination; e.ContentType = MessageEnvelopeCodec.ContentType; e.MessageType = NativeWireFixture.TypeId; });
            await Until(async () => await Count("fixture_effect") == 1 && await Count("logical_receipts") == 1);
            const string sentinel = "synthetic-forbidden-ingress-token";
            var rejectedIds = new List<Guid>();
            foreach (var scenario in new[] { "unknown-type", "malformed-wire", "oversize", "wrong-content-type" })
            {
                var id = Guid.NewGuid(); rejectedIds.Add(id);
                var body = scenario == "oversize" ? Encoding.UTF8.GetBytes(new string('x', 32 * 1024) + sentinel) : Encoding.UTF8.GetBytes(sentinel);
                await sender.SendRawMessageAsync(body, configure: e =>
                {
                    e.Id = id; e.Destination = destination; e.MessageType = scenario == "unknown-type" ? sentinel : NativeWireFixture.TypeId;
                    e.ContentType = scenario == "wrong-content-type" ? sentinel : MessageEnvelopeCodec.ContentType;
                    e.Headers[sentinel] = sentinel; e.Source = sentinel; e.TenantId = sentinel;
                    e.ReplyUri = new Uri("https://example.invalid/" + sentinel);
                });
            }
            await Until(async () => await Count("wolverine_dead_letters") == 4);
            using var query = new NpgsqlCommand($"SELECT id,body,exception_message FROM {schema}.wolverine_dead_letters", connection) { CommandTimeout = 5 };
            await using (var reader = await query.ExecuteReaderAsync(ct))
            {
                var seen = 0;
                while (await reader.ReadAsync(ct))
                {
                    Assert.Contains(reader.GetGuid(0), rejectedIds); seen++;
                    var bytes = (byte[])reader.GetValue(1); Assert.DoesNotContain(sentinel, Encoding.UTF8.GetString(bytes));
                    var native = Wolverine.Runtime.Serialization.EnvelopeSerializer.Deserialize(bytes);
                    Assert.Equal(RejectedEnvelopeDiagnostic.TypeId, native.MessageType);
                    Assert.Equal(RejectedEnvelopeDiagnostic.ContentType, native.ContentType);
                    Assert.DoesNotContain(sentinel, reader.IsDBNull(2) ? "" : reader.GetString(2));
                }
                Assert.Equal(4, seen);
            }
            Assert.Equal(1, await Count("fixture_effect")); Assert.Equal(1, await Count("logical_receipts"));
            Assert.DoesNotContain(logs.Lines, line => line.Contains(sentinel, StringComparison.Ordinal));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await host.StopAsync(cleanup.Token); }
            finally { if (created) await Sql($"DROP SCHEMA {schema} CASCADE", cleanup.Token); }
        }
        async Task Sql(string sql, CancellationToken? cancellation = null)
        { using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 5 }; await command.ExecuteNonQueryAsync(cancellation ?? ct); }
        async Task<long> Count(string table)
        { using var command = new NpgsqlCommand($"SELECT count(*) FROM {schema}.{table}", connection) { CommandTimeout = 5 }; return (long)(await command.ExecuteScalarAsync(ct))!; }
        async Task Until(Func<Task<bool>> condition)
        { while (!await condition()) await Task.Delay(50, ct); }
    }

    private sealed class CaptureLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }
        private sealed class Capture(CaptureLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
            {
                if (owner.Lines.Count >= 2000) throw new InvalidOperationException("Synthetic ingress log budget exceeded.");
                owner.Lines.Enqueue(formatter(state, error) + (error is null ? "" : error.ToString()));
            }
        }
    }
}
