using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kordra.Modules.ArtifactManagement.Infrastructure.Registry;

/// <summary>Immutable stopped-installation connection metadata; never authority or a secret.</summary>
public sealed class RegistryTrust
{
    public Guid InstallationId { get; }
    public Guid TenantId { get; }
    public Guid ProjectId { get; }
    public Guid RegistryId { get; }
    public string DisplayName { get; }
    public string Repository { get; }
    public Uri ApiOrigin { get; }
    public ReadOnlyCollection<string> DestinationIps { get; }
    public string ServerSpkiSha256 { get; }
    public string AuthMode { get; }
    public string ConfigurationDigest { get; }
    private readonly byte[] _ca;
    public byte[] ExportCaCertificateDer() => (byte[])_ca.Clone();

    internal RegistryTrust(Guid installation, Guid tenant, Guid project, Guid registry, string display,
        string repository, Uri origin, string[] ips, byte[] ca, string pin, string auth)
    {
        InstallationId = installation; TenantId = tenant; ProjectId = project; RegistryId = registry;
        DisplayName = display; Repository = repository; ApiOrigin = origin;
        DestinationIps = Array.AsReadOnly((string[])ips.Clone()); _ca = (byte[])ca.Clone();
        ServerSpkiSha256 = pin; AuthMode = auth;
        ConfigurationDigest = Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes()));
    }

    public byte[] CanonicalBytes()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject(); w.WriteNumber("schemaVersion", 1);
            w.WriteString("installationId", InstallationId); w.WriteString("tenantId", TenantId);
            w.WriteString("projectId", ProjectId); w.WriteString("registryId", RegistryId);
            w.WriteString("displayName", DisplayName); w.WriteString("repository", Repository);
            w.WriteString("apiOrigin", ApiOrigin.GetLeftPart(UriPartial.Authority));
            w.WriteStartArray("destinationIps");
            foreach (var ip in DestinationIps) w.WriteStringValue(ip);
            w.WriteEndArray();
            w.WriteString("caCertificateDerSha256", Convert.ToHexStringLower(SHA256.HashData(_ca)));
            w.WriteString("serverSpkiSha256", ServerSpkiSha256); w.WriteString("authMode", AuthMode);
            w.WriteEndObject();
        }
        return stream.ToArray();
    }
    public override string ToString() => "RegistryTrust [private]";
}
