using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Data;
using System.Data.Common;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Npgsql;
using Weasel.Postgresql;
using Wolverine.Postgresql.Transport;
using Wolverine.Runtime;

namespace Kordra.Modules.Messaging.Infrastructure.Recovery;

public enum ReceiptInspectionStatus { MatchingObserved, Conflict, StillUnknown, Unavailable }

/// <summary>Observation only. No status grants authority, proves custody or permits replay/resuming a writer.</summary>
public sealed class ReceiptInspection
{
    public ReceiptInspectionStatus Status { get; }
    public Guid? OriginalMessageId { get; }
    public LogicalReceiptOutcome? OriginalOutcome { get; }
    /// <summary>The exact receipt query returned no row. This does not establish a terminal operation.</summary>
    public bool ExactKeyAbsent { get; }
    internal ReceiptInspection(ReceiptInspectionStatus status, Guid? messageId = null, LogicalReceiptOutcome? outcome = null,
        bool exactKeyAbsent = false)
    { Status = status; OriginalMessageId = messageId; OriginalOutcome = outcome; ExactKeyAbsent = exactKeyAbsent; }
    public override string ToString() => "ReceiptInspection [private observation]";
}

/// <summary>
/// Bounded read-only exact-key inspection of MSG-owned receipts. The trusted coordinator supplies
/// scope/authorization, an independently opened reader and evidence that the original operation is
/// terminal. This component opens no connection and cannot attest to any of those external facts.
/// Missing/unavailable rows remain unknown; matching rows are observations, not a Durable outcome.
/// Expired binding can still be inspected for reconciliation without making it execution-eligible.
/// </summary>
public sealed class MessageReceiptInspector
{
    private readonly DbConnection _connection;
    private readonly string _schema;
    private readonly Guid? _fixtureCaseId;
    public MessageReceiptInspector(DbConnection connection) : this(connection, "msg") { }
    private MessageReceiptInspector(DbConnection connection, string schema, Guid? fixtureCaseId = null)
    { _connection = connection ?? throw new ArgumentNullException(nameof(connection)); _schema = schema; _fixtureCaseId = fixtureCaseId; }

    public static MessageReceiptInspector CreateIsolatedTestInspector(NpgsqlConnection connection, Guid caseId)
        => CreateIsolatedTestInspector((DbConnection)connection, caseId);

    public static MessageReceiptInspector CreateIsolatedTestInspector(DbConnection connection, Guid caseId)
        => new(connection, IsolatedMessagingFixture.SchemaFor(connection, caseId), caseId);

    public static MessageReceiptInspector CreateDisposableTestInspector(DbConnection connection, Guid caseId, DisposableTestBinding profile)
        => new(connection, IsolatedMessagingFixture.SchemaForDisposableTest(connection, caseId, profile), caseId);

    /// <summary>
    /// Reads terminal quarantine evidence for one exact expired delivery in the isolated profile.
    /// A trusted coordinator must hold custody and keep all native writers quiesced across this read.
    /// </summary>
    public Task<bool> ObserveExpiredQuarantineAsync(VerifiedMessageEnvelope envelope, CompiledMessageRegistry registry,
        MessageContext native, DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(native);
        return ObserveExpiredQuarantineAsync(envelope, registry,
            native.Runtime.Options.Transports.GetOrCreate<PostgresqlTransport>(), effectiveUtc, ct);
    }

    public async Task<bool> ObserveExpiredQuarantineAsync(VerifiedMessageEnvelope envelope, CompiledMessageRegistry registry,
        PostgresqlTransport transport, DateTimeOffset effectiveUtc, CancellationToken ct = default)
        => await ObserveExpiredTerminalAsync(envelope, registry, transport, effectiveUtc, quarantine: true, ct);

    /// <summary>Exact expired handled-delivery observation. An aborted business root must be proved separately.</summary>
    public Task<bool> ObserveExpiredHandledAsync(VerifiedMessageEnvelope envelope, CompiledMessageRegistry registry,
        MessageContext native, DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(native);
        return ObserveExpiredHandledAsync(envelope, registry,
            native.Runtime.Options.Transports.GetOrCreate<PostgresqlTransport>(), effectiveUtc, ct);
    }

    public Task<bool> ObserveExpiredHandledAsync(VerifiedMessageEnvelope envelope, CompiledMessageRegistry registry,
        PostgresqlTransport transport, DateTimeOffset effectiveUtc, CancellationToken ct = default)
        => ObserveExpiredTerminalAsync(envelope, registry, transport, effectiveUtc, quarantine: false, ct);

    private async Task<bool> ObserveExpiredTerminalAsync(VerifiedMessageEnvelope envelope, CompiledMessageRegistry registry,
        PostgresqlTransport transport, DateTimeOffset effectiveUtc, bool quarantine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(transport);
        ct.ThrowIfCancellationRequested();
        if (_fixtureCaseId is not { } caseId || envelope.Binding.Identity.InstallationId != caseId ||
            envelope.Binding.Identity.Scope.TenantId != caseId || _connection.State != ConnectionState.Open ||
            transport.TransportSchemaName != _schema || effectiveUtc < envelope.Binding.DeadlineUtc)
            return false;

        try
        {
            var route = registry.RouteFor(envelope.Binding);
            var queue = transport.SanitizeIdentifier(route);
            var declared = transport.Queues.Select(q => q.Name).Distinct(StringComparer.Ordinal).Take(257).ToArray();
            if (declared.Length > 256 || !declared.Contains(queue, StringComparer.Ordinal))
                return false;

            var schema = Quote(_schema);
            var pending = new List<string> {
                $"SELECT 1 FROM {schema}.wolverine_outgoing_envelopes WHERE id=@id",
                $"SELECT 1 FROM {schema}.wolverine_incoming_envelopes WHERE id=@id AND status <> 'Handled'"
            };
            foreach (var name in declared)
            {
                var table = "wolverine_queue_" + name;
                pending.Add($"SELECT 1 FROM {schema}.{Quote(PostgresqlIdentifier.Shorten(table))} WHERE id=@id");
                pending.Add($"SELECT 1 FROM {schema}.{Quote(PostgresqlIdentifier.Shorten(table + "_scheduled"))} WHERE id=@id");
            }
            // A journal row ties the original canonical binding to the delivery ID. A native DLQ row
            // on its own can be replayable, belong to a different route, or contain a different payload.
            using var command = _connection.CreateCommand();
            command.CommandTimeout = 5;
            var terminal = quarantine ? $"""
                SELECT count(*) = 1 AND coalesce(bool_and(replayable IS FALSE AND archived_at IS NULL
                    AND received_at=@destination AND message_type=@diagnostic), false)
                FROM (
                    SELECT replayable, archived_at, received_at, message_type
                    FROM {schema}.wolverine_dead_letters WHERE id=@id LIMIT 2
                ) AS terminal
                """ : $"""
                SELECT count(*) = 1 AND coalesce(bool_and(status='Handled' AND received_at=@destination), false)
                FROM (
                    SELECT status, received_at FROM {schema}.wolverine_incoming_envelopes WHERE id=@id LIMIT 2
                ) AS terminal
                """;
            var conflictingQuarantine = quarantine ? "" :
                $" AND NOT EXISTS (SELECT 1 FROM {schema}.wolverine_dead_letters WHERE id=@id)";
            command.CommandText = $"""
                SELECT EXISTS (
                    SELECT 1 FROM {schema}.quarantine_journal
                    WHERE record_id=@id AND installation_id=@installation AND reason='Expired'
                      AND observed_hash=@hash AND recorded_at_utc BETWEEN @deadline AND @now
                ) AND ({terminal}){conflictingQuarantine} AND NOT EXISTS ({string.Join(" UNION ALL ", pending)})
                """;
            Add("id", DbType.Guid, envelope.Delivery.MessageId);
            Add("installation", DbType.Guid, caseId);
            Add("hash", DbType.String, envelope.PayloadHash);
            Add("deadline", DbType.DateTime, envelope.Binding.DeadlineUtc.UtcDateTime);
            Add("now", DbType.DateTime, effectiveUtc.UtcDateTime);
            Add("destination", DbType.String, new Uri("postgresql://" + queue).ToString());
            if (quarantine) Add("diagnostic", DbType.String, RejectedEnvelopeDiagnostic.TypeId);
            return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;

            void Add(string name, DbType type, object value)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name; parameter.DbType = type; parameter.Value = value;
                command.Parameters.Add(parameter);
            }
        }
        catch (Exception error) when (error is DbException or InvalidOperationException or ArgumentException)
        { return false; }
    }

    private static string Quote(string identifier)
    {
        if (identifier is not { Length: > 0 and <= 63 } || identifier.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')))
            throw new InvalidOperationException("Compiled native identifier is invalid.");
        return '"' + identifier + '"';
    }

    public Task<ReceiptInspection> InspectPublicationAsync(MessageIdentity identity, string expectedHash, CancellationToken cancellationToken)
        => InspectAsync(identity, expectedHash, true, cancellationToken);

    public Task<ReceiptInspection> InspectLogicalAsync(MessageIdentity identity, string expectedHash, CancellationToken cancellationToken)
        => InspectAsync(identity, expectedHash, false, cancellationToken);

    private async Task<ReceiptInspection> InspectAsync(MessageIdentity identity, string expectedHash, bool publication, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!CanonicalMessageBinding.Matches(expectedHash, expectedHash))
            throw new ArgumentException("Expected lowercase canonical binding SHA-256 required.", nameof(expectedHash));
        ct.ThrowIfCancellationRequested();
        if (_connection.State != ConnectionState.Open) return new(ReceiptInspectionStatus.Unavailable);
        try
        {
            var table = _schema + (publication ? ".publication_receipts" : ".logical_receipts");
            var field = publication ? "message_id" : "outcome";
            using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT binding_hash,{field} FROM {table} WHERE installation_id=@installation AND " +
                "scope_kind=@scope AND tenant_key=@tenant AND operation_id=@operation AND step_id=@step AND " +
                "step_version=@version AND type_id=@type LIMIT 2";
            command.CommandTimeout = 5;
            Add("installation", DbType.Guid, identity.InstallationId); Add("scope", DbType.String, identity.Scope.Kind);
            Add("tenant", DbType.Guid, identity.Scope.TenantId ?? Guid.Empty); Add("operation", DbType.Guid, identity.OperationId);
            Add("step", DbType.Guid, identity.StepId); Add("version", DbType.Int64, identity.StepVersion); Add("type", DbType.String, identity.TypeId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return new(ReceiptInspectionStatus.StillUnknown, exactKeyAbsent: true);
            var storedHash = reader.GetString(0);
            if (!CanonicalMessageBinding.Matches(storedHash, storedHash)) return new(ReceiptInspectionStatus.Unavailable);
            Guid? messageId = publication ? reader.GetGuid(1) : null;
            LogicalReceiptOutcome? outcome = publication ? null : reader.GetString(1) switch
            {
                "Completed" => LogicalReceiptOutcome.Completed,
                "Denied" => LogicalReceiptOutcome.Denied,
                _ => null
            };
            if ((publication ? messageId == Guid.Empty : outcome is null) || await reader.ReadAsync(ct).ConfigureAwait(false))
                return new(ReceiptInspectionStatus.Unavailable);
            return CanonicalMessageBinding.Matches(storedHash, expectedHash)
                ? new(ReceiptInspectionStatus.MatchingObserved, messageId, outcome)
                : new(ReceiptInspectionStatus.Conflict);

            void Add(string name, DbType type, object value)
            {
                var parameter = command.CreateParameter(); parameter.ParameterName = name;
                parameter.DbType = type; parameter.Value = value; command.Parameters.Add(parameter);
            }
        }
        catch (Exception error) when (error is DbException or InvalidOperationException or InvalidCastException)
        { return new(ReceiptInspectionStatus.Unavailable); }
    }
}
