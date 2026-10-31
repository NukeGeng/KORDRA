using Npgsql;

namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

/// <summary>Testability guard only; no connection opening, schema creation or operational permission.</summary>
internal static class IsolatedMessagingFixture
{
    internal static string SchemaFor(NpgsqlConnection connection, Guid caseId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var target = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (caseId == Guid.Empty || target.Host != "127.0.0.1" || target.Port != 5432 ||
            target.Database != "kordra_test" || target.Username != "kordra_test" || target.Multiplexing ||
            !string.IsNullOrEmpty(target.SearchPath) || !string.IsNullOrEmpty(target.Options))
            throw new InvalidOperationException("Exact local UUID messaging fixture target required.");
        return "t12_" + caseId.ToString("N");
    }
}
