using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kordra.ArtifactManagement.Domain;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.ArtifactManagement.Infrastructure.Registry;
using Xunit;

namespace Kordra.Artifacts.Tests;

public sealed class ArtifactRegistryVerifierTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
    private const string Manifest = "application/vnd.oci.image.manifest.v1+json";
    private const string Index = "application/vnd.oci.image.index.v1+json";
    private const string Config = "application/vnd.oci.image.config.v1+json";
    private static byte[] Bytes(JsonObject json) => Encoding.UTF8.GetBytes(json.ToJsonString());
    private static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static JsonObject Descriptor(byte[] bytes, string media) => new()
    { ["digest"] = Hash(bytes), ["size"] = bytes.Length, ["mediaType"] = media };
    private static JsonObject Image(byte[] config) => new()
    {
        ["schemaVersion"] = 2,
        ["mediaType"] = Manifest,
        ["config"] = Descriptor(config, Config),
        ["layers"] = new JsonArray(new JsonObject
        {
            ["digest"] = "sha256:" + new string('a', 64),
            ["size"] = 1000000000L,
            ["mediaType"] = "application/vnd.oci.image.layer.v1.tar+gzip",
            ["urls"] = new JsonArray("http://127.0.0.1/never-fetch")
        })
    };
    private static ArtifactIdentity Identity(byte[] root, string architecture = "amd64", string? variant = null) =>
        ArtifactIdentity.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "team/frontend",
            OCIImageDigest.Parse(Hash(root)), ArtifactPlatform.Create("linux", architecture, variant));

    private sealed class Responses
    {
        private readonly Dictionary<string, RegistryResponse> _responses = [];
        public List<string> Calls { get; } = [];
        public List<byte[]> Borrowed { get; } = [];
        public void Add(bool manifest, byte[] body, string type, string? key = null) =>
            _responses[(manifest ? "manifest/" : "blob/") + (key ?? Hash(body))] = new(type, body);
        public Task<RegistryResponse> Fetch(bool manifest, OCIImageDigest digest, ReadOnlyMemory<byte> secret, CancellationToken ct)
        {
            var key = (manifest ? "manifest/" : "blob/") + digest.Value; Calls.Add(key);
            var response = _responses[key]; var borrowed = (byte[])response.Body.Clone(); Borrowed.Add(borrowed);
            return Task.FromResult(new RegistryResponse(response.MediaType, borrowed));
        }
        public void AssertCleared() { foreach (var bytes in Borrowed) Assert.All(bytes, b => Assert.Equal(0, b)); }
    }

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("application/json")]
    [InlineData(Config)]
    public async Task Single_image_checks_exact_manifest_config_and_never_fetches_layers(string blobType)
    {
        var config = Bytes(new() { ["os"] = "linux", ["architecture"] = "amd64", ["config"] = new JsonObject { ["Env"] = new JsonArray("SYNTHETIC=private") } });
        var root = Bytes(Image(config)); var responses = new Responses();
        responses.Add(true, root, Manifest); responses.Add(false, config, blobType);
        var proof = await ArtifactRegistryVerifier.VerifyCoreAsync(Identity(root), ReadOnlyMemory<byte>.Empty, responses.Fetch, () => Now, default);
        Assert.Equal(2, responses.Calls.Count); Assert.Equal(Now, proof.CapturedAtUtc);
        Assert.Equal(Hash(config), proof.ConfigDigest.Value); Assert.Equal("none", proof.VariantEvidence);
        Assert.Equal("artifact-registry-v1", proof.NewRecord(Guid.NewGuid(), Now).VerifierProfile);
        responses.AssertCleared();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_index_arm64_v8_selection_records_descriptor_or_config_evidence(bool configHasVariant)
    {
        var imageConfig = new JsonObject { ["os"] = "linux", ["architecture"] = "arm64" };
        if (configHasVariant) imageConfig["variant"] = "v8";
        var config = Bytes(imageConfig); var child = Bytes(Image(config)); var selected = Descriptor(child, Manifest);
        selected["platform"] = new JsonObject { ["os"] = "linux", ["architecture"] = "arm64", ["variant"] = "v8" };
        var other = Descriptor(child, "application/vnd.unknown+json");
        other["platform"] = new JsonObject { ["os"] = "linux", ["architecture"] = "amd64" };
        var root = Bytes(new() { ["schemaVersion"] = 2, ["mediaType"] = Index, ["manifests"] = new JsonArray(other, selected) });
        var responses = new Responses(); responses.Add(true, root, Index); responses.Add(true, child, Manifest); responses.Add(false, config, "application/octet-stream");
        var proof = await ArtifactRegistryVerifier.VerifyCoreAsync(Identity(root, "arm64", "v8"), ReadOnlyMemory<byte>.Empty, responses.Fetch, () => Now, default);
        Assert.Equal(3, responses.Calls.Count); Assert.Equal(configHasVariant ? "config" : "descriptor", proof.VariantEvidence);
        Assert.Equal(Hash(child), proof.SelectedManifestDigest.Value); responses.AssertCleared();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("nested")]
    [InlineData("constraints")]
    [InlineData("count")]
    public async Task Ambiguous_missing_nested_or_constrained_platform_never_fetches_a_child(string fault)
    {
        var child = Encoding.UTF8.GetBytes("{}"); var selected = Descriptor(child, fault == "nested" ? Index : Manifest);
        selected["platform"] = new JsonObject { ["os"] = "linux", ["architecture"] = fault == "missing" ? "arm64" : "amd64" };
        if (fault == "constraints") selected["platform"]!["os.features"] = new JsonArray("unsupported");
        var entries = new JsonArray(selected);
        if (fault == "duplicate") entries.Add(selected.DeepClone());
        if (fault == "count") for (var i = 1; i < 33; i++) entries.Add(selected.DeepClone());
        var root = Bytes(new() { ["schemaVersion"] = 2, ["mediaType"] = Index, ["manifests"] = entries });
        var responses = new Responses(); responses.Add(true, root, Index);
        await Assert.ThrowsAsync<InvalidDataException>(() => ArtifactRegistryVerifier.VerifyCoreAsync(Identity(root), ReadOnlyMemory<byte>.Empty, responses.Fetch, () => Now, default));
        Assert.Single(responses.Calls); responses.AssertCleared();
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("size")]
    [InlineData("schema")]
    [InlineData("artifact")]
    [InlineData("manifest-type")]
    [InlineData("config-type")]
    [InlineData("layers")]
    [InlineData("platform")]
    [InlineData("variant")]
    [InlineData("single-v8")]
    [InlineData("config-media")]
    [InlineData("duplicate-key")]
    [InlineData("depth")]
    public async Task Invalid_content_never_becomes_verifier_facts_and_clears_raw_bytes(string fault)
    {
        var configuration = new JsonObject { ["os"] = fault == "platform" ? "windows" : "linux", ["architecture"] = fault == "single-v8" ? "arm64" : "amd64" };
        if (fault == "variant") configuration["variant"] = "v8";
        var config = Bytes(configuration); var image = Image(config);
        switch (fault)
        {
            case "size": image["config"]!["size"] = config.Length + 1; break;
            case "schema": image["schemaVersion"] = 1; break;
            case "artifact": image["artifactType"] = "application/other"; break;
            case "manifest-type": image["mediaType"] = Index; break;
            case "config-type": image["config"]!["mediaType"] = "application/other"; break;
            case "layers":
                for (var i = 1; i < 129; i++) image["layers"]!.AsArray().Add(image["layers"]![0]!.DeepClone()); break;
        }
        var root = Bytes(image);
        if (fault == "duplicate-key") root = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(root).Replace("{\"schemaVersion\":2", "{\"schemaVersion\":2,\"schemaVersion\":2", StringComparison.Ordinal));
        if (fault == "depth") root = Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"ignored\":" + new string('[', 33) + "0" + new string(']', 33) + "}");
        var responses = new Responses(); var identity = Identity(root, fault == "single-v8" ? "arm64" : "amd64", fault == "single-v8" ? "v8" : null);
        responses.Add(true, fault == "digest" ? Encoding.UTF8.GetBytes("{\"changed\":true}") : root, Manifest, Hash(root));
        responses.Add(false, config, fault == "config-media" ? "text/plain" : Config);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ArtifactRegistryVerifier.VerifyCoreAsync(identity, ReadOnlyMemory<byte>.Empty, responses.Fetch, () => Now, default));
        Assert.Null(error.InnerException); Assert.DoesNotContain("changed", error.ToString()); responses.AssertCleared();
    }
}
