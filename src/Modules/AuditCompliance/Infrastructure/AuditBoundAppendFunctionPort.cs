using Kordra.Modules.AuditCompliance.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Kordra.Modules.AuditCompliance.Infrastructure;

public sealed record AuditBoundAppendResult(Guid RecordId, string CanonicalHashHex, bool Duplicate);

/// <summary>
/// T12.3a PoC (inactive, no DI registration): appends one permit-bound AUD intent through <c>app.aud_append_bound_intent</c>
/// (<c>aud-bound-append-v1.sql</c>) on the caller-owned root. <see cref="AuditIntentCanonicalizer"/> stays the only
/// canonicalizer (D2); the function verifies the hash and binds the columns. Never opens, commits or rolls back.
/// </summary>
public sealed class AuditBoundAppendFunctionPort
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _root;

    public AuditBoundAppendFunctionPort(NpgsqlConnection connection, NpgsqlTransaction root)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(root);
        if (!ReferenceEquals(root.Connection, connection) || connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("Owner functions require the caller's open root connection and transaction.");
        (_connection, _root) = (connection, root);
    }

    public async Task<AuditBoundAppendResult> AppendAsync(Guid permitId, Guid operationId, string specificationHash,
        AuditIntent intent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(specificationHash);
        var canonical = Canonicalize(intent);
        if (_root.Connection != _connection) throw new InvalidOperationException("The caller root is no longer active.");
        await using var command = new NpgsqlCommand("SELECT record_id, duplicate FROM app.aud_append_bound_intent(" +
            "@permit, @operation, @hash, @intent, @intent_hash)", _connection, _root);
        command.Parameters.Add(new("permit", NpgsqlDbType.Uuid) { Value = permitId });
        command.Parameters.Add(new("operation", NpgsqlDbType.Uuid) { Value = operationId });
        command.Parameters.Add(new("hash", NpgsqlDbType.Text) { Value = specificationHash });
        command.Parameters.Add(new("intent", NpgsqlDbType.Text) { Value = canonical.CanonicalJson });
        command.Parameters.Add(new("intent_hash", NpgsqlDbType.Bytea) { Value = canonical.Hash });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Audit append returned no receipt.");
        return new(reader.GetGuid(0), canonical.HashHex, reader.GetBoolean(1));
    }

    /// <summary>The exact canonical text and hash the function receives (golden-vector parity with the in-process path).</summary>
    public static (string CanonicalJson, byte[] Hash) CanonicalFor(AuditIntent intent)
    {
        var canonical = Canonicalize(intent);
        return (canonical.CanonicalJson, [.. canonical.Hash]);
    }

    private static CanonicalizedAuditIntent Canonicalize(AuditIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        try { return AuditIntentCanonicalizer.Canonicalize(intent); }
        catch (AuditIntentValidationException) { throw new ArgumentException("The audit intent contains invalid fields.", nameof(intent)); }
    }
}
