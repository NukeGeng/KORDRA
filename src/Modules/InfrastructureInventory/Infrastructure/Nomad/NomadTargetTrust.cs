using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;

/// <summary>Installation-owned immutable trust, never a browser-supplied transport plan.</summary>
public sealed class NomadTargetTrust
{
    public Guid InstallationId { get; }
    public Guid TenantId { get; }
    public Guid TargetId { get; }
    public string ScopeKind { get; }
    public Guid ScopeId { get; }
    public Uri ApiOrigin { get; }
    public ReadOnlyCollection<string> DestinationIps { get; }
    private readonly byte[] _ca;
    public byte[] ExportCaCertificateDer() => (byte[])_ca.Clone();
    public string ServerSpkiSha256 { get; }
    public string Region { get; }
    public string NamespaceName { get; }
    public string NamespaceCreateIndex { get; }
    public string DestinationKind { get; }
    public string ConfigurationDigest { get; }

    internal NomadTargetTrust(Guid installation, Guid tenant, Guid target, string scopeKind, Guid scope,
        Uri origin, string[] ips, byte[] ca, string pin, string region, string ns, string index, string destinationKind)
    {
        InstallationId = installation; TenantId = tenant; TargetId = target; ScopeKind = scopeKind; ScopeId = scope;
        ApiOrigin = origin; DestinationIps = Array.AsReadOnly((string[])ips.Clone()); _ca = (byte[])ca.Clone();
        ServerSpkiSha256 = pin; Region = region; NamespaceName = ns; NamespaceCreateIndex = index; DestinationKind = destinationKind;
        ConfigurationDigest = Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes()));
    }

    public byte[] CanonicalBytes()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject(); w.WriteNumber("schemaVersion", 1); w.WriteString("provider", "nomad-v1");
            w.WriteString("installationId", InstallationId); w.WriteString("tenantId", TenantId); w.WriteString("targetId", TargetId);
            w.WriteString("scopeKind", ScopeKind); w.WriteString("scopeId", ScopeId);
            w.WriteString("apiOrigin", ApiOrigin.GetLeftPart(UriPartial.Authority));
            w.WriteStartArray("destinationIps"); foreach (var ip in DestinationIps) w.WriteStringValue(ip); w.WriteEndArray();
            w.WriteString("caCertificateDerSha256", Convert.ToHexStringLower(SHA256.HashData(_ca)));
            w.WriteString("serverSpkiSha256", ServerSpkiSha256); w.WriteString("region", Region); w.WriteString("namespaceName", NamespaceName);
            w.WriteString("namespaceCreateIndex", NamespaceCreateIndex); w.WriteString("destinationKind", DestinationKind); w.WriteEndObject();
        }
        return stream.ToArray();
    }
    public override string ToString() => "NomadTargetTrust [private]";
}
