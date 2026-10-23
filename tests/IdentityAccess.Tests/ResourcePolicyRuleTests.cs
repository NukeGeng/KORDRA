using System.Text;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class ResourcePolicyRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
    internal static ResourcePolicyPayload Payload(string scope = "environment") => new(1, "target.read", scope, "active", Now, null,
        [new(scope == "environment" ? "Development" : "NoEnvironment", "allow")]);

    [Fact]
    public void Canonical_policy_is_fixed_order_and_round_trips_without_a_second_allowlist()
    {
        var p = Payload() with { Rules = [new("Staging", "allow"), new("Production", "deny"), new("Development", "allow")] };
        var bytes = ResourcePolicies.CanonicalBytes(p);
        const string expected = "{\"schemaVersion\":1,\"action\":\"target.read\",\"scopeKind\":\"environment\",\"status\":\"active\",\"effectiveFromUtc\":\"2026-10-22T00:00:00.000000Z\",\"expiresAtUtc\":null,\"rules\":[{\"case\":\"Development\",\"effect\":\"allow\"},{\"case\":\"Production\",\"effect\":\"deny\"},{\"case\":\"Staging\",\"effect\":\"allow\"}]}";
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));
        Assert.Equal(bytes, ResourcePolicies.CanonicalBytes(ResourcePolicies.Parse(expected)!));
        Assert.Equal(ResourcePolicies.Hash(p), ResourcePolicies.Hash(p with { Rules = p.Rules.Reverse().ToArray() }));
    }

    [Theory]
    [InlineData("unknown-field")][InlineData("duplicate-field")][InlineData("duplicate-rule-field")][InlineData("duplicate-rule")]
    [InlineData("unknown-action")][InlineData("unknown-condition")][InlineData("unknown-case")][InlineData("wrong-scope")]
    [InlineData("unknown-version")][InlineData("unknown-status")][InlineData("non-utc")][InlineData("imprecise-time")]
    public void Unknown_or_ambiguous_payload_cannot_be_accepted(string fault)
    {
        var json = Encoding.UTF8.GetString(ResourcePolicies.CanonicalBytes(Payload()));
        json = fault switch
        {
            "unknown-field" => json.Replace("\"rules\":", "\"allowed_classifications\":[],\"rules\":"),
            "duplicate-field" => json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            "duplicate-rule-field" => json.Replace("\"case\":\"Development\"", "\"case\":\"Development\",\"case\":\"Development\""),
            "duplicate-rule" => json.Replace("\"rules\":[", "\"rules\":[{\"case\":\"Development\",\"effect\":\"allow\"},"),
            "unknown-action" => json.Replace("target.read", "deployment.execute"),
            "unknown-condition" => json.Replace("\"effect\":\"allow\"", "\"effect\":\"allow\",\"condition\":true"),
            "unknown-case" => json.Replace("Development", "dev"),
            "wrong-scope" => json.Replace("environment", "project_without_environment"),
            "unknown-version" => json.Replace("schemaVersion\":1", "schemaVersion\":2"),
            "unknown-status" => json.Replace("active", "unknown"),
            "non-utc" => json.Replace("000000Z", "000000+07:00"),
            _ => json.Replace("000000Z", "0000001Z")
        };
        Assert.Null(ResourcePolicies.Parse(json));
    }

    [Theory]
    [InlineData("allow", true)][InlineData("deny", false)][InlineData("absent", false)][InlineData("expired", false)]
    [InlineData("future", false)][InlineData("inactive", false)][InlineData("foreign-tenant", false)][InlineData("foreign-project", false)]
    [InlineData("wrong-policy", false)][InlineData("old-revision", false)][InlineData("missing-binding", false)][InlineData("corrupt-hash", false)]
    [InlineData("explicit-deny", false)]
    public void Exact_eligibility_and_exclusive_validity_are_enforced(string scenario, bool allow)
    {
        var policy = new ResourcePolicySnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Payload(), "");
        var selection = new ResourcePolicySelection(Guid.NewGuid(), 1, policy.TenantId, policy.ProjectId, Guid.NewGuid(), "environment", policy.PolicyId, 1, "Development");
        var p = policy.Payload;
        switch (scenario)
        {
            case "deny": p = p with { Rules = [new("Development", "deny")] }; break;
            case "absent": p = p with { Rules = [new("Production", "allow")] }; break;
            case "expired": p = p with { EffectiveFromUtc = Now.AddHours(-1), ExpiresAtUtc = Now }; break;
            case "future": p = p with { EffectiveFromUtc = Now.AddHours(1) }; break;
            case "inactive": p = p with { Status = "inactive" }; break;
            case "foreign-tenant": selection = selection with { TenantId = Guid.NewGuid() }; break;
            case "foreign-project": selection = selection with { ProjectId = Guid.NewGuid() }; break;
            case "wrong-policy": selection = selection with { PolicyId = Guid.NewGuid() }; break;
            case "old-revision": selection = selection with { PolicyRevision = 2 }; break;
            case "missing-binding": selection = selection with { BindingId = Guid.Empty }; break;
            case "explicit-deny": p = p with { Rules = [new("Development", "allow"), new("Development", "deny")] }; break;
        }
        policy = policy with { Payload = p, ContentHash = ResourcePolicies.Hash(p) };
        if (scenario == "corrupt-hash") policy = policy with { ContentHash = new string('0', 64) };
        Assert.Equal(allow ? ResourceEligibility.Allow : ResourceEligibility.Deny, ResourcePolicies.Evaluate(policy, selection, Now));
    }

    [Fact]
    public void No_environment_requires_its_own_explicit_rule_without_classification_fallback()
    {
        var p = Payload("project_without_environment");
        var policy = new ResourcePolicySnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, p, ResourcePolicies.Hash(p));
        var selected = new ResourcePolicySelection(Guid.NewGuid(), 1, policy.TenantId, policy.ProjectId, null, p.ScopeKind, policy.PolicyId, 1, null);
        Assert.Equal(ResourceEligibility.Allow, ResourcePolicies.Evaluate(policy, selected, Now));
        Assert.Equal(ResourceEligibility.Deny, ResourcePolicies.Evaluate(policy, selected with { Classification = "Development" }, Now));
        Assert.Equal(ResourceEligibility.Deny, ResourcePolicies.Evaluate(policy, selected with { ScopeKind = "environment" }, Now));
    }
}
