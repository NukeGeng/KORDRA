using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;

/// <summary>Strict, once-loaded private installation configuration. No networking or credentials.</summary>
public sealed class NomadTrustConfiguration
{
    public Guid InstallationId { get; }
    public string FileSha256 { get; }
    public ReadOnlyCollection<NomadTargetTrust> Targets { get; }
    private NomadTrustConfiguration(Guid installation, string hash, NomadTargetTrust[] targets)
    { InstallationId = installation; FileSha256 = hash; Targets = Array.AsReadOnly(targets); }

    public static NomadTrustConfiguration Load(string path, string expectedSha256, Guid installation)
    {
        // This pilot supports Unix private-file custody, not unverified Windows ACLs.
        if (OperatingSystem.IsWindows()) throw Invalid();
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || file.Length is <= 0 or > 65536 || (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0) throw Invalid();
        var forbidden = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((File.GetUnixFileMode(file.FullName) & forbidden) != 0) throw Invalid();
        for (var parent = file.Directory; parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw Invalid();
        // Configuration is loaded only under stopped-API installation custody, never from an HTTP path.
        using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > 65536) throw Invalid();
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw Invalid();
        return Parse(bytes, expectedSha256, installation);
    }

    public static NomadTrustConfiguration Parse(ReadOnlySpan<byte> bytes, string expectedSha256, Guid installation)
    {
        if (installation == Guid.Empty || !IsHash(expectedSha256) || bytes.Length is <= 0 or > 65536 ||
            Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedSha256) throw Invalid();
        try
        {
            using var doc = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement; Shape(root, "schemaVersion", "installationId", "targets");
            if (!root.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1 || Id(root, "installationId") != installation) throw Invalid();
            var list = root.GetProperty("targets");
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 32) throw Invalid();
            var targets = list.EnumerateArray().Select(x => ReadTarget(x, installation)).ToArray();
            if (targets.Select(x => x.TargetId).Distinct().Count() != targets.Length ||
                targets.Select(x => (x.ApiOrigin, x.Region, x.NamespaceName)).Distinct().Count() != targets.Length) throw Invalid();
            return new(installation, expectedSha256, targets);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException or CryptographicException or OverflowException)
        { throw Invalid(); }
    }

    private static NomadTargetTrust ReadTarget(JsonElement e, Guid installation)
    {
        Shape(e, "tenantId", "targetId", "scopeKind", "scopeId", "apiOrigin", "destinationIps", "caCertificateDerBase64",
            "serverSpkiSha256", "region", "namespaceName", "namespaceCreateIndex", "destinationKind");
        var tenant = Id(e, "tenantId"); var target = Id(e, "targetId"); var scope = Id(e, "scopeId");
        var scopeKind = Text(e, "scopeKind"); if (scopeKind is not ("project" or "environment")) throw Invalid();
        var rawOrigin = Text(e, "apiOrigin");
        if (rawOrigin.Any(c => !char.IsAscii(c) || char.IsWhiteSpace(c)) || rawOrigin.IndexOfAny(['%', '\\', '?', '#']) >= 0 ||
            !Uri.TryCreate(rawOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https" || origin.UserInfo.Length != 0 ||
            origin.AbsolutePath != "/" || origin.Host.EndsWith('.') || origin.Port is < 1 or > 65535) throw Invalid();
        var authorityAndPath = rawOrigin[(rawOrigin.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var slash = authorityAndPath.IndexOf('/');
        if (slash >= 0 && authorityAndPath[slash..] != "/") throw Invalid();
        var rawAuthority = slash < 0 ? authorityAndPath : authorityAndPath[..slash];
        if (rawAuthority.Contains('@')) throw Invalid();
        var rawHost = rawAuthority.StartsWith('[') ? rawAuthority[1..rawAuthority.IndexOf(']')] : rawAuthority.Split(':')[0];
        var host = origin.Host.Trim('[', ']');
        if (IPAddress.TryParse(host, out var literal)) host = Address(rawHost).ToString();
        else if (origin.HostNameType != UriHostNameType.Dns || host.Split('.').Any(label => label.Length is < 1 or > 63 ||
            label.StartsWith('-') || label.EndsWith('-') || label.StartsWith("xn--",StringComparison.OrdinalIgnoreCase) || label.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))) throw Invalid();
        host = host.ToLowerInvariant();
        if (host.Contains(':')) host = "[" + host + "]";
        origin = new Uri("https://" + host + (origin.Port == 443 ? "" : ":" + origin.Port.ToString(CultureInfo.InvariantCulture)));
        var ipArray = e.GetProperty("destinationIps");
        if (ipArray.ValueKind != JsonValueKind.Array || ipArray.GetArrayLength() is < 1 or > 8) throw Invalid();
        var addresses = ipArray.EnumerateArray().Select(x => Address(x.GetString() ?? throw Invalid())).ToArray();
        var ips = addresses.Select(x => x.ToString()).Order(StringComparer.Ordinal).ToArray();
        if (ips.Distinct(StringComparer.Ordinal).Count() != ips.Length) throw Invalid();
        var kind = Text(e, "destinationKind");
        if (kind == "personal-loopback")
        {
            if (ips.Length != 1 || ips[0] != "127.0.0.1" || origin.Host != "127.0.0.1" ||
                !rawOrigin.StartsWith("https://127.0.0.1:", StringComparison.Ordinal)) throw Invalid();
        }
        else if (kind != "private-network" || addresses.Any(Forbidden)) throw Invalid();
        if (literal is not null && !ips.Contains(literal.IsIPv4MappedToIPv6 ? literal.MapToIPv4().ToString() : literal.ToString(), StringComparer.Ordinal)) throw Invalid();
        var ca = Convert.FromBase64String(Text(e, "caCertificateDerBase64"));
        if (ca.Length is < 1 or > 16384) throw Invalid();
        using var certificate = X509CertificateLoader.LoadCertificate(ca);
        if (!certificate.RawData.AsSpan().SequenceEqual(ca) || certificate.HasPrivateKey || certificate.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault()?.CertificateAuthority != true ||
            (certificate.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault()?.KeyUsages & X509KeyUsageFlags.KeyCertSign) != X509KeyUsageFlags.KeyCertSign) throw Invalid();
        var pin = Text(e, "serverSpkiSha256"); if (!IsHash(pin)) throw Invalid();
        var region = Label(e, "region"); var ns = Label(e, "namespaceName");
        var index = Text(e, "namespaceCreateIndex");
        if (!ulong.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed == 0 || parsed.ToString(CultureInfo.InvariantCulture) != index) throw Invalid();
        return new(installation, tenant, target, scopeKind, scope, origin, ips, ca, pin, region, ns, index, kind);
    }

    private static IPAddress Address(string raw)
    {
        if (raw.Contains('%') || !IPAddress.TryParse(raw, out var ip)) throw Invalid();
        if (ip.AddressFamily == AddressFamily.InterNetwork && raw != ip.ToString()) throw Invalid();
        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }
    private static bool Forbidden(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast) return true;
        var b = ip.GetAddressBytes();
        return ip.AddressFamily == AddressFamily.InterNetwork && (b[0] == 0 || b[0] >= 224 || b[0] == 169 && b[1] == 254);
    }
    private static Guid Id(JsonElement e, string key)
    { var s = Text(e, key); if (!Guid.TryParseExact(s, "D", out var id) || id == Guid.Empty || s != id.ToString("D")) throw Invalid(); return id; }
    private static string Label(JsonElement e, string key)
    { var s = Text(e, key); if (s.Length is < 1 or > 63 || s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))) throw Invalid(); return s; }
    private static string Text(JsonElement e, string key) => e.GetProperty(key).GetString() ?? throw Invalid();
    private static bool IsHash(string? s) => s is { Length: 64 } && s.All(char.IsAsciiHexDigitLower);
    private static void Shape(JsonElement e, params string[] keys)
    {
        if (e.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in e.EnumerateObject())
            if (!seen.Add(property.Name) || !keys.Contains(property.Name, StringComparer.Ordinal) || property.Value.ValueKind == JsonValueKind.Null) throw Invalid();
        if (seen.Count != keys.Length) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("Invalid private Nomad trust configuration.");
    public override string ToString() => "NomadTrustConfiguration [private]";
}
