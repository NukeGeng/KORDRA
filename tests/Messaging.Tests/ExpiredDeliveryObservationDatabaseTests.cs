using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Npgsql;
using Wolverine;
using Wolverine.Postgresql;
using Wolverine.Postgresql.Transport;
using Xunit;

namespace Kordra.Messaging.Tests;

/// <summary>SQL observation qualification with synthetic metadata. Does not run or settle a native delivery.</summary>
public sealed class ExpiredDeliveryObservationDatabaseTests
{
    public static readonly Guid CaseId = Guid.Parse("63b488c2-b2ec-4e7b-bc62-e8080c072e64");
    private static string Schema => "t12_" + CaseId.ToString("N");
    private const string ManifestVariable = "KORDRA_MSG_EXPIRED_OBSERVATION_MANIFEST";
    private static string CreationSql => NativeIngressDatabaseTests.CreationSql(CaseId);
    private static string CleanupSql => "DROP SCHEMA " + Schema + " CASCADE;\n";

    [Fact]
    public void ExportObservationSchemaWithoutConnecting()
    {
        var sql = CreationSql;
        Assert.Equal(sql, CreationSql);
        Assert.Contains(Schema + ".quarantine_journal", sql);
        Assert.Contains(Schema + ".wolverine_dead_letters", sql);
        Assert.DoesNotContain("CREATE ROLE", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_MSG_EXPIRED_OBSERVATION_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        Write("creation.sql", sql);
        Write("cleanup.sql", CleanupSql);
        void Write(string name, string content)
        {
            var file = Path.Combine(directory, name);
            if (File.Exists(file)) Assert.Equal(content, File.ReadAllText(file));
            else File.WriteAllText(file, content);
        }
    }

    [ExpiredObservationFact]
    public async Task ExactTerminalMetadataRejectsPartialAmbiguousReplayableOrPendingEvidence()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(
            Environment.GetEnvironmentVariable(ManifestVariable) ?? throw new InvalidOperationException("Exact operation approval required.")));
        var approval = manifest.RootElement;
        if (approval.GetProperty("status").GetString() != "approved_for_execution" ||
            approval.GetProperty("caseId").GetGuid() != CaseId ||
            approval.GetProperty("creationSha256").GetString() != Hash(CreationSql) ||
            approval.GetProperty("cleanupSha256").GetString() != Hash(CleanupSql))
            throw new InvalidOperationException("Frozen observation SQL approval required.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_MSG_EXPIRED_OBSERVATION_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private isolated test locator required.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = budget.Token;
        await using var writer = new NpgsqlConnection(locator);
        await using var reader = new NpgsqlConnection(locator);
        var inspector = MessagingFixtureSelection.MessageReceipt(reader, CaseId);
        _ = MessagingFixtureSelection.MessageReceipt(writer, CaseId);
        var options = new WolverineOptions();
        options.UsePostgresqlPersistenceAndTransport(locator, Schema, Schema);
        options.ListenToPostgresqlQueue("nativecompatibility");
        options.PublishMessage<string>().ToPostgresqlQueue("native_result");
        var transport = options.Transports.GetOrCreate<PostgresqlTransport>();
        await writer.OpenAsync(ct);
        await reader.OpenAsync(ct);
        Assert.Equal(0L, await Scalar("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", ("schema", Schema)));
        var created = false;
        try
        {
            await using (var ddl = await writer.BeginTransactionAsync(ct))
            {
                await Execute(CreationSql, ddl);
                await ddl.CommitAsync(ct);
                created = true;
            }
            var scenarios = new[] { "matching", "missing-journal", "wrong-hash", "wrong-installation", "wrong-reason",
                "future-journal", "missing-dlq", "replayable", "null-replay", "archived", "wrong-route", "wrong-type",
                "ambiguous", "outgoing", "incoming", "scheduled-inbox", "unknown-inbox", "queue", "scheduled-queue",
                "other-queue", "handled" };
            foreach (var scenario in scenarios)
            {
                var message = NativeWireFixture.Create(CaseId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-3));
                var id = message.Delivery.MessageId;
                var now = DateTimeOffset.UtcNow;
                var destination = new Uri("postgresql://nativecompatibility").ToString();
                await using (var seed = await writer.BeginTransactionAsync(ct))
                {
                    if (scenario != "missing-journal")
                        await Execute($"INSERT INTO {Schema}.quarantine_journal VALUES (@id,@installation,@reason,@hash,@time)", seed,
                            ("id", id), ("installation", scenario == "wrong-installation" ? Guid.NewGuid() : CaseId),
                            ("reason", scenario == "wrong-reason" ? "UnknownCommit" : "Expired"),
                            ("hash", scenario == "wrong-hash" ? new string('f', 64) : message.PayloadHash),
                            ("time", scenario == "future-journal" ? now.AddMinutes(1) : now.AddSeconds(-1)));
                    if (scenario != "missing-dlq")
                    {
                        await DeadLetter(scenario == "wrong-route" ? "postgresql://elsewhere" : destination);
                        if (scenario == "ambiguous") await DeadLetter("postgresql://duplicate");
                    }
                    if (scenario == "outgoing")
                        await Execute($"INSERT INTO {Schema}.wolverine_outgoing_envelopes(id,owner_id,destination,body,message_type) " +
                            "VALUES (@id,0,@destination,@body,@type)", seed,
                            ("id", id), ("destination", destination), ("body", new byte[] { 0 }), ("type", NativeWireFixture.TypeId));
                    if (scenario is "incoming" or "scheduled-inbox" or "unknown-inbox" or "handled")
                        await Execute($"INSERT INTO {Schema}.wolverine_incoming_envelopes(id,status,owner_id,body,message_type,received_at) " +
                            "VALUES (@id,@status,0,@body,@type,@destination)", seed, ("id", id),
                            ("status", scenario == "handled" ? "Handled" : scenario == "incoming" ? "Incoming" :
                                scenario == "scheduled-inbox" ? "Scheduled" : "Unknown"),
                            ("body", new byte[] { 0 }), ("type", NativeWireFixture.TypeId), ("destination", destination));
                    if (scenario is "queue" or "scheduled-queue" or "other-queue")
                    {
                        var table = scenario == "other-queue" ? "native_result" : "nativecompatibility";
                        var columns = scenario == "scheduled-queue" ? ",execution_time" : "";
                        var values = scenario == "scheduled-queue" ? ",@time" : "";
                        if (scenario == "scheduled-queue") table += "_scheduled";
                        await Execute($"INSERT INTO {Schema}.wolverine_queue_{table}(id,body,message_type{columns}) " +
                            $"VALUES (@id,@body,@type{values})", seed, ("id", id), ("body", new byte[] { 0 }),
                            ("type", NativeWireFixture.TypeId), ("time", now.AddMinutes(1)));
                    }
                    Assert.False(await inspector.ObserveExpiredQuarantineAsync(message, NativeWireFixture.Registry(), transport, now, ct));
                    await seed.CommitAsync(ct);

                    async Task DeadLetter(string route) => await Execute(
                        $"INSERT INTO {Schema}.wolverine_dead_letters(id,body,message_type,received_at,replayable,archived_at) " +
                        "VALUES (@id,@body,@type,@route,@replay,@archived)", seed, ("id", id), ("body", new byte[] { 0 }),
                        ("type", scenario == "wrong-type" ? NativeWireFixture.TypeId : RejectedEnvelopeDiagnostic.TypeId),
                        ("route", route), ("replay", scenario == "null-replay" ? DBNull.Value : scenario == "replayable"),
                        ("archived", scenario == "archived" ? now : DBNull.Value));
                }
                var observation = await inspector.ObserveExpiredQuarantineAsync(message, NativeWireFixture.Registry(), transport, now, ct);
                Assert.True(observation == (scenario is "matching" or "handled"), "Unexpected SQL observation for " + scenario);
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (created)
            {
                await using var drop = new NpgsqlCommand(CleanupSql, writer) { CommandTimeout = 5 };
                await drop.ExecuteNonQueryAsync(cleanup.Token);
            }
            await using var verify = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", reader);
            verify.Parameters.AddWithValue("schema", Schema);
            Assert.Equal(0L, await verify.ExecuteScalarAsync(cleanup.Token));
        }
        async Task Execute(string sql, NpgsqlTransaction transaction, params (string Name, object Value)[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, writer, transaction) { CommandTimeout = 3 };
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync(ct);
        }
        async Task<object?> Scalar(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, reader) { CommandTimeout = 3 };
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return await command.ExecuteScalarAsync(ct);
        }
    }

    private static string Hash(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    public sealed class ExpiredObservationFactAttribute : FactAttribute
    {
        public ExpiredObservationFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ManifestVariable)))
                Skip = "Requires exact UUID PostgreSQL observation fixture approval.";
        }
    }
}
