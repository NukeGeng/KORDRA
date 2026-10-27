using System.Text;
using System.Text.Json.Nodes;
using Kordra.ApprovalGovernance.Domain;
using Xunit;

namespace Kordra.ApprovalGovernance.Domain.Tests;

public sealed class ReleaseApprovalPolicyTests
{
    private const string Valid = """
        {"schemaVersion":1,"purpose":"release-intent-v1","status":"active","effectiveFromUtc":"2026-10-27T00:00:00.000000Z","expiresAtUtc":null,"rules":[{"classification":"Test","effect":"allow","separationOfDuties":"independent","allowUnattestedProvenance":true,"pendingLifetimeSeconds":86400,"grantLifetimeSeconds":1800,"blockedWindowsUtc":[]}]}
        """;
    private const string Canonical = """
        {"effectiveFromUtc":"2026-10-27T00:00:00.000000Z","expiresAtUtc":null,"purpose":"release-intent-v1","rules":[{"allowUnattestedProvenance":true,"blockedWindowsUtc":[],"classification":"Test","effect":"allow","grantLifetimeSeconds":1800,"pendingLifetimeSeconds":86400,"separationOfDuties":"independent"}],"schemaVersion":1,"status":"active"}
        """;

    [Fact]
    public void IndependentVector_BindsCanonicalBytesAndDomain()
    {
        var policy = Parse(Valid);
        Assert.Equal(Canonical, Encoding.UTF8.GetString(policy.GetCanonicalBytes()));
        Assert.Equal("3cb7f351b5a21315c32349942bb31c4bac99b88d8be59540227b388f2e52feca", policy.PayloadHash);
        var returned = policy.GetCanonicalBytes();
        returned[0] = 0;
        Assert.Equal(Canonical, Encoding.UTF8.GetString(policy.GetCanonicalBytes()));
        Assert.Equal(policy.PayloadHash, Parse(Canonical).PayloadHash);
    }

    [Theory]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":2")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")]
    [InlineData("\"status\":\"active\"", "\"status\":\"active\",\"unexpected\":true")]
    [InlineData("\"expiresAtUtc\":null,", "")]
    [InlineData("2026-10-27T00:00:00.000000Z", "2026-10-27T00:00:00Z")]
    [InlineData("2026-10-27T00:00:00.000000Z", "2026-10-27T00:00:00.0000000Z")]
    [InlineData("2026-10-27T00:00:00.000000Z", "2026-10-27T00:00:00.000000+00:00")]
    [InlineData("\"grantLifetimeSeconds\":1800", "\"grantLifetimeSeconds\":59")]
    [InlineData("\"pendingLifetimeSeconds\":86400", "\"pendingLifetimeSeconds\":86401")]
    [InlineData("\"classification\":\"Test\"", "\"classification\":\"Production\"")]
    [InlineData("\"effect\":\"allow\"", "\"effect\":\"deny\"")]
    [InlineData("\"status\":\"active\"", "\"status\":\"unknown\"")]
    public void InvalidClosedPolicy_IsRejected(string oldValue, string newValue) =>
        Assert.Throws<ArgumentException>(() => Parse(Valid.Replace(oldValue, newValue, StringComparison.Ordinal)));

    [Fact]
    public void SoloPolicy_RequiresPilotClassification()
    {
        var solo = Valid.Replace("\"independent\"", "\"self_allowed\"", StringComparison.Ordinal);
        Assert.Equal("self_allowed", Parse(solo).Rules[0].SeparationOfDuties);
        var production = solo.Replace("\"Test\"", "\"Production\"", StringComparison.Ordinal)
            .Replace("\"allowUnattestedProvenance\":true", "\"allowUnattestedProvenance\":false", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Parse(production));
    }

    [Fact]
    public void RuleDuplicates_AreRejectedAndDifferentRulesSortCanonically()
    {
        var root = JsonNode.Parse(Valid)!;
        var rules = root["rules"]!.AsArray();
        rules.Add(rules[0]!.DeepClone());
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
        rules[1]!["classification"] = "Development";
        var policy = Parse(root.ToJsonString());
        Assert.Equal(new[] { "Development", "Test" }, policy.Rules.Select(x => x.Classification));
    }

    [Fact]
    public void Windows_RejectOverlapAndAcceptTouchingHalfOpenIntervals()
    {
        var root = JsonNode.Parse(Valid)!;
        var windows = root["rules"]![0]!["blockedWindowsUtc"]!.AsArray();
        windows.Add(Window("2026-11-01T00:00:00.000000Z", "2026-11-01T01:00:00.000000Z"));
        windows.Add(Window("2026-11-01T01:00:00.000000Z", "2026-11-01T02:00:00.000000Z"));
        Assert.Equal(2, Parse(root.ToJsonString()).Rules[0].BlockedWindowsUtc.Count);
        windows[1]!["startsAtUtc"] = "2026-11-01T00:59:59.000000Z";
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
    }

    [Fact]
    public void RuleAndWindowBounds_AreValidatedWithoutFallback()
    {
        var root = JsonNode.Parse(Valid)!; var rules = root["rules"]!.AsArray();
        rules.Clear(); Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
        root = JsonNode.Parse(Valid)!; rules = root["rules"]!.AsArray();
        for (var i = 0; i < 8; i++) rules.Add(rules[0]!.DeepClone());
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
        root = JsonNode.Parse(Valid)!; var windows = root["rules"]![0]!["blockedWindowsUtc"]!.AsArray();
        for (var i = 0; i < 17; i++)
        {
            var start = DateTimeOffset.Parse("2026-11-01T00:00:00Z").AddHours(i * 2);
            windows.Add(Window(start.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ"), start.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ")));
        }
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
        windows.RemoveAt(16); Assert.Equal(16, Parse(root.ToJsonString()).Rules[0].BlockedWindowsUtc.Count);
        root["rules"]![0]!["pendingLifetimeSeconds"] = 60;
        root["rules"]![0]!["grantLifetimeSeconds"] = 86400;
        var policy = Parse(root.ToJsonString()); Assert.Equal(60, policy.Rules[0].PendingLifetimeSeconds);
        Assert.Equal(86400, policy.Rules[0].GrantLifetimeSeconds);
    }

    [Fact]
    public void Expiry_MustFollowEffectiveTime()
    {
        var root = JsonNode.Parse(Valid)!;
        root["expiresAtUtc"] = "2026-10-27T00:00:00.000000Z";
        Assert.Throws<ArgumentException>(() => Parse(root.ToJsonString()));
    }

    [Fact]
    public void Applicability_DenyWinsAndTimeBoundsAreExclusiveAtExpiry()
    {
        var root = JsonNode.Parse(Valid)!;
        root["expiresAtUtc"] = "2026-10-28T00:00:00.000000Z";
        var policy = Parse(root.ToJsonString());
        var start = DateTimeOffset.Parse("2026-10-27T00:00:00Z");
        Assert.Null(policy.FindApplicableAllowRule("Test", start.AddTicks(-1)));
        Assert.NotNull(policy.FindApplicableAllowRule("Test", start));
        Assert.Null(policy.FindApplicableAllowRule("Test", start.AddDays(1)));
        Assert.Null(policy.FindApplicableAllowRule("Development", start));
        var rules = root["rules"]!.AsArray();
        var deny = rules[0]!.DeepClone();
        deny["effect"] = "deny";
        deny["allowUnattestedProvenance"] = false;
        rules.Add(deny);
        Assert.Null(Parse(root.ToJsonString()).FindApplicableAllowRule("Test", start));
        root["status"] = "inactive";
        rules.RemoveAt(1);
        Assert.Null(Parse(root.ToJsonString()).FindApplicableAllowRule("Test", start));
    }

    private static JsonObject Window(string start, string end) => new() { ["startsAtUtc"] = start, ["endsAtUtc"] = end };
    private static ReleaseApprovalPolicy Parse(string value) => ReleaseApprovalPolicy.Parse(Encoding.UTF8.GetBytes(value));
}
