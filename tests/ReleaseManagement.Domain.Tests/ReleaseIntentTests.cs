using System.Text;
using System.Text.Json.Nodes;
using Kordra.ReleaseManagement.Domain;
using Xunit;

namespace Kordra.ReleaseManagement.Domain.Tests;

public sealed class ReleaseIntentTests
{
    // Independently generated with Python sorted compact JSON and SHA-256 over the contract domain + LF.
    private const string Input = """
        {"schemaVersion":1,"subjectKind":"release-intent-v1","tenantId":"11111111-1111-1111-1111-111111111111","project":{"id":"22222222-2222-2222-2222-222222222222","revision":1},"application":{"id":"33333333-3333-3333-3333-333333333333","revision":2},"environment":{"id":"44444444-4444-4444-4444-444444444444","revision":3,"classification":"Test"},"target":{"id":"55555555-5555-5555-5555-555555555555","kind":"kubernetes","revision":4,"associationRevision":5},"artifact":{"artifactId":"66666666-6666-6666-6666-666666666666","projectId":"22222222-2222-2222-2222-222222222222","registryId":"77777777-7777-7777-7777-777777777777","repository":"team/frontend","rootDigest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","platform":{"os":"linux","architecture":"amd64","variant":null},"rootMediaType":"application/vnd.oci.image.manifest.v1+json","rootSizeBytes":100,"selectedManifestDigest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","selectedManifestSizeBytes":100,"selectedManifestMediaType":"application/vnd.oci.image.manifest.v1+json","configDigest":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","configSizeBytes":50,"observedPlatform":{"os":"linux","architecture":"amd64","variant":null},"variantEvidence":"none","verifierProfile":"artifact-registry-v1","verifiedAtUtc":"2026-10-26T12:00:01.000000Z","createdAtUtc":"2026-10-26T12:00:02.000000Z","contentVerification":"manifest_config_hash_verified","layerAvailability":"not_checked","signatureVerification":"not_checked"},"approvalPolicy":{"policyId":"88888888-8888-8888-8888-888888888888","policyRevision":1,"payloadHash":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","bindingId":"99999999-9999-9999-9999-999999999999","bindingRevision":1},"qualification":{"profile":"pilot","provenanceClass":"unattested","executionAuthorized":false}}
        """;
    private const string Canonical = """
        {"application":{"id":"33333333-3333-3333-3333-333333333333","revision":2},"approvalPolicy":{"bindingId":"99999999-9999-9999-9999-999999999999","bindingRevision":1,"payloadHash":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","policyId":"88888888-8888-8888-8888-888888888888","policyRevision":1},"artifact":{"artifactId":"66666666-6666-6666-6666-666666666666","configDigest":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","configSizeBytes":50,"contentVerification":"manifest_config_hash_verified","createdAtUtc":"2026-10-26T12:00:02.000000Z","layerAvailability":"not_checked","observedPlatform":{"architecture":"amd64","os":"linux","variant":null},"platform":{"architecture":"amd64","os":"linux","variant":null},"projectId":"22222222-2222-2222-2222-222222222222","registryId":"77777777-7777-7777-7777-777777777777","repository":"team/frontend","rootDigest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","rootMediaType":"application/vnd.oci.image.manifest.v1+json","rootSizeBytes":100,"selectedManifestDigest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","selectedManifestMediaType":"application/vnd.oci.image.manifest.v1+json","selectedManifestSizeBytes":100,"signatureVerification":"not_checked","variantEvidence":"none","verifiedAtUtc":"2026-10-26T12:00:01.000000Z","verifierProfile":"artifact-registry-v1"},"environment":{"classification":"Test","id":"44444444-4444-4444-4444-444444444444","revision":3},"project":{"id":"22222222-2222-2222-2222-222222222222","revision":1},"qualification":{"executionAuthorized":false,"profile":"pilot","provenanceClass":"unattested"},"schemaVersion":1,"subjectKind":"release-intent-v1","target":{"associationRevision":5,"id":"55555555-5555-5555-5555-555555555555","kind":"kubernetes","revision":4},"tenantId":"11111111-1111-1111-1111-111111111111"}
        """;
    private const string Hash = "f01df47824ad1d1ba2c951513715e4bd282c117b7cd0239795284d6fad134978";

    [Fact]
    public void CanonicalVector_IsByteExactAndStoredReadRejectsTampering()
    {
        var intent = Parse(Input);
        Assert.Equal(Canonical, Encoding.UTF8.GetString(intent.GetCanonicalBytes()));
        Assert.Equal(Hash, intent.SpecHash);
        var copy = intent.GetCanonicalBytes(); copy[0] = 0;
        Assert.Equal((byte)'{', intent.GetCanonicalBytes()[0]);
        Assert.Equal(Hash, ReleaseIntent.ReadStored(intent.GetCanonicalBytes(), Hash).SpecHash);
        Assert.Throws<InvalidDataException>(() => ReleaseIntent.ReadStored(Encoding.UTF8.GetBytes(Input), Hash));
        Assert.Throws<InvalidDataException>(() => ReleaseIntent.ReadStored(intent.GetCanonicalBytes(), new string('a', 64)));
        Assert.Throws<InvalidDataException>(() => ReleaseIntent.ReadStored(intent.GetCanonicalBytes(), Hash.ToUpperInvariant()));
    }

    [Fact]
    public void EveryOwnerRevisionAndArtifactDigestChangesTheSubjectHash()
    {
        var root = JsonNode.Parse(Input)!;
        root["target"]!["associationRevision"] = 6;
        Assert.NotEqual(Hash, Parse(root.ToJsonString()).SpecHash);
        root = JsonNode.Parse(Input)!;
        root["artifact"]!["configDigest"] = "sha256:" + new string('d', 64);
        Assert.NotEqual(Hash, Parse(root.ToJsonString()).SpecHash);
    }

    [Theory]
    [InlineData("\"subjectKind\":\"release-intent-v1\"", "\"subjectKind\":\"execution-plan-v1\"")]
    [InlineData("\"executionAuthorized\":false", "\"executionAuthorized\":true")]
    [InlineData("\"classification\":\"Test\"", "\"classification\":\"Production\"")]
    [InlineData("\"signatureVerification\":\"not_checked\"", "\"signatureVerification\":\"verified\"")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")]
    [InlineData("\"revision\":1", "\"revision\":0")]
    public void RejectsUnsupportedAndMalformedSubject(string from, string to) =>
        Assert.Throws<ArgumentException>(() => Parse(Input.Replace(from, to, StringComparison.Ordinal)));

    [Fact]
    public void ForeignArtifactAndUnknownOrMissingFieldsAreRejected()
    {
        var root = JsonNode.Parse(Input)!;
        root["artifact"]!["projectId"] = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
        root = JsonNode.Parse(Input)!; root["artifact"]!["unexpected"] = true;
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
        root = JsonNode.Parse(Input)!; root["artifact"]!.AsObject().Remove("configDigest");
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
    }

    [Fact]
    public void SelectedManifestCannotBeSubstitutedForSingleManifest()
    {
        var root = JsonNode.Parse(Input)!;
        root["artifact"]!["selectedManifestDigest"] = "sha256:" + new string('f', 64);
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
    }

    [Fact]
    public void DescriptorVariantAndSha512AreAcceptedOnlyWithConsistentT10Facts()
    {
        var root = JsonNode.Parse(Input)!; var artifact = root["artifact"]!;
        artifact["rootMediaType"] = "application/vnd.oci.image.index.v1+json";
        artifact["rootDigest"] = "sha512:" + new string('f', 128);
        artifact["rootSizeBytes"] = 200;
        artifact["platform"]!["architecture"] = "arm64"; artifact["platform"]!["variant"] = "v8";
        artifact["observedPlatform"]!["architecture"] = "arm64"; artifact["observedPlatform"]!["variant"] = null;
        artifact["variantEvidence"] = "descriptor";
        var parsed = Parse(root.ToJsonString()); Assert.NotEqual(Hash, parsed.SpecHash);
        artifact["observedPlatform"]!["variant"] = "v8";
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
    }

    [Fact]
    public void NoncanonicalTimestampsAndNonIntegerSizesAreRejected()
    {
        var root = JsonNode.Parse(Input)!; root["artifact"]!["verifiedAtUtc"] = "2026-10-26T12:00:01Z";
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
        root = JsonNode.Parse(Input)!; root["artifact"]!["rootSizeBytes"] = 100.5;
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
        root = JsonNode.Parse(Input)!; root["approvalPolicy"]!["payloadHash"] = new string('A', 64);
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
    }

    private static ReleaseIntent Parse(string value) => ReleaseIntent.FromOwnerFacts(Encoding.UTF8.GetBytes(value));
}
