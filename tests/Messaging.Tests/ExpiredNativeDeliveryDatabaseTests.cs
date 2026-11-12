using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine;
using Wolverine.Postgresql.Transport;
using Wolverine.Runtime;
using Wolverine.Runtime.Serialization;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class ExpiredNativeDeliveryDatabaseTests
{
    public static readonly Guid CaseId = Guid.Parse("a71684ed-08f2-414c-b644-f17c5fbb64b7");
    private static string Schema => "t12_" + CaseId.ToString("N");
    private const string ManifestVariable = "KORDRA_MSG_EXPIRED_NATIVE_MANIFEST";
    private static string CreationSql => NativeIngressDatabaseTests.CreationSql(CaseId);
    private static string CleanupSql => "DROP SCHEMA " + Schema + " CASCADE;\n";

    [Fact]
    public void ExportNativeExpirySqlWithoutConnecting()
    {
        var sql = CreationSql;
        Assert.Equal(sql, CreationSql);
        Assert.Contains(Schema + ".wolverine_dead_letters", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_MSG_EXPIRED_NATIVE_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, content) in new[] { ("creation.sql", sql), ("cleanup.sql", CleanupSql) })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) Assert.Equal(content, File.ReadAllText(path));
            else File.WriteAllText(path, content);
        }
    }

    [ExpiredNativeFact]
    public async Task NativeExpiredDeliveryRetainsDiagnosticAndRequiresCommittedJournalWithoutBusinessEffects()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Environment.GetEnvironmentVariable(ManifestVariable) ?? throw new InvalidOperationException("Exact native expiry approval required.")));
        var manifest = document.RootElement;
        if (manifest.GetProperty("status").GetString() != "approved_for_execution" ||
            manifest.GetProperty("caseId").GetGuid() != CaseId ||
            manifest.GetProperty("creationSha256").GetString() != Hash(CreationSql) ||
            manifest.GetProperty("cleanupSha256").GetString() != Hash(CleanupSql))
            throw new InvalidOperationException("Native expiry fixture differs from the approved SQL.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_MSG_EXPIRED_NATIVE_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private local test locator required.");
        var settings = new NpgsqlConnectionStringBuilder(locator) { Timeout = 3, CommandTimeout = 3 };
        if (Kordra.Testing.RegressionPostgreSqlSelection.LoadRunnerProfile() is null)
            settings.MaxPoolSize = 4;
        await using var writer = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Pooling = false }.ConnectionString);
        await using var observer = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(settings.ConnectionString) { Pooling = false }.ConnectionString);
        var inspector = MessagingFixtureSelection.MessageReceipt(observer, CaseId);
        _ = MessagingFixtureSelection.MessageReceipt(writer, CaseId);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        await writer.OpenAsync(ct); await observer.OpenAsync(ct);
        Assert.Equal(0L, await CountNamespace(ct));
        using var host = CompatibilityProgram.Builder(settings.ConnectionString, CaseId, "ingress", true)
            .ConfigureLogging(logging => logging.ClearProviders()).Build();
        var created = false; var started = false;
        try
        {
            await using (var ddl = await writer.BeginTransactionAsync(ct))
            {
                await using var command = new NpgsqlCommand(CreationSql, writer, ddl);
                await command.ExecuteNonQueryAsync(ct); await ddl.CommitAsync(ct); created = true;
            }
            await host.StartAsync(ct); started = true;
            var message = NativeWireFixture.Create(CaseId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-3));
            var wire = new MessageEnvelopeCodec(NativeWireFixture.Registry()).Write(message.Binding, message.Delivery, message.CopyPayload());
            var destination = new Uri("postgresql://nativecompatibility");
            await host.Services.GetRequiredService<IMessageBus>().EndpointFor(destination).SendRawMessageAsync(wire, configure: envelope => {
                envelope.Id = message.Delivery.MessageId; envelope.Destination = destination;
                envelope.MessageType = NativeWireFixture.TypeId; envelope.ContentType = MessageEnvelopeCodec.ContentType;
            });
            while (await Count("wolverine_dead_letters", ct) != 1) await Task.Delay(50, ct);
            await host.StopAsync(ct); started = false;
            await using (var command = new NpgsqlCommand($"SELECT id,body,replayable FROM {Schema}.wolverine_dead_letters", observer))
            await using (var row = await command.ExecuteReaderAsync(ct))
            {
                Assert.True(await row.ReadAsync(ct));
                Assert.Equal(message.Delivery.MessageId, row.GetGuid(0));
                var diagnostic = EnvelopeSerializer.Deserialize(row.GetFieldValue<byte[]>(1));
                Assert.True(RejectedEnvelopeDiagnostic.MatchesExpired(diagnostic, wire, CaseId, message.Delivery.MessageId, destination));
                Assert.False(row.IsDBNull(2));
                Assert.False(row.GetBoolean(2));
                Assert.False(await row.ReadAsync(ct));
            }
            var transport = host.Services.GetRequiredService<IWolverineRuntime>().Options.Transports.GetOrCreate<PostgresqlTransport>();
            Assert.False(await inspector.ObserveExpiredQuarantineAsync(message, NativeWireFixture.Registry(), transport, DateTimeOffset.UtcNow, ct));
            foreach (var commit in new[] { false, true })
            {
                await using var root = await writer.BeginTransactionAsync(ct);
                await MessagingFixtureSelection.Journal(writer, root, CaseId).StageQuarantineAsync(
                    message.Delivery.MessageId, CaseId, MessageQuarantineReason.Expired, message.PayloadHash, DateTimeOffset.UtcNow, ct);
                Assert.False(await inspector.ObserveExpiredQuarantineAsync(message, NativeWireFixture.Registry(), transport, DateTimeOffset.UtcNow, ct));
                if (commit) await root.CommitAsync(ct); else await root.RollbackAsync(ct);
                Assert.Equal(commit, await inspector.ObserveExpiredQuarantineAsync(message, NativeWireFixture.Registry(), transport, DateTimeOffset.UtcNow, ct));
            }
            Assert.Equal(0L, await Count("fixture_effect", ct));
            Assert.Equal(0L, await Count("logical_receipts", ct));
            Assert.Equal(0L, await Count("aud_intents", ct));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { if (started) await host.StopAsync(cleanup.Token); }
            finally
            {
                if (created)
                {
                    await using var drop = new NpgsqlCommand(CleanupSql, writer);
                    await drop.ExecuteNonQueryAsync(cleanup.Token);
                }
                Assert.Equal(0L, await CountNamespace(cleanup.Token));
            }
        }
        async Task<object?> CountNamespace(CancellationToken token)
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", observer);
            command.Parameters.AddWithValue("schema", Schema);
            return await command.ExecuteScalarAsync(token);
        }
        async Task<long> Count(string table, CancellationToken token)
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM {Schema}.{table}", observer);
            return (long)(await command.ExecuteScalarAsync(token))!;
        }
    }

    private static string Hash(string sql) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
    public sealed class ExpiredNativeFactAttribute : FactAttribute
    {
        public ExpiredNativeFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ManifestVariable)))
                Skip = "Requires exact native expiry UUID fixture approval.";
        }
    }
}
