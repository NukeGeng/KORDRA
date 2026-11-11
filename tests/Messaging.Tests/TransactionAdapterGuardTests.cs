using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Kordra.Testing;
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

    [Fact]
    public void CommitReplyFaultRequiresTheSelectedUnpooledTarget()
    {
        var runId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var digest = new string('a', 64);
        var database = "kordra_test_" + runId.ToString("N");
        var profile = DisposablePostgreSqlProfile.Read(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, kind = "disposable-postgresql-regression",
            runId = runId.ToString("D"), host = "127.0.0.1", port = 5432, database,
            role = "kordra_test", schema = "app", sourceSha256 = digest, operationManifestSha256 = digest,
            expiresAtUtc = now.AddMinutes(1).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                System.Globalization.CultureInfo.InvariantCulture)
        }), runId, digest, digest, now);
        const string legacy = "Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_test;Pooling=false";
        var selected = new NpgsqlConnectionStringBuilder(legacy) { Database = database };
        CommitReplyFault.ValidateTarget(legacy, null);
        CommitReplyFault.ValidateTarget(selected.ConnectionString, profile);
        Assert.Throws<InvalidOperationException>(() => CommitReplyFault.ValidateTarget(legacy, profile));
        Assert.Throws<InvalidOperationException>(() => CommitReplyFault.ValidateTarget(selected.ConnectionString, null));
        selected.Pooling = true;
        Assert.Throws<InvalidOperationException>(() => CommitReplyFault.ValidateTarget(selected.ConnectionString, profile));
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
