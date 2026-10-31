using System.Text.RegularExpressions;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

public sealed record NativeMessagingObject(string Schema, string Name, string Kind);

/// <summary>Offline inventory. Unknown objects never receive broad grants as a fallback.</summary>
public sealed class NativeMessagingInventory
{
    public IReadOnlyList<NativeMessagingObject> Objects { get; }
    public string ReviewSql { get; }
    public bool FunctionAndImplicitSequenceReviewRequired => !IsReviewedEmptyCatalogProfile;
    public bool IsReviewedEmptyCatalogProfile { get; }

    public NativeMessagingInventory(IDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var objects = database.AllObjects().Select(o => new NativeMessagingObject(o.Identifier.Schema,
            o.Identifier.Name, o is Table ? "table" : o is Sequence ? "sequence" : "requires_review"))
            .OrderBy(o => o.Schema, StringComparer.Ordinal).ThenBy(o => o.Name, StringComparer.Ordinal).ToArray();
        if (objects.Length == 0 || objects.Select(o => (o.Schema, o.Name)).Distinct().Count() != objects.Length)
            throw new InvalidOperationException("Native inventory is missing or ambiguous.");
        foreach (var obj in objects)
            if (obj.Schema is not ("msg" or "msg_transport") || !Regex.IsMatch(obj.Name, "^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Native object is outside the reviewed MSG schemas.");
        Objects = Array.AsReadOnly(objects);
        // Exact 6.59.0 empty business-catalog profile, verified against an isolated PostgreSQL catalog.
        // Adding a queue/function/table invalidates this disposition; no generic future-object approval.
        var reviewedTables = new[] { "wolverine_agent_restrictions", "wolverine_control_queue", "wolverine_dead_letter_history",
            "wolverine_dead_letters", "wolverine_incoming_envelopes", "wolverine_node_assignments", "wolverine_node_records",
            "wolverine_nodes", "wolverine_outgoing_envelopes" };
        IsReviewedEmptyCatalogProfile = objects.Length == reviewedTables.Length &&
            objects.All(o => o.Schema == "msg" && o.Kind == "table") &&
            objects.Select(o => o.Name).SequenceEqual(reviewedTables);
        var lines = new List<string> { "-- REVIEW ONLY: exact direct objects; functions and implicit identity sequences still require review." };
        foreach (var obj in objects)
        {
            var name = $"\"{obj.Schema}\".\"{obj.Name}\"";
            if (obj.Kind == "table")
            {
                lines.Add($"REVOKE ALL ON TABLE {name} FROM PUBLIC, kordra_msg_runtime;");
                lines.Add($"GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE {name} TO kordra_msg_runtime;");
            }
            else if (obj.Kind == "sequence")
            {
                lines.Add($"REVOKE ALL ON SEQUENCE {name} FROM PUBLIC, kordra_msg_runtime;");
                lines.Add($"GRANT USAGE, SELECT ON SEQUENCE {name} TO kordra_msg_runtime;");
            }
            else lines.Add($"-- Explicit signature/privilege review required for {name}; no EXECUTE grant emitted.");
        }
        // Reviewed SERIAL dependencies in Wolverine 6.59.0's empty-catalog profile.
        // These are not inferred from all future tables or granted via a blanket sequence rule.
        foreach (var (table, sequence) in new[]
        {
            ("wolverine_nodes", "wolverine_nodes_node_number_seq"),
            ("wolverine_node_records", "wolverine_node_records_id_seq")
        })
        {
            if (!objects.Any(o => o.Schema == "msg" && o.Name == table && o.Kind == "table"))
                throw new InvalidOperationException("Reviewed native identity table is missing.");
            lines.Add($"-- Catalog verification still required for the reviewed identity sequence msg.{sequence}.");
            lines.Add($"REVOKE ALL ON SEQUENCE \"msg\".\"{sequence}\" FROM PUBLIC, kordra_msg_runtime;");
            lines.Add($"GRANT USAGE, SELECT ON SEQUENCE \"msg\".\"{sequence}\" TO kordra_msg_runtime;");
        }
        ReviewSql = string.Join('\n', lines) + "\n";
    }
}
