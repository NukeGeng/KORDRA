using System.Net;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class ProtectedReadHttpTests
{
    [Fact]
    public async Task Real_cookie_current_session_and_durable_UD_precede_one_actual_HTTPS_body()
    {
        var facts = new Facts();
        await using var host = await LocalAuthenticationTestHost.StartAsync(afterAuthentication: facts.Handle);
        var user = host.NewUserName(); Assert.Equal(LocalAuthenticationStatus.Success, (await host.EnrollAsync(user)).Status);
        using var client = host.CreateClient(); using var login = await host.LoginAsync(client, user, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var request = client.GetAsync("/test/protected", HttpCompletionOption.ResponseHeadersRead);
        await facts.ProviderStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(request.IsCompleted); Assert.Equal([AuthorityStage.Use], facts.Intents.Select(x => x.Stage));
        facts.Result.TrySetResult(new("{\"synthetic\":true}"u8, 1));
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"synthetic\":true}", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal([AuthorityStage.Use, AuthorityStage.Disclosure], facts.Intents.Select(x => x.Stage));
        Assert.NotEqual(facts.Intents[0].IntentId, facts.Intents[1].IntentId);
        Assert.Equal(1, facts.Reads); Assert.Equal(1, facts.Handoffs);
        Assert.NotNull(facts.Intents[1].ResultDigest);
    }

    [Fact]
    public async Task Pending_writer_rebind_between_U_and_D_suppresses_actual_HTTP_output()
    {
        var facts = new Facts();
        await using var host = await LocalAuthenticationTestHost.StartAsync(afterAuthentication: facts.Handle);
        var user = host.NewUserName(); await host.EnrollAsync(user);
        using var client = host.CreateClient(); using var login = await host.LoginAsync(client, user, LocalAuthenticationTestHost.ValidPassword);
        var request = client.GetAsync("/test/protected", HttpCompletionOption.ResponseHeadersRead);
        await facts.ProviderStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await using (var writer = await facts.Gate!.AcquireWriteAsync())
        {
            facts.BindingRevision++;
            facts.Result.TrySetResult(new("{\"neverDisclose\":true}"u8, 1));
            await Task.Yield(); Assert.False(request.IsCompleted);
        }
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Single(facts.Intents); Assert.Equal(0, facts.Handoffs);
    }

    [Fact]
    public async Task Anonymous_request_never_reaches_adapter_or_protected_output()
    {
        var facts = new Facts();
        await using var host = await LocalAuthenticationTestHost.StartAsync(afterAuthentication: facts.Handle);
        using var client = host.CreateClient(); using var response = await client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync()); Assert.Equal(0, facts.Reads); Assert.Empty(facts.Intents);
    }

    [Fact]
    public async Task Actual_HTTP_write_retains_D_admission_until_transport_unwinds_and_then_writer_progresses()
    {
        var facts = new Facts { BlockTransport = true };
        await using var host = await LocalAuthenticationTestHost.StartAsync(afterAuthentication: facts.Handle);
        var user = host.NewUserName(); await host.EnrollAsync(user);
        using var client = host.CreateClient(); using var login = await host.LoginAsync(client, user, LocalAuthenticationTestHost.ValidPassword);
        var request = client.GetAsync("/test/protected", HttpCompletionOption.ResponseHeadersRead);
        await facts.ProviderStarted.Task.WaitAsync(TimeSpan.FromSeconds(3)); facts.Result.TrySetResult(new("{\"bounded\":true}"u8, 1));
        await facts.TransportStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var writer = facts.Gate!.AcquireWriteAsync().AsTask(); Assert.False(writer.IsCompleted);
        facts.TransportRelease.TrySetResult();
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"bounded\":true}", await response.Content.ReadAsStringAsync());
        await using var admittedWriter = await writer.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, facts.Reads); Assert.Equal(1, facts.Handoffs); Assert.Equal(2, facts.Intents.Count);
    }

    [Fact]
    public async Task Actual_partial_HTTP_delivery_aborts_without_a_second_admission_or_response_retry()
    {
        var facts = new Facts { FailPartialTransport = true };
        await using var host = await LocalAuthenticationTestHost.StartAsync(afterAuthentication: facts.Handle);
        var user = host.NewUserName(); await host.EnrollAsync(user);
        using var client = host.CreateClient(); using var login = await host.LoginAsync(client, user, LocalAuthenticationTestHost.ValidPassword);
        var reading = Record.ExceptionAsync(async () =>
        { using var response = await client.GetAsync("/test/protected"); await response.Content.ReadAsStringAsync(); });
        await facts.ProviderStarted.Task.WaitAsync(TimeSpan.FromSeconds(3)); facts.Result.TrySetResult(new("{\"partial\":true}"u8, 1));
        Assert.NotNull(await reading.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, facts.Reads); Assert.Equal(1, facts.Handoffs); Assert.Equal(2, facts.Intents.Count);
    }

    // Synthetic owner facts exist only here. Session, audit, clock, gate, HTTP and cookie code are real.
    private sealed class Facts : IScopedGrantReader, IRoleDefinitionReader, ITargetAuthorityReader, IResourcePolicyReader, IProtectedTargetRead, IAuthorityCommitter
    {
        public readonly TaskCompletionSource ProviderStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<PrivateReadBuffer> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<AuthorityCommitment> Intents = [];
        public FairAsyncReaderWriterAdmissionGate? Gate;
        public long BindingRevision = 1;
        public int Reads, Handoffs;
        public bool BlockTransport, FailPartialTransport;
        public readonly TaskCompletionSource TransportStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource TransportRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Guid _target = Guid.NewGuid(), _tenant = Guid.NewGuid(), _project = Guid.NewGuid(), _definition = Guid.NewGuid(),
            _assignment = Guid.NewGuid(), _member = Guid.NewGuid(), _policy = Guid.NewGuid(), _binding = Guid.NewGuid();
        private LocalAuthenticationService _authentication = null!;
        public async Task Handle(HttpContext context, RequestDelegate next)
        {
            if (context.Request.Path != "/test/protected") { await next(context); return; }
            var session = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
            if (session is null) { context.Response.StatusCode = 401; return; }
            _authentication = context.RequestServices.GetRequiredService<LocalAuthenticationService>();
            Gate = context.RequestServices.GetRequiredService<FairAsyncReaderWriterAdmissionGate>();
            if (BlockTransport || FailPartialTransport) context.Response.Body = new InstrumentedTransport(context.Response.Body, this);
            var enforcer = new ProtectedReadAdmission(Gate, _authentication, this, this, this, this, this, _authentication);
            var response = new ObservedResponse(new CallerBoundResponseCommitter(context, session, _target), this);
            var status = await enforcer.ExecuteAsync(session, _target, this, response, context.RequestAborted);
            if (!context.Response.HasStarted && status != ProtectedReadStatus.Completed) context.Response.StatusCode = status == ProtectedReadStatus.Denied ? 403 : 503;
        }
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission, string scopeKind, Guid scopeId, CancellationToken ct = default) =>
            Task.FromResult<ScopedGrant?>(new(principal, _tenant, _member, 1, 1, _assignment, 1, _definition, 1, "target.read", "project", _project, null));
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken cancellationToken = default) =>
            Task.FromResult<RoleDefinitionSnapshot?>(new(_definition, 1, "fixture", [new("target.read", "project")]));
        public Task<bool> IsActivePrincipalAsync(Guid principal, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<TargetAuthority?> ReadTargetUnderAdmissionAsync(Guid target, CancellationToken ct = default) => Task.FromResult<TargetAuthority?>(new(_target, _tenant, _project, 1, 1, 1, null, null, null));
        public Task<ResourcePolicyAuthority?> ReadPolicyUnderAdmissionAsync(TargetAuthority target, string permission, CancellationToken ct = default) =>
            Task.FromResult<ResourcePolicyAuthority?>(new(_tenant, _project, null, "target.read", 1, _policy, _binding, BindingRevision,
                new string('a', 64), new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero), null, ResourceEligibility.Allow));
        public Task<PrivateReadBuffer> Admit(TypedReadSelection selection, CancellationToken ct)
        { Interlocked.Increment(ref Reads); ProviderStarted.TrySetResult(); return Result.Task.WaitAsync(ct); }
        public async Task<AuthorityCommitStatus> CommitUnderAdmissionAsync(AuthorityCommitment c, CancellationToken ct = default)
        { var outcome = await _authentication.CommitUnderAdmissionAsync(c, ct); if (outcome == AuthorityCommitStatus.Durable) Intents.Add(c); return outcome; }
        private sealed class ObservedResponse(ICallerBoundResponseCommitter real, Facts facts) : ICallerBoundResponseCommitter
        {
            public Task CommitAsync(TypedReadSelection selection, ReadOnlyMemory<byte> buffer, CancellationToken ct)
            { Assert.Equal(AuthorityStage.Disclosure, facts.Intents.Last().Stage); Interlocked.Increment(ref facts.Handoffs); return real.CommitAsync(selection, buffer, ct); }
            public void Abort() => real.Abort();
        }
        private sealed class InstrumentedTransport(Stream inner, Facts facts) : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() => throw new NotSupportedException();
            public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                facts.TransportStarted.TrySetResult();
                if (facts.BlockTransport) await facts.TransportRelease.Task.WaitAsync(cancellationToken);
                if (facts.FailPartialTransport)
                {
                    await inner.WriteAsync(buffer[..1], cancellationToken); await inner.FlushAsync(cancellationToken);
                    throw new IOException("Synthetic failure after an actual partial HTTP write.");
                }
                await inner.WriteAsync(buffer, cancellationToken);
            }
        }
    }
}
