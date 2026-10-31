using System.Data;
using System.Data.Common;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Npgsql;

namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

public enum ReceiptStageDisposition { Staged, ExistingInTransaction, Conflict }
public enum LogicalReceiptOutcome { Completed, Denied }

/// <summary>Transaction-local evidence only. No result here establishes a durable commit or authority.</summary>
public sealed record ReceiptStageResult(ReceiptStageDisposition Disposition,
    Guid? OriginalMessageId, LogicalReceiptOutcome? OriginalOutcome);

/// <summary>
/// Stages immutable MSG receipts on the caller's transaction. A newly staged logical receipt
/// holds the unique key until the caller commits/rolls back; stage before invoking an owner effect.
/// Existing/conflicting receipts must never run the effect again. Any failure requires caller rollback.
/// This library is not an activated consumer, admission check or post-commit lookup service.
/// </summary>
public sealed class CallerOwnedReceiptStore
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly string _schema;

    public CallerOwnedReceiptStore(DbConnection connection, DbTransaction transaction) : this(connection, transaction, "msg") { }

    private CallerOwnedReceiptStore(DbConnection connection, DbTransaction transaction, string schema)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _schema = schema;
        RequireBinding();
    }

    /// <summary>UUID-only local test seam; never opens a connection or provisions a schema.</summary>
    public static CallerOwnedReceiptStore CreateIsolatedTestStore(NpgsqlConnection connection, DbTransaction transaction, Guid caseId)
        => new(connection, transaction, IsolatedMessagingFixture.SchemaFor(connection, caseId));

    internal void RequireCaller(DbConnection connection, DbTransaction transaction, string schema)
    {
        RequireBinding();
        if (!ReferenceEquals(connection, _connection) || !ReferenceEquals(transaction, _transaction) || _schema != schema)
            throw new InvalidOperationException("Receipt and native stagers require the same caller transaction and schema.");
    }

    public Task<ReceiptStageResult> StagePublicationAsync(VerifiedMessageEnvelope envelope,
        DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
        StageAsync(envelope, null, recordedAtUtc, cancellationToken);

    public Task<ReceiptStageResult> StageLogicalAsync(VerifiedMessageEnvelope envelope,
        LogicalReceiptOutcome outcome, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        return StageAsync(envelope, outcome, recordedAtUtc, cancellationToken);
    }

    private async Task<ReceiptStageResult> StageAsync(VerifiedMessageEnvelope envelope,
        LogicalReceiptOutcome? outcome, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        RequireBinding();
        ArgumentNullException.ThrowIfNull(envelope);
        if (recordedAtUtc == default) throw new ArgumentException("Explicit recording time required.", nameof(recordedAtUtc));
        if (recordedAtUtc < envelope.Binding.OccurredAtUtc || recordedAtUtc >= envelope.Binding.DeadlineUtc)
            throw new InvalidOperationException("Message is outside its original execution lifetime.");
        cancellationToken.ThrowIfCancellationRequested();
        var publication = outcome is null;
        // Fixed table names; schema is production msg or a guarded UUID fixture, never a wire/locator value.
        var table = _schema + (publication ? ".publication_receipts" : ".logical_receipts");
        var field = publication ? "message_id" : "outcome";
        const string keyColumns = "installation_id, scope_kind, tenant_key, operation_id, step_id, step_version, type_id";
        const string keyValues = "@installation, @scope, @tenant, @operation, @step, @version, @type";
        const string predicate = "installation_id=@installation AND scope_kind=@scope AND tenant_key=@tenant AND " +
            "operation_id=@operation AND step_id=@step AND step_version=@version AND type_id=@type";
        using var insert = Command($"INSERT INTO {table} ({keyColumns}, type_schema_version, binding_hash, original_deadline_utc, {field}, recorded_at_utc) " +
            $"VALUES ({keyValues}, @schemaVersion, @hash, @deadline, @value, @recorded) ON CONFLICT ({keyColumns}) DO NOTHING", envelope);
        Add(insert, "schemaVersion", DbType.Int32, envelope.Binding.TypeSchemaVersion);
        Add(insert, "hash", DbType.String, envelope.PayloadHash);
        Add(insert, "deadline", DbType.DateTime, envelope.Binding.DeadlineUtc.UtcDateTime);
        Add(insert, "value", publication ? DbType.Guid : DbType.String,
            publication ? envelope.Delivery.MessageId : outcome!.Value.ToString());
        Add(insert, "recorded", DbType.DateTime, recordedAtUtc.UtcDateTime);
        var inserted = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted == 1)
            return new(ReceiptStageDisposition.Staged, publication ? envelope.Delivery.MessageId : null, outcome);
        if (inserted != 0) throw new InvalidOperationException("Unexpected receipt insertion result.");

        // Separate statement sees the winner under READ COMMITTED after bounded unique-key contention.
        // Snapshot/serialization failures propagate; absence is never treated as permission to replay.
        using var lookup = Command($"SELECT binding_hash, {field} FROM {table} WHERE {predicate}", envelope);
        await using var reader = await lookup.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Conflicting receipt could not be resolved in caller transaction.");
        var hash = reader.GetString(0);
        Guid? originalMessage = publication ? reader.GetGuid(1) : null;
        LogicalReceiptOutcome? originalOutcome = publication ? null : reader.GetString(1) switch
        {
            "Completed" => LogicalReceiptOutcome.Completed,
            "Denied" => LogicalReceiptOutcome.Denied,
            _ => throw new InvalidOperationException("Invalid persisted receipt outcome.")
        };
        if (originalMessage == Guid.Empty || await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Invalid persisted receipt identity.");
        // Conflicts disclose no result from a different business binding, even within the same logical key.
        return CanonicalMessageBinding.Matches(hash, envelope.PayloadHash)
            ? new(ReceiptStageDisposition.ExistingInTransaction, originalMessage, originalOutcome)
            : new(ReceiptStageDisposition.Conflict, null, null);
    }

    private DbCommand Command(string sql, VerifiedMessageEnvelope envelope)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql; command.Transaction = _transaction; command.CommandTimeout = 5;
        var id = envelope.Binding.Identity;
        Add(command, "installation", DbType.Guid, id.InstallationId);
        Add(command, "scope", DbType.String, id.Scope.Kind);
        Add(command, "tenant", DbType.Guid, id.Scope.TenantId ?? Guid.Empty);
        Add(command, "operation", DbType.Guid, id.OperationId);
        Add(command, "step", DbType.Guid, id.StepId);
        Add(command, "version", DbType.Int64, id.StepVersion);
        Add(command, "type", DbType.String, id.TypeId);
        return command;
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name; parameter.DbType = type; parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private void RequireBinding()
    {
        if (_connection.State != ConnectionState.Open || !ReferenceEquals(_transaction.Connection, _connection))
            throw new InvalidOperationException("Open caller connection and its active transaction required.");
    }
}
