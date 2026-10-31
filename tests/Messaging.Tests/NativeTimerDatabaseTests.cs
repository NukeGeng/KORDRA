using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.RDBMS;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeTimerDatabaseTests
{
    [Fact]
    public async Task NativeScheduledInboxCommitsAtomicallyAndSurvivesHostRestartWithoutEarlyEffect()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("KORDRA_MSG_TIMER_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact native timer operation approval required before database access.")));
        var plan = manifest.RootElement;
        if (plan.GetProperty("status").GetString() != "approved_for_execution" || plan.GetProperty("purpose").GetString() != "T12.1e-timer")
            throw new InvalidOperationException("Exact native timer scope required.");
        var caseId = plan.GetProperty("caseId").GetGuid(); var schema = "t12_" + caseId.ToString("N");
        var ddl = NativeIngressDatabaseTests.CreationSql(caseId);
        var actualDdlHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ddl)));
        if (actualDdlHash != plan.GetProperty("creationSha256").GetString())
        {
            var product = new DirectoryInfo(AppContext.BaseDirectory);
            while (product is not null && !File.Exists(Path.Combine(product.FullName, "Kordra.slnx"))) product = product.Parent;
            var directory = Path.Combine(product?.FullName ?? throw new InvalidOperationException("Product root missing."),
                ".amr-local", "t12-1-design", "schema-review");
            if (Directory.Exists(directory) && new DirectoryInfo(directory).LinkTarget is null)
            {
                var output = Path.Combine(directory, "timer-observed-" + actualDdlHash + ".sql");
                if (!File.Exists(output)) await File.WriteAllTextAsync(output, ddl);
            }
            throw new InvalidOperationException("Timer DDL differs from approved review artifact; observed SHA-256=" + actualDdlHash + "; case=" + caseId.ToString("N"));
        }
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private exact timer fixture locator required.");
        AuditDbContextFactory.ValidateTestTarget(locator);
        var target = new NpgsqlConnectionStringBuilder(locator);
        if (target.Multiplexing || !string.IsNullOrEmpty(target.Options) || !string.IsNullOrEmpty(target.SearchPath))
            throw new InvalidOperationException("Timer fixture rejects session overrides.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(55)); var ct = budget.Token;
        await using var observer = new NpgsqlConnection(locator); await observer.OpenAsync(ct);
        var created = false;
        using var first = CompatibilityProgram.Builder(locator, caseId, "timer-producer", false).Build();
        using var restarted = CompatibilityProgram.Builder(locator, caseId, "consumer", true).Build();
        try
        {
            await Sql($"CREATE SCHEMA {schema}"); created = true;
            await Sql(ddl[($"CREATE SCHEMA {schema};\n").Length..]);
            await first.StartAsync(ct);
            var store = Assert.IsAssignableFrom<IMessageDatabase>(first.Services.GetRequiredService<IMessageStore>());
            var now = DateTimeOffset.UtcNow; var due = now.AddSeconds(12);
            var verified = NativeWireFixture.Create(caseId, Guid.NewGuid(), now);
            var wire = new MessageEnvelopeCodec(NativeWireFixture.Registry()).Write(verified.Binding, verified.Delivery, verified.CopyPayload());
            var envelope = new Envelope { Id = verified.Delivery.MessageId, Destination = new Uri("postgresql://nativecompatibility"),
                Data = wire, ContentType = MessageEnvelopeCodec.ContentType, MessageType = NativeWireFixture.TypeId,
                ScheduledTime = due, Status = EnvelopeStatus.Scheduled, Attempts = 1 };
            await using (var writer = new NpgsqlConnection(locator))
            {
                await writer.OpenAsync(ct);
                await using (var root = await writer.BeginTransactionAsync(ct))
                {
                    await store.StoreIncomingAsync(root, [envelope]);
                    Assert.Equal(0, await Count("wolverine_incoming_envelopes"));
                    await root.RollbackAsync(ct); // Actual owner-side completion, not best-effort native ClearAllAsync.
                }
                Assert.Equal(0, await Count("wolverine_incoming_envelopes"));
                await using (var root = await writer.BeginTransactionAsync(ct))
                {
                    await store.StoreIncomingAsync(root, [envelope]);
                    Assert.Equal(0, await Count("wolverine_incoming_envelopes"));
                    await root.CommitAsync(ct);
                }
            }
            await first.StopAsync(ct);
            Assert.Equal(1, await Count("wolverine_incoming_envelopes"));
            Assert.Equal(0, await Count("fixture_effect")); Assert.Equal(0, await Count("logical_receipts"));
            using (var query = new NpgsqlCommand($"SELECT execution_time,attempts FROM {schema}.wolverine_incoming_envelopes WHERE id=@id AND status='Scheduled'", observer))
            {
                query.Parameters.AddWithValue("id", envelope.Id);
                await using var row = await query.ExecuteReaderAsync(ct); Assert.True(await row.ReadAsync(ct));
                var persistedDue = new DateTimeOffset(row.GetDateTime(0));
                Assert.InRange((persistedDue - due).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
                Assert.Equal(1, row.GetInt32(1)); Assert.False(await row.ReadAsync(ct));
            }
            // Restart a new native host from the same committed inbox row; no memory schedule is carried over.
            Assert.True(DateTimeOffset.UtcNow < due, "Fixture startup exceeded its pre-due restart window.");
            await restarted.StartAsync(ct);
            while (DateTimeOffset.UtcNow < due.AddMilliseconds(-10))
            {
                Assert.Equal(0, await Count("fixture_effect")); await Task.Delay(50, ct);
            }
            while (await Count("fixture_effect") != 1) await Task.Delay(50, ct);
            using (var query = new NpgsqlCommand($"SELECT recorded_at_utc,binding_hash FROM {schema}.logical_receipts", observer))
            {
                await using var row = await query.ExecuteReaderAsync(ct); Assert.True(await row.ReadAsync(ct));
                Assert.True(new DateTimeOffset(row.GetDateTime(0)) >= due);
                Assert.Equal(verified.PayloadHash, row.GetString(1)); Assert.False(await row.ReadAsync(ct));
            }
            Assert.Equal(1, await Count("logical_receipts")); Assert.Equal(1, await Count("aud_intents"));
            using var handled = new NpgsqlCommand($"SELECT count(*) FROM {schema}.wolverine_incoming_envelopes WHERE id=@id AND status='Handled' AND keep_until>=@minimum", observer);
            handled.Parameters.AddWithValue("id", envelope.Id); handled.Parameters.AddWithValue("minimum", due.AddDays(7));
            Assert.Equal(1L, await handled.ExecuteScalarAsync(ct));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await first.StopAsync(cleanup.Token); await restarted.StopAsync(cleanup.Token); }
            finally { if (created) await Sql($"DROP SCHEMA {schema} CASCADE", cleanup.Token); }
        }
        async Task Sql(string sql, CancellationToken? token = null)
        { using var command = new NpgsqlCommand(sql, observer) { CommandTimeout = 5 }; await command.ExecuteNonQueryAsync(token ?? ct); }
        async Task<long> Count(string table)
        { using var command = new NpgsqlCommand($"SELECT count(*) FROM {schema}.{table}", observer) { CommandTimeout = 5 }; return (long)(await command.ExecuteScalarAsync(ct))!; }
    }
}
