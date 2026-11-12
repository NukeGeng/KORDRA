using System.Security.Claims;
using System.Security.Cryptography;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowCallerBoundResponseCommitterTests
{
    private const string Path = "/api/v1/workflows/synthetic";
    private const string Query = "?beforeVersion=50";
    private readonly SessionSelection session = new(Guid.NewGuid(), new('a', 43), Guid.NewGuid());
    private readonly WorkflowReadScope scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private readonly Clock clock = new();
    private readonly FairAsyncReaderWriterAdmissionGate gate = new();

    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    public async Task CommitsOnlyOnceAndDoesNotCache(int status)
    {
        var http = CreateContext();
        var payload = "{}"u8.ToArray();
        var commitment = CommitTo(payload, status);
        var response = CreateCommitter(http);

        await response.CommitAsync(commitment, payload, default);

        Assert.Equal(payload, ((MemoryStream)http.Response.Body).ToArray());
        Assert.Equal(status, http.Response.StatusCode);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => response.CommitAsync(commitment, payload, default));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("instance")]
    [InlineData("cursor")]
    [InlineData("query")]
    [InlineData("expiry")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("oversize")]
    [InlineData("closed")]
    [InlineData("session")]
    public async Task RejectsMismatchedOrExpiredHandoffWithoutWritingBytes(string fault)
    {
        var http = CreateContext();
        var payload = fault == "oversize" ? new byte[256 * 1024 + 1] : "{}"u8.ToArray();
        var commitment = CommitTo(payload, 200);
        switch (fault)
        {
            case "tenant": commitment = commitment with { Scope = scope with { TenantId = Guid.NewGuid() } }; break;
            case "instance": commitment = commitment with { Scope = scope with { InstanceId = Guid.NewGuid() } }; break;
            case "cursor": commitment = commitment with { BeforeVersion = 51 }; break;
            case "query": http.Request.QueryString = new("?beforeVersion=51"); break;
            case "expiry": clock.Now = commitment.ValidUntilUtc; break;
            case "hash": commitment = commitment with { PayloadSha256 = new('0', 64) }; break;
            case "size": commitment = commitment with { PayloadSize = 1 }; break;
            case "closed": gate.Close(); break;
            case "session": http.User = Principal(new(session.PrincipalId, new('b', 43), session.BootGeneration)); break;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateCommitter(http).CommitAsync(commitment, payload, default));
        Assert.Equal(0, http.Response.Body.Length);
    }

    private WorkflowReadCommitment CommitTo(byte[] payload, int status) => new(Guid.NewGuid(), scope, 50,
        Convert.ToHexStringLower(SHA256.HashData(payload)), payload.Length, status, clock.Now.AddSeconds(2));
    private WorkflowCallerBoundResponseCommitter CreateCommitter(HttpContext http) =>
        new(http, session, scope, 50, Path, Query, clock, gate);

    private DefaultHttpContext CreateContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new("localhost", 5128);
        http.Request.Path = Path;
        http.Request.QueryString = new(Query);
        http.User = Principal(session);
        http.Response.Body = new MemoryStream();
        return http;
    }

    private static ClaimsPrincipal Principal(SessionSelection selection) => new(new ClaimsIdentity([
        new Claim("kordra:principal-reference", selection.PrincipalId.ToString("D")),
        new Claim("kordra:session-reference", selection.Reference),
        new Claim("kordra:boot-generation", selection.BootGeneration.ToString("D")),
        new Claim("kordra:authenticated-at", "1")], "test"));

    private sealed class Clock : IAuthorityClock
    {
        public DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset GetTrustedUtcNow() => Now;
    }
}
