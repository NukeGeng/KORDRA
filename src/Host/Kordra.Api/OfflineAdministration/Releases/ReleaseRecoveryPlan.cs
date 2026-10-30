using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kordra.ReleaseManagement.Domain;

namespace Kordra.Api;

/// <summary>Private stopped-custody selection. Its hash binds evidence, never human authorization.</summary>
public sealed record ReleaseRecoveryPlan(int SchemaVersion, Guid PlanId, Guid InstallationId, Guid BootId, int OldProcessId,
    Guid TenantId, Guid PrincipalId, string Action, string Resource, string ScopedKeyHash, string BusinessHash,
    string? ReceiptHash, string? AuditHash)
{
    public byte[] CanonicalBytes()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject(); w.WriteString("action", Action);
            if (AuditHash is null) w.WriteNull("auditHash"); else w.WriteString("auditHash", AuditHash);
            w.WriteString("bootId", BootId.ToString("D")); w.WriteString("businessHash", BusinessHash);
            w.WriteString("installationId", InstallationId.ToString("D")); w.WriteNumber("oldProcessId", OldProcessId);
            w.WriteString("planId", PlanId.ToString("D")); w.WriteString("principalId", PrincipalId.ToString("D"));
            if (ReceiptHash is null) w.WriteNull("receiptHash"); else w.WriteString("receiptHash", ReceiptHash);
            w.WriteString("resource", Resource); w.WriteNumber("schemaVersion", SchemaVersion);
            w.WriteString("scopedKeyHash", ScopedKeyHash); w.WriteString("tenantId", TenantId.ToString("D")); w.WriteEndObject();
        }
        return stream.ToArray();
    }
    public string Hash()
    {
        var prefix = Encoding.UTF8.GetBytes("release-recovery-plan-v1\n"); var bytes = CanonicalBytes();
        var input = new byte[prefix.Length + bytes.Length]; prefix.CopyTo(input, 0); bytes.CopyTo(input, prefix.Length);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
    public static ReleaseRecoveryPlan? Parse(ReadOnlyMemory<byte> json)
    {
        if (json.Length is 0 or > 16384) return null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 3 });
            var value = document.RootElement;
            string[] names = ["schemaVersion", "planId", "installationId", "bootId", "oldProcessId", "tenantId", "principalId", "action", "resource", "scopedKeyHash", "businessHash", "receiptHash", "auditHash"];
            if (value.ValueKind != JsonValueKind.Object) return null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in value.EnumerateObject()) if (!names.Contains(field.Name, StringComparer.Ordinal) || !seen.Add(field.Name)) return null;
            if (seen.Count != names.Length || value.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number ||
                value.GetProperty("schemaVersion").GetRawText() != "1" || !value.GetProperty("oldProcessId").TryGetInt32(out var pid) || pid <= 0 ||
                value.GetProperty("oldProcessId").GetRawText() != pid.ToString(System.Globalization.CultureInfo.InvariantCulture)) return null;
            Guid Id(string name) => ReleaseRequestParser.GuidValue(value.GetProperty(name).GetString(), out var id) ? id : throw new FormatException();
            string Text(string name) => value.GetProperty(name).ValueKind == JsonValueKind.String ? value.GetProperty(name).GetString()! : throw new FormatException();
            string? OptionalHash(string name) => value.GetProperty(name).ValueKind == JsonValueKind.Null ? null : ValidHash(Text(name)) ? Text(name) : throw new FormatException();
            var planId = Id("planId"); var installation = Id("installationId"); var boot = Id("bootId"); var tenant = Id("tenantId"); var principal = Id("principalId");
            var action = Text("action");
            if (action is not ("create-draft" or "next-draft" or "replace-draft" or "freeze" or "retire")) return null;
            var resource = Text("resource"); var key = Text("scopedKeyHash"); var business = Text("businessHash");
            var receipt = OptionalHash("receiptHash"); var audit = OptionalHash("auditHash");
            if (!ValidHash(key) || !ValidHash(business) || (receipt is null) != (audit is null)) return null;
            // Reuse the exact route/action validator; synthetic key is never returned or persisted.
            _ = ReleaseCommandHash.ScopedKey(installation, tenant, principal, action, resource, "recovery-validation-key-1234");
            return new(1, planId, installation, boot, pid, tenant, principal, action, resource, key, business, receipt, audit);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or FormatException or InvalidOperationException or OverflowException)
        { return null; }
    }
    public static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
}
