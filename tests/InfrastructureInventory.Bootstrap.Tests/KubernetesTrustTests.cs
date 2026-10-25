using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Kubernetes;
using Xunit;

namespace Kordra.Modules.InfrastructureInventory.Bootstrap.Tests;

public sealed class KubernetesTrustTests
{
    // Public synthetic CA only. Its throwaway signing key is not retained.
    private const string Ca = "MIIDQTCCAimgAwIBAgIUeT2xJn2iPdXbVjLQNGJunlDyLUwwDQYJKoZIhvcNAQELBQAwKDEmMCQGA1UEAwwdU3ludGhldGljIE5vbWFkIFRydXN0IFRlc3QgQ0EwHhcNMjYxMDI0MDk0NzI2WhcNMjcxMDI0MDk0NzI2WjAoMSYwJAYDVQQDDB1TeW50aGV0aWMgTm9tYWQgVHJ1c3QgVGVzdCBDQTCCASIwDQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBALiCujUm+Kw4WYJiN6oqoR2ifVSGdF+FTIaJQTmSeRacO0c5BogfvbyQ3ibVnf8AGLQMQnxOxzBueWvBou8S4E0qFPsQMACbmqjBYQpXCk6DC67LOI0bfdSS1kzIk8hhmQ0slS3iwSGklmY5tHI0cO3lwMSV6HhZmngn2PFJZmJUz84HhVtouksmY7uou/nFUJ3JwNRaw1wamIbAbXjrcwdBR2yL/8AoOgkeunBsw1KWddkqMKWFupH3lnY8u0Vrfc79Vd3TL5WHphnSoENOt2VoViOjpESxNEBKAAXZKL1l7avn7ip5iXT9gzkWT9n8X+If9wt0q891xegzGNrH0l0CAwEAAaNjMGEwHQYDVR0OBBYEFJoHUUHwbI3Mnd1ZjavstV3PbdtQMB8GA1UdIwQYMBaAFJoHUUHwbI3Mnd1ZjavstV3PbdtQMA8GA1UdEwEB/wQFMAMBAf8wDgYDVR0PAQH/BAQDAgEGMA0GCSqGSIb3DQEBCwUAA4IBAQCtIu97RTsXnNbBF307PM2JEMMQnqjYwU7gbDhnPrqkPT/riwXzElnUSPI/Jcl1m0of4EWU2trXTMnvCRgLir2148HUD+e5C7QVucaHNvZhUbIpU3klR8457F4uzvBlPY9vUilwn5H2is6r+V0rqhnuF/kCteFqbKQRzftRZvNhkUR3olxyGYqQff+nDrUaqV51EkBZNTxNt7PC+JB+3sgZiClBUrdFmNJRSFPk0+biuQiITDxBTXok6dPvSa856HG2a6a0RzAmwbTzltxG/8JRhuLdWxxk00WIETpBVYgfKh5hqgj5tCOZ6TRxm8/gugi77ebnxt8QDVZHdkvv8L6c";
    private static readonly Guid Installation = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private static JsonObject Config() => new()
    {
        ["schemaVersion"] = 1, ["installationId"] = Installation.ToString(),
        ["targets"] = new JsonArray(new JsonObject
        {
            ["tenantId"] = "00000002-0000-0000-0000-000000000000", ["targetId"] = "00000003-0000-0000-0000-000000000000",
            ["scopeKind"] = "project", ["scopeId"] = "00000004-0000-0000-0000-000000000000",
            ["apiOrigin"] = "https://KUBERNETES.example.test:443/", ["destinationIps"] = new JsonArray("10.0.0.3", "10.0.0.2"),
            ["caCertificateDerBase64"] = Ca, ["serverSpkiSha256"] = new string('a', 64), ["namespaceName"] = "default", ["namespaceUid"] = "synthetic-namespace-uid"
        })
    };
    private static KubernetesTrustConfiguration Parse(JsonObject value)
    { var bytes = JsonSerializer.SerializeToUtf8Bytes(value); return KubernetesTrustConfiguration.Parse(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), Installation); }
    private static JsonObject Entry(JsonObject c) => c["targets"]![0]!.AsObject();

    [Fact]
    public void Canonical_vector_normalizes_origin_ip_order_and_keeps_defensive_snapshots()
    {
        var c = Parse(Config()); var t = Assert.Single(c.Targets);
        Assert.Equal("839aea252163e35bfbe4324cb0951683d020dba14111d0733c99e3d6adfd88dc", t.ConfigurationDigest);
        var bytes = t.ExportCaCertificateDer(); bytes[0] ^= 255;
        Assert.Equal(Convert.FromBase64String(Ca), t.ExportCaCertificateDer());
        Assert.Equal(new[] { "10.0.0.2", "10.0.0.3" }, t.DestinationIps);
        Assert.DoesNotContain("kubernetes.example", t.ToString());
        var config = Config(); Entry(config)["destinationIps"] = new JsonArray("10.0.0.2", "10.0.0.3");
        Entry(config)["apiOrigin"] = "https://kubernetes.example.test";
        Assert.Equal(t.ConfigurationDigest, Assert.Single(Parse(config).Targets).ConfigurationDigest);
    }

    [Theory]
    [InlineData("tenantId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("targetId", "BAD")][InlineData("scopeKind", "platform")]
    [InlineData("scopeId", "00000004-0000-0000-0000-00000000000A")]
    [InlineData("namespaceUid", "")][InlineData("namespaceUid", "bad uid")]
    [InlineData("namespaceName", "*")][InlineData("namespaceName", "space name")]
    [InlineData("namespaceName", "UPPER")][InlineData("namespaceName", "-bad")]
    [InlineData("apiOrigin", "http://kubernetes.example.test")]
    [InlineData("apiOrigin", "https://kubernetes.example.test/path")]
    [InlineData("apiOrigin", "https://kubernetes.example.test?")]
    [InlineData("apiOrigin", "https://kubernetes.example.test#")]
    [InlineData("apiOrigin", "https://user@kubernetes.example.test")]
    [InlineData("apiOrigin", "https://@kubernetes.example.test")]
    [InlineData("apiOrigin", "https://kubernetes.example.test/../")]
    [InlineData("apiOrigin", "https://kubernetes.example.test.")]
    [InlineData("apiOrigin", "https://0x0a000002")]
    [InlineData("apiOrigin", "https://[fe80::1%25en0]")]
    [InlineData("serverSpkiSha256", "short")][InlineData("caCertificateDerBase64", "invalid")]
    public void Invalid_or_untrusted_entry_fails_closed(string key, string value)
    { var c = Config(); Entry(c)[key] = value; Assert.Throws<InvalidDataException>(() => Parse(c)); }

    [Theory]
    [InlineData("127.0.0.1")][InlineData("127.0.0.2")][InlineData("169.254.169.254")]
    [InlineData("224.0.0.1")][InlineData("0.0.0.0")][InlineData("255.255.255.255")]
    [InlineData("::1")][InlineData("::")][InlineData("fe80::1")][InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")][InlineData("10.1")]
    public void Production_destinations_reject_loopback_metadata_multicast_and_ambiguous_addresses(string ip)
    { var c = Config(); Entry(c)["destinationIps"] = new JsonArray(ip); Assert.Throws<InvalidDataException>(() => Parse(c)); }

    [Fact]
    public void Unknown_duplicate_null_missing_fields_and_trailing_data_are_rejected()
    {
        var c = Config(); Entry(c)["extra"] = true; Assert.Throws<InvalidDataException>(() => Parse(c));
        c = Config(); Entry(c)["namespaceUid"] = null; Assert.Throws<InvalidDataException>(() => Parse(c));
        c = Config(); Entry(c).Remove("namespaceUid"); Assert.Throws<InvalidDataException>(() => Parse(c));
        var text = Config().ToJsonString();
        foreach (var invalid in new[] { text.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), text + "{}", text.Replace("\"namespaceUid\":\"synthetic-namespace-uid\"", "\"namespaceUid\":\"synthetic-namespace-uid\",\"namespaceUid\":\"synthetic-namespace-uid\"") })
        { var bytes = System.Text.Encoding.UTF8.GetBytes(invalid); Assert.Throws<InvalidDataException>(() => KubernetesTrustConfiguration.Parse(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), Installation)); }
    }

    [Fact]
    public void Installation_hash_duplicate_targets_aliases_and_size_bounds_are_enforced()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Config()); var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Throws<InvalidDataException>(() => KubernetesTrustConfiguration.Parse(bytes, new string('0',64), Installation));
        Assert.Throws<InvalidDataException>(() => KubernetesTrustConfiguration.Parse(bytes, hash, Guid.NewGuid()));
        Assert.Throws<InvalidDataException>(() => KubernetesTrustConfiguration.Parse(new byte[65537], hash, Installation));
        var c = Config(); c["targets"]!.AsArray().Add(Entry(c).DeepClone()); Assert.Throws<InvalidDataException>(() => Parse(c));
        c["targets"]![1]!["targetId"] = Guid.NewGuid().ToString(); Assert.Throws<InvalidDataException>(() => Parse(c));
        c = Config(); for (var i=0;i<32;i++) c["targets"]!.AsArray().Add(Entry(c).DeepClone()); Assert.Throws<InvalidDataException>(() => Parse(c));
    }

    [Fact]
    public void File_loader_rejects_public_permissions_and_symlinks_without_mutating_files()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "kubernetes-trust-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir,"trust.json"); var bytes = JsonSerializer.SerializeToUtf8Bytes(Config()); File.WriteAllBytes(path,bytes);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead);
            Assert.Throws<InvalidDataException>(() => KubernetesTrustConfiguration.Load(path,hash,Installation));
            File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            // macOS /var is an OS symlink; a custody-approved path must name /private/var directly.
            var canonical = OperatingSystem.IsMacOS() && path.StartsWith("/var/",StringComparison.Ordinal) ? "/private"+path : path;
            Assert.Single(KubernetesTrustConfiguration.Load(canonical,hash,Installation).Targets);
            var link = Path.Combine(dir,"link.json"); File.CreateSymbolicLink(link,path);
            Assert.Throws<InvalidDataException>(() => KubernetesTrustConfiguration.Load(link,hash,Installation));
        }
        finally { Directory.Delete(dir,true); }
    }
    [Fact]
    public void Namespace_names_retain_the_separate_63_character_label_limit()
    {
        var c=Config();Entry(c)["namespaceName"]=new string('a',63);Assert.Single(Parse(c).Targets);
        Entry(c)["namespaceName"]=new string('a',64);Assert.Throws<InvalidDataException>(()=>Parse(c));
    }

}
