using System.Text;
using Kordra.Api;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ApprovalRecoveryPlanTests
{
    private const string Json = """{"action":"grant","auditHash":null,"bootId":"33333333-3333-3333-3333-333333333333","businessHash":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","installationId":"22222222-2222-2222-2222-222222222222","oldProcessId":1234,"planId":"11111111-1111-1111-1111-111111111111","principalId":"55555555-5555-5555-5555-555555555555","receiptHash":null,"resource":"/api/v1/environments/66666666-6666-6666-6666-666666666666/release-approvals/77777777-7777-7777-7777-777777777777/grant","schemaVersion":1,"scopedKeyHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","tenantId":"44444444-4444-4444-4444-444444444444"}""";
    [Fact]
    public void CanonicalPlanMatchesIndependentVectorAndRejectsCrossOwnerRecovery()
    {
        var bytes = Encoding.UTF8.GetBytes(Json);
        var plan = ApprovalRecoveryPlan.Parse(bytes); Assert.NotNull(plan);
        Assert.Equal("480c54caf3c9aaf6c9797460ec8133defff53b18d7cffb2d717dd92801622271", plan.Hash());
        Assert.Null(ReleaseRecoveryPlan.Parse(bytes));
        var release = Json.Replace("grant", "freeze", StringComparison.Ordinal).Replace("environments", "projects", StringComparison.Ordinal)
            .Replace("release-approvals", "release-revisions", StringComparison.Ordinal);
        Assert.NotNull(ReleaseRecoveryPlan.Parse(Encoding.UTF8.GetBytes(release)));
        Assert.Null(ApprovalRecoveryPlan.Parse(Encoding.UTF8.GetBytes(release)));
        var bound = plan with { ReceiptHash = new string('c', 64), AuditHash = new string('d', 64) };
        Assert.NotEqual(plan.Hash(), bound.Hash()); Assert.Equal(bound, ApprovalRecoveryPlan.Parse(bound.CanonicalBytes()));
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("unpaired")]
    [InlineData("numeric")]
    public void MalformedRecoverySelectionCannotBeCertified(string fault)
    {
        var text = fault switch
        {
            "duplicate" => Json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            "unknown" => Json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"token\":\"x\""),
            "unpaired" => Json.Replace("\"receiptHash\":null", "\"receiptHash\":\"" + new string('c',64) + "\""),
            _ => Json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1.0")
        };
        Assert.Null(ApprovalRecoveryPlan.Parse(Encoding.UTF8.GetBytes(text)));
    }
}
