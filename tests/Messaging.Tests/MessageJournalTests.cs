using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Xunit;
using static Kordra.Messaging.Tests.CallerOwnedReceiptStoreTests;

namespace Kordra.Messaging.Tests;

public sealed class MessageJournalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JournalStagesOnlyClosedMetadataWithoutCommittingOrReplaying(bool quarantine)
    {
        using var connection = new EvidenceConnection(); using var transaction = new EvidenceTransaction(connection);
        var journal = new CallerOwnedMessageJournal(connection, transaction); var hash = new string('a', 64);
        if (quarantine) await journal.StageQuarantineAsync(Guid.NewGuid(), Guid.NewGuid(), MessageQuarantineReason.UnknownCommit, hash, DateTimeOffset.UtcNow, default);
        else await journal.StageRecoveryAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MessageRecoveryDisposition.StillUnknown, hash, DateTimeOffset.UtcNow, default);
        var command = Assert.Single(connection.Commands);
        Assert.StartsWith(quarantine ? "INSERT INTO msg.quarantine_journal" : "INSERT INTO msg.recovery_journal", command.CommandText);
        Assert.Same(transaction, command.Transaction); Assert.Equal(5, command.CommandTimeout);
        Assert.Equal(hash, command.Parameters["hash"].Value);
        Assert.Equal(quarantine ? "UnknownCommit" : "StillUnknown", command.Parameters["category"].Value);
        Assert.DoesNotContain(hash, command.CommandText);
        Assert.DoesNotContain("UPDATE", command.CommandText); Assert.DoesNotContain("DELETE", command.CommandText);
        Assert.False(command.Parameters.Contains("payload")); Assert.False(command.Parameters.Contains("exception"));
        Assert.Equal(0, transaction.Commits); Assert.Equal(0, transaction.Rollbacks); Assert.Equal(0, connection.OpenCalls);
    }

    [Fact]
    public async Task InvalidOrUnboundMetadataAndCancellationNeverWrite()
    {
        using var connection = new EvidenceConnection(); using var transaction = new EvidenceTransaction(connection);
        var journal = new CallerOwnedMessageJournal(connection, transaction); var id = Guid.NewGuid();
        var hash = new string('a', 64); var now = DateTimeOffset.UtcNow;
        foreach (var invalid in new[] { "", new string('A', 64), "synthetic-forbidden-token", hash + "0" })
        {
            var error = await Assert.ThrowsAsync<ArgumentException>(() => journal.StageQuarantineAsync(id, id, MessageQuarantineReason.InvalidEnvelope, invalid, now, default));
            if (invalid.Length > 0) Assert.DoesNotContain(invalid, error.Message);
        }
        await Assert.ThrowsAsync<ArgumentException>(() => journal.StageRecoveryAsync(id, id, Guid.Empty, MessageRecoveryDisposition.DurableFound, hash, now, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => journal.StageQuarantineAsync(id, id, (MessageQuarantineReason)99, hash, now, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.StageQuarantineAsync(id, id, MessageQuarantineReason.Expired, hash, now, new(true)));
        transaction.Detach();
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.StageRecoveryAsync(id, id, id, MessageRecoveryDisposition.InspectRequested, hash, now, default));
        Assert.Empty(connection.Commands);
    }

    [Fact]
    public async Task FailedAppendNeverBecomesDurableOrRetries()
    {
        using var connection = new EvidenceConnection { InsertCount = 0 }; using var transaction = new EvidenceTransaction(connection);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CallerOwnedMessageJournal(connection, transaction)
            .StageQuarantineAsync(Guid.NewGuid(), Guid.NewGuid(), MessageQuarantineReason.HashConflict, new string('a', 64), DateTimeOffset.UtcNow, default));
        Assert.Single(connection.Commands); Assert.Equal(0, transaction.Commits); Assert.Equal(0, transaction.Rollbacks);
    }
}
