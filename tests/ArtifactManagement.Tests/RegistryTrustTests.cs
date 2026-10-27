using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kordra.Modules.ArtifactManagement.Infrastructure.Registry;
using Xunit;

namespace Kordra.Artifacts.Tests;

public sealed class RegistryTrustTests
{
    private static readonly Guid Installation = Guid.Parse("45daefae-a44d-4cf0-9123-d43c644ecb91");
    private static readonly Guid Tenant = Guid.Parse("27bf8a5a-dbe5-441d-a1c4-029118c03bb2");
    private static readonly Guid Project = Guid.Parse("24cb54fc-5acf-4e16-b945-8cd6c27a0269");
    private static readonly Guid Registry = Guid.Parse("d513a7f1-3897-452c-abdd-429316147708");
    private static readonly byte[] Ca = CreateCa();

    private static byte[] CreateCa()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=T10 Synthetic Registry CA", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        return certificate.Export(X509ContentType.Cert);
    }
    private static JsonObject Entry() => new()
    {
        ["tenantId"] = Tenant,
        ["projectId"] = Project,
        ["registryId"] = Registry,
        ["displayName"] = "Synthetic Registry",
        ["repository"] = "team/frontend",
        ["apiOrigin"] = "https://registry.example.test:443/",
        ["destinationIps"] = new JsonArray("10.42.0.9", "10.42.0.2"),
        ["caCertificateDerBase64"] = Convert.ToBase64String(Ca),
        ["serverSpkiSha256"] = new string('a', 64),
        ["authMode"] = "basic"
    };
    private static JsonObject Root(JsonObject? entry = null) => new()
    { ["schemaVersion"] = 1, ["installationId"] = Installation, ["registries"] = new JsonArray(entry ?? Entry()) };
    private static byte[] Bytes(JsonObject root) => Encoding.UTF8.GetBytes(root.ToJsonString());
    private static RegistryTrustConfiguration Parse(byte[] bytes) =>
        RegistryTrustConfiguration.Parse(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), Installation);

    [Fact]
    public void Canonical_digest_pins_exact_order_sorted_addresses_and_defensive_certificate_copy()
    {
        var input = Bytes(Root()); var config = Parse(input); var trust = Assert.Single(config.Registries);
        var expected = "{\"schemaVersion\":1,\"installationId\":\"" + Installation + "\",\"tenantId\":\"" + Tenant +
            "\",\"projectId\":\"" + Project + "\",\"registryId\":\"" + Registry +
            "\",\"displayName\":\"Synthetic Registry\",\"repository\":\"team/frontend\",\"apiOrigin\":\"https://registry.example.test\"," +
            "\"destinationIps\":[\"10.42.0.2\",\"10.42.0.9\"],\"caCertificateDerSha256\":\"" +
            Convert.ToHexStringLower(SHA256.HashData(Ca)) + "\",\"serverSpkiSha256\":\"" + new string('a', 64) + "\",\"authMode\":\"basic\"}";
        Assert.Equal(expected, Encoding.UTF8.GetString(trust.CanonicalBytes()));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected))), trust.ConfigurationDigest);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(input)), config.FileSha256);
        var exported = trust.ExportCaCertificateDer(); exported[0] ^= 1;
        Assert.Equal(Ca, trust.ExportCaCertificateDer());
        Assert.DoesNotContain("registry.example.test", trust.ToString());
    }

    [Theory]
    [InlineData("http://registry.example.test")]
    [InlineData("HTTPS://registry.example.test")]
    [InlineData("https://Registry.example.test")]
    [InlineData("https://registry.example.test.")]
    [InlineData("https://user@registry.example.test")]
    [InlineData("https://registry.example.test/v2/")]
    [InlineData("https://registry.example.test/../")]
    [InlineData("https://registry.example.test?query")]
    [InlineData("https://registry.example.test#fragment")]
    [InlineData("https://registry.example.test/%2f")]
    [InlineData("https://xn--registry-9za.example.test")]
    [InlineData("https://[fe80::1%25en0]")]
    public void Noncanonical_or_unsafe_origin_is_rejected_without_echo(string origin)
    {
        var entry = Entry(); entry["apiOrigin"] = origin;
        var error = Assert.Throws<InvalidDataException>(() => Parse(Bytes(Root(entry))));
        Assert.DoesNotContain(origin, error.Message);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("100.100.100.200")]
    [InlineData("169.254.169.254")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("224.0.0.1")]
    [InlineData("10.42.0.01")]
    [InlineData("10.42.0.2%zone")]
    public void Forbidden_or_ambiguous_destination_cannot_be_configured(string address)
    {
        var entry = Entry(); entry["destinationIps"] = new JsonArray(address);
        Assert.Throws<InvalidDataException>(() => Parse(Bytes(Root(entry))));
    }

    [Theory]
    [InlineData("team/frontend:latest")]
    [InlineData("Team/frontend")]
    [InlineData("../frontend")]
    [InlineData("team//frontend")]
    [InlineData("https://registry.example.test/team")]
    [InlineData("team/frontend\n")]
    [InlineData("team/%66rontend")]
    public void Repository_is_exact_and_never_normalized(string repository)
    {
        var entry = Entry(); entry["repository"] = repository;
        Assert.Throws<InvalidDataException>(() => Parse(Bytes(Root(entry))));
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("basic")]
    [InlineData("static-bearer")]
    public void Only_explicit_authentication_modes_are_supported(string mode)
    {
        var entry = Entry(); entry["authMode"] = mode;
        Assert.Equal(mode, Assert.Single(Parse(Bytes(Root(entry))).Registries).AuthMode);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("missing")]
    [InlineData("identity")]
    [InlineData("auth")]
    [InlineData("pin")]
    [InlineData("ca")]
    [InlineData("ips-empty")]
    [InlineData("ips-duplicate")]
    [InlineData("ips-limit")]
    [InlineData("registry-id")]
    [InlineData("alias")]
    [InlineData("limit")]
    public void Strict_shape_identity_and_bounds_fail_closed(string fault)
    {
        var root = Root(); var entry = root["registries"]![0]!.AsObject();
        switch (fault)
        {
            case "unknown": entry["password"] = "SYNTHETIC"; break;
            case "null": entry["repository"] = null; break;
            case "missing": entry.Remove("authMode"); break;
            case "identity": root["installationId"] = Guid.NewGuid(); break;
            case "auth": entry["authMode"] = "challenge"; break;
            case "pin": entry["serverSpkiSha256"] = new string('A', 64); break;
            case "ca": entry["caCertificateDerBase64"] = "AAAA"; break;
            case "ips-empty": entry["destinationIps"] = new JsonArray(); break;
            case "ips-duplicate": entry["destinationIps"] = new JsonArray("10.42.0.2", "::ffff:10.42.0.2"); break;
            case "ips-limit": entry["destinationIps"] = new JsonArray(Enumerable.Range(1, 9).Select(x => JsonValue.Create("10.42.0." + x)).ToArray<JsonNode?>()); break;
            case "registry-id": root["registries"]!.AsArray().Add(entry.DeepClone()); break;
            case "alias":
                var alias = entry.DeepClone().AsObject(); alias["registryId"] = Guid.NewGuid(); alias["apiOrigin"] = "https://registry.example.test";
                root["registries"]!.AsArray().Add(alias); break;
            case "limit":
                for (var i = 1; i < 33; i++) root["registries"]!.AsArray().Add(Entry()); break;
        }
        var bytes = Bytes(root);
        if (fault == "duplicate") bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("{", "{\"schemaVersion\":1,", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => Parse(bytes));
    }

    [Fact]
    public void Hash_mismatch_and_oversized_configuration_are_not_empty_fallbacks()
    {
        var bytes = Bytes(Root());
        Assert.Throws<InvalidDataException>(() => RegistryTrustConfiguration.Parse(bytes, new string('0', 64), Installation));
        Assert.Throws<InvalidDataException>(() => Parse(new byte[65537]));
        Assert.Throws<InvalidDataException>(() => Parse([]));
        var root = Root(); root["registries"] = new JsonArray();
        Assert.Empty(Parse(Bytes(root)).Registries);
    }

    [Fact]
    public void Private_file_load_checks_permissions_hash_and_symlink_custody()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Throws<InvalidDataException>(() => RegistryTrustConfiguration.Load("unused", new string('a', 64), Installation));
            return;
        }
        var product = new DirectoryInfo(AppContext.BaseDirectory);
        while (product is not null && !File.Exists(Path.Combine(product.FullName, "Kordra.slnx"))) product = product.Parent;
        if (product is null) throw new InvalidOperationException("Product fixture root missing.");
        // macOS /var is itself a symlink. Use the canonical Product fixture root so
        // the production custody check stays strict rather than exempting temp paths.
        var directory = Path.Combine(product.FullName, ".amr-local", "t10", "fixtures", "trust-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(directory, "trust.json"); var link = Path.Combine(directory, "link.json");
        try
        {
            var bytes = Bytes(Root()); File.WriteAllBytes(path, bytes);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.Single(RegistryTrustConfiguration.Load(path, hash, Installation).Registries);
            File.CreateSymbolicLink(link, path);
            Assert.Throws<InvalidDataException>(() => RegistryTrustConfiguration.Load(link, hash, Installation));
            Assert.Throws<InvalidDataException>(() => RegistryTrustConfiguration.Load(path, new string('0', 64), Installation));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            Assert.Throws<InvalidDataException>(() => RegistryTrustConfiguration.Load(path, hash, Installation));
        }
        finally
        {
            File.Delete(link); File.Delete(path); Directory.Delete(directory);
        }
        Assert.False(Directory.Exists(directory));
    }
}
