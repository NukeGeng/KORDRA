using System.Globalization;
using System.Text.Json;
using Kordra.Modules.InfrastructureInventory.Contracts;

namespace Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;

public static class NomadWorkloadProjection
{
    internal static void Identity(byte[] bytes, NomadTargetTrust trust)
    {
        using var doc = Parse(bytes); var e = doc.RootElement;
        if (e.ValueKind != JsonValueKind.Object || Text(e, "Name", 63) != trust.NamespaceName ||
            !e.TryGetProperty("CreateIndex", out var index) || !index.TryGetUInt64(out var n) || n == 0 ||
            n.ToString(CultureInfo.InvariantCulture) != trust.NamespaceCreateIndex) throw NomadReadOnlyTransport.Unavailable();
    }
    public static NomadResourceSetV1 Project(string kind, ReadOnlyMemory<byte> bytes, string ns, string index, bool partial)
    {
        if (kind is not ("Jobs" or "Allocations") || !ValidIndex(index)) throw NomadReadOnlyTransport.Unavailable();
        using var doc = Parse(bytes); var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > 25) throw NomadReadOnlyTransport.Unavailable();
        var items = new List<NomadWorkloadItemV1>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in root.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) throw NomadReadOnlyTransport.Unavailable();
            // Nomad list stubs carry Namespace; never trust a foreign namespace from a broad token.
            if (Text(e, "Namespace", 63) != ns) throw NomadReadOnlyTransport.Unavailable();
            var id = Text(e, "ID", 256); if (!ids.Add(id)) throw NomadReadOnlyTransport.Unavailable();
            if (kind == "Jobs") items.Add(new NomadJobV1(id, Text(e, "Name", 256),
                Enum(e, "Type", ["service", "batch", "system", "sysbatch"]), Enum(e, "Status", ["pending", "running", "dead"])));
            else
            {
                if (!Guid.TryParseExact(id, "D", out var parsed) || parsed == Guid.Empty || parsed.ToString("D") != id) throw NomadReadOnlyTransport.Unavailable();
                items.Add(new NomadAllocationV1(id, Text(e, "JobID", 256), Text(e, "TaskGroup", 128),
                    Enum(e, "ClientStatus", ["pending", "running", "complete", "failed", "lost", "unknown"]), Enum(e, "DesiredStatus", ["run", "stop", "evict"])));
            }
        }
        return new(kind, "observed", partial ? "partial" : "complete", index,
            Array.AsReadOnly(items.OrderBy(x => x is NomadJobV1 j ? j.Id : ((NomadAllocationV1)x).Id, StringComparer.Ordinal).ToArray()));
    }
    internal static bool ValidIndex(string? s) => ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 && n.ToString(CultureInfo.InvariantCulture) == s;
    private static string Text(JsonElement e, string name, int max)
    {
        if (!e.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } s ||
            s.Length > max || s.Any(c => c is < ' ' or > '~')) throw NomadReadOnlyTransport.Unavailable(); return s;
    }
    private static string? Enum(JsonElement e, string name, string[] allowed)
    {
        if (!e.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw NomadReadOnlyTransport.Unavailable();
        var s = value.GetString(); return allowed.Contains(s, StringComparer.Ordinal) ? s : null;
    }
    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > 262144) throw NomadReadOnlyTransport.Unavailable();
        try { var d = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); try { Unique(d.RootElement); return d; } catch { d.Dispose(); throw; } }
        catch (JsonException) { throw NomadReadOnlyTransport.Unavailable(); }
    }
    private static void Unique(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var p in e.EnumerateObject()) { if (!seen.Add(p.Name)) throw NomadReadOnlyTransport.Unavailable(); Unique(p.Value); } }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) Unique(item);
    }
}
