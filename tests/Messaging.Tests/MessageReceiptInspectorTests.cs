using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Xunit;
using static Kordra.Messaging.Tests.CallerOwnedReceiptStoreTests;

namespace Kordra.Messaging.Tests;

public sealed class MessageReceiptInspectorTests
{
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
