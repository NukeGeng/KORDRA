using System.Security.Cryptography;
using System.Text.Json;
using Kordra.ArtifactManagement.Domain;

namespace Kordra.Modules.ArtifactManagement.Infrastructure.Registry;

public sealed class ArtifactRegistryUnavailableException() : Exception("Artifact registry is unavailable.");

/// <summary>Bounded content proof, not authorization, provenance, layer availability or deployment proof.</summary>
public sealed class ArtifactRegistryVerifier(RegistryTrust trust, Func<DateTimeOffset> now)
{
    public async Task<ArtifactVerificationFacts> VerifyAsync(ArtifactIdentity identity, ReadOnlyMemory<byte> credential, CancellationToken ct)
    {
        if (identity is null || identity.TenantId != trust.TenantId || identity.ProjectId != trust.ProjectId ||
            identity.RegistryId != trust.RegistryId || identity.Repository != trust.Repository) throw Unavailable();
        using var transport = new RegistryReadOnlyTransport(trust, now);
        var unavailable = false;
        async Task<RegistryResponse> Fetch(bool manifest, OCIImageDigest digest, ReadOnlyMemory<byte> secret, CancellationToken token)
        {
            try { return await transport.GetAsync(manifest, digest, secret, token); }
            catch (RegistryContentMismatchException) { throw; }
            catch { unavailable = true; throw; }
        }
        try { return await VerifyCoreAsync(identity, credential, Fetch, now, ct); }
        catch when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch when (unavailable) { throw new ArtifactRegistryUnavailableException(); }
    }

    internal static async Task<ArtifactVerificationFacts> VerifyCoreAsync(ArtifactIdentity identity, ReadOnlyMemory<byte> credential,
        Func<bool, OCIImageDigest, ReadOnlyMemory<byte>, CancellationToken, Task<RegistryResponse>> fetch,
        Func<DateTimeOffset> now, CancellationToken ct)
    {
        var owned = new List<byte[]>(3);
        var total = 0;
        async Task<RegistryResponse> Read(bool manifest, OCIImageDigest digest, long? size = null)
        {
            ct.ThrowIfCancellationRequested();
            var response = await fetch(manifest, digest, credential, ct);
            owned.Add(response.Body); total = checked(total + response.Body.Length);
            if (response.Body.Length is < 1 or > 1048576 || total > 3145728 ||
                !digest.MatchesContent(response.Body, size ?? response.Body.Length)) throw Unavailable();
            return response;
        }
        try
        {
            var root = await Read(true, identity.RootDigest);
            using var document = Parse(root.Body);
            var rootElement = document.RootElement;
            ValidateManifest(rootElement, root.MediaType);
            var selected = root; var selectedDigest = identity.RootDigest;
            var selectedElement = rootElement; JsonDocument? child = null;
            var index = root.MediaType is ArtifactVerificationFacts.OciIndex or ArtifactVerificationFacts.DockerIndex;
            try
            {
                if (index)
                {
                    var manifests = rootElement.GetProperty("manifests");
                    if (manifests.ValueKind != JsonValueKind.Array || manifests.GetArrayLength() is < 1 or > 32) throw Unavailable();
                    Descriptor? match = null;
                    foreach (var item in manifests.EnumerateArray())
                    {
                        var descriptor = ReadDescriptor(item, bounded: true);
                        if (!item.TryGetProperty("platform", out var platform)) continue;
                        Unique(platform, "os", "architecture", "variant", "os.version", "os.features");
                        if (Text(platform, "os") != identity.Platform.Os || Text(platform, "architecture") != identity.Platform.Architecture ||
                            OptionalText(platform, "variant") != identity.Platform.Variant) continue;
                        if (platform.TryGetProperty("os.version", out _) || platform.TryGetProperty("os.features", out _) ||
                            descriptor.MediaType is not (ArtifactVerificationFacts.OciManifest or ArtifactVerificationFacts.DockerManifest) || match is not null) throw Unavailable();
                        match = descriptor;
                    }
                    if (match is null) throw Unavailable();
                    selectedDigest = match.Digest;
                    selected = await Read(true, match.Digest, match.Size);
                    if (selected.MediaType != match.MediaType) throw Unavailable();
                    child = Parse(selected.Body); selectedElement = child.RootElement;
                    ValidateManifest(selectedElement, selected.MediaType);
                }
                var configDescriptor = ReadDescriptor(selectedElement.GetProperty("config"), bounded: true);
                if (configDescriptor.MediaType is not ("application/vnd.oci.image.config.v1+json" or "application/vnd.docker.container.image.v1+json")) throw Unavailable();
                var layers = selectedElement.GetProperty("layers");
                if (layers.ValueKind != JsonValueKind.Array || layers.GetArrayLength() > 128) throw Unavailable();
                foreach (var layer in layers.EnumerateArray()) _ = ReadDescriptor(layer, bounded: false);
                var config = await Read(false, configDescriptor.Digest, configDescriptor.Size);
                if (config.MediaType != configDescriptor.MediaType && config.MediaType is not ("application/octet-stream" or "application/json")) throw Unavailable();
                using var configuration = Parse(config.Body); var image = configuration.RootElement;
                Unique(image, "os", "architecture", "variant", "os.version", "os.features");
                if (Text(image, "os") != identity.Platform.Os || Text(image, "architecture") != identity.Platform.Architecture ||
                    image.TryGetProperty("os.version", out _) || image.TryGetProperty("os.features", out _)) throw Unavailable();
                var variant = OptionalText(image, "variant");
                var evidence = "none";
                if (identity.Platform.Variant is null)
                { if (variant is not null) throw Unavailable(); }
                else if (variant == identity.Platform.Variant) evidence = "config";
                else if (index && variant is null) evidence = "descriptor";
                else throw Unavailable();
                ct.ThrowIfCancellationRequested();
                return new(identity, root.MediaType, root.Body.Length, selectedDigest, selected.MediaType, selected.Body.Length,
                    configDescriptor.Digest, config.Body.Length, ArtifactPlatform.Create(identity.Platform.Os, identity.Platform.Architecture, variant), evidence, now());
            }
            finally { child?.Dispose(); }
        }
        catch { throw Unavailable(); }
        finally { foreach (var bytes in owned) CryptographicOperations.ZeroMemory(bytes); }
    }

    private sealed record Descriptor(OCIImageDigest Digest, string MediaType, long Size);
    private static Descriptor ReadDescriptor(JsonElement item, bool bounded)
    {
        Unique(item, "digest", "mediaType", "size", "platform");
        var digest = OCIImageDigest.Parse(Text(item, "digest")); var type = Text(item, "mediaType");
        if (type.Length is < 1 or > 128 || type.Any(c => c is <= ' ' or > '~') ||
            !item.GetProperty("size").TryGetInt64(out var size) || size < 1 || bounded && size > 1048576) throw Unavailable();
        return new(digest, type, size);
    }
    private static JsonDocument Parse(byte[] bytes) => JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
    private static void ValidateManifest(JsonElement element, string mediaType)
    {
        Unique(element, "schemaVersion", "mediaType", "config", "layers", "manifests", "artifactType", "subject");
        if (mediaType is not (ArtifactVerificationFacts.OciManifest or ArtifactVerificationFacts.OciIndex or ArtifactVerificationFacts.DockerManifest or ArtifactVerificationFacts.DockerIndex) ||
            !element.GetProperty("schemaVersion").TryGetInt32(out var schema) || schema != 2 ||
            element.TryGetProperty("mediaType", out var type) && type.GetString() != mediaType ||
            element.TryGetProperty("artifactType", out _) || element.TryGetProperty("subject", out _)) throw Unavailable();
    }
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw Unavailable();
    private static string? OptionalText(JsonElement element, string name) => element.TryGetProperty(name, out _) ? Text(element, name) : null;
    private static void Unique(JsonElement element, params string[] interpreted)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Unavailable();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (interpreted.Contains(property.Name, StringComparer.Ordinal) && !seen.Add(property.Name)) throw Unavailable();
    }
    private static InvalidDataException Unavailable() => new("Artifact verification is unavailable.");
}
