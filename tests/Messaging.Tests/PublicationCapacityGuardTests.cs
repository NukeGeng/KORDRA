using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Wolverine;
using Wolverine.Postgresql;
using Wolverine.Postgresql.Transport;
using Xunit;
using static Kordra.Messaging.Tests.CallerOwnedReceiptStoreTests;

namespace Kordra.Messaging.Tests;

public sealed class PublicationCapacityGuardTests
{
    [Theory]
    [InlineData(0, PublicationCapacityStatus.WithinWatermark)]
    [InlineData(10000, PublicationCapacityStatus.WithinWatermark)]
    [InlineData(10001, PublicationCapacityStatus.AboveWatermark)]
    [InlineData(-1, PublicationCapacityStatus.Unavailable)]
    [InlineData(10002, PublicationCapacityStatus.Unavailable)]
    public async Task ExactBoundedPendingReadDoesNotCountHandledOrMutateCommittedWork(long count, PublicationCapacityStatus expected)
    {
        using var connection = new EvidenceConnection { ScalarResult = count }; using var transaction = new EvidenceTransaction(connection);
        var options = new WolverineOptions(); MessagingSchemaProfile.Configure(options, "Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened");
        options.ListenToPostgresqlQueue("nativecompatibility");
        options.PublishMessage<string>().ToPostgresqlQueue("other_committed_results");
        var status = await new PublicationCapacityGuard(connection, transaction).CheckAsync(NativeWireFixture.Registry(),
            options.Transports.GetOrCreate<PostgresqlTransport>(), default);
        Assert.Equal(expected, status);
        var query = Assert.Single(connection.Commands);
        Assert.Same(transaction, query.Transaction); Assert.Equal(5, query.CommandTimeout);
        Assert.Contains("status IN ('Incoming','Scheduled')", query.CommandText);
        Assert.Contains("msg_transport", query.CommandText); Assert.Contains("wolverine_queue_nativecompatibility_scheduled", query.CommandText);
        Assert.Contains("wolverine_queue_other_committed_results", query.CommandText);
        Assert.DoesNotContain("Handled", query.CommandText); Assert.DoesNotContain("dead_letter", query.CommandText);
        Assert.DoesNotContain("DELETE", query.CommandText); Assert.DoesNotContain("UPDATE", query.CommandText);
        Assert.DoesNotContain("INSERT", query.CommandText); Assert.EndsWith("LIMIT 10001) AS pending", query.CommandText);
        Assert.Equal(0, transaction.Commits); Assert.Equal(0, transaction.Rollbacks); Assert.Equal(0, connection.OpenCalls);
    }

    [Fact]
    public async Task MissingQueueWrongSchemaClosedBindingAndDatabaseFailureFailClosed()
    {
        using var connection = new EvidenceConnection { ScalarResult = 0L }; using var transaction = new EvidenceTransaction(connection);
        var options = new WolverineOptions(); MessagingSchemaProfile.Configure(options, "Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened");
        var transport = options.Transports.GetOrCreate<PostgresqlTransport>(); var guard = new PublicationCapacityGuard(connection, transaction);
        Assert.Equal(PublicationCapacityStatus.Unavailable, await guard.CheckAsync(NativeWireFixture.Registry(), transport, default));
        Assert.Empty(connection.Commands); Assert.Empty(transport.Queues);
        options.ListenToPostgresqlQueue("nativecompatibility");
        connection.ReadFailure = new Npgsql.NpgsqlException("synthetic-forbidden-token");
        Assert.Equal(PublicationCapacityStatus.Unavailable, await guard.CheckAsync(NativeWireFixture.Registry(), transport, default));
        Assert.Single(connection.Commands);
        connection.Commands.Clear(); transport.TransportSchemaName = "elsewhere";
        Assert.Equal(PublicationCapacityStatus.Unavailable, await guard.CheckAsync(NativeWireFixture.Registry(), transport, default));
        Assert.Empty(connection.Commands);
        connection.Close();
        Assert.Equal(PublicationCapacityStatus.Unavailable, await guard.CheckAsync(CompiledMessageRegistry.Empty, transport, default));
        Assert.Empty(connection.Commands);
    }
}
