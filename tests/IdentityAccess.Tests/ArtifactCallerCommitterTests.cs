using System.IO.Pipelines;
using System.Security.Claims;
using System.Security.Cryptography;
using Kordra.Api;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class ArtifactCallerCommitterTests
{
    [Fact]
    public async Task Exact_body_status_caller_and_deadline_are_one_use()
    {
        var f = new Fixture();
        await f.Committer.CommitAsync(f.Selection, f.Bytes, default);
        Assert.Equal(f.Bytes, f.Body.Stream.ToArray()); Assert.True(f.Response.HasStarted);
        Assert.Equal("no-store", f.Context.Response.Headers.CacheControl.ToString());
        Assert.Equal(f.Bytes.Length, f.Context.Response.ContentLength);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Committer.CommitAsync(f.Selection, f.Bytes, default));
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("project")]
    [InlineData("body")]
    [InlineData("session")]
    [InlineData("boot")]
    [InlineData("host")]
    [InlineData("http")]
    [InlineData("status")]
    public async Task Mismatched_handoff_never_starts_headers_or_writes_body(string fault)
    {
        var f = new Fixture(); var selected = f.Selection;
        switch (fault)
        {
            case "expiry": f.Clock.Now = selected.ValidUntilUtc; break;
            case "project": selected = selected with { ProjectId = Guid.NewGuid() }; break;
            case "body": selected = selected with { PayloadSha256 = new string('0', 64) }; break;
            case "session": f.Context.User = f.Principal(new string('b', 43), f.Subject.BootGeneration); break;
            case "boot": f.Context.User = f.Principal(f.Subject.Reference, Guid.NewGuid()); break;
            case "host": f.Context.Request.Host = new HostString("other.localhost:5128"); break;
            case "http": f.Context.Request.Scheme = "http"; break;
            case "status": selected = selected with { StatusCode = 302 }; break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Committer.CommitAsync(selected, f.Bytes, default));
        Assert.False(f.Response.HasStarted); Assert.Equal(0, f.Body.Stream.Length);
    }

    [Fact]
    public async Task Expiry_in_the_preheader_callback_withholds_the_body()
    {
        var f = new Fixture(); f.Body.BeforeHeaders = () => f.Clock.Now = f.Selection.ValidUntilUtc;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Committer.CommitAsync(f.Selection, f.Bytes, default));
        Assert.False(f.Response.HasStarted); Assert.Equal(0, f.Body.Stream.Length);
    }

    private sealed class Fixture
    {
        public readonly Clock Clock = new();
        public readonly DefaultHttpContext Context = new();
        public readonly ResponseFeature Response = new();
        public readonly BodyFeature Body;
        public readonly SessionSelection Subject = new(Guid.NewGuid(), new string('a', 43), Guid.NewGuid());
        public readonly byte[] Bytes = "{\"schemaVersion\":1}"u8.ToArray();
        public ArtifactResponseCommitment Selection { get; }
        public ArtifactCallerBoundResponseCommitter Committer { get; }
        public Fixture()
        {
            Body = new(Response); Context.Features.Set<IHttpResponseFeature>(Response); Context.Features.Set<IHttpResponseBodyFeature>(Body);
            Context.Request.Scheme = "https"; Context.Request.Host = new("localhost:5128");
            Context.User = Principal(Subject.Reference, Subject.BootGeneration);
            var project = Guid.NewGuid(); Selection = new(Guid.NewGuid(), project,
                Convert.ToHexStringLower(SHA256.HashData(Bytes)), 200, Clock.Now.AddSeconds(1));
            Committer = new(Context, Subject, project, Clock);
        }
        public ClaimsPrincipal Principal(string reference, Guid boot) => new(new ClaimsIdentity(new[]
        {
            new Claim("kordra:principal-reference", Subject.PrincipalId.ToString("D")),
            new Claim("kordra:session-reference", reference), new Claim("kordra:boot-generation", boot.ToString("D")),
            new Claim("kordra:authenticated-at", "1")
        }, "Synthetic test only"));
    }
    private sealed class Clock : IAuthorityClock
    { public DateTimeOffset Now = DateTimeOffset.UtcNow; public DateTimeOffset GetTrustedUtcNow() => Now; }
    private sealed class ResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; private set; }
        public void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) { }
        public async Task Start()
        { foreach (var entry in _starting.AsEnumerable().Reverse()) await entry.Callback(entry.State); HasStarted = true; }
    }
    private sealed class BodyFeature(ResponseFeature response) : IHttpResponseBodyFeature
    {
        public MemoryStream Stream { get; } = new();
        Stream IHttpResponseBodyFeature.Stream => Stream;
        public PipeWriter Writer => PipeWriter.Create(Stream, new StreamPipeWriterOptions(leaveOpen: true));
        public Action? BeforeHeaders;
        public void DisableBuffering() { }
        public Task StartAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); BeforeHeaders?.Invoke(); return response.Start(); }
        public Task SendFileAsync(string path, long offset, long? count, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CompleteAsync() => Task.CompletedTask;
    }
}
