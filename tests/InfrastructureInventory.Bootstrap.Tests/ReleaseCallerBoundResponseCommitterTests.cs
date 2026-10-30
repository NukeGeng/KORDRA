using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Kordra.Api;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ReleaseCallerBoundResponseCommitterTests
{
    private readonly Guid _project = Guid.NewGuid(), _principal = Guid.NewGuid(), _boot = Guid.NewGuid();
    private readonly string _reference = new('a', 43);
    private readonly Clock _clock = new();
    private const string Route = "/api/v1/projects/synthetic/releases";
    private SessionSelection Subject => new(_principal, _reference, _boot);
    private DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext(); context.Request.Scheme = "https"; context.Request.Host = new("localhost", 5128);
        context.Request.Path = Route; context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("kordra:principal-reference", _principal.ToString("D")), new Claim("kordra:session-reference", _reference),
            new Claim("kordra:boot-generation", _boot.ToString("D")), new Claim("kordra:authenticated-at", "1")], "test"));
        return context;
    }
    private ReleaseResponseCommitment Commitment(byte[] body) => new(Guid.NewGuid(), _project,
        Convert.ToHexStringLower(SHA256.HashData(body)), 201, $"\"rel-v1:{Guid.NewGuid():D}:1\"", _clock.Now.AddSeconds(5));
    [Fact]
    public async Task WritesExactResponseOnceToTheBoundSessionAndRoute()
    {
        var context = Context(); var body = Encoding.UTF8.GetBytes("{\"ok\":true}"); var selection = Commitment(body);
        var committer = new ReleaseCallerBoundResponseCommitter(context, Subject, _project, Route, _clock);
        await committer.CommitAsync(selection, body, default);
        Assert.Equal(201, context.Response.StatusCode); Assert.Equal(selection.Etag, context.Response.Headers.ETag.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => committer.CommitAsync(selection, body, default));
    }
    [Theory]
    [InlineData("host")]
    [InlineData("scheme")]
    [InlineData("path")]
    [InlineData("query")]
    [InlineData("subject")]
    [InlineData("expired")]
    [InlineData("hash")]
    public async Task RejectsWrongCallerRouteDeadlineOrBytesBeforeHandoff(string fault)
    {
        var context = Context(); var body = Encoding.UTF8.GetBytes("{}"); var selection = Commitment(body); var subject = Subject;
        switch (fault)
        {
            case "host": context.Request.Host = new("example.test", 5128); break;
            case "scheme": context.Request.Scheme = "http"; break;
            case "path": context.Request.Path = "/different"; break;
            case "query": context.Request.QueryString = new("?tenant=x"); break;
            case "subject": subject = new(Guid.NewGuid(), _reference, _boot); break;
            case "expired": _clock.Now = selection.ValidUntilUtc; break;
            case "hash": body = Encoding.UTF8.GetBytes("{\"different\":true}"); break;
        }
        var committer = new ReleaseCallerBoundResponseCommitter(context, subject, _project, Route, _clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => committer.CommitAsync(selection, body, default));
        Assert.Equal(0, context.Response.Body.Length);
    }
    private sealed class Clock : IAuthorityClock
    {
        public DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset GetTrustedUtcNow() => Now;
    }
}
