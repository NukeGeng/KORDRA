using System.Text;
using Kordra.Api;
using Kordra.ReleaseManagement.Domain;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ApprovalRequestParserTests
{
    private const string Environment = "44444444-4444-4444-4444-444444444444";
    private const string Request = "55555555-5555-5555-5555-555555555555";
    private const string Correlation = "66666666-6666-6666-6666-666666666666";
    private static byte[] Body(string field) => Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"correlationId\":\"" + Correlation + "\"," + field + "}");
    private static string Etag => "\"apr-v1:" + Request + ":2\"";
    [Fact]
    public void RequestAndDecisionUseClosedBodiesAndCanonicalApprovalRoutes()
    {
        var parsed = ApprovalRequestParser.Parse("request", Environment, null, Body("\"releaseRevisionId\":\"" + Request + "\""), StringValues.Empty);
        Assert.Null(parsed.RequestId); Assert.Null(parsed.ExpectedVersion); Assert.Equal(Guid.Parse(Request), parsed.ReleaseRevisionId);
        Assert.Equal("5d970c1bfe4affb9cac28ebce8451f3f136493685552bca47e6bf1549db57cc3", parsed.BusinessHash);
        Assert.Equal("/api/v1/environments/" + Environment + "/release-approvals", parsed.Resource);
        foreach (var pair in new[] { ("grant", "approved"), ("reject", "changes_required"), ("revoke", "owner_cancelled") })
        {
            var decision = ApprovalRequestParser.Parse(pair.Item1, Environment, Request, Body("\"reasonCode\":\"" + pair.Item2 + "\""), Etag);
            Assert.Equal(2, decision.ExpectedVersion); Assert.EndsWith("/" + Request + "/" + pair.Item1, decision.Resource);
            if (pair.Item1 == "grant") Assert.Equal("b571fea77a4eb9a3585ffefd1604fa3dd9ec1324faff98c9e57a328f5c92849c", decision.BusinessHash);
            Assert.Equal(64, ReleaseCommandHash.ScopedKey(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), pair.Item1, decision.Resource, "synthetic-key-1234").Length);
        }
    }
    [Theory]
    [InlineData("grant", "owner_cancelled")]
    [InlineData("reject", "approved")]
    [InlineData("revoke", "incorrect_artifact")]
    public void RejectsReasonFromAnotherOperation(string action, string reason) => Assert.Throws<FormatException>(() =>
        ApprovalRequestParser.Parse(action, Environment, Request, Body("\"reasonCode\":\"" + reason + "\""), Etag));
    [Fact]
    public void RejectsUnknownDuplicateFieldsWrongVersionsAndMissingPreconditions()
    {
        var body = Body("\"reasonCode\":\"approved\"");
        Assert.Throws<ReleasePreconditionException>(() => ApprovalRequestParser.Parse("grant", Environment, Request, body, StringValues.Empty));
        Assert.Throws<ReleasePreconditionException>(() => ApprovalRequestParser.Parse("grant", Environment, Request, body, "\"apr-v1:" + Environment + ":2\""));
        foreach (var etag in new[] { "W/" + Etag, Etag.Replace(":2", ":02"), Etag.Replace("apr-v1", "rel-v1"), Etag.Replace(":2", ":0") })
            Assert.Throws<FormatException>(() => ApprovalRequestParser.Parse("grant", Environment, Request, body, etag));
        foreach (var fields in new[] { "\"reasonCode\":\"approved\",\"tenantId\":\"x\"", "\"reasonCode\":\"approved\",\"reasonCode\":\"approved\"" })
            Assert.Throws<FormatException>(() => ApprovalRequestParser.Parse("grant", Environment, Request, Body(fields), Etag));
        Assert.Throws<ReleaseBodyLimitException>(() => ApprovalRequestParser.Parse("grant", Environment, Request, new byte[16385], Etag));
    }
}
