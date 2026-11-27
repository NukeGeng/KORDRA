using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kordra.Modules.IdentityAccess.Infrastructure;

namespace Kordra.Api;

/// <summary>
/// 12.3d (D6): one offline service manifest file (bootstrap, rotate, retire or revoke one service). Strict JSON: unknown,
/// duplicate or kind-inappropriate fields make the file invalid. The owner approves the hash of its canonical bytes.
/// </summary>
public sealed partial record ServiceManifestDocument(Guid ManifestId, Guid InstallationId, string ExpectedDatabase, string ExpectedRole,
    string Kind, Guid ServiceId, string? ServiceKind, string? LoginName, string? SpkiSha256, DateTimeOffset? PinNotBeforeUtc,
    DateTimeOffset? PinNotAfterUtc, IReadOnlyList<WorkflowServiceBinding> Bindings)
{
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("kordra-service-manifest-v1\n");
    private static readonly string[] Kinds = ["bootstrap", "rotate", "retire", "revoke"];
    private static readonly string[] TransitionKinds = ["admit", "checkpoint", "timer_due", "cancel"];

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex Identifier();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex TemplateId();

    public static ServiceManifestDocument? Parse(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 6 });
            var root = Fields(document.RootElement, ["schemaVersion", "manifestId", "installationId", "expectedDatabase", "expectedRole",
                "kind", "serviceId"], ["serviceKind", "loginName", "pin", "bindings"]);
            if (root is null || !root["schemaVersion"].TryGetInt32(out var schema) || schema != 1) return null;
            var kind = Text(root["kind"]);
            if (kind is null || !Kinds.Contains(kind, StringComparer.Ordinal)) return null;
            var manifestId = Id(root["manifestId"]); var installation = Id(root["installationId"]); var service = Id(root["serviceId"]);
            var database = Text(root["expectedDatabase"]); var role = Text(root["expectedRole"]);
            if (manifestId is null || installation is null || service is null || database is null || !Identifier().IsMatch(database) ||
                role is null || !Identifier().IsMatch(role)) return null;
            var bootstrap = kind == "bootstrap";
            // bootstrap: kind, login, pin and bindings; rotate: pin only; retire/revoke: nothing else.
            if (bootstrap != root.ContainsKey("serviceKind") || bootstrap != root.ContainsKey("loginName") ||
                bootstrap != root.ContainsKey("bindings") || (kind is "bootstrap" or "rotate") != root.ContainsKey("pin")) return null;
            string? serviceKind = null, login = null, spki = null;
            DateTimeOffset? notBefore = null, notAfter = null;
            var bindings = new List<WorkflowServiceBinding>();
            if (bootstrap)
            {
                serviceKind = Text(root["serviceKind"]); login = Text(root["loginName"]);
                if (serviceKind is not ("api" or "worker") || login is null || !Identifier().IsMatch(login)) return null;
                if (root["bindings"].ValueKind != JsonValueKind.Array || root["bindings"].GetArrayLength() > 64) return null;
                foreach (var item in root["bindings"].EnumerateArray())
                {
                    var b = Fields(item, ["tenantId", "environmentId", "templateId", "templateVersion", "transitionKind"], []);
                    if (b is null) return null;
                    var tenant = Id(b["tenantId"]); var environment = Id(b["environmentId"]); var template = Text(b["templateId"]);
                    var transition = Text(b["transitionKind"]);
                    if (tenant is null || environment is null || template is null || !TemplateId().IsMatch(template) ||
                        !b["templateVersion"].TryGetInt32(out var version) || version < 1 || transition is null ||
                        !TransitionKinds.Contains(transition, StringComparer.Ordinal)) return null;
                    bindings.Add(new(tenant.Value, environment.Value, template, version, transition));
                }
                if (bindings.Distinct().Count() != bindings.Count) return null;
            }
            if (root.TryGetValue("pin", out var pinElement))
            {
                var pin = Fields(pinElement, ["spkiSha256", "notBeforeUtc", "notAfterUtc"], []);
                if (pin is null) return null;
                spki = Text(pin["spkiSha256"]); notBefore = Time(pin["notBeforeUtc"]); notAfter = Time(pin["notAfterUtc"]);
                if (!IsPin(spki) || notBefore is null || notAfter is null || notAfter <= notBefore) return null;
            }
            return new(manifestId.Value, installation.Value, database, role, kind, service.Value, serviceKind, login, spki, notBefore, notAfter,
                bindings.OrderBy(x => x.TenantId).ThenBy(x => x.EnvironmentId).ThenBy(x => x.TemplateId, StringComparer.Ordinal)
                    .ThenBy(x => x.TemplateVersion).ThenBy(x => x.TransitionKind, StringComparer.Ordinal).ToArray());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    public static bool IsPin(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    public byte[] CanonicalBytes()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("manifestId", ManifestId.ToString("D"));
            writer.WriteString("installationId", InstallationId.ToString("D"));
            writer.WriteString("expectedDatabase", ExpectedDatabase);
            writer.WriteString("expectedRole", ExpectedRole);
            writer.WriteString("kind", Kind);
            writer.WriteString("serviceId", ServiceId.ToString("D"));
            if (ServiceKind is not null) writer.WriteString("serviceKind", ServiceKind);
            if (LoginName is not null) writer.WriteString("loginName", LoginName);
            if (SpkiSha256 is not null)
            {
                writer.WriteStartObject("pin");
                writer.WriteString("spkiSha256", SpkiSha256);
                writer.WriteString("notBeforeUtc", PinNotBeforeUtc!.Value.ToString(TimeFormat, CultureInfo.InvariantCulture));
                writer.WriteString("notAfterUtc", PinNotAfterUtc!.Value.ToString(TimeFormat, CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            if (Kind == "bootstrap")
            {
                writer.WriteStartArray("bindings");
                foreach (var b in Bindings)
                {
                    writer.WriteStartObject();
                    writer.WriteString("tenantId", b.TenantId.ToString("D")); writer.WriteString("environmentId", b.EnvironmentId.ToString("D"));
                    writer.WriteString("templateId", b.TemplateId); writer.WriteNumber("templateVersion", b.TemplateVersion);
                    writer.WriteString("transitionKind", b.TransitionKind);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>Owner-approved hash: SHA-256 of a domain prefix and the canonical bytes; also the IAM manifest receipt key.</summary>
    public string Hash() => Convert.ToHexStringLower(SHA256.HashData([.. Domain, .. CanonicalBytes()]));

    /// <summary>The IAM function-port manifest; the manifest id is the AUD intent of the apply.</summary>
    public WorkflowServiceManifest ToPortManifest() => new(Hash(), Kind, InstallationId, ServiceId, ServiceKind, LoginName, SpkiSha256,
        PinNotBeforeUtc, PinNotAfterUtc, Bindings, ManifestId);

    private static Dictionary<string, JsonElement>? Fields(JsonElement element, string[] required, string[] optional)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!(required.Contains(property.Name, StringComparer.Ordinal) || optional.Contains(property.Name, StringComparer.Ordinal)) ||
                !result.TryAdd(property.Name, property.Value)) return null;
        return required.All(result.ContainsKey) ? result : null;
    }

    private static string? Text(JsonElement element) =>
        element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 and <= 128 } value && !value.Any(char.IsControl) ? value : null;

    private static Guid? Id(JsonElement element) =>
        Text(element) is { } text && Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty && text == id.ToString("D") ? id : null;

    private static DateTimeOffset? Time(JsonElement element) =>
        Text(element) is { } text && DateTimeOffset.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) ? time : null;
}
