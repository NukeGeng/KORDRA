using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kordra.Modules.ConnectionManagement.Contracts;

public sealed record RegistryCredentialSelectionV1(Guid InstallationId, Guid TenantId, Guid ProjectId,
    Guid RegistryId, string Repository, string ConsumerId = "artifact.registry.read.v1", string Operation = "artifact.register")
{
    private static readonly Regex RepositoryPattern = new(
        @"\A[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*(/[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*)*\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public bool IsValid => InstallationId != Guid.Empty && TenantId != Guid.Empty && ProjectId != Guid.Empty &&
        RegistryId != Guid.Empty && Repository is { Length: > 0 and <= 255 } && RepositoryPattern.IsMatch(Repository) &&
        ConsumerId == "artifact.registry.read.v1" && Operation == "artifact.register";
}

public sealed record RegistryCredentialBindingSnapshotV1(int SchemaVersion, RegistryCredentialSelectionV1 Selection,
    Guid BindingId, long Revision, Guid ProfileId, Guid ReferenceId, string ProviderKind, string CredentialFormat,
    string State, DateTimeOffset NotBeforeUtc, DateTimeOffset ExpiresAtUtc)
{
    public bool IsValidAt(DateTimeOffset now) => SchemaVersion == 1 && Selection is not null && Selection.IsValid &&
        BindingId != Guid.Empty && Revision > 0 && ProfileId != Guid.Empty && ReferenceId != Guid.Empty &&
        ProviderKind == "macos-keychain-file-v1" && CredentialFormat == "opaque-bytes-v1" && State == "active" &&
        FiniteUtc(now) && FiniteUtc(NotBeforeUtc) && FiniteUtc(ExpiresAtUtc) && NotBeforeUtc <= now && now < ExpiresAtUtc;

    public string Digest()
    {
        using var output = new MemoryStream();
        using (var w = new Utf8JsonWriter(output))
        {
            w.WriteStartObject(); w.WriteNumber("schemaVersion", SchemaVersion);
            w.WriteString("installationId", Selection.InstallationId); w.WriteString("tenantId", Selection.TenantId);
            w.WriteString("projectId", Selection.ProjectId); w.WriteString("registryId", Selection.RegistryId);
            w.WriteString("repository", Selection.Repository); w.WriteString("consumerId", Selection.ConsumerId); w.WriteString("operation", Selection.Operation);
            w.WriteString("bindingId", BindingId); w.WriteNumber("revision", Revision); w.WriteString("profileId", ProfileId);
            w.WriteString("referenceId", ReferenceId); w.WriteString("providerKind", ProviderKind); w.WriteString("credentialFormat", CredentialFormat);
            w.WriteString("state", State); w.WriteString("notBeforeUtc", NotBeforeUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            w.WriteString("expiresAtUtc", ExpiresAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)); w.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(output.ToArray()));
    }

    private static bool FiniteUtc(DateTimeOffset time) => time.Offset == TimeSpan.Zero && time != default && time != DateTimeOffset.MaxValue;
}

public interface IRegistryCredentialBindingReader
{
    Task<RegistryCredentialBindingSnapshotV1?> ReadCurrentUnderAdmissionAsync(RegistryCredentialSelectionV1 selection, CancellationToken ct = default);
}

/// <summary>Server-only invocation/connection context. Never deserialize from a browser or use as a permission.</summary>
public sealed record RegistryCredentialUseContextV1(Guid InvocationId, RegistryCredentialSelectionV1 Selection,
    string ConfigurationDigest)
{
    public bool IsValid => InvocationId != Guid.Empty && Selection is not null && Selection.IsValid &&
        ConfigurationDigest is { Length: 64 } && ConfigurationDigest.All(char.IsAsciiHexDigitLower);
}
