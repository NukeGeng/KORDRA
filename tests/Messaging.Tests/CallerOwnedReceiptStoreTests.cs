using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Npgsql;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class CallerOwnedReceiptStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewReceiptIsOnlyStagedOnCallerTransaction(bool publication)
    {
        var envelope = Envelope();
        using var connection = new EvidenceConnection(); using var transaction = new EvidenceTransaction(connection);
        var store = new CallerOwnedReceiptStore(connection, transaction);
        var result = publication ? await store.StagePublicationAsync(envelope, Now, default)
            : await store.StageLogicalAsync(envelope, LogicalReceiptOutcome.Completed, Now, default);
        Assert.Equal(ReceiptStageDisposition.Staged, result.Disposition);
        Assert.Equal(publication ? envelope.Delivery.MessageId : (Guid?)null, result.OriginalMessageId);
        Assert.Equal(publication ? null : (LogicalReceiptOutcome?)LogicalReceiptOutcome.Completed, result.OriginalOutcome);
        var command = Assert.Single(connection.Commands);
        Assert.Same(transaction, command.Transaction); Assert.Equal(5, command.CommandTimeout);
        Assert.Contains("ON CONFLICT (installation_id, scope_kind, tenant_key, operation_id, step_id, step_version, type_id) DO NOTHING", command.CommandText);
        Assert.DoesNotContain(envelope.PayloadHash, command.CommandText);
        Assert.Equal(envelope.PayloadHash, command.Parameters["hash"].Value);
        Assert.Equal(envelope.Binding.Identity.Scope.TenantId, command.Parameters["tenant"].Value);
        Assert.Equal(envelope.Binding.DeadlineUtc.UtcDateTime, command.Parameters["deadline"].Value);
        Assert.Equal(0, transaction.Commits); Assert.Equal(0, transaction.Rollbacks);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task DuplicateKeepsOriginalResultAndConflictDisclosesNone(bool publication, bool sameHash)
    {
        var envelope = Envelope(); var originalMessage = Guid.NewGuid();
        using var connection = new EvidenceConnection { InsertCount = 0 };
        connection.Rows.Columns.Add("hash", typeof(string));
        connection.Rows.Columns.Add("value", publication ? typeof(Guid) : typeof(string));
        connection.Rows.Rows.Add(sameHash ? envelope.PayloadHash : new string('f', 64), publication ? originalMessage : "Denied");
        using var transaction = new EvidenceTransaction(connection);
        var store = new CallerOwnedReceiptStore(connection, transaction);
        var result = publication ? await store.StagePublicationAsync(envelope, Now, default)
            : await store.StageLogicalAsync(envelope, LogicalReceiptOutcome.Completed, Now, default);
        Assert.Equal(sameHash ? ReceiptStageDisposition.ExistingInTransaction : ReceiptStageDisposition.Conflict, result.Disposition);
        Assert.Equal(sameHash && publication ? originalMessage : (Guid?)null, result.OriginalMessageId);
        Assert.Equal(sameHash && !publication ? LogicalReceiptOutcome.Denied : (LogicalReceiptOutcome?)null, result.OriginalOutcome);
        Assert.Equal(2, connection.Commands.Count);
        foreach (var command in connection.Commands)
        {
            Assert.Same(transaction, command.Transaction); Assert.Equal(5, command.CommandTimeout);
            Assert.Equal(envelope.Binding.Identity.InstallationId, command.Parameters["installation"].Value);
            Assert.Equal(envelope.Binding.Identity.OperationId, command.Parameters["operation"].Value);
            Assert.Equal(envelope.Binding.Identity.StepId, command.Parameters["step"].Value);
            Assert.Equal(envelope.Binding.Identity.StepVersion, command.Parameters["version"].Value);
            Assert.Equal(envelope.Binding.Identity.TypeId, command.Parameters["type"].Value);
            Assert.DoesNotContain("UPDATE", command.CommandText); Assert.DoesNotContain("DELETE", command.CommandText);
        }
        Assert.Equal(0, transaction.Commits); Assert.Equal(0, transaction.Rollbacks);
    }

    [Fact]
    public async Task UnresolvedConflictAndDatabaseFailureDoNotBecomeSuccessOrRetry()
    {
        using var connection = new EvidenceConnection { InsertCount = 0 };
        using var transaction = new EvidenceTransaction(connection);
        var store = new CallerOwnedReceiptStore(connection, transaction);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePublicationAsync(Envelope(), Now, default));
        Assert.Equal(2, connection.Commands.Count);
        connection.Commands.Clear(); connection.FailWrite = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePublicationAsync(Envelope(), Now, default));
        Assert.Single(connection.Commands); Assert.Equal(0, transaction.Commits); Assert.Equal(0, transaction.Rollbacks);
    }

    [Fact]
    public async Task InvalidBindingCancellationAndOutcomeFailBeforeCommand()
    {
        using var connection = new EvidenceConnection(); using var other = new EvidenceConnection();
        using var transaction = new EvidenceTransaction(connection); using var wrong = new EvidenceTransaction(other);
        Assert.Throws<InvalidOperationException>(() => new CallerOwnedReceiptStore(connection, wrong));
        var store = new CallerOwnedReceiptStore(connection, transaction);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.StagePublicationAsync(Envelope(), Now, new(true)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StagePublicationAsync(Envelope(), default, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.StageLogicalAsync(Envelope(), (LogicalReceiptOutcome)99, Now, default));
        transaction.Detach();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePublicationAsync(Envelope(), Now, default));
        Assert.Empty(connection.Commands); Assert.Equal(0, connection.OpenCalls);
        connection.Close(); Assert.Throws<InvalidOperationException>(() => new CallerOwnedReceiptStore(connection, transaction));
    }

    [Fact]
    public async Task TimeMustBeRecheckedWhenStagingNotOnlyWhenWireWasRead()
    {
        var envelope = Envelope();
        using var connection = new EvidenceConnection(); using var transaction = new EvidenceTransaction(connection);
        var store = new CallerOwnedReceiptStore(connection, transaction);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePublicationAsync(envelope, Now.AddTicks(-1), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StageLogicalAsync(envelope,
            LogicalReceiptOutcome.Completed, envelope.Binding.DeadlineUtc, default));
        Assert.Empty(connection.Commands);
    }

    [Fact]
    public async Task ExplicitSystemScopeUsesZeroMarkerWithoutBecomingTenantScope()
    {
        using var connection = new EvidenceConnection(); using var transaction = new EvidenceTransaction(connection);
        await new CallerOwnedReceiptStore(connection, transaction).StagePublicationAsync(Envelope(true), Now, default);
        var command = Assert.Single(connection.Commands);
        Assert.Equal("system", command.Parameters["scope"].Value);
        Assert.Equal(Guid.Empty, command.Parameters["tenant"].Value);
    }

    internal static VerifiedMessageEnvelope Envelope(bool system = false)
    {
        var scope = system ? MessageScope.System() : MessageScope.Tenant(Guid.NewGuid());
        var registry = new CompiledMessageRegistry([new CompiledMessageRegistration<bool>(
            new("fixture.observe", 1, system ? MessageScopeKind.System : MessageScopeKind.Tenant, false),
            new BooleanCodec(), new Consumer(), "fixture.observe", new())]);
        var codec = new MessageEnvelopeCodec(registry);
        var binding = new MessageBinding(new(Guid.NewGuid(), scope, Guid.NewGuid(), Guid.NewGuid(), 1, "fixture.observe"),
            1, null, Now, Now.AddSeconds(120));
        var wire = codec.Write(binding, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), "{\"enabled\":true}"u8);
        return codec.Read(wire, MessageEnvelopeCodec.ContentType, binding.Identity.InstallationId, scope, Now);
    }
    private sealed class BooleanCodec : IMessagePayloadCodec<bool>
    {
        public bool Read(JsonElement payload) => payload.GetProperty("enabled").GetBoolean();
        public void Write(Utf8JsonWriter writer, bool payload)
        { writer.WriteStartObject(); writer.WriteBoolean("enabled", payload); writer.WriteEndObject(); }
    }
    private sealed class Consumer : IMessageConsumer<bool>
    { public ValueTask HandleAsync(bool message, CancellationToken cancellationToken) => throw new InvalidOperationException("Never dispatch in store tests."); }

    // Command/transaction contract evidence only; not PostgreSQL uniqueness, concurrency or durability evidence.
    internal sealed class EvidenceConnection(string database = "synthetic", string dataSource = "in-memory") : DbConnection
    {
        private ConnectionState _state = ConnectionState.Open;
        public int InsertCount { get; set; } = 1;
        public bool FailWrite { get; set; }
        public Exception? ReadFailure { get; set; }
        public object? ScalarResult { get; set; }
        public int OpenCalls { get; private set; }
        public List<EvidenceCommand> Commands { get; } = [];
        public DataTable Rows { get; } = new();
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => database;
        public override string DataSource => dataSource;
        public override string ServerVersion => "fixture";
        public override ConnectionState State => _state;
        public override void Open() { OpenCalls++; throw new InvalidOperationException("No connection opening allowed."); }
        public override void Close() => _state = ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand()
        { var command = new EvidenceCommand(this); Commands.Add(command); return command; }
    }
    internal sealed class EvidenceTransaction(EvidenceConnection connection) : DbTransaction
    {
        private DbConnection? _connection = connection;
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection? DbConnection => _connection;
        public void Detach() => _connection = null;
        public override void Commit() { Commits++; throw new InvalidOperationException("Store must not commit."); }
        public override void Rollback() { Rollbacks++; throw new InvalidOperationException("Store must not rollback caller."); }
    }
    internal sealed class EvidenceCommand(EvidenceConnection connection) : DbCommand
    {
        private readonly NpgsqlCommand _parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
        public override void Cancel() { }
        public override void Prepare() => throw new NotSupportedException();
        public override int ExecuteNonQuery() => connection.FailWrite ? throw new InvalidOperationException("Synthetic write failure.") : connection.InsertCount;
        public override object? ExecuteScalar() => connection.ReadFailure is { } failure ? throw failure : connection.ScalarResult;
        protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            connection.ReadFailure is { } failure ? throw failure : connection.Rows.CreateDataReader();
    }
}
