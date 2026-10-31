using System.Reflection;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Npgsql;
using Wolverine;
using Wolverine.RDBMS;
using Xunit;
using static Kordra.Messaging.Tests.CallerOwnedReceiptStoreTests;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Wolverine.Runtime;

namespace Kordra.Messaging.Tests;

public sealed class HandledRetentionTests
{
    [Theory]
    [InlineData("Host=127.0.0.1;Database=kordra;Username=kordra_test")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=postgres")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=kordra_app")]
    [InlineData("Host=192.0.2.1;Database=kordra_test;Username=kordra_test")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Search Path=public")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Options=-c role=postgres")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Multiplexing=true")]
    public void IsolatedReceiptFactoryCannotWidenRuntimeRoleDatabaseOrSession(string locator)
    {
        using var connection = new NpgsqlConnection(locator);
        using var evidence = new EvidenceConnection(); using var transaction = new EvidenceTransaction(evidence);
        var error = Assert.Throws<InvalidOperationException>(() => CallerOwnedReceiptStore.CreateIsolatedTestStore(connection, transaction, Guid.NewGuid()));
        Assert.Equal("Exact local UUID messaging fixture target required.", error.Message);
        Assert.Throws<InvalidOperationException>(() => CallerOwnedMessageJournal.CreateIsolatedTestJournal(connection, transaction, Guid.NewGuid()));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task PublicationRejectsDifferentCallerAndEmptyCatalogBeforeAnyReceiptOrOutboxWrite()
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened");
        using var metadata = source.CreateConnection();
        using var connection = new EvidenceConnection(metadata.Database, metadata.DataSource);
        using var other = new EvidenceConnection(metadata.Database, metadata.DataSource);
        using var transaction = new EvidenceTransaction(connection); using var wrong = new EvidenceTransaction(other);
        var store = DispatchProxy.Create<IMessageDatabase, StoreProbe>(); ((StoreProbe)(object)store).Source = source;
        var stager = new CallerOwnedMessageStager(connection, transaction, store, "msg");
        var runtime = DispatchProxy.Create<IWolverineRuntime, RuntimeProbe>(); ((RuntimeProbe)(object)runtime).Store = store;
        var context = new MessageContext(runtime);
        await context.EnlistInOutboxAsync(new CallerOwnedEnvelopeEnlistment(stager, () => ValueTask.CompletedTask));
        var envelope = CallerOwnedReceiptStoreTests.Envelope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => stager.StagePublicationAsync(new(other, wrong), CompiledMessageRegistry.Empty,
            context, envelope, envelope.Binding.OccurredAtUtc, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => stager.StagePublicationAsync(new(connection, transaction), CompiledMessageRegistry.Empty,
            context, envelope, envelope.Binding.OccurredAtUtc, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => stager.StageConsumptionAsync(new(connection, transaction), CompiledMessageRegistry.Empty,
            context, envelope, LogicalReceiptOutcome.Completed, envelope.Binding.OccurredAtUtc, default));
        Assert.Empty(connection.Commands); Assert.Empty(other.Commands);
    }

    [Fact]
    public async Task CallerTimeProducesSevenDaysOnTheSameTransactionWithoutOpeningNativeStore()
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened");
        using var metadata = source.CreateConnection();
        using var connection = new EvidenceConnection(metadata.Database, metadata.DataSource);
        using var transaction = new EvidenceTransaction(connection);
        var store = DispatchProxy.Create<IMessageDatabase, StoreProbe>(); var probe = (StoreProbe)(object)store;
        probe.Source = source;
        var stager = new CallerOwnedMessageStager(connection, transaction, store, "msg");
        var incoming = new Envelope { Id = Guid.NewGuid(), Destination = new Uri("postgresql://fixture"), WasPersistedInInbox = true };
        var time = new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.FromHours(7));
        await stager.StageHandledAsync(incoming, time, default);
        var arguments = Assert.IsType<object[]>(probe.HandledArguments);
        Assert.Same(connection, arguments[0]); Assert.Same(transaction, arguments[1]); Assert.Same(incoming, arguments[2]);
        Assert.Equal(time.ToUniversalTime().AddDays(7), Assert.IsType<DateTimeOffset>(arguments[3]));
        Assert.Equal(1, probe.HandledCalls); Assert.Equal(0, transaction.Commits); Assert.Equal(0, transaction.Rollbacks);
        Assert.Equal(0, connection.OpenCalls); Assert.Empty(connection.Commands);
    }

    [Fact]
    public async Task MissingTimeExpiredIdentityOverflowAndCancellationNeverMarkHandled()
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened");
        using var metadata = source.CreateConnection();
        using var connection = new EvidenceConnection(metadata.Database, metadata.DataSource);
        using var transaction = new EvidenceTransaction(connection);
        var store = DispatchProxy.Create<IMessageDatabase, StoreProbe>(); var probe = (StoreProbe)(object)store; probe.Source = source;
        var stager = new CallerOwnedMessageStager(connection, transaction, store, "msg");
        var incoming = new Envelope { Id = Guid.NewGuid(), Destination = new Uri("postgresql://fixture"), WasPersistedInInbox = true };
        await Assert.ThrowsAsync<ArgumentException>(() => stager.StageHandledAsync(incoming, default, default));
        await Assert.ThrowsAsync<ArgumentException>(() => stager.StageHandledAsync(incoming, DateTimeOffset.MaxValue, default));
        await Assert.ThrowsAsync<OperationCanceledException>(() => stager.StageHandledAsync(incoming, DateTimeOffset.UtcNow, new(true)));
        incoming.WasPersistedInInbox = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => stager.StageHandledAsync(incoming, DateTimeOffset.UtcNow, default));
        Assert.Equal(0, probe.HandledCalls);
    }

    public class StoreProbe : DispatchProxy
    {
        public NpgsqlDataSource Source { get; set; } = null!;
        public object?[]? HandledArguments { get; private set; }
        public int HandledCalls { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_SchemaName" => "msg",
            "get_DataSource" => Source,
            "MarkIncomingEnvelopeAsHandledInTransactionAsync" => Mark(args),
            _ => throw new InvalidOperationException("Unexpected native store access.")
        };
        private Task Mark(object?[]? args) { HandledCalls++; HandledArguments = args; return Task.CompletedTask; }
    }
    public class RuntimeProbe : DispatchProxy
    {
        public IMessageDatabase Store { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "get_Storage"
            ? Store : throw new InvalidOperationException("Unexpected runtime access.");
    }
}
