using System.Data;
using System.Data.Common;
using System.Reflection;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Npgsql;
using Wolverine.RDBMS;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class TransactionAdapterGuardTests
{
    [Fact]
    public void NullBindingsFailBeforeStoreAccess()
    {
        using var connection = Connection(); using var transaction = new UnusableTransaction(connection);
        var store = DispatchProxy.Create<IMessageDatabase, RefuseStoreAccess>();
        Assert.Throws<ArgumentNullException>(() => new CallerOwnedMessageStager(null!, transaction, store, "msg"));
        Assert.Throws<ArgumentNullException>(() => new CallerOwnedMessageStager(connection, null!, store, "msg"));
        Assert.Throws<ArgumentNullException>(() => new CallerOwnedMessageStager(connection, transaction, null!, "msg"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingExplicitSchemaIsRejectedWithoutOpeningConnection(string schema)
    {
        using var connection = Connection(); using var transaction = new UnusableTransaction(connection);
        var store = DispatchProxy.Create<IMessageDatabase, RefuseStoreAccess>();
        Assert.Throws<ArgumentException>(() => new CallerOwnedMessageStager(connection, transaction, store, schema));
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void ClosedCallerConnectionIsNeverOpenedOrReplaced()
    {
        using var connection = Connection(); using var transaction = new UnusableTransaction(connection);
        var store = DispatchProxy.Create<IMessageDatabase, RefuseStoreAccess>();
        Assert.Throws<InvalidOperationException>(() => new CallerOwnedMessageStager(connection, transaction, store, "msg"));
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Equal(0, transaction.CommitCalls);
        Assert.Equal(0, transaction.RollbackCalls);
    }

    [Fact]
    public void StageSurfaceCannotOwnCommitRollbackOrDelivery()
    {
        var names = typeof(CallerOwnedMessageStager).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Order().ToArray();
        Assert.Equal(new[] { "StageConsumptionAsync", "StageHandledAsync", "StageIncomingAsync", "StageOutgoingAsync", "StagePublicationAsync" }, names);
    }

    private static NpgsqlConnection Connection() => new("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Timeout=1");

    public class RefuseStoreAccess : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Store must not be accessed for invalid caller binding.");
    }

    private sealed class UnusableTransaction(DbConnection connection) : DbTransaction
    {
        protected override DbConnection DbConnection => connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public int CommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }
        public override void Commit() { CommitCalls++; throw new InvalidOperationException(); }
        public override void Rollback() { RollbackCalls++; throw new InvalidOperationException(); }
    }
}
