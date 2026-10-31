using System.Data;
using System.Data.Common;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Npgsql;

namespace Kordra.Modules.Messaging.Infrastructure.Recovery;

public enum ReceiptInspectionStatus { MatchingObserved, Conflict, StillUnknown, Unavailable }

/// <summary>Observation only. No status grants authority, proves custody or permits replay/resuming a writer.</summary>
public sealed class ReceiptInspection
{
    public ReceiptInspectionStatus Status { get; }
    public Guid? OriginalMessageId { get; }
    public LogicalReceiptOutcome? OriginalOutcome { get; }
    internal ReceiptInspection(ReceiptInspectionStatus status, Guid? messageId = null, LogicalReceiptOutcome? outcome = null)
    { Status = status; OriginalMessageId = messageId; OriginalOutcome = outcome; }
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
    public MessageReceiptInspector(DbConnection connection) : this(connection, "msg") { }
    private MessageReceiptInspector(DbConnection connection, string schema)
    { _connection = connection ?? throw new ArgumentNullException(nameof(connection)); _schema = schema; }

    public static MessageReceiptInspector CreateIsolatedTestInspector(NpgsqlConnection connection, Guid caseId)
        => new(connection, IsolatedMessagingFixture.SchemaFor(connection, caseId));

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
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return new(ReceiptInspectionStatus.StillUnknown);
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
