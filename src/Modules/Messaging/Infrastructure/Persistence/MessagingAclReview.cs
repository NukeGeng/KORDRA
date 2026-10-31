namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

/// <summary>Exact owned-object grants for review only; native object grants require generated inventory first.</summary>
public static class MessagingAclReview
{
    public static IReadOnlyList<string> ImmutableTables { get; } = Array.AsReadOnly(new[]
        { "publication_receipts", "logical_receipts", "quarantine_journal", "recovery_journal" });

    public static string OwnedObjectSql
    {
        get
        {
            var lines = new List<string> { "GRANT USAGE ON SCHEMA msg, msg_transport TO kordra_msg_runtime;" };
            foreach (var table in ImmutableTables)
            {
                lines.Add($"REVOKE ALL ON TABLE msg.{table} FROM PUBLIC, kordra_msg_runtime;");
                lines.Add($"GRANT SELECT, INSERT ON TABLE msg.{table} TO kordra_msg_runtime;");
            }
            lines.Add("REVOKE ALL ON TABLE msg.schema_manifest FROM PUBLIC, kordra_msg_runtime;");
            lines.Add("GRANT SELECT ON TABLE msg.schema_manifest TO kordra_msg_runtime;");
            return string.Join('\n', lines) + "\n";
        }
    }
}
