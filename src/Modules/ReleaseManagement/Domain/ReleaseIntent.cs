using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kordra.ReleaseManagement.Domain;

/// <summary>
/// Closed release-intent bytes and hash. Constructing this value is not authorization, an owner lookup,
/// artifact verification, a frozen database revision or permission to execute a deployment.
/// Host must resolve and compare every owner fact under current admission before persisting it.
/// </summary>
public sealed class ReleaseIntent
{
    private const int MaximumBytes = 64 * 1024;
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";
    private const string OciManifest = "application/vnd.oci.image.manifest.v1+json";
    private const string OciIndex = "application/vnd.oci.image.index.v1+json";
    private const string DockerManifest = "application/vnd.docker.distribution.manifest.v2+json";
    private const string DockerIndex = "application/vnd.docker.distribution.manifest.list.v2+json";
    private static readonly Regex Repository = new(@"\A[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*(/[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*)*\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("release-intent-v1\n");
    private readonly byte[] _bytes;

    private ReleaseIntent(byte[] bytes)
    {
        _bytes = bytes;
        var input = new byte[Domain.Length + bytes.Length];
        Domain.CopyTo(input, 0); bytes.CopyTo(input, Domain.Length);
        SpecHash = Convert.ToHexStringLower(SHA256.HashData(input));
    }

    public string SpecHash { get; }
    public byte[] GetCanonicalBytes() => (byte[])_bytes.Clone();

    /// <summary>Canonicalizes closed facts already resolved by trusted owners; never use client facts as authority.</summary>
    public static ReleaseIntent FromOwnerFacts(ReadOnlyMemory<byte> json)
    {
        if (json.Length is 0 or > MaximumBytes) throw new ArgumentException("Release intent exceeds its input bound.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            Validate(document.RootElement);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                WriteCanonical(writer, document.RootElement);
            if (stream.Length > MaximumBytes) throw new ArgumentException("Release intent exceeds its canonical bound.");
            return new ReleaseIntent(stream.ToArray());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new ArgumentException("Invalid release intent.", nameof(json), error);
        }
    }

    /// <summary>Stored bytes are never repaired: malformed, noncanonical or mismatched data is an integrity failure.</summary>
    public static ReleaseIntent ReadStored(ReadOnlyMemory<byte> bytes, string expectedHash)
    {
        try
        {
            if (!ValidHash(expectedHash)) throw new InvalidDataException("Invalid release intent hash.");
            var value = FromOwnerFacts(bytes);
            if (!CryptographicOperations.FixedTimeEquals(bytes.Span, value._bytes) ||
                !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHash), Convert.FromHexString(value.SpecHash)))
                throw new InvalidDataException("Release intent integrity failure.");
            return value;
        }
        catch (ArgumentException error) { throw new InvalidDataException("Release intent integrity failure.", error); }
    }

    private static void Validate(JsonElement root)
    {
        var f = Fields(root, "schemaVersion", "subjectKind", "tenantId", "project", "application", "environment", "target", "artifact", "approvalPolicy", "qualification");
        if (Number(f["schemaVersion"]) != 1 || Text(f["subjectKind"]) != "release-intent-v1") throw Invalid();
        _ = Id(f["tenantId"]);
        var project = Identity(f["project"]); _ = Identity(f["application"]);
        var env = Fields(f["environment"], "id", "revision", "classification");
        _ = Id(env["id"]); Positive(env["revision"]);
        if (Text(env["classification"]) is not ("Development" or "Test")) throw Invalid();
        var target = Fields(f["target"], "id", "kind", "revision", "associationRevision");
        _ = Id(target["id"]); Positive(target["revision"]); Positive(target["associationRevision"]);
        var kind = Text(target["kind"]);
        if (kind.Length is < 1 or > 64 || !kind.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-')) throw Invalid();
        var policy = Fields(f["approvalPolicy"], "policyId", "policyRevision", "payloadHash", "bindingId", "bindingRevision");
        _ = Id(policy["policyId"]); _ = Id(policy["bindingId"]); Positive(policy["policyRevision"]); Positive(policy["bindingRevision"]);
        if (!ValidHash(Text(policy["payloadHash"]))) throw Invalid();
        var qualification = Fields(f["qualification"], "profile", "provenanceClass", "executionAuthorized");
        if (Text(qualification["profile"]) != "pilot" || Text(qualification["provenanceClass"]) != "unattested" ||
            qualification["executionAuthorized"].ValueKind != JsonValueKind.False) throw Invalid();
        ValidateArtifact(f["artifact"], project);
    }

    private static void ValidateArtifact(JsonElement value, Guid projectId)
    {
        var f = Fields(value, "artifactId", "projectId", "registryId", "repository", "rootDigest", "platform", "rootMediaType",
            "rootSizeBytes", "selectedManifestDigest", "selectedManifestSizeBytes", "selectedManifestMediaType", "configDigest",
            "configSizeBytes", "observedPlatform", "variantEvidence", "verifierProfile", "verifiedAtUtc", "createdAtUtc",
            "contentVerification", "layerAvailability", "signatureVerification");
        _ = Id(f["artifactId"]); _ = Id(f["registryId"]);
        if (Id(f["projectId"]) != projectId) throw Invalid();
        var repository = Text(f["repository"]);
        if (repository.Length is < 1 or > 255 || !Repository.IsMatch(repository)) throw Invalid();
        var root = Digest(f["rootDigest"]); var selected = Digest(f["selectedManifestDigest"]); _ = Digest(f["configDigest"]);
        var rootSize = Size(f["rootSizeBytes"]); var selectedSize = Size(f["selectedManifestSizeBytes"]); _ = Size(f["configSizeBytes"]);
        var rootType = Text(f["rootMediaType"]); var selectedType = Text(f["selectedManifestMediaType"]);
        if (rootType is not (OciManifest or OciIndex or DockerManifest or DockerIndex) || selectedType is not (OciManifest or DockerManifest)) throw Invalid();
        var single = rootType is OciManifest or DockerManifest;
        if (single && (root != selected || rootSize != selectedSize || rootType != selectedType)) throw Invalid();
        var requested = Platform(f["platform"]); var observed = Platform(f["observedPlatform"]);
        if (requested.Os != observed.Os || requested.Architecture != observed.Architecture) throw Invalid();
        var variant = Text(f["variantEvidence"]);
        if (!(variant == "none" && requested.Variant is null && observed.Variant is null ||
            variant == "config" && requested.Variant == "v8" && observed.Variant == "v8" ||
            variant == "descriptor" && !single && requested.Variant == "v8" && observed.Variant is null)) throw Invalid();
        if (Text(f["verifierProfile"]) != "artifact-registry-v1" || Text(f["contentVerification"]) != "manifest_config_hash_verified" ||
            Text(f["layerAvailability"]) != "not_checked" || Text(f["signatureVerification"]) != "not_checked") throw Invalid();
        Time(f["verifiedAtUtc"]); Time(f["createdAtUtc"]);
    }

    private static (string Os, string Architecture, string? Variant) Platform(JsonElement element)
    {
        var f = Fields(element, "os", "architecture", "variant");
        var os = Text(f["os"]); var architecture = Text(f["architecture"]);
        var variant = f["variant"].ValueKind == JsonValueKind.Null ? null : Text(f["variant"]);
        if (os != "linux" || architecture is not ("amd64" or "arm64") ||
            variant is not null && (architecture != "arm64" || variant != "v8")) throw Invalid();
        return (os, architecture, variant);
    }

    private static Guid Identity(JsonElement value)
    {
        var f = Fields(value, "id", "revision"); Positive(f["revision"]); return Id(f["id"]);
    }
    private static Dictionary<string, JsonElement> Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var expected = names.ToHashSet(StringComparer.Ordinal); var actual = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!expected.Contains(property.Name) || !actual.TryAdd(property.Name, property.Value)) throw Invalid();
        if (actual.Count != expected.Count) throw Invalid();
        return actual;
    }
    private static Guid Id(JsonElement value)
    {
        var text = Text(value);
        if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text) throw Invalid();
        return id;
    }
    private static string Text(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) throw Invalid();
        var text = value.GetString() ?? throw Invalid();
        // Every v1 subject string is a fixed ASCII vocabulary, digest, repository, timestamp or ID.
        if (text.Any(c => c is < ' ' or > '~')) throw Invalid();
        return text;
    }
    private static long Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
        ? result : throw Invalid();
    private static void Positive(JsonElement value) { if (Number(value) <= 0) throw Invalid(); }
    private static long Size(JsonElement value) { var n = Number(value); if (n is < 1 or > 1048576) throw Invalid(); return n; }
    private static string Digest(JsonElement value)
    {
        var text = Text(value);
        if (!((text.StartsWith("sha256:", StringComparison.Ordinal) && text.Length == 71) ||
              (text.StartsWith("sha512:", StringComparison.Ordinal) && text.Length == 135)) ||
            !text.AsSpan(7).ToString().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) throw Invalid();
        return text;
    }
    private static void Time(JsonElement value)
    {
        var text = Text(value);
        if (!DateTimeOffset.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ||
            parsed == DateTimeOffset.MinValue || parsed == DateTimeOffset.MaxValue ||
            parsed.ToString(TimeFormat, CultureInfo.InvariantCulture) != text) throw Invalid();
    }
    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static ArgumentException Invalid() => new("Invalid release intent.");
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Number) writer.WriteNumberValue(Number(value));
        else value.WriteTo(writer);
    }
}
