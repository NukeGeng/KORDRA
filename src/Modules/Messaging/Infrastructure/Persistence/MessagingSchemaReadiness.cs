using System.Data;
using System.Data.Common;
using Kordra.Modules.Messaging.Infrastructure.Serialization;

namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

/// <summary>Read-only schema check. This is not the full ACL, authority or host readiness decision.</summary>
public static class MessagingSchemaReadiness
{
    public static async Task<bool> MatchesAsync(DbConnection connection, string expectedManifestSha256, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open || !CanonicalMessageBinding.Matches(expectedManifestSha256, expectedManifestSha256)) return false;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT revision, manifest_sha256 FROM msg.schema_manifest ORDER BY revision LIMIT 2";
            command.CommandTimeout = 5;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return false;
            var matches = !reader.IsDBNull(0) && !reader.IsDBNull(1) && reader.GetInt32(0) == MessagingSchemaProfile.Revision &&
                CanonicalMessageBinding.Matches(expectedManifestSha256, reader.GetString(1));
            return matches && !await reader.ReadAsync(cancellationToken);
        }
        catch (Exception error) when (error is DbException or InvalidOperationException or OperationCanceledException or InvalidCastException)
        { return false; }
    }
}
