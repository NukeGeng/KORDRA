using System.Text;
using Kordra.Api;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class ResourcePolicyManifestTests
{
    private static ResourcePolicyManifest Example() => new(1, Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"),
        new("kordra_test", "kordra_test"), Guid.Parse("33333333-3333-3333-3333-333333333333"), Guid.Parse("44444444-4444-4444-4444-444444444444"),
        "create", null, 0, ResourcePolicyRuleTests.Payload(), [new(null, "environment", Guid.Parse("55555555-5555-5555-5555-555555555555"), 0)]);

    [Fact]
    public void Canonical_manifest_round_trip_binds_all_approved_inputs_and_keeps_explicit_nulls()
    {
        var m = Example(); var json = Encoding.UTF8.GetString(m.CanonicalBytes());
        Assert.StartsWith("{\"schemaVersion\":1,\"manifestId\":\"11111111-1111-1111-1111-111111111111\",\"installationId\":", json);
        Assert.Contains("\"policyId\":null,\"expectedCurrentRevision\":0,\"payload\":", json);
        var parsed = ResourcePolicyManifest.Parse(json); Assert.NotNull(parsed);
        Assert.Equal(m.CanonicalBytes(), parsed.CanonicalBytes()); Assert.Equal(m.Hash(), parsed.Hash());
        Assert.NotEqual(m.Hash(), (m with { ProjectId = Guid.NewGuid() }).Hash());
        Assert.NotEqual(m.Hash(), (m with { Bindings = [] }).Hash());
        Assert.NotEqual(m.Hash(), (m with { Payload = m.Payload! with { Status = "inactive" } }).Hash());
    }

    [Theory]
    [InlineData("duplicate-root")][InlineData("unknown-root")][InlineData("duplicate-database")][InlineData("unknown-binding")]
    [InlineData("duplicate-selector")][InlineData("unknown-operation")][InlineData("missing-null")][InlineData("fractional-revision")]
    public void Ambiguous_or_unrecognized_offline_manifest_never_becomes_an_approved_operation(string fault)
    {
        var json = Encoding.UTF8.GetString(Example().CanonicalBytes());
        json = fault switch
        {
            "duplicate-root" => json.Insert(1, "\"schemaVersion\":1,"),
            "unknown-root" => json.Insert(1, "\"approval\":true,"),
            "duplicate-database" => json.Replace("\"databaseName\":\"kordra_test\"", "\"databaseName\":\"kordra_test\",\"databaseName\":\"kordra_test\""),
            "unknown-binding" => json.Replace("\"expectedBindingRevision\":0", "\"expectedBindingRevision\":0,\"fallback\":true"),
            "duplicate-selector" => json.Replace("\"bindings\":[", "\"bindings\":[{\"bindingId\":null,\"scopeKind\":\"environment\",\"environmentId\":\"55555555-5555-5555-5555-555555555555\",\"expectedBindingRevision\":0},"),
            "unknown-operation" => json.Replace("\"operation\":\"create\"", "\"operation\":\"deploy\""),
            "missing-null" => json.Replace("\"policyId\":null,", ""),
            _ => json.Replace("\"expectedCurrentRevision\":0", "\"expectedCurrentRevision\":0.5")
        };
        Assert.Null(ResourcePolicyManifest.Parse(json));
    }

    [Fact]
    public void Writer_inventory_includes_current_online_and_offline_authority_entrypoints()
    {
        AuthorityWriterRegistry.Validate();
        Assert.Contains(AuthorityWriterRegistry.Entries, x => x.Id == "policy.publish-bind" && x.OfflineOnly);
        Assert.Contains(AuthorityWriterRegistry.Entries, x => x.Id == "catalog.environment-v2" && !x.OfflineOnly);
        Assert.Contains(AuthorityWriterRegistry.Entries, x => x.Id == "iam.logout-all" && !x.OfflineOnly);
    }
}
