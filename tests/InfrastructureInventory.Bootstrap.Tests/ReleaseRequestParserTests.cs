using System.Text;
using System.Text.Json;
using Kordra.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ReleaseRequestParserTests
{
    private readonly Guid _project = Guid.NewGuid(), _revision = Guid.NewGuid(), _correlation = Guid.NewGuid();
    private byte[] Freeze(string hash) => JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = _correlation.ToString("D"), expectedSelectionHash = hash });
    private string Etag => $"\"rel-v1:{_revision:D}:3\"";
    [Fact]
    public void ParserBindsExactRevisionAndExcludesCorrelationFromBusinessIdentity()
    {
        var first = ReleaseRequestParser.Parse("freeze", _project.ToString("D"), _revision.ToString("D"), Freeze(new string('a', 64)), Etag);
        Assert.Equal(_correlation, first.CorrelationId); Assert.Equal(3, first.Command.ExpectedVersion);
        var second = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = Guid.NewGuid().ToString("D"), expectedSelectionHash = new string('a', 64) });
        Assert.Equal(first.Command.BusinessHash, ReleaseRequestParser.Parse("freeze", _project.ToString("D"), _revision.ToString("D"), second, Etag).Command.BusinessHash);
    }
    [Fact]
    public void ParsesOnlyTheApprovedCreateNextReplaceAndRetireShapes()
    {
        var application = Guid.NewGuid(); var environment = Guid.NewGuid(); var target = Guid.NewGuid(); var artifact = Guid.NewGuid();
        byte[] draft = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = _correlation.ToString("D"),
            label = "Frontend", applicationId = application.ToString("D"), environmentId = environment.ToString("D"),
            targetId = target.ToString("D"), artifactId = artifact.ToString("D") });
        var create = ReleaseRequestParser.Parse("create-draft", _project.ToString("D"), null, draft, StringValues.Empty);
        Assert.Equal("release.create", create.Command.Permission); Assert.Equal(application, create.Command.Selection!.ApplicationId);
        var replace = ReleaseRequestParser.Parse("replace-draft", _project.ToString("D"), _revision.ToString("D"), draft, Etag);
        Assert.Equal(3, replace.Command.ExpectedVersion);
        var nextBody = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = _correlation.ToString("D"),
            baseRevisionId = _revision.ToString("D"), label = "Frontend", applicationId = application.ToString("D"),
            environmentId = environment.ToString("D"), targetId = target.ToString("D"), artifactId = artifact.ToString("D") });
        Assert.Equal(_revision, ReleaseRequestParser.Parse("next-draft", _project.ToString("D"), Guid.NewGuid().ToString("D"), nextBody, StringValues.Empty).Command.BaseRevisionId);
        var retireBody = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = _correlation.ToString("D"), reasonCode = "obsolete" });
        Assert.Equal("release.retire", ReleaseRequestParser.Parse("retire", _project.ToString("D"), _revision.ToString("D"), retireBody, Etag).Command.Permission);
        Assert.Throws<FormatException>(() => ReleaseRequestParser.Parse("create-draft", _project.ToString("D"), null, draft, Etag));
    }

    [Fact]
    public void MissingOrForeignEtagIsAPreconditionFailure()
    {
        Assert.Throws<ReleasePreconditionException>(() => ReleaseRequestParser.Parse("freeze", _project.ToString("D"), _revision.ToString("D"), Freeze(new string('a', 64)), StringValues.Empty));
        Assert.Throws<ReleasePreconditionException>(() => ReleaseRequestParser.Parse("freeze", _project.ToString("D"), _revision.ToString("D"), Freeze(new string('a', 64)), $"\"rel-v1:{Guid.NewGuid():D}:3\""));
    }
    [Theory]
    [InlineData("*")]
    [InlineData("W/\"rel-v1:00000000-0000-0000-0000-000000000001:3\"")]
    [InlineData("\"rel-v1:00000000-0000-0000-0000-000000000001:03\"")]
    [InlineData("\"rel-v1:00000000-0000-0000-0000-000000000001:3\", \"rel-v1:00000000-0000-0000-0000-000000000001:4\"")]
    public void WeakWildcardAndMalformedEtagsAreRejected(string tag) => Assert.Throws<FormatException>(() =>
        ReleaseRequestParser.Parse("freeze", _project.ToString("D"), _revision.ToString("D"), Freeze(new string('a', 64)), tag));
    [Fact]
    public void RejectsDuplicateUnknownCoercedOrForeignAuthorityFields()
    {
        var valid = Encoding.UTF8.GetString(Freeze(new string('a', 64)));
        foreach (var body in new[] { valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"tenantId\":\"" + Guid.NewGuid() + "\""),
            valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1.0"), valid.Replace("\"correlationId\":\"", "\"correlationId\":\"INVALID") })
            Assert.Throws<FormatException>(() => ReleaseRequestParser.Parse("freeze", _project.ToString("D"), _revision.ToString("D"), Encoding.UTF8.GetBytes(body), Etag));
        Assert.Throws<FormatException>(() => ReleaseRequestParser.Parse("freeze", _project.ToString("D").ToUpperInvariant(), _revision.ToString("D"), Freeze(new string('a', 64)), Etag));
        Assert.Throws<FormatException>(() => ReleaseRequestParser.Parse("freeze", _project.ToString("D"), _revision.ToString("D"), Freeze(new string('a', 64)), new StringValues([Etag, Etag])));
    }
    [Fact]
    public async Task BodyAndPaginationAreBoundedAndFramingCannotConflict()
    {
        Assert.Equal(ReleaseRequestParser.MaximumBodyBytes, (await ReleaseRequestParser.ReadBodyAsync(new MemoryStream(new byte[ReleaseRequestParser.MaximumBodyBytes]))).Length);
        await Assert.ThrowsAsync<ReleaseBodyLimitException>(() => ReleaseRequestParser.ReadBodyAsync(new MemoryStream(new byte[ReleaseRequestParser.MaximumBodyBytes + 1])));
        var query = new QueryCollection(new Dictionary<string, StringValues> { ["limit"] = "01" });
        Assert.Throws<FormatException>(() => ReleaseRequestParser.ParsePage(query));
        var extra = new QueryCollection(new Dictionary<string, StringValues> { ["tenantId"] = Guid.NewGuid().ToString("D") });
        Assert.Throws<FormatException>(() => ReleaseRequestParser.ParsePage(extra));
        var http = new DefaultHttpContext(); http.Request.Headers["Content-Length"] = "1"; http.Request.Headers.TransferEncoding = "chunked";
        Assert.False(ReleaseRequestParser.ValidFraming(http.Request));
    }
}
