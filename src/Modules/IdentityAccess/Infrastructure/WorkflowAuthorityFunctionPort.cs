using Npgsql;
using NpgsqlTypes;

namespace Kordra.Modules.IdentityAccess.Infrastructure;

/// <summary>Exact worker permit claim; every field is only compared against the persisted permit, never trusted.</summary>
public sealed record WorkflowPermitClaim(Guid PermitId, Guid OperationId, Guid InstanceId, long ExpectedVersion,
    string SpecificationHash, Guid BootId, long CustodyEpoch);

public sealed record WorkflowPermitConsumption(Guid TenantId, ulong ConsumedTransactionId, bool Duplicate);

/// <summary>
/// T12.3a PoC (inactive, no DI registration): calls the IAM owner functions of <c>iam-workflow-authority-v1.sql</c> on the
/// caller-owned root connection/transaction. Never opens, commits or rolls back; identity comes from the session login only.
/// Owner errors surface as <see cref="PostgresException"/> with a Kordra <c>KD…</c> SQLSTATE.
/// </summary>
public sealed class WorkflowAuthorityFunctionPort
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _root;

    public WorkflowAuthorityFunctionPort(NpgsqlConnection connection, NpgsqlTransaction root)
    {
        (_connection, _root) = WorkflowFunctionRoot.Require(connection, root);
    }

    public async Task<long> AcquireFenceAsync(Guid installationId, CancellationToken ct = default)
    {
        await using var command = Command("SELECT iam_workflow.acquire_fence(@installation)");
        command.Parameters.Add(new("installation", NpgsqlDbType.Uuid) { Value = installationId });
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<long> AdvanceEpochAsync(Guid installationId, string reasonCode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reasonCode);
        await using var command = Command("SELECT iam_workflow.advance_epoch(@installation, @reason)");
        command.Parameters.Add(new("installation", NpgsqlDbType.Uuid) { Value = installationId });
        command.Parameters.Add(new("reason", NpgsqlDbType.Text) { Value = reasonCode });
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<WorkflowPermitConsumption> ConsumePermitAsync(WorkflowPermitClaim claim, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using var command = Command("SELECT tenant_id, consumed_xact::text, duplicate FROM iam_workflow.consume_permit(" +
            "@permit, @operation, @instance, @version, @hash, @boot, @custody)");
        command.Parameters.Add(new("permit", NpgsqlDbType.Uuid) { Value = claim.PermitId });
        command.Parameters.Add(new("operation", NpgsqlDbType.Uuid) { Value = claim.OperationId });
        command.Parameters.Add(new("instance", NpgsqlDbType.Uuid) { Value = claim.InstanceId });
        command.Parameters.Add(new("version", NpgsqlDbType.Bigint) { Value = claim.ExpectedVersion });
        command.Parameters.Add(new("hash", NpgsqlDbType.Text) { Value = claim.SpecificationHash ?? (object)DBNull.Value });
        command.Parameters.Add(new("boot", NpgsqlDbType.Uuid) { Value = claim.BootId });
        command.Parameters.Add(new("custody", NpgsqlDbType.Bigint) { Value = claim.CustodyEpoch });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Permit consumption returned no receipt.");
        return new(reader.GetGuid(0), ulong.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture), reader.GetBoolean(2));
    }

    public async Task<DateTimeOffset> AdvanceFloorAsync(Guid installationId, DateTimeOffset admittedUtc, CancellationToken ct = default)
    {
        await using var command = Command("SELECT iam_workflow.advance_floor(@installation, @admitted)");
        command.Parameters.Add(new("installation", NpgsqlDbType.Uuid) { Value = installationId });
        command.Parameters.Add(new("admitted", NpgsqlDbType.TimestampTz) { Value = admittedUtc.ToUniversalTime() });
        var floor = (DateTime)(await command.ExecuteScalarAsync(ct))!;
        return new DateTimeOffset(DateTime.SpecifyKind(floor, DateTimeKind.Utc));
    }

    private NpgsqlCommand Command(string sql)
    {
        if (_root.Connection != _connection)
            throw new InvalidOperationException("The caller root is no longer active.");
        return new NpgsqlCommand(sql, _connection, _root);
    }
}

/// <summary>Shared guard: owner function ports run only on an open caller connection with its own live root.</summary>
internal static class WorkflowFunctionRoot
{
    public static (NpgsqlConnection Connection, NpgsqlTransaction Root) Require(NpgsqlConnection connection, NpgsqlTransaction root)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(root);
        if (!ReferenceEquals(root.Connection, connection) || connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("Owner functions require the caller's open root connection and transaction.");
        return (connection, root);
    }
}
