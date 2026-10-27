using System.Text;
using System.Text.Json.Nodes;
using Kordra.ApprovalGovernance.Domain;
using Xunit;

namespace Kordra.ApprovalGovernance.Domain.Tests;

public sealed class ApprovalPolicyManifestTests
{
    private const string Valid = """
        {"schemaVersion":1,"manifestId":"11111111-1111-1111-1111-111111111111","installationId":"22222222-2222-2222-2222-222222222222","expectedDatabase":"kordra","expectedRole":"kordra_admin","tenantId":"33333333-3333-3333-3333-333333333333","projectId":"44444444-4444-4444-4444-444444444444","policyId":"55555555-5555-5555-5555-555555555555","expectedPolicyRevision":0,"payload":{"schemaVersion":1,"purpose":"release-intent-v1","status":"active","effectiveFromUtc":"2026-10-27T00:00:00.000000Z","expiresAtUtc":null,"rules":[{"classification":"Test","effect":"allow","separationOfDuties":"independent","allowUnattestedProvenance":true,"pendingLifetimeSeconds":86400,"grantLifetimeSeconds":1800,"blockedWindowsUtc":[]}]},"bindingChanges":[]}
        """;
    [Fact]
    public void ManifestHash_UsesIndependentCanonicalVectorAndCopyIsolation()
    {
        var manifest = Parse(Valid);
        Assert.Equal("6c511078110cc95c7a080d3b68c9a1a95b761097fdb2f69ec503c463a6ba6994", manifest.CommandHash);
        var data = manifest.GetCanonicalBytes(); data[0] = 0;
        Assert.Equal((byte)'{', manifest.GetCanonicalBytes()[0]);
        Assert.Equal(manifest.CommandHash, Parse(Encoding.UTF8.GetString(manifest.GetCanonicalBytes())).CommandHash);
    }

    [Theory]
    [InlineData("\"expectedRole\":\"kordra_admin\"", "\"expectedRole\":\"postgres\"")]
    [InlineData("\"schemaVersion\":1,\"manifestId\"", "\"schemaVersion\":1,\"schemaVersion\":1,\"manifestId\"")]
    [InlineData("\"expectedPolicyRevision\":0", "\"expectedPolicyRevision\":-1")]
    [InlineData("\"expectedDatabase\":\"kordra\"", "\"expectedDatabase\":\"\"")]
    [InlineData("11111111-1111-1111-1111-111111111111", "00000000-0000-0000-0000-000000000000")]
    [InlineData("55555555-5555-5555-5555-555555555555", "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    public void InvalidManifest_IsRejected(string before, string after) =>
        Assert.Throws<ArgumentException>(() => Parse(Valid.Replace(before, after, StringComparison.Ordinal)));

    [Fact]
    public void BindingRequiresMatchingIdentityRevisionAndUniqueOrderedEnvironments()
    {
        var root = JsonNode.Parse(Valid)!;
        var changes = root["bindingChanges"]!.AsArray();
        changes.Add(Binding("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", null, 0));
        Assert.Single(Parse(root.ToJsonString()).BindingChanges);
        changes[0]!["expectedBindingRevision"] = 1;
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
        changes[0]!["expectedBindingId"] = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        Assert.Single(Parse(root.ToJsonString()).BindingChanges);
        changes.Add(Binding("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", null, 0));
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
        changes[1]!["environmentId"] = "99999999-9999-9999-9999-999999999999";
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
    }

    [Fact]
    public void NonAsciiDatabase_UsesLiteralUtf8WithoutOptionalEscapes()
    {
        var root = JsonNode.Parse(Valid)!; root["expectedDatabase"] = "kordra-例\\\"";
        var canonical = Encoding.UTF8.GetString(Parse(root.ToJsonString()).GetCanonicalBytes());
        Assert.Contains("\"expectedDatabase\":\"kordra-例\\\\\\\"\"", canonical, StringComparison.Ordinal);
    }

    private static JsonObject Binding(string environment, string? binding, int revision) => new()
    {
        ["environmentId"] = environment,
        ["expectedEnvironmentRevision"] = 1,
        ["expectedBindingId"] = binding,
        ["expectedBindingRevision"] = revision
    };
    private static ApprovalPolicyManifest Parse(string value) => ApprovalPolicyManifest.Parse(Encoding.UTF8.GetBytes(value));
}
