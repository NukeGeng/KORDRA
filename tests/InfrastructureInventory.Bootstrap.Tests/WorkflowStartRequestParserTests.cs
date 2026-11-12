using System.Text;
using Kordra.Api;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowStartRequestParserTests
{
    private static readonly Guid EnvironmentId = Guid.NewGuid();
    private static readonly Guid ReleaseId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private const string Deadline = "2026-11-06T18:00:00.0000000Z";
    private static readonly string Valid =
        $$"""{"schemaVersion":1,"releaseId":"{{ReleaseId:D}}","releaseRevision":2,"releaseHash":"{{new string('a',64)}}","templateId":"pilot.checkpoint.v1","templateVersion":1,"originalDeadline":"{{Deadline}}","requestId":"{{RequestId:D}}"}""";

    [Fact]
    public void ReadsOnlyBusinessFieldsAndRedactsKey()
    {
        var parsed = WorkflowStartRequestParser.Parse(EnvironmentId.ToString("D"), Encoding.UTF8.GetBytes(Valid), "fixture-key");
        Assert.Equal((EnvironmentId, ReleaseId, 2L, RequestId), (parsed.EnvironmentId, parsed.ReleaseId, parsed.ReleaseRevision, parsed.RequestId));
        Assert.DoesNotContain("fixture-key", parsed.ToString());
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("duplicate")]
    [InlineData("template")]
    [InlineData("utc")]
    [InlineData("multi-key")]
    [InlineData("combined-key")]
    public void RejectsUnknownAuthorityFieldsAndAmbiguousInput(string fault)
    {
        var body = fault switch
        {
            "scope" => Valid.Insert(1, "\"tenantId\":\"untrusted\","),
            "duplicate" => Valid.Insert(1, "\"schemaVersion\":1,"),
            "template" => Valid.Replace("pilot.checkpoint.v1", "deploy", StringComparison.Ordinal),
            "utc" => Valid.Replace(Deadline, "2026-11-06T18:00:00.0000000+00:00", StringComparison.Ordinal),
            _ => Valid
        };
        string[] keys = fault switch
        {
            "multi-key" => ["first", "second"],
            "combined-key" => ["first,second"],
            _ => ["fixture-key"]
        };
        Assert.Throws<FormatException>(() =>
            WorkflowStartRequestParser.Parse(EnvironmentId.ToString("D"), Encoding.UTF8.GetBytes(body), keys));
    }
}
