using System.Text;
using Kordra.Api;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ReleaseRecoveryPlanTests
{
    private const string Json = """
        {"schemaVersion":1,"planId":"11111111-1111-1111-1111-111111111111","installationId":"22222222-2222-2222-2222-222222222222","bootId":"33333333-3333-3333-3333-333333333333","oldProcessId":1234,"tenantId":"44444444-4444-4444-4444-444444444444","principalId":"55555555-5555-5555-5555-555555555555","action":"freeze","resource":"/api/v1/projects/66666666-6666-6666-6666-666666666666/release-revisions/77777777-7777-7777-7777-777777777777/freeze","scopedKeyHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","businessHash":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","receiptHash":null,"auditHash":null}
        """;
    [Fact]
    public void CanonicalHashMatchesIndependentVectorAndChangesWhenEvidenceIsBound()
    {
        var plan = ReleaseRecoveryPlan.Parse(Encoding.UTF8.GetBytes(Json)); Assert.NotNull(plan);
        Assert.Equal("94a3595c1835d3948aa067149ba7449fa444f6b0abef660053c04c548669385a", plan.Hash());
        var bound = plan with { ReceiptHash = new string('c', 64), AuditHash = new string('d', 64) };
        Assert.NotEqual(plan.Hash(), bound.Hash()); Assert.Equal(bound, ReleaseRecoveryPlan.Parse(bound.CanonicalBytes()));
    }
    [Fact]
    public void RejectsUnknownDuplicateIncompleteAndInconsistentEvidence()
    {
        foreach (var text in new[] { Json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            Json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"token\":\"x\""),
            Json.Replace("\"auditHash\":null", "\"auditHash\":\"" + new string('c', 64) + "\""),
            Json.Replace("\"oldProcessId\":1234", "\"oldProcessId\":0"),
            Json.Replace("\"action\":\"freeze\"", "\"action\":\"retire\""),
            Json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1.0") })
            Assert.Null(ReleaseRecoveryPlan.Parse(Encoding.UTF8.GetBytes(text)));
        Assert.Null(ReleaseRecoveryPlan.Parse(new byte[16385]));
    }
}
