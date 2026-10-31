using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Npgsql;
using Weasel.Postgresql;
using Wolverine.Postgresql.Transport;

namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

public enum PublicationCapacityStatus { WithinWatermark, AboveWatermark, Unavailable }

/// <summary>
/// Read-only bounded MSG-owned availability observation, not a hard quota or authorization decision.
/// Native FetchCountsAsync may estimate large outboxes; admission instead reads at most watermark+1
/// pending rows per compiled table and in aggregate. Handled/DLQ history is not pending work.
/// </summary>
public sealed class PublicationCapacityGuard
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly string _schema;
    private readonly string _transportSchema;
    public PublicationCapacityGuard(DbConnection connection, DbTransaction transaction) : this(connection, transaction, "msg", "msg_transport") { }
    private PublicationCapacityGuard(DbConnection connection, DbTransaction transaction, string schema, string transportSchema)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _schema = schema; _transportSchema = transportSchema;
    }
    public static PublicationCapacityGuard CreateIsolatedTestGuard(NpgsqlConnection connection, DbTransaction transaction, Guid caseId)
    {
        var schema = IsolatedMessagingFixture.SchemaFor(connection, caseId);
        return new(connection, transaction, schema, schema);
    }

    internal void RequireCaller(DbConnection connection, DbTransaction transaction, string schema)
    {
        if (!ReferenceEquals(connection, _connection) || !ReferenceEquals(transaction, _transaction) || schema != _schema)
            throw new InvalidOperationException("Capacity and publication must share the exact caller binding.");
    }

    public async Task<PublicationCapacityStatus> CheckAsync(CompiledMessageRegistry registry, PostgresqlTransport transport, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(transport);
        ct.ThrowIfCancellationRequested();
        if (_connection.State != ConnectionState.Open || !ReferenceEquals(_transaction.Connection, _connection) ||
            transport.TransportSchemaName != _transportSchema) return PublicationCapacityStatus.Unavailable;
        var tables = new List<string>
        {
            $"SELECT 1 FROM {Quote(_schema)}.wolverine_incoming_envelopes WHERE status IN ('Incoming','Scheduled') LIMIT 10001",
            $"SELECT 1 FROM {Quote(_schema)}.wolverine_outgoing_envelopes LIMIT 10001"
        };
        var declaredQueues = transport.Queues.ToArray();
        if (declaredQueues.Length > 256) return PublicationCapacityStatus.Unavailable;
        foreach (var route in registry.Routes)
        {
            var name = transport.SanitizeIdentifier(route);
            // Enumerate only already-declared endpoints; the cache indexer would silently create a new queue.
            if (!declaredQueues.Any(q => q.Name == name)) return PublicationCapacityStatus.Unavailable;
        }
        // Count every declared native queue, including internal/result queues: omitting queues not
        // referenced by the particular registry would under-report committed pending work.
        foreach (var name in declaredQueues.Select(q => q.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            foreach (var suffix in new[] { "", "_scheduled" })
            {
                var table = PostgresqlIdentifier.Shorten("wolverine_queue_" + name + suffix);
                tables.Add($"SELECT 1 FROM {Quote(_transportSchema)}.{Quote(table)} LIMIT 10001");
            }
        }
        try
        {
            using var command = _connection.CreateCommand(); command.Transaction = _transaction; command.CommandTimeout = 5;
            command.CommandText = "SELECT count(*) FROM (" + string.Join(" UNION ALL ", tables.Select(t => "(" + t + ")")) + " LIMIT 10001) AS pending";
            var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return value is long count && count is >= 0 and <= 10001
                ? count > 10000 ? PublicationCapacityStatus.AboveWatermark : PublicationCapacityStatus.WithinWatermark
                : PublicationCapacityStatus.Unavailable;
        }
        catch (Exception error) when (error is DbException or InvalidOperationException)
        { return PublicationCapacityStatus.Unavailable; }
    }

    private static string Quote(string identifier)
    {
        if (!Regex.IsMatch(identifier, "^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Compiled native identifier is invalid.");
        return '"' + identifier + '"';
    }
}
