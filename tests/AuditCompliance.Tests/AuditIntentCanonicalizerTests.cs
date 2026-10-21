using System.Security.Cryptography;
using System.Text;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Xunit;

namespace Kordra.Modules.AuditCompliance.Tests;

public sealed class AuditIntentCanonicalizerTests
{
    [Fact]
    public void Canonical_hash_is_stable_across_case_and_timezone_representations()
    {
        var instant = new DateTimeOffset(2026, 10, 21, 10, 30, 0, TimeSpan.Zero);
        var first = CreateIntent() with
        {
            Permission = "Deployment.Execute",
            ScopeKind = "Target",
            SafeReason = "Approved",
            OccurredAtUtc = instant
        };
        var equivalent = first with
        {
            Permission = "deployment.execute",
            ScopeKind = "target",
            SafeReason = "approved",
            OccurredAtUtc = instant.ToOffset(TimeSpan.FromHours(7))
        };

        Assert.Equal(AuditIntentCanonicalizer.ComputeHash(first), AuditIntentCanonicalizer.ComputeHash(equivalent));
    }

    [Fact]
    public void Global_self_scoped_intent_can_omit_tenant_resource_and_scope_ids()
    {
        var intent = CreateIntent() with
        {
            SubjectRef = null,
            TenantRef = null,
            ResourceRef = null,
            ScopeKind = "global",
            ScopeId = null,
            PolicyRevision = null,
            AuthorityRevision = null
        };

        Assert.Matches("^[0-9a-f]{64}$", AuditIntentCanonicalizer.ComputeHash(intent));
    }

    [Fact]
    public void Optional_references_reject_empty_guids_when_present()
    {
        var invalid = CreateIntent() with { TenantRef = Guid.Empty };

        Assert.Throws<ArgumentException>(() => AuditIntentCanonicalizer.ComputeHash(invalid));
    }

    [Fact]
    public void Disclosure_hashes_only_a_digest_and_size_not_the_result_payload()
    {
        const string payload = "synthetic-result-secret-marker-must-never-be-persisted";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var intent = CreateIntent() with
        {
            Stage = AuditIntentStage.Disclosure,
            ResultDigest = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant(),
            ResultSizeBytes = payloadBytes.Length
        };

        var canonical = AuditIntentCanonicalizer.ComputeHash(intent);
        Assert.Matches("^[0-9a-f]{64}$", canonical);
        Assert.DoesNotContain(payload, canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_safe_reason_fails_without_echoing_input()
    {
        const string secretMarker = "credential=synthetic-secret-marker";
        var invalid = CreateIntent() with { SafeReason = secretMarker };

        var exception = Assert.Throws<ArgumentException>(() => AuditIntentCanonicalizer.ComputeHash(invalid));

        Assert.DoesNotContain(secretMarker, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("not-a-sha256", 2)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", -1)]
    public void Disclosure_requires_a_valid_digest_and_nonnegative_size(string? digest, long size)
    {
        var invalid = CreateIntent() with
        {
            Stage = AuditIntentStage.Disclosure,
            ResultDigest = digest,
            ResultSizeBytes = size
        };

        Assert.Throws<ArgumentException>(() => AuditIntentCanonicalizer.ComputeHash(invalid));
    }

    [Fact]
    public void Non_disclosure_stages_reject_result_metadata()
    {
        var invalid = CreateIntent() with
        {
            ResultDigest = new string('a', 64),
            ResultSizeBytes = 1
        };

        Assert.Throws<ArgumentException>(() => AuditIntentCanonicalizer.ComputeHash(invalid));
    }

    [Fact]
    public void Stage_outcome_is_recorded_separately_and_is_required_only_for_outcome_stage()
    {
        var denied = CreateIntent() with
        {
            Stage = AuditIntentStage.Outcome,
            ActionOutcome = AuditActionOutcome.Denied
        };
        var unknown = denied with { ActionOutcome = AuditActionOutcome.Unknown };

        Assert.NotEqual(AuditIntentCanonicalizer.ComputeHash(denied), AuditIntentCanonicalizer.ComputeHash(unknown));
        Assert.Throws<ArgumentException>(() => AuditIntentCanonicalizer.ComputeHash(denied with { ActionOutcome = null }));
        Assert.Throws<ArgumentException>(() => AuditIntentCanonicalizer.ComputeHash(
            CreateIntent() with { ActionOutcome = AuditActionOutcome.Denied }));
    }

    internal static AuditIntent CreateIntent(Guid? intentId = null) => new(
        SchemaVersion: 1,
        IntentId: intentId ?? Guid.NewGuid(),
        InvocationId: Guid.NewGuid(),
        Stage: AuditIntentStage.Use,
        SubjectRef: Guid.NewGuid(),
        TenantRef: Guid.NewGuid(),
        ResourceRef: Guid.NewGuid(),
        Permission: "target.read",
        ScopeKind: "target",
        ScopeId: Guid.NewGuid(),
        PolicyRevision: 1,
        AuthorityRevision: 1,
        SafeReason: "permitted",
        OccurredAtUtc: DateTimeOffset.UtcNow,
        CorrelationId: Guid.NewGuid());
}
