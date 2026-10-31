using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Npgsql;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class ReceiptDatabaseConformanceTests
{
    public static string CreationSql(Guid caseId)
    {
        if (caseId == Guid.Empty) throw new InvalidOperationException("Nonempty fixture case required.");
        var schema = "t12_" + caseId.ToString("N");
        return $"CREATE SCHEMA {schema};\n" + OwnedMessagingSchema.TablesSql.Replace("msg.", schema + ".", StringComparison.Ordinal) +
            $"CREATE TABLE {schema}.owner_state (id integer PRIMARY KEY, version integer NOT NULL, effects integer NOT NULL);" +
            $"INSERT INTO {schema}.owner_state VALUES (1,1,0)";
    }

    [Fact]
    public async Task ImmutableReceiptsAndOwnerCasShareRootTransactionUnderContention()
    {
        var manifestPath = Environment.GetEnvironmentVariable("KORDRA_MSG_RECEIPT_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Explicit receipt fixture operation approval required before database access.");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var plan = manifest.RootElement;
        if (plan.GetProperty("status").GetString() != "approved_for_execution" || plan.GetProperty("purpose").GetString() != "T12.1d-receipts")
            throw new InvalidOperationException("Exact receipt fixture scope required.");
        var caseId = plan.GetProperty("caseId").GetGuid();
        if (caseId == Guid.Empty) throw new InvalidOperationException("Nonempty receipt fixture case required.");
        var schema = "t12_" + caseId.ToString("N");
        var creationSql = CreationSql(caseId);
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(creationSql))) != plan.GetProperty("creationSha256").GetString())
            throw new InvalidOperationException("Fixture SQL differs from the approved review artifact.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private test locator required.");
        AuditDbContextFactory.ValidateTestTarget(locator);
        var target = new NpgsqlConnectionStringBuilder(locator);
        if (!string.IsNullOrEmpty(target.SearchPath) || !string.IsNullOrEmpty(target.Options) || target.Multiplexing)
            throw new InvalidOperationException("Receipt fixture rejects session overrides and multiplexing.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var ct = budget.Token;
        await using var setup = new NpgsqlConnection(locator); await setup.OpenAsync(ct);
        var created = false;
        try
        {
            // CREATE without IF NOT EXISTS: an existing schema is never adopted or cleaned up by this fixture.
            await Sql(setup, null, $"CREATE SCHEMA {schema}", ct); created = true;
            await Sql(setup, null, creationSql[($"CREATE SCHEMA {schema};\n").Length..], ct);
            var envelope = FixtureEnvelope();
            await using var a = new NpgsqlConnection(locator); await a.OpenAsync(ct);
            await using var b = new NpgsqlConnection(locator); await b.OpenAsync(ct);
            using var mappedA = new MappedConnection(a, schema); using var mappedB = new MappedConnection(b, schema);

            // Before root commit an independent reader sees neither receipt nor owner effect.
            await using (var tx = await mappedA.BeginTransactionAsync(ct))
            {
                var result = await new CallerOwnedReceiptStore(mappedA, tx).StagePublicationAsync(envelope, envelope.Binding.OccurredAtUtc, ct);
                Assert.Equal(ReceiptStageDisposition.Staged, result.Disposition);
                Assert.Equal(0, await Scalar(setup, $"SELECT count(*) FROM {schema}.publication_receipts", ct));
                await tx.CommitAsync(ct);
            }
            Assert.Equal(1, await Scalar(setup, $"SELECT count(*) FROM {schema}.publication_receipts", ct));
            await using (var tx = await mappedA.BeginTransactionAsync(ct))
            {
                var store = new CallerOwnedReceiptStore(mappedA, tx);
                var duplicate = await store.StagePublicationAsync(FixtureEnvelope(envelope), envelope.Binding.OccurredAtUtc, ct);
                Assert.Equal(ReceiptStageDisposition.ExistingInTransaction, duplicate.Disposition);
                Assert.Equal(envelope.Delivery.MessageId, duplicate.OriginalMessageId);
                var conflict = await store.StagePublicationAsync(FixtureEnvelope(envelope, changedPayload: true), envelope.Binding.OccurredAtUtc, ct);
                Assert.Equal(ReceiptStageDisposition.Conflict, conflict.Disposition);
                await tx.RollbackAsync(ct);
            }
            // Two transactions contend on the full logical key. The losing transaction must not execute an effect.
            await using (var first = await mappedA.BeginTransactionAsync(ct))
            await using (var second = await mappedB.BeginTransactionAsync(ct))
            {
                Assert.Equal(ReceiptStageDisposition.Staged, (await new CallerOwnedReceiptStore(mappedA, first)
                    .StageLogicalAsync(envelope, LogicalReceiptOutcome.Completed, envelope.Binding.OccurredAtUtc, ct)).Disposition);
                var waiting = new CallerOwnedReceiptStore(mappedB, second).StageLogicalAsync(FixtureEnvelope(envelope),
                    LogicalReceiptOutcome.Completed, envelope.Binding.OccurredAtUtc, ct);
                try
                {
                    await AssertWaitingForOwnLock(setup, b.ProcessID, waiting, ct);
                    await Sql(a, ((MappedTransaction)first).Inner, $"UPDATE {schema}.owner_state SET effects=effects+1 WHERE id=1", ct);
                    Assert.Equal(0, await Scalar(setup, $"SELECT effects FROM {schema}.owner_state WHERE id=1", ct));
                    await first.CommitAsync(ct);
                    var result = await waiting;
                    Assert.Equal(ReceiptStageDisposition.ExistingInTransaction, result.Disposition);
                    Assert.Equal(LogicalReceiptOutcome.Completed, result.OriginalOutcome);
                    await second.CommitAsync(ct);
                }
                finally
                {
                    // Drain the owned command before fixture connections or schema are released, even on assertion failure.
                    if (!waiting.IsCompleted) { try { await first.RollbackAsync(CancellationToken.None); } catch (InvalidOperationException) { } }
                    try { await waiting; } catch when (budget.IsCancellationRequested) { }
                }
            }
            Assert.Equal(1, await Scalar(setup, $"SELECT effects FROM {schema}.owner_state WHERE id=1", ct));
            Assert.Equal(1, await Scalar(setup, $"SELECT count(*) FROM {schema}.logical_receipts", ct));
            await using (var tx = await mappedA.BeginTransactionAsync(ct))
            {
                var conflict = await new CallerOwnedReceiptStore(mappedA, tx).StageLogicalAsync(
                    FixtureEnvelope(envelope, changedPayload: true), LogicalReceiptOutcome.Completed, envelope.Binding.OccurredAtUtc, ct);
                Assert.Equal(ReceiptStageDisposition.Conflict, conflict.Disposition);
                Assert.Null(conflict.OriginalOutcome);
                await tx.RollbackAsync(ct);
            }

            // A failed owner precondition rolls back the provisional terminal receipt; it cannot become success.
            var next = FixtureEnvelope();
            await using (var tx = await mappedA.BeginTransactionAsync(ct))
            {
                Assert.Equal(ReceiptStageDisposition.Staged, (await new CallerOwnedReceiptStore(mappedA, tx)
                    .StageLogicalAsync(next, LogicalReceiptOutcome.Completed, next.Binding.OccurredAtUtc, ct)).Disposition);
                Assert.Equal(0, await Sql(a, ((MappedTransaction)tx).Inner,
                    $"UPDATE {schema}.owner_state SET version=3,effects=effects+1 WHERE id=1 AND version=2", ct));
                await tx.RollbackAsync(ct);
            }
            Assert.Equal(1, await Scalar(setup, $"SELECT count(*) FROM {schema}.logical_receipts", ct));
            Assert.Equal(1, await Scalar(setup, $"SELECT version FROM {schema}.owner_state WHERE id=1", ct));
            await using (var tx = await mappedA.BeginTransactionAsync(ct))
            {
                Assert.Equal(ReceiptStageDisposition.Staged, (await new CallerOwnedReceiptStore(mappedA, tx)
                    .StageLogicalAsync(next, LogicalReceiptOutcome.Completed, next.Binding.OccurredAtUtc, ct)).Disposition);
                Assert.Equal(1, await Sql(a, ((MappedTransaction)tx).Inner,
                    $"UPDATE {schema}.owner_state SET version=2,effects=effects+1 WHERE id=1 AND version=1", ct));
                await tx.CommitAsync(ct);
            }
            Assert.Equal(2, await Scalar(setup, $"SELECT count(*) FROM {schema}.logical_receipts", ct));
            Assert.Equal(2, await Scalar(setup, $"SELECT effects FROM {schema}.owner_state WHERE id=1", ct));
            // A terminal denial is immutable and must not acquire a business effect on a later delivery.
            var denied = FixtureEnvelope();
            await using (var tx = await mappedA.BeginTransactionAsync(ct))
            {
                Assert.Equal(ReceiptStageDisposition.Staged, (await new CallerOwnedReceiptStore(mappedA, tx)
                    .StageLogicalAsync(denied, LogicalReceiptOutcome.Denied, denied.Binding.OccurredAtUtc, ct)).Disposition);
                await tx.CommitAsync(ct);
            }
            await using (var tx = await mappedB.BeginTransactionAsync(ct))
            {
                var existing = await new CallerOwnedReceiptStore(mappedB, tx).StageLogicalAsync(FixtureEnvelope(denied),
                    LogicalReceiptOutcome.Completed, denied.Binding.OccurredAtUtc, ct);
                Assert.Equal(ReceiptStageDisposition.ExistingInTransaction, existing.Disposition);
                Assert.Equal(LogicalReceiptOutcome.Denied, existing.OriginalOutcome);
                await tx.RollbackAsync(ct);
            }
            Assert.Equal(3, await Scalar(setup, $"SELECT count(*) FROM {schema}.logical_receipts", ct));
            Assert.Equal(2, await Scalar(setup, $"SELECT effects FROM {schema}.owner_state WHERE id=1", ct));
        }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await Sql(setup, null, $"DROP SCHEMA {schema} CASCADE", cleanup.Token);
                using var check = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", setup);
                check.Parameters.AddWithValue("schema", schema);
                Assert.Equal(0L, await check.ExecuteScalarAsync(cleanup.Token));
            }
        }
    }

    private static async Task AssertWaitingForOwnLock(NpgsqlConnection observer, int ownPid, Task pending, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            Assert.False(pending.IsCompleted, "Unique-key contender completed before root transaction released its key.");
            using var command = new NpgsqlCommand("SELECT wait_event_type FROM pg_stat_activity WHERE pid=@pid AND usename=current_user AND datname=current_database()", observer);
            command.Parameters.AddWithValue("pid", ownPid);
            if (await command.ExecuteScalarAsync(ct) is string value && value == "Lock") return;
            await Task.Delay(10, ct);
        }
        throw new InvalidOperationException("Owned contender lock evidence not observed.");
    }
    private static async Task<int> Sql(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken ct)
    { using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 5 }; return await command.ExecuteNonQueryAsync(ct); }
    private static async Task<long> Scalar(NpgsqlConnection connection, string sql, CancellationToken ct)
    { using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 5 }; return Convert.ToInt64(await command.ExecuteScalarAsync(ct)); }

    private static VerifiedMessageEnvelope FixtureEnvelope(VerifiedMessageEnvelope? original = null, bool changedPayload = false)
    {
        var registry = new CompiledMessageRegistry([new CompiledMessageRegistration<bool>(new("fixture.observe", 1, MessageScopeKind.Tenant, false),
            new BooleanCodec(), new Consumer(), "fixture.observe", new())]);
        var codec = new MessageEnvelopeCodec(registry);
        var now = original?.Binding.OccurredAtUtc ?? DateTimeOffset.UtcNow;
        var binding = original?.Binding ?? new MessageBinding(new(Guid.NewGuid(), MessageScope.Tenant(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), 1, "fixture.observe"), 1, null, now, now.AddSeconds(120));
        var wire = codec.Write(binding, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), changedPayload ? "{\"enabled\":false}"u8 : "{\"enabled\":true}"u8);
        return codec.Read(wire, MessageEnvelopeCodec.ContentType, binding.Identity.InstallationId, binding.Identity.Scope, now);
    }
    private sealed class BooleanCodec : IMessagePayloadCodec<bool>
    {
        public bool Read(JsonElement payload) => payload.GetProperty("enabled").GetBoolean();
        public void Write(Utf8JsonWriter writer, bool payload) { writer.WriteStartObject(); writer.WriteBoolean("enabled", payload); writer.WriteEndObject(); }
    }
    private sealed class Consumer : IMessageConsumer<bool>
    { public ValueTask HandleAsync(bool message, CancellationToken cancellationToken) => throw new InvalidOperationException("Receipt fixture must not dispatch."); }

    // Test-only UUID remapping of compiled msg-qualified SQL; production store has no schema override API.
    // No connection fallback: command and transaction always unwrap to the exact caller-owned Npgsql pair.
    private sealed class MappedConnection(NpgsqlConnection inner, string schema) : DbConnection
    {
        [AllowNull] public override string ConnectionString { get => inner.ConnectionString; set => throw new NotSupportedException(); }
        public override string Database => inner.Database;
        public override string DataSource => inner.DataSource;
        public override string ServerVersion => inner.ServerVersion;
        public override ConnectionState State => inner.State;
        public override void Open() => throw new InvalidOperationException("No fallback open.");
        public override void Close() => throw new InvalidOperationException("Fixture retains connection custody.");
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new MappedTransaction(this, inner.BeginTransaction(isolationLevel));
        protected override DbCommand CreateDbCommand() => new MappedCommand(this, inner.CreateCommand(), schema);
        protected override void Dispose(bool disposing) { /* The fixture retains the inner connection. */ }
    }
    private sealed class MappedTransaction(MappedConnection connection, NpgsqlTransaction inner) : DbTransaction
    {
        public NpgsqlTransaction Inner => inner;
        public override IsolationLevel IsolationLevel => inner.IsolationLevel;
        protected override DbConnection? DbConnection => inner.Connection is null ? null : connection;
        public override void Commit() => inner.Commit();
        public override void Rollback() => inner.Rollback();
        public override Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);
        public override Task RollbackAsync(CancellationToken ct = default) => inner.RollbackAsync(ct);
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
    private sealed class MappedCommand(MappedConnection connection, NpgsqlCommand inner, string schema) : DbCommand
    {
        private string _sql = "";
        private DbTransaction? _transaction;
        [AllowNull] public override string CommandText { get => _sql; set { _sql = value ?? ""; inner.CommandText = _sql.Replace("msg.", schema + ".", StringComparison.Ordinal); } }
        public override int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
        public override CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
        public override bool DesignTimeVisible { get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value; }
        public override UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
        protected override DbConnection? DbConnection { get => connection; set => throw new NotSupportedException(); }
        protected override DbTransaction? DbTransaction { get => _transaction; set { _transaction = value; inner.Transaction = value is null ? null : ((MappedTransaction)value).Inner; } }
        protected override DbParameterCollection DbParameterCollection => inner.Parameters;
        public override void Cancel() => inner.Cancel();
        public override void Prepare() => throw new NotSupportedException();
        public override int ExecuteNonQuery() => inner.ExecuteNonQuery();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken ct) => inner.ExecuteNonQueryAsync(ct);
        public override object? ExecuteScalar() => inner.ExecuteScalar();
        protected override DbParameter CreateDbParameter() => inner.CreateParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => inner.ExecuteReader(behavior);
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken ct) => await inner.ExecuteReaderAsync(behavior, ct);
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
