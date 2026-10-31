using System.Data;
using System.Data.Common;
using Npgsql;

namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

public enum MessageQuarantineReason { InvalidEnvelope, UnknownType, ScopeMismatch, HashConflict, Expired, RetryExhausted, UnknownCommit }
public enum MessageRecoveryDisposition { InspectRequested, DurableFound, Conflict, StillUnknown }

/// <summary>
/// Append-only metadata on the exact caller transaction. Deliberately accepts neither payload,
/// exception, free-form reason, SQL, credential nor an authority decision. Completion only means staged.
/// Duplicate record IDs fail rather than silently replacing or accepting conflicting metadata.
/// </summary>
public sealed class CallerOwnedMessageJournal
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly string _schema;
    public CallerOwnedMessageJournal(DbConnection connection, DbTransaction transaction) : this(connection, transaction, "msg") { }
    private CallerOwnedMessageJournal(DbConnection connection, DbTransaction transaction, string schema)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _schema = schema;
        RequireBinding();
    }
    public static CallerOwnedMessageJournal CreateIsolatedTestJournal(NpgsqlConnection connection, DbTransaction transaction, Guid caseId)
        => new(connection, transaction, IsolatedMessagingFixture.SchemaFor(connection, caseId));

    public Task StageQuarantineAsync(Guid recordId, Guid installationId, MessageQuarantineReason reason,
        string observedHash, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        Validate(recordId, installationId, observedHash, recordedAtUtc);
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        return AppendAsync($"INSERT INTO {_schema}.quarantine_journal (record_id,installation_id,reason,observed_hash,recorded_at_utc) " +
            "VALUES (@record,@installation,@category,@hash,@recorded)", recordId, installationId, null,
            reason.ToString(), observedHash, recordedAtUtc, cancellationToken);
    }

    public Task StageRecoveryAsync(Guid recordId, Guid installationId, Guid operationId, MessageRecoveryDisposition disposition,
        string bindingHash, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        Validate(recordId, installationId, bindingHash, recordedAtUtc);
        if (operationId == Guid.Empty || !Enum.IsDefined(disposition))
            throw new ArgumentException("Bound operation and known recovery disposition required.");
        return AppendAsync($"INSERT INTO {_schema}.recovery_journal (record_id,installation_id,operation_id,disposition,binding_hash,recorded_at_utc) " +
            "VALUES (@record,@installation,@operation,@category,@hash,@recorded)", recordId, installationId, operationId,
            disposition.ToString(), bindingHash, recordedAtUtc, cancellationToken);
    }

    private async Task AppendAsync(string compiledSql, Guid recordId, Guid installationId, Guid? operationId,
        string category, string hash, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        RequireBinding(); cancellationToken.ThrowIfCancellationRequested();
        using var command = _connection.CreateCommand();
        command.CommandText = compiledSql; command.Transaction = _transaction; command.CommandTimeout = 5;
        Add("record", DbType.Guid, recordId); Add("installation", DbType.Guid, installationId);
        if (operationId is { } operation) Add("operation", DbType.Guid, operation);
        Add("category", DbType.String, category); Add("hash", DbType.String, hash);
        Add("recorded", DbType.DateTime, recordedAtUtc.UtcDateTime);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Journal append was not confirmed within caller transaction.");
        void Add(string name, DbType type, object value)
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = name;
            parameter.DbType = type; parameter.Value = value; command.Parameters.Add(parameter);
        }
    }

    private static void Validate(Guid recordId, Guid installationId, string hash, DateTimeOffset recordedAtUtc)
    {
        if (recordId == Guid.Empty || installationId == Guid.Empty || recordedAtUtc == default ||
            hash is not { Length: 64 } || hash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Bound metadata identity, lowercase SHA-256 and explicit time required.");
    }

    private void RequireBinding()
    {
        if (_connection.State != ConnectionState.Open || !ReferenceEquals(_transaction.Connection, _connection))
            throw new InvalidOperationException("Open caller connection and its active transaction required.");
    }
}
