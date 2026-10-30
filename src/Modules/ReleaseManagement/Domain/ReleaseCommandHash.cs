using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kordra.ReleaseManagement.Domain;

/// <summary>Deterministic private idempotency identities; these hashes never establish authority or commit.</summary>
public static partial class ReleaseCommandHash
{
    public static string ScopedKey(Guid installationId, Guid tenantId, Guid principalId, string action, string resource, string key)
    {
        if (installationId == Guid.Empty || tenantId == Guid.Empty || principalId == Guid.Empty ||
            !ValidRoute(action, resource) || key is null || key.Length is < 16 or > 128 ||
            !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')) throw new ArgumentException("Invalid command scope.");
        return Hash("release-command-key-v1\n", writer =>
        {
            writer.WriteStartObject(); writer.WriteString("action", action); writer.WriteString("idempotencyKey", key);
            writer.WriteString("installationId", installationId.ToString("D")); writer.WriteString("principalId", principalId.ToString("D"));
            writer.WriteString("resource", resource); writer.WriteNumber("schemaVersion", 1); writer.WriteString("tenantId", tenantId.ToString("D"));
            writer.WriteEndObject();
        });
    }

    /// <summary>Stable private intent identity lets stopped recovery detect audit-only commits without a random ID.</summary>
    public static Guid AuditIntentId(string scopedKeyHash)
    {
        if (scopedKeyHash is not { Length: 64 } || !scopedKeyHash.All(char.IsAsciiHexDigitLower))
            throw new ArgumentException("Invalid release audit scope.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("release-audit-intent-v1\n" + scopedKeyHash));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x80); hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    /// <summary>Nonsecret AUD reason binds the exact private key scope and business identity.</summary>
    public static string AuditReason(Guid installationId, string scopedKeyHash, string businessHash)
    {
        static bool Valid(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
        if (installationId == Guid.Empty || !Valid(scopedKeyHash) || !Valid(businessHash)) throw new ArgumentException("Invalid release audit binding.");
        var bytes = Encoding.UTF8.GetBytes("release-audit-binding-v1\n" + scopedKeyHash + "\n" + businessHash);
        return "release-m-" + installationId.ToString("N") + "-" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Accepts the already validated business body without correlationId. Host enforces wire framing and scope.</summary>
    public static string Body(string action, ReadOnlyMemory<byte> json, string? ifMatch)
    {
        if (json.Length is 0 or > 16 * 1024) throw new ArgumentException("Invalid command body.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Invalid command body.");
            string[] fields = action switch
            {
                "create-draft" or "replace-draft" => ["schemaVersion", "label", "applicationId", "environmentId", "targetId", "artifactId"],
                "next-draft" => ["schemaVersion", "baseRevisionId", "label", "applicationId", "environmentId", "targetId", "artifactId"],
                "freeze" => ["schemaVersion", "expectedSelectionHash"],
                "retire" or "grant" or "reject" or "revoke" => ["schemaVersion", "reasonCode"],
                "request" => ["schemaVersion", "releaseRevisionId"],
                _ => throw new ArgumentException("Invalid command action.")
            };
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!fields.Contains(property.Name, StringComparer.Ordinal) || !values.TryAdd(property.Name, property.Value))
                    throw new ArgumentException("Unknown or duplicate command field.");
            if (values.Count != fields.Length || values["schemaVersion"].ValueKind != JsonValueKind.Number ||
                !values["schemaVersion"].TryGetInt32(out var version) || version != 1 || values["schemaVersion"].GetRawText() != "1")
                throw new ArgumentException("Invalid command schema.");
            foreach (var (name, value) in values)
            {
                if (name == "schemaVersion") continue;
                if (value.ValueKind != JsonValueKind.String) throw new ArgumentException("Invalid command field.");
                var text = value.GetString()!;
                if (name.EndsWith("Id", StringComparison.Ordinal) && (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text))
                    throw new ArgumentException("Invalid command identity.");
                if (name == "expectedSelectionHash" && (text.Length != 64 || !text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')))
                    throw new ArgumentException("Invalid selection hash.");
                if (name == "reasonCode" && !(action switch
                {
                    "retire" => text is "obsolete" or "operator_cancelled",
                    "grant" => text == "approved",
                    "reject" => text is "changes_required" or "incorrect_artifact" or "incorrect_target" or "policy_denied",
                    "revoke" => text is "obsolete" or "requester_cancelled" or "owner_cancelled",
                    _ => false
                })) throw new ArgumentException("Invalid reason.");
            }
            if (values.TryGetValue("label", out var label))
            {
                ReleaseDraft.ValidateLabel(label.GetString()!);
            }
            var requiresMatch = action is "replace-draft" or "freeze" or "retire" or "grant" or "reject" or "revoke";
            if (requiresMatch ? ifMatch is null || !ValidEtag(ifMatch, action is "grant" or "reject" or "revoke") : ifMatch is not null)
                throw new ArgumentException("Invalid version precondition.");
            return Hash("release-command-body-v1\n", writer =>
            {
                writer.WriteStartObject(); writer.WritePropertyName("body"); writer.WriteStartObject();
                foreach (var property in values.OrderBy(x => x.Key, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Key); property.Value.WriteTo(writer); }
                writer.WriteEndObject();
                if (ifMatch is null) writer.WriteNull("ifMatch"); else writer.WriteString("ifMatch", ifMatch);
                writer.WriteEndObject();
            });
        }
        catch (JsonException error) { throw new ArgumentException("Invalid command body.", nameof(json), error); }
    }

    private static string Hash(string domain, Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetBytes(domain));
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) write(writer);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
    private static bool ValidEtag(string value, bool approval)
    {
        if (value.Length > 80) return false;
        if (!value.StartsWith(approval ? "\"apr-v1:" : "\"rel-v1:", StringComparison.Ordinal)) return false;
        var match = StrongEtag().Match(approval ? "\"rel-v1:" + value[8..] : value);
        return match.Success && Guid.TryParseExact(match.Groups[1].Value, "D", out var id) && id != Guid.Empty &&
            long.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var version) && version > 0;
    }
    private static bool ValidRoute(string action, string resource)
    {
        if (resource is null || resource.Length > 256) return false;
        if (action is "request" or "grant" or "reject" or "revoke")
        {
            var parts = resource.Split('/');
            if (parts.Length is not (6 or 8) || parts[0] != "" || parts[1] != "api" || parts[2] != "v1" ||
                parts[3] != "environments" || parts[5] != "release-approvals" ||
                !Guid.TryParseExact(parts[4], "D", out var environment) || environment == Guid.Empty || environment.ToString("D") != parts[4]) return false;
            if (action == "request") return parts.Length == 6;
            return parts.Length == 8 && parts[7] == action && Guid.TryParseExact(parts[6], "D", out var request) &&
                request != Guid.Empty && request.ToString("D") == parts[6];
        }
        var match = Route().Match(resource);
        if (!match.Success || !Guid.TryParseExact(match.Groups[1].Value, "D", out var project) || project == Guid.Empty || project.ToString("D") != match.Groups[1].Value)
            return false;
        var suffix = match.Groups[2].Value;
        if (action == "create-draft") return suffix == "releases";
        var segments = suffix.Split('/');
        if (segments.Length != 3 || !Guid.TryParseExact(segments[1], "D", out var id) || id == Guid.Empty || id.ToString("D") != segments[1]) return false;
        return action switch
        {
            "next-draft" => segments[0] == "releases" && segments[2] == "revisions",
            "replace-draft" => segments[0] == "release-revisions" && segments[2] == "draft",
            "freeze" => segments[0] == "release-revisions" && segments[2] == "freeze",
            "retire" => segments[0] == "release-revisions" && segments[2] == "retire",
            _ => false
        };
    }
    [GeneratedRegex(@"\A/api/v1/projects/([0-9a-f-]{36})/(.+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Route();
    [GeneratedRegex("\\A\\\"rel-v1:([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}):([1-9][0-9]*)\\\"\\z", RegexOptions.CultureInvariant)]
    private static partial Regex StrongEtag();
}
