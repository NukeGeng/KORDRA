using Wolverine;
using Wolverine.Persistence.Durability;

namespace Kordra.Modules.Messaging.Infrastructure.Transactions;

/// <summary>Native persistence enlistment; commit stays exclusively in the caller coordinator.</summary>
public sealed class CallerOwnedEnvelopeEnlistment : IEnvelopeTransaction
{
    private readonly CallerOwnedMessageStager _stager;
    private readonly Func<ValueTask> _rollback;

    public CallerOwnedEnvelopeEnlistment(CallerOwnedMessageStager stager, Func<ValueTask> coordinatorRollback)
    {
        _stager = stager ?? throw new ArgumentNullException(nameof(stager));
        _rollback = coordinatorRollback ?? throw new ArgumentNullException(nameof(coordinatorRollback));
    }
    internal bool IsFor(CallerOwnedMessageStager stager) => ReferenceEquals(_stager, stager);
    public Task PersistOutgoingAsync(Envelope envelope) => _stager.StageOutgoingAsync([envelope]);
    public Task PersistOutgoingAsync(Envelope[] envelopes)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        // Native MessageContext.CopyToAsync forwards its outstanding snapshot even when empty.
        return envelopes.Length == 0 ? Task.CompletedTask : _stager.StageOutgoingAsync(envelopes);
    }
    public Task PersistIncomingAsync(Envelope envelope) => _stager.StageIncomingAsync([envelope]);
    public ValueTask RollbackAsync() => _rollback();
    public Task<bool> TryMakeEagerIdempotencyCheckAsync(Envelope envelope, DurabilitySettings settings, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!envelope.WasPersistedInInbox)
            throw new InvalidOperationException("This adapter requires a transport-persisted inbox identity.");
        return Task.FromResult(true);
    }
}
