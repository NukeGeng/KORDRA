using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Wolverine;
using Wolverine.RDBMS;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Wolverine.Runtime;
using Wolverine.Postgresql.Transport;

namespace Kordra.Modules.Messaging.Infrastructure.Transactions;

/// <summary>
/// Compatibility prototype for ADR-0020. Stages native writes only; the caller retains
/// commit, rollback, outcome reconciliation and post-commit delivery control.
/// Not a production consumer or an authorization boundary.
/// </summary>
public sealed class CallerOwnedMessageStager
{
    // Shared by every stager/enlistment for the same root transaction. Weak keys keep
    // completed transactions collectible without a process-wide transaction registry.
    private static readonly ConditionalWeakTable<DbTransaction, StagingBudget> Budgets = new();
    private readonly StagingBudget _budget;
    private string? _generation;
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly IMessageDatabase _store;
    private readonly string _schema;

    public CallerOwnedMessageStager(DbConnection connection, DbTransaction transaction,
        IMessageDatabase store, string expectedSchema)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(expectedSchema))
            throw new ArgumentException("Explicit message schema required.", nameof(expectedSchema));
        _connection = connection;
        _transaction = transaction;
        _store = store;
        _schema = expectedSchema;
        RequireBinding();
        _budget = Budgets.GetValue(transaction, _ => new StagingBudget());
    }

    public async Task StageOutgoingAsync(Envelope[] envelopes)
    {
        RequireBinding();
        await ReserveBatchAsync(envelopes).ConfigureAwait(false);
        await _store.StoreOutgoingAsync(_transaction, envelopes).ConfigureAwait(false);
    }

    /// <summary>Receipt first, native outbox only for a new key. Still provisional until the owner commits.</summary>
    public async Task<ReceiptStageResult> StagePublicationAsync(CallerOwnedReceiptStore receipts,
        CompiledMessageRegistry registry, MessageContext context, VerifiedMessageEnvelope message, DateTimeOffset effectiveNow, CancellationToken cancellationToken)
    {
        RequireBinding(); ArgumentNullException.ThrowIfNull(receipts); ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(message); ArgumentNullException.ThrowIfNull(context);
        receipts.RequireCaller(_connection, _transaction, _schema);
        if (!ReferenceEquals(context.Runtime.Storage, _store) || !ReferenceEquals(context.Storage, _store) ||
            context.Transaction is not CallerOwnedEnvelopeEnlistment enlistment || !enlistment.IsFor(this))
            throw new InvalidOperationException("Publication requires this exact caller-owned native context enlistment.");
        cancellationToken.ThrowIfCancellationRequested();
        var codec = new MessageEnvelopeCodec(registry);
        var wire = codec.Write(message.Binding, message.Delivery, message.CopyPayload());
        var rechecked = codec.Read(wire, MessageEnvelopeCodec.ContentType, message.Binding.Identity.InstallationId,
            message.Binding.Identity.Scope, effectiveNow);
        if (!CanonicalMessageBinding.Matches(message.PayloadHash, rechecked.PayloadHash))
            throw new InvalidOperationException("Publication binding changed under the compiled registry.");
        var destination = new Uri("postgresql://" + registry.RouteFor(message.Binding));
        if (!context.Runtime.Endpoints.GetOrBuildSendingAgent(destination).IsDurable)
            throw new InvalidOperationException("Publication requires a native durable endpoint before staging receipts.");
        var result = await receipts.StagePublicationAsync(message, effectiveNow, cancellationToken).ConfigureAwait(false);
        if (result.Disposition != ReceiptStageDisposition.Staged) return result;
        // Duplicate/conflict has no new work to admit. For a new key, any capacity failure must
        // roll back this provisional receipt together with owner/AUD state; no native send occurs.
        var capacity = _schema == "msg" ? new PublicationCapacityGuard(_connection, _transaction) :
            _connection is Npgsql.NpgsqlConnection fixture && _schema.StartsWith("t12_", StringComparison.Ordinal) &&
            Guid.TryParseExact(_schema[4..], "N", out var caseId)
                ? PublicationCapacityGuard.CreateIsolatedTestGuard(fixture, _transaction, caseId)
                : throw new InvalidOperationException("No approved publication capacity profile for this schema.");
        capacity.RequireCaller(_connection, _transaction, _schema);
        var capacityStatus = await capacity.CheckAsync(registry,
            context.Runtime.Options.Transports.GetOrCreate<PostgresqlTransport>(), cancellationToken).ConfigureAwait(false);
        if (capacityStatus != PublicationCapacityStatus.WithinWatermark)
            throw new InvalidOperationException("Publication capacity is unavailable or above its admission watermark.");
        // Public native API prepares the sender, enlists persistence and retains the envelope for
        // the coordinator's post-commit flush. Direct store writes alone would strand the live context.
        await context.EndpointFor(destination).SendRawMessageAsync(wire, configure: outgoing =>
        {
            outgoing.Id = message.Delivery.MessageId; outgoing.Destination = destination;
            outgoing.ContentType = MessageEnvelopeCodec.ContentType; outgoing.MessageType = message.Binding.Identity.TypeId;
            outgoing.CorrelationId = message.Delivery.CorrelationId.ToString("D");
            outgoing.Status = EnvelopeStatus.Outgoing;
        }).ConfigureAwait(false);
        // If persistence fails, the caller must roll back both provisional receipt and owner state.
        // No native flush, delivery, fallback transaction, or Durable result is issued here.
        return result;
    }

    public async Task StageIncomingAsync(Envelope[] envelopes)
    {
        RequireBinding();
        await ReserveBatchAsync(envelopes).ConfigureAwait(false);
        await _store.StoreIncomingAsync(_transaction, envelopes).ConfigureAwait(false);
    }

    /// <summary>Provisional logical uniqueness gate; only Staged may proceed to an owner effect.</summary>
    public Task<ReceiptStageResult> StageConsumptionAsync(CallerOwnedReceiptStore receipts,
        CompiledMessageRegistry registry, MessageContext context, VerifiedMessageEnvelope message,
        LogicalReceiptOutcome outcome, DateTimeOffset effectiveNow, CancellationToken cancellationToken)
    {
        RequireBinding(); ArgumentNullException.ThrowIfNull(receipts); ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(message);
        receipts.RequireCaller(_connection, _transaction, _schema);
        var incoming = context.Envelope;
        if (!ReferenceEquals(context.Runtime.Storage, _store) || !ReferenceEquals(context.Storage, _store) ||
            context.Transaction is not CallerOwnedEnvelopeEnlistment enlistment || !enlistment.IsFor(this) ||
            incoming is null || !incoming.WasPersistedInInbox || incoming.Id != message.Delivery.MessageId ||
            incoming.MessageType != message.Binding.Identity.TypeId ||
            incoming.Destination != new Uri("postgresql://" + registry.RouteFor(message.Binding)) ||
            incoming.ContentType != MessageEnvelopeCodec.ContentType || incoming.Data is null)
            throw new InvalidOperationException("Consumption requires the exact enlisted native inbox identity and registered destination.");
        cancellationToken.ThrowIfCancellationRequested();
        var rechecked = new MessageEnvelopeCodec(registry).Read(incoming.Data, incoming.ContentType,
            message.Binding.Identity.InstallationId, message.Binding.Identity.Scope, effectiveNow);
        if (rechecked.Binding != message.Binding || rechecked.Delivery != message.Delivery ||
            !CanonicalMessageBinding.Matches(message.PayloadHash, rechecked.PayloadHash))
            throw new InvalidOperationException("Inbox binding differs from the verified message.");
        // Do not mark Handled here. Owner effect/result, AUD/floor and inbox disposition still
        // have to stage before the coordinator's only commit; any failure requires root rollback.
        return receipts.StageLogicalAsync(message, outcome, effectiveNow, cancellationToken);
    }

    /// <summary>Caller supplies trusted current time, not an arbitrary expiry; retention is at least seven days.</summary>
    public Task StageHandledAsync(Envelope envelope, DateTimeOffset effectiveNow, CancellationToken cancellationToken)
    {
        RequireBinding();
        ArgumentNullException.ThrowIfNull(envelope);
        if (!envelope.WasPersistedInInbox || envelope.Destination is null || envelope.Id == Guid.Empty)
            throw new InvalidOperationException("Persisted inbox identity and destination required.");
        if (effectiveNow == default || effectiveNow > DateTimeOffset.MaxValue.AddDays(-7))
            throw new ArgumentException("Valid caller effective time required for minimum handled retention.", nameof(effectiveNow));
        cancellationToken.ThrowIfCancellationRequested();
        var keepUntil = effectiveNow.ToUniversalTime().AddDays(7);
        return _store.MarkIncomingEnvelopeAsHandledInTransactionAsync(
            _connection, _transaction, envelope, keepUntil, cancellationToken);
    }

    private async Task ReserveBatchAsync(Envelope[] envelopes)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        if (envelopes.Length is < 1 or > 10 || envelopes.Any(e => e is null || e.Data is null))
            throw new ArgumentException("A bounded nonempty serialized envelope batch is required.", nameof(envelopes));
        // Native Data is the serialized payload persisted by Wolverine. Include all wire
        // overhead: never trust an inner JSON length or defer serialization until after admission.
        var bytes = envelopes.Sum(e => (long)e.Data!.Length);
        // Npgsql recycles the same transaction object after completion. Object identity alone
        // cannot distinguish its next root transaction. Read the server's full transaction ID
        // on the caller connection/root; no fallback connection or native persistence SQL.
        using var command = _connection.CreateCommand();
        command.Transaction = _transaction; command.CommandTimeout = 5;
        command.CommandText = "SELECT pg_current_xact_id()::text";
        var generation = await command.ExecuteScalarAsync().ConfigureAwait(false) as string;
        if (!ulong.TryParse(generation, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var transactionId) || transactionId == 0)
            throw new InvalidOperationException("Caller transaction identity unavailable.");
        lock (_budget)
        {
            if (_generation is not null && _generation != generation)
                throw new InvalidOperationException("Caller transaction generation changed.");
            _generation = generation;
            if (_budget.Generation != generation)
            {
                _budget.Generation = generation;
                _budget.Envelopes = 0; _budget.Bytes = 0;
            }
            if (envelopes.Length > 10 - _budget.Envelopes || bytes > 160 * 1024 - _budget.Bytes)
                throw new InvalidOperationException("Caller transaction staging limit exceeded.");
            _budget.Envelopes += envelopes.Length;
            _budget.Bytes += bytes;
        }
        // Do not release reservations after a native failure: writes may already have been
        // staged. Caller must roll back the root, not obtain a fresh budget via another stager.
        // StagePublication's public native send uses this same outgoing enlistment exactly once.
    }

    private sealed class StagingBudget
    {
        internal string? Generation;
        internal int Envelopes;
        internal long Bytes;
    }

    private void RequireBinding()
    {
        if (_connection.State != ConnectionState.Open || !ReferenceEquals(_transaction.Connection, _connection) ||
            !string.Equals(_store.SchemaName, _schema, StringComparison.Ordinal))
            throw new InvalidOperationException("Caller transaction or message schema binding is invalid.");
        // A closed comparison connection is metadata-only. It is never opened or used as a fallback.
        using var target = _store.DataSource.CreateConnection();
        if (string.IsNullOrEmpty(_connection.Database) || string.IsNullOrEmpty(_connection.DataSource) ||
            !string.Equals(target.Database, _connection.Database, StringComparison.Ordinal) ||
            !string.Equals(target.DataSource, _connection.DataSource, StringComparison.Ordinal))
            throw new InvalidOperationException("Message store must match the caller database target.");
    }
}
