using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>
/// T12.3a PoC (inactive, no DI registration): calls <c>wfl.stage_transition</c> of <c>wfl-owner-functions-v1.sql</c> on the
/// caller-owned root. The function requires an IAM permit consumed in this same root; scope values are only compared.
/// Never opens, commits or rolls back. Owner errors surface as <see cref="PostgresException"/> with a <c>KD1xx</c> SQLSTATE.
/// </summary>
public sealed class WorkflowTransitionFunctionPort
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _root;

    public WorkflowTransitionFunctionPort(NpgsqlConnection connection, NpgsqlTransaction root)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(root);
        if (!ReferenceEquals(root.Connection, connection) || connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("Owner functions require the caller's open root connection and transaction.");
        (_connection, _root) = (connection, root);
    }

    public async Task<WorkflowStoredResult> StageTransitionAsync(Guid permitId, WorkflowIdentity identity, Guid stepId,
        Guid operationId, string specificationHash, long expectedVersion, WorkflowState nextState, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(specificationHash);
        if (!Enum.IsDefined(nextState)) throw new ArgumentOutOfRangeException(nameof(nextState));
        await using var command = new NpgsqlCommand("SELECT resulting_version, duplicate FROM wfl.stage_transition(" +
            "@permit, @installation, @tenant, @instance, @step, @operation, @hash, @version, @next)", _connection, _root);
        command.Parameters.Add(new("permit", NpgsqlDbType.Uuid) { Value = permitId });
        command.Parameters.Add(new("installation", NpgsqlDbType.Uuid) { Value = identity.InstallationId });
        command.Parameters.Add(new("tenant", NpgsqlDbType.Uuid) { Value = identity.TenantId });
        command.Parameters.Add(new("instance", NpgsqlDbType.Uuid) { Value = identity.InstanceId });
        command.Parameters.Add(new("step", NpgsqlDbType.Uuid) { Value = stepId });
        command.Parameters.Add(new("operation", NpgsqlDbType.Uuid) { Value = operationId });
        command.Parameters.Add(new("hash", NpgsqlDbType.Text) { Value = specificationHash });
        command.Parameters.Add(new("version", NpgsqlDbType.Bigint) { Value = expectedVersion });
        command.Parameters.Add(new("next", NpgsqlDbType.Integer) { Value = (int)nextState });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Workflow transition returned no receipt.");
        return new(identity.InstanceId, reader.GetInt64(0), reader.GetBoolean(1));
    }
}
