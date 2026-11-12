using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Wolverine;
using Wolverine.Postgresql;
using Wolverine.Postgresql.Transport;
using MessageIdentity = Kordra.Modules.Messaging.Contracts.MessageIdentity;
using Xunit;
using static Kordra.Messaging.Tests.CallerOwnedReceiptStoreTests;

namespace Kordra.Messaging.Tests;

public sealed class MessageReceiptInspectorTests
{
    [Fact]
    public async Task HandledSettlementObservationStillRequiresJournalAndRejectsConflictingQuarantineOrPendingCopies()
    {
        var caseId = Guid.NewGuid();
        using var connection = new EvidenceConnection {
            ConnectionString = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC",
            ScalarResult = true
        };
        var schema = "t12_" + caseId.ToString("N");
        var options = new WolverineOptions();
        options.UsePostgresqlPersistenceAndTransport(connection.ConnectionString, schema, schema);
        options.ListenToPostgresqlQueue("nativecompatibility");
        var transport = options.Transports.GetOrCreate<PostgresqlTransport>();
        var message = NativeWireFixture.Create(caseId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var inspector = MessageReceiptInspector.CreateIsolatedTestInspector(connection, caseId);
        Assert.True(await inspector.ObserveExpiredHandledAsync(message, NativeWireFixture.Registry(), transport, message.Binding.DeadlineUtc));
        var query = Assert.Single(connection.Commands);
        Assert.Contains("reason='Expired'", query.CommandText);
        Assert.Contains("observed_hash=@hash", query.CommandText);
        Assert.Contains("status='Handled' AND received_at=@destination", query.CommandText);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM", query.CommandText);
        Assert.Contains("wolverine_dead_letters WHERE id=@id", query.CommandText);
        Assert.Contains("wolverine_outgoing_envelopes WHERE id=@id", query.CommandText);
        Assert.False(query.Parameters.Contains("diagnostic"));
        connection.ScalarResult = false;
        Assert.False(await inspector.ObserveExpiredHandledAsync(message, NativeWireFixture.Registry(), transport, message.Binding.DeadlineUtc));
        connection.ReadFailure = new Npgsql.NpgsqlException("synthetic-unavailable");
        Assert.False(await inspector.ObserveExpiredHandledAsync(message, NativeWireFixture.Registry(), transport, message.Binding.DeadlineUtc));
    }

    [Fact]
    public async Task ExpiredObservationUsesTheNativeSanitizedDestination()
    {
        var caseId = Guid.NewGuid();
        using var connection = new EvidenceConnection {
            ConnectionString = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC",
            ScalarResult = true
        };
        var schema = "t12_" + caseId.ToString("N");
        var options = new WolverineOptions();
        options.UsePostgresqlPersistenceAndTransport(connection.ConnectionString, schema, schema);
        options.ListenToPostgresqlQueue("native-compatibility");
        var transport = options.Transports.GetOrCreate<PostgresqlTransport>();
        var message = NativeWireFixture.Create(caseId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.True(await MessageReceiptInspector.CreateIsolatedTestInspector(connection, caseId)
            .ObserveExpiredQuarantineAsync(message, NativeWireFixture.Registry("native-compatibility"),
                transport, message.Binding.DeadlineUtc));
        Assert.Equal(new Uri("postgresql://" + transport.SanitizeIdentifier("native-compatibility")).ToString(),
            Assert.Single(connection.Commands).Parameters["destination"].Value);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    [InlineData(1L, false)]
    public async Task ExpiredQuarantineReadRequiresExactJournalTerminalRowAndNoPendingCopies(object? observed, bool expected)
    {
        var caseId = Guid.NewGuid();
        using var connection = new EvidenceConnection {
            ConnectionString = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC",
            ScalarResult = observed
        };
        var schema = "t12_" + caseId.ToString("N");
        var options = new WolverineOptions();
        options.UsePostgresqlPersistenceAndTransport(connection.ConnectionString, schema, schema);
        options.ListenToPostgresqlQueue("nativecompatibility");
        options.PublishMessage<string>().ToPostgresqlQueue("other_committed_results");
        var transport = options.Transports.GetOrCreate<PostgresqlTransport>();
        var message = NativeWireFixture.Create(caseId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var inspector = MessageReceiptInspector.CreateIsolatedTestInspector(connection, caseId);
        Assert.Equal(expected, await inspector.ObserveExpiredQuarantineAsync(message, NativeWireFixture.Registry(),
            transport, message.Binding.DeadlineUtc));
        var query = Assert.Single(connection.Commands);
        Assert.Equal(5, query.CommandTimeout);
        Assert.Null(query.Transaction);
        Assert.Contains("reason='Expired'", query.CommandText);
        Assert.Contains("record_id=@id AND installation_id=@installation", query.CommandText);
        Assert.Contains("observed_hash=@hash", query.CommandText);
        Assert.Contains("replayable IS FALSE", query.CommandText);
        Assert.Contains("count(*) = 1", query.CommandText);
        Assert.Contains("LIMIT 2", query.CommandText);
        Assert.Contains("received_at=@destination AND message_type=@diagnostic", query.CommandText);
        Assert.Contains("status <> 'Handled'", query.CommandText);
        Assert.Contains("wolverine_queue_other_committed_results_scheduled", query.CommandText);
        Assert.Contains("wolverine_outgoing_envelopes WHERE id=@id", query.CommandText);
        Assert.DoesNotContain("DELETE", query.CommandText);
        Assert.DoesNotContain("INSERT", query.CommandText);
        Assert.DoesNotContain("UPDATE", query.CommandText);
        Assert.Equal(message.Delivery.MessageId, query.Parameters["id"].Value);
        Assert.Equal(caseId, query.Parameters["installation"].Value);
        Assert.Equal(message.PayloadHash, query.Parameters["hash"].Value);
        Assert.Equal("postgresql://nativecompatibility/", query.Parameters["destination"].Value);
        Assert.Equal(0, connection.OpenCalls);
    }

    [Fact]
    public async Task ExpiredQuarantineReaderRejectsWrongScopeEarlyClockMissingQueueAndReadFailure()
    {
        var caseId = Guid.NewGuid();
        using var connection = new EvidenceConnection {
            ConnectionString = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC",
            ScalarResult = true
        };
        var options = new WolverineOptions();
        options.UsePostgresqlPersistenceAndTransport(connection.ConnectionString, "t12_" + caseId.ToString("N"), "t12_" + caseId.ToString("N"));
        var transport = options.Transports.GetOrCreate<PostgresqlTransport>();
        var inspector = MessageReceiptInspector.CreateIsolatedTestInspector(connection, caseId);
        var message = NativeWireFixture.Create(caseId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.False(await inspector.ObserveExpiredQuarantineAsync(message, NativeWireFixture.Registry(), transport, message.Binding.DeadlineUtc));
        options.ListenToPostgresqlQueue("nativecompatibility");
        Assert.False(await inspector.ObserveExpiredQuarantineAsync(message, NativeWireFixture.Registry(), transport, message.Binding.OccurredAtUtc));
        var other = NativeWireFixture.Create(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.False(await inspector.ObserveExpiredQuarantineAsync(other, NativeWireFixture.Registry(), transport, other.Binding.DeadlineUtc));
        Assert.Empty(connection.Commands);
        connection.ReadFailure = new Npgsql.NpgsqlException("synthetic-forbidden-token");
        Assert.False(await inspector.ObserveExpiredQuarantineAsync(message, NativeWireFixture.Registry(), transport, message.Binding.DeadlineUtc));
        Assert.Single(connection.Commands);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.ObserveExpiredQuarantineAsync(message,
            NativeWireFixture.Registry(), transport, message.Binding.DeadlineUtc, new(true)));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task OnlyExactKeyAndMatchingHashExposeOriginalTechnicalResult(bool publication, bool sameHash)
    {
        using var connection = new EvidenceConnection(); var expected = new string('a', 64); var original = Guid.NewGuid();
        connection.Rows.Columns.Add("hash", typeof(string)); connection.Rows.Columns.Add("value", publication ? typeof(Guid) : typeof(string));
        connection.Rows.Rows.Add(sameHash ? expected : new string('b', 64), publication ? original : "Denied");
        var identity = new MessageIdentity(Guid.NewGuid(), MessageScope.Tenant(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), 7, "fixture.observe");
        var inspector = new MessageReceiptInspector(connection);
        var result = publication ? await inspector.InspectPublicationAsync(identity, expected, default) : await inspector.InspectLogicalAsync(identity, expected, default);
        Assert.Equal(sameHash ? ReceiptInspectionStatus.MatchingObserved : ReceiptInspectionStatus.Conflict, result.Status);
        Assert.False(result.ExactKeyAbsent);
        Assert.Equal(sameHash && publication ? original : (Guid?)null, result.OriginalMessageId);
        Assert.Equal(sameHash && !publication ? LogicalReceiptOutcome.Denied : (LogicalReceiptOutcome?)null, result.OriginalOutcome);
        var command = Assert.Single(connection.Commands); Assert.StartsWith("SELECT ", command.CommandText);
        Assert.EndsWith("LIMIT 2", command.CommandText); Assert.Equal(5, command.CommandTimeout); Assert.Null(command.Transaction);
        Assert.Equal(identity.InstallationId, command.Parameters["installation"].Value);
        Assert.Equal(identity.Scope.Kind, command.Parameters["scope"].Value);
        Assert.Equal(identity.Scope.TenantId, command.Parameters["tenant"].Value);
        Assert.Equal(identity.OperationId, command.Parameters["operation"].Value);
        Assert.Equal(identity.StepId, command.Parameters["step"].Value);
        Assert.Equal(identity.StepVersion, command.Parameters["version"].Value);
        Assert.Equal(identity.TypeId, command.Parameters["type"].Value);
        Assert.Equal(7, command.Parameters.Count); Assert.Equal(0, connection.OpenCalls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("invalid-hash")]
    [InlineData("invalid-outcome")]
    [InlineData("null")]
    public async Task MissingOrMalformedEvidenceNeverProvesSafeReplay(string scenario)
    {
        using var connection = new EvidenceConnection(); var hash = new string('a', 64);
        connection.Rows.Columns.Add("hash", typeof(string)); connection.Rows.Columns.Add("value", typeof(string));
        if (scenario != "missing") connection.Rows.Rows.Add(scenario == "invalid-hash" ? "bad" : scenario == "null" ? DBNull.Value : hash,
            scenario == "invalid-outcome" ? "Pending" : "Completed");
        if (scenario == "ambiguous") connection.Rows.Rows.Add(hash, "Completed");
        var identity = new MessageIdentity(Guid.NewGuid(), MessageScope.System(), Guid.NewGuid(), Guid.NewGuid(), 1, "fixture.observe");
        var inspector = new MessageReceiptInspector(connection);
        var result = await inspector.InspectLogicalAsync(identity, hash, default);
        Assert.Equal(scenario == "missing" ? ReceiptInspectionStatus.StillUnknown : ReceiptInspectionStatus.Unavailable, result.Status);
        Assert.Equal(scenario == "missing", result.ExactKeyAbsent);
        Assert.Null(result.OriginalOutcome); Assert.Null(result.OriginalMessageId);
        Assert.Equal(Guid.Empty, Assert.Single(connection.Commands).Parameters["tenant"].Value);
        connection.Close(); Assert.Equal(ReceiptInspectionStatus.Unavailable, (await inspector.InspectLogicalAsync(identity, hash, default)).Status);
        Assert.Single(connection.Commands); Assert.Equal(0, connection.OpenCalls);
    }

    [Fact]
    public async Task InvalidHashAndCancellationDoNotRunQuery()
    {
        using var connection = new EvidenceConnection(); var inspector = new MessageReceiptInspector(connection);
        var identity = new MessageIdentity(Guid.NewGuid(), MessageScope.System(), Guid.NewGuid(), Guid.NewGuid(), 1, "fixture.observe");
        await Assert.ThrowsAsync<ArgumentException>(() => inspector.InspectLogicalAsync(identity, "synthetic-forbidden-token", default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.InspectLogicalAsync(identity, new string('a', 64), new(true)));
        Assert.Empty(connection.Commands);
    }

    [Fact]
    public async Task FailedDatabaseReadIsUnavailableAndDoesNotDiscloseErrorOrRetry()
    {
        using var connection = new EvidenceConnection { ReadFailure = new Npgsql.NpgsqlException("synthetic-forbidden-token") };
        var inspector = new MessageReceiptInspector(connection);
        var identity = new MessageIdentity(Guid.NewGuid(), MessageScope.System(), Guid.NewGuid(), Guid.NewGuid(), 1, "fixture.observe");
        var result = await inspector.InspectLogicalAsync(identity, new string('a', 64), default);
        Assert.Equal(ReceiptInspectionStatus.Unavailable, result.Status);
        Assert.False(result.ExactKeyAbsent);
        Assert.Null(result.OriginalOutcome); Assert.Null(result.OriginalMessageId);
        Assert.DoesNotContain("synthetic-forbidden-token", result.ToString());
        Assert.Single(connection.Commands); Assert.Equal(0, connection.OpenCalls);
    }

    [Fact]
    public async Task CancellationDuringQueryPropagatesWithoutAutomaticRetryOrWriting()
    {
        using var connection = new EvidenceConnection { ReadFailure = new OperationCanceledException() };
        var identity = new MessageIdentity(Guid.NewGuid(), MessageScope.System(), Guid.NewGuid(), Guid.NewGuid(), 1, "fixture.observe");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MessageReceiptInspector(connection)
            .InspectLogicalAsync(identity, new string('a', 64), default));
        Assert.Single(connection.Commands); Assert.Equal(0, connection.OpenCalls);
    }

    [Fact]
    public async Task EmptyPersistedPublicationIdentityIsUnavailableWithoutOutcomeDisclosure()
    {
        using var connection = new EvidenceConnection(); var hash = new string('a', 64);
        connection.Rows.Columns.Add("hash", typeof(string)); connection.Rows.Columns.Add("value", typeof(Guid));
        connection.Rows.Rows.Add(hash, Guid.Empty);
        var identity = new MessageIdentity(Guid.NewGuid(), MessageScope.System(), Guid.NewGuid(), Guid.NewGuid(), 1, "fixture.observe");
        var result = await new MessageReceiptInspector(connection).InspectPublicationAsync(identity, hash, default);
        Assert.Equal(ReceiptInspectionStatus.Unavailable, result.Status); Assert.Null(result.OriginalMessageId);
    }
}
