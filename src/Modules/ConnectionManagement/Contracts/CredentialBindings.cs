using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kordra.Modules.ConnectionManagement.Contracts;

public sealed record CredentialSelectionV1(Guid InstallationId, Guid TenantId, Guid TargetId,
    string ConsumerId = "inventory.readonly.v1", string Operation = "target.read")
{
    public bool IsValid => InstallationId != Guid.Empty && TenantId != Guid.Empty && TargetId != Guid.Empty &&
        ConsumerId == "inventory.readonly.v1" && Operation == "target.read";
}
public sealed record CredentialBindingSnapshotV1(int SchemaVersion, CredentialSelectionV1 Selection,
    Guid BindingId, long Revision, Guid ProfileId, Guid ReferenceId, string ProviderKind, string CredentialFormat,
    string State, DateTimeOffset NotBeforeUtc, DateTimeOffset ExpiresAtUtc)
{
    public bool IsValidAt(DateTimeOffset now) => SchemaVersion == 1 && Selection.IsValid && BindingId != Guid.Empty &&
        Revision > 0 && ProfileId != Guid.Empty && ReferenceId != Guid.Empty && ProviderKind == "macos-keychain-file-v1" &&
        CredentialFormat == "opaque-bytes-v1" && State == "active" && NotBeforeUtc <= now && now < ExpiresAtUtc &&
        NotBeforeUtc.Offset == TimeSpan.Zero && ExpiresAtUtc.Offset == TimeSpan.Zero;
    public string Digest()
    {
        using var output = new MemoryStream();
        using (var w = new Utf8JsonWriter(output))
        {
            w.WriteStartObject(); w.WriteNumber("schemaVersion", SchemaVersion);
            w.WriteString("installationId", Selection.InstallationId.ToString("D")); w.WriteString("tenantId", Selection.TenantId.ToString("D"));
            w.WriteString("targetId", Selection.TargetId.ToString("D")); w.WriteString("consumerId", Selection.ConsumerId); w.WriteString("operation", Selection.Operation);
            w.WriteString("bindingId", BindingId.ToString("D")); w.WriteNumber("revision", Revision); w.WriteString("profileId", ProfileId.ToString("D"));
            w.WriteString("referenceId", ReferenceId.ToString("D")); w.WriteString("providerKind", ProviderKind); w.WriteString("credentialFormat", CredentialFormat);
            w.WriteString("state", State); w.WriteString("notBeforeUtc", NotBeforeUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("expiresAtUtc", ExpiresAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)); w.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(output.ToArray()));
    }
}
/// <summary>Metadata only. The caller holds the installation's common admission fence.</summary>
public interface ICredentialBindingReader
{
    Task<CredentialBindingSnapshotV1?> ReadCurrentUnderAdmissionAsync(CredentialSelectionV1 selection, CancellationToken ct = default);
}


/// <summary>Exact server-admitted invocation/scope supplied only to the registered consumer.</summary>
public sealed record CredentialUseContextV1(Guid InvocationId, CredentialSelectionV1 Selection, string ScopeKind, Guid ScopeId)
{
    public bool IsValid => InvocationId != Guid.Empty && Selection is not null && Selection.IsValid && ScopeKind is "project" or "environment" && ScopeId != Guid.Empty;
}
