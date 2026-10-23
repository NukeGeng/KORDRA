using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.ProjectEnvironment.Contracts;

namespace Kordra.Api;

public sealed record ResourcePolicyDatabaseIdentity(string DatabaseName, string ProvisioningRole);
public sealed record ResourcePolicyManifest(int SchemaVersion, Guid ManifestId, Guid InstallationId,
    ResourcePolicyDatabaseIdentity DatabaseIdentity, Guid TenantId, Guid ProjectId, string Operation, Guid? PolicyId,
    long ExpectedCurrentRevision, ResourcePolicyPayload? Payload, IReadOnlyList<PolicyBindingChange> Bindings)
{
    public static ResourcePolicyManifest? Parse(string json)
    {
        try
        {
            if (json is null || Encoding.UTF8.GetByteCount(json) > 128 * 1024) return null;
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 }); var e = doc.RootElement;
            if (!ResourcePolicies.Exact(e, "schemaVersion", "manifestId", "installationId", "databaseIdentity", "tenantId", "projectId", "operation", "policyId", "expectedCurrentRevision", "payload", "bindings")) return null;
            var database = e.GetProperty("databaseIdentity");
            if (!ResourcePolicies.Exact(database, "databaseName", "provisioningRole")) return null;
            var bindings = e.GetProperty("bindings"); if (bindings.ValueKind != JsonValueKind.Array || bindings.GetArrayLength() > 50) return null;
            var items = new List<PolicyBindingChange>();
            foreach (var b in bindings.EnumerateArray())
            {
                if (!ResourcePolicies.Exact(b, "bindingId", "scopeKind", "environmentId", "expectedBindingRevision")) return null;
                items.Add(new(OptionalGuid(b.GetProperty("bindingId")), b.GetProperty("scopeKind").GetString()!, OptionalGuid(b.GetProperty("environmentId")), b.GetProperty("expectedBindingRevision").GetInt64()));
            }
            var raw = e.GetProperty("payload");
            var payload = raw.ValueKind == JsonValueKind.Null ? null : ResourcePolicies.Parse(raw.GetRawText());
            if (raw.ValueKind != JsonValueKind.Null && payload is null) return null;
            var m = new ResourcePolicyManifest(e.GetProperty("schemaVersion").GetInt32(), e.GetProperty("manifestId").GetGuid(), e.GetProperty("installationId").GetGuid(),
                new(database.GetProperty("databaseName").GetString()!, database.GetProperty("provisioningRole").GetString()!), e.GetProperty("tenantId").GetGuid(), e.GetProperty("projectId").GetGuid(),
                e.GetProperty("operation").GetString()!, OptionalGuid(e.GetProperty("policyId")), e.GetProperty("expectedCurrentRevision").GetInt64(), payload, items.AsReadOnly());
            return m.IsValid() ? m : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException or OverflowException) { return null; }
    }
    private static Guid? OptionalGuid(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetGuid();
    public bool IsValid() => SchemaVersion == 1 && ManifestId != Guid.Empty && InstallationId != Guid.Empty && TenantId != Guid.Empty && ProjectId != Guid.Empty &&
        DatabaseIdentity is not null && (DatabaseIdentity.DatabaseName == "kordra" && DatabaseIdentity.ProvisioningRole == "kordra_admin" ||
            DatabaseIdentity.DatabaseName == "kordra_test" && DatabaseIdentity.ProvisioningRole == "kordra_test") &&
        Operation is "create" or "publish" or "bind" && ExpectedCurrentRevision >= 0 && ExpectedCurrentRevision < long.MaxValue &&
        (Operation == "create" ? PolicyId is null && ExpectedCurrentRevision == 0 : PolicyId is { } id && id != Guid.Empty && ExpectedCurrentRevision > 0) &&
        (Operation == "bind" ? Payload is null : ResourcePolicies.IsValid(Payload)) &&
        Bindings is { Count: <= 50 } && (Operation != "bind" || Bindings.Count > 0) &&
        Bindings.All(b => b is not null && (b.BindingId is null ? b.ExpectedBindingRevision == 0 : b.BindingId != Guid.Empty && b.ExpectedBindingRevision > 0) &&
            b.ExpectedBindingRevision < long.MaxValue && (b.EnvironmentId is null ? b.ScopeKind == "project_without_environment" : b.EnvironmentId != Guid.Empty && b.ScopeKind == "environment")) &&
        Bindings.Select(b => (b.ScopeKind, b.EnvironmentId)).Distinct().Count() == Bindings.Count &&
        Bindings.Where(b => b.BindingId is not null).Select(b => b.BindingId).Distinct().Count() == Bindings.Count(b => b.BindingId is not null);

    public byte[] CanonicalBytes()
    {
        if (!IsValid()) throw new InvalidDataException("Invalid resource-policy manifest.");
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject(); w.WriteNumber("schemaVersion", SchemaVersion); w.WriteString("manifestId", ManifestId); w.WriteString("installationId", InstallationId);
            w.WriteStartObject("databaseIdentity"); w.WriteString("databaseName", DatabaseIdentity.DatabaseName); w.WriteString("provisioningRole", DatabaseIdentity.ProvisioningRole); w.WriteEndObject();
            w.WriteString("tenantId", TenantId); w.WriteString("projectId", ProjectId); w.WriteString("operation", Operation);
            if (PolicyId is { } id) w.WriteString("policyId", id); else w.WriteNull("policyId");
            w.WriteNumber("expectedCurrentRevision", ExpectedCurrentRevision);
            w.WritePropertyName("payload"); if (Payload is null) w.WriteNullValue(); else ResourcePolicies.Write(w, Payload);
            w.WriteStartArray("bindings");
            foreach (var b in Bindings.OrderBy(x => x.ScopeKind, StringComparer.Ordinal).ThenBy(x => x.EnvironmentId?.ToString("D"), StringComparer.Ordinal))
            {
                w.WriteStartObject(); if (b.BindingId is { } binding) w.WriteString("bindingId", binding); else w.WriteNull("bindingId");
                w.WriteString("scopeKind", b.ScopeKind); if (b.EnvironmentId is { } environment) w.WriteString("environmentId", environment); else w.WriteNull("environmentId");
                w.WriteNumber("expectedBindingRevision", b.ExpectedBindingRevision); w.WriteEndObject();
            }
            w.WriteEndArray(); w.WriteEndObject();
        }
        return stream.ToArray();
    }
    public string Hash() => Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes()));
}
