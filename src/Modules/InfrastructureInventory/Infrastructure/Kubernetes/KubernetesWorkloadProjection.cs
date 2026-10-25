using System.Text.Json;
using System.Text.RegularExpressions;
using Kordra.Modules.InfrastructureInventory.Contracts;

namespace Kordra.Modules.InfrastructureInventory.Infrastructure.Kubernetes;

/// <summary>Typed allowlist only. Provider documents and diagnostic strings never cross this boundary.</summary>
public static partial class KubernetesWorkloadProjection
{
    internal static void Identity(ReadOnlyMemory<byte> bytes, KubernetesTargetTrust trust)
    {
        using var doc = Parse(bytes); var e = doc.RootElement;
        MatchType(e, "Namespace", "v1");
        var metadata = Object(e, "metadata"); var status = Object(e, "status");
        if (Text(metadata, "name", 63) != trust.NamespaceName || Text(metadata, "uid", 128) != trust.NamespaceUid ||
            Text(status, "phase", 32) != "Active" || Present(metadata, "deletionTimestamp")) throw Invalid();
    }

    public static KubernetesResourceSetV1 Project(string kind, ReadOnlyMemory<byte> bytes, string ns)
    {
        if (kind is not ("Pod" or "Deployment" or "StatefulSet" or "DaemonSet")) throw Invalid();
        using var doc = Parse(bytes); var root = doc.RootElement;
        var version = kind == "Pod" ? "v1" : "apps/v1";
        MatchType(root, kind + "List", version);
        var metadata = Object(root, "metadata");
        var rv = Text(metadata, "resourceVersion", 128);
        var continuation = OptionalText(metadata, "continue", 262144, allowEmpty: true);
        var items = root.GetProperty("items");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 25) throw Invalid();
        var names = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<KubernetesWorkloadItemV1>();
        foreach (var item in items.EnumerateArray())
        {
            MatchType(item, kind, version); var m = Object(item, "metadata");
            var name = Text(m, "name", 253); var uid = Text(m, "uid", 128);
            if (!DnsSubdomain().IsMatch(name) || Text(m, "namespace", 63) != ns ||
                !names.Add(name) || !ids.Add(uid)) throw Invalid();
            var spec = OptionalObject(item, "spec"); var status = OptionalObject(item, "status");
            var podSpec = kind == "Pod" ? spec : OptionalObject(OptionalObject(spec, "template"), "spec");
            var images = Images(podSpec, kind == "Pod" ? status : default);
            int? desired = null, ready = null, available = null; string? phase = null; bool? podReady = null;
            if (kind == "Pod")
            {
                phase = OptionalText(status, "phase", 32);
                if (phase is not (null or "Pending" or "Running" or "Succeeded" or "Failed" or "Unknown")) phase = null;
                if (Present(status, "conditions"))
                {
                    var conditions = status.GetProperty("conditions"); if (conditions.ValueKind != JsonValueKind.Array) throw Invalid();
                    var seenReady = false;
                    foreach (var condition in conditions.EnumerateArray())
                    {
                        if (Text(condition, "type", 128) != "Ready") continue;
                        if (seenReady) throw Invalid(); seenReady = true;
                        podReady = Text(condition, "status", 32) switch { "True" => true, "False" => false, "Unknown" => null, _ => throw Invalid() };
                    }
                }
            }
            else if (kind == "DaemonSet")
            { desired = Count(status, "desiredNumberScheduled"); ready = Count(status, "numberReady"); available = Count(status, "numberAvailable"); }
            else { desired = Count(spec, "replicas"); ready = Count(status, "readyReplicas"); available = Count(status, "availableReplicas"); }
            result.Add(new(kind, name, uid, Generation(m, "generation"), Generation(status, "observedGeneration"),
                desired, ready, available, phase, podReady, images));
        }
        return new(kind, "observed", string.IsNullOrEmpty(continuation) ? "complete" : "partial", rv,
            Array.AsReadOnly(result.OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Uid, StringComparer.Ordinal).ToArray()));
    }

    private static IReadOnlyList<KubernetesImageV1> Images(JsonElement spec, JsonElement status)
    {
        var runtime = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Present(status, "containerStatuses"))
        {
            var statuses = status.GetProperty("containerStatuses");
            if (statuses.ValueKind != JsonValueKind.Array || statuses.GetArrayLength() > 16) throw Invalid();
            foreach (var s in statuses.EnumerateArray())
            {
                var name = Text(s, "name", 63); if (!DnsLabel().IsMatch(name)) throw Invalid();
                var raw = OptionalText(s, "imageID", 512, allowEmpty: true);
                if (!runtime.TryAdd(name, RuntimeDigest(raw))) throw Invalid();
            }
        }
        if (!Present(spec, "containers")) return Array.Empty<KubernetesImageV1>();
        var containers = spec.GetProperty("containers");
        if (containers.ValueKind != JsonValueKind.Array || containers.GetArrayLength() > 16) throw Invalid();
        var names = new HashSet<string>(StringComparer.Ordinal); var images = new List<KubernetesImageV1>();
        foreach (var c in containers.EnumerateArray())
        {
            var name = Text(c, "name", 63); if (!DnsLabel().IsMatch(name) || !names.Add(name)) throw Invalid();
            var raw = OptionalText(c, "image", 512, allowEmpty: true);
            var reference = Reference(raw); var digest = reference is null ? null : DigestSuffix(reference);
            images.Add(new(name, reference, digest, runtime.GetValueOrDefault(name)));
        }
        return Array.AsReadOnly(images.OrderBy(x => x.ContainerName, StringComparer.Ordinal).ToArray());
    }
    private static string? Reference(string? raw) => raw is { Length: > 0 } && raw.All(c => c is > ' ' and <= '~') && ImageReference().IsMatch(raw) ? raw : null;
    private static string? DigestSuffix(string raw)
    { var at = raw.LastIndexOf('@'); return at >= 0 ? NormalizeDigest(raw[(at + 1)..]) : null; }
    private static string? RuntimeDigest(string? raw)
    {
        if (raw is null) return null;
        if (raw.StartsWith("containerd://", StringComparison.Ordinal)) return NormalizeDigest(raw[13..]);
        if (raw.StartsWith("docker-pullable://", StringComparison.Ordinal))
        { var reference = Reference(raw[18..]); return reference is null ? null : DigestSuffix(reference); }
        return NormalizeDigest(raw);
    }
    private static string? NormalizeDigest(string s) => s.Length == 71 && s.All(c => c is > ' ' and <= '~') && Sha256().IsMatch(s) ? s.ToLowerInvariant() : null;
    private static int? Count(JsonElement e, string name)
    { if (!Present(e, name)) return null; var p = e.GetProperty(name); if (!p.TryGetInt32(out var n) || n < 0) throw Invalid(); return n; }
    private static long? Generation(JsonElement e, string name)
    { if (!Present(e, name)) return null; var p = e.GetProperty(name); if (!p.TryGetInt64(out var n) || n < 0) throw Invalid(); return n; }
    private static void MatchType(JsonElement e, string kind, string version)
    {
        if (e.ValueKind != JsonValueKind.Object || Present(e, "kind") && Text(e, "kind", 64) != kind ||
            Present(e, "apiVersion") && Text(e, "apiVersion", 64) != version) throw Invalid();
    }
    private static bool Present(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null;
    private static JsonElement Object(JsonElement e, string name)
    { var p = e.GetProperty(name); if (p.ValueKind != JsonValueKind.Object) throw Invalid(); return p; }
    private static JsonElement OptionalObject(JsonElement e, string name) => Present(e, name) ? Object(e, name) : default;
    private static string Text(JsonElement e, string name, int max) => OptionalText(e, name, max) ?? throw Invalid();
    private static string? OptionalText(JsonElement e, string name, int max, bool allowEmpty = false)
    {
        if (!Present(e, name)) return null;
        var p = e.GetProperty(name); if (p.ValueKind != JsonValueKind.String) throw Invalid();
        var s = p.GetString()!;
        // Unsupported image syntax is omitted later; length always fails instead of truncating.
        if (s.Length > max || !allowEmpty && (s.Length == 0 || s.Any(c => c is <= ' ' or > '~'))) throw Invalid();
        return s;
    }
    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > 262144) throw Invalid();
        try { var d = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); try { Unique(d.RootElement); return d; } catch { d.Dispose(); throw; } }
        catch (JsonException) { throw Invalid(); }
    }
    private static void Unique(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw Invalid(); Unique(p.Value); } }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Unique(x);
    }
    private static InvalidDataException Invalid() => KubernetesReadOnlyTransport.Unavailable();
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)] private static partial Regex DnsLabel();
    // Kubernetes DNS1123 subdomains cap total length at 253 (Text above), not each segment at 63.
    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*\z", RegexOptions.CultureInvariant)] private static partial Regex DnsSubdomain();
    [GeneratedRegex("^sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)] private static partial Regex Sha256();
    [GeneratedRegex("^(?:(?:[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?)(?::[0-9]{1,5})?/)?[a-z0-9]+(?:[._-][a-z0-9]+)*(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*(?::[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})?(?:@sha256:[0-9a-fA-F]{64})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ImageReference();
}
