using System.Security.Cryptography;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Api;

/// <summary>One exact session, route, response hash, ETag and deadline for the current HTTP caller.</summary>
public sealed class ApprovalCallerBoundResponseCommitter(HttpContext context, SessionSelection subject,
    Guid environmentId, string route, IAuthorityClock clock, string expectedQuery = "") : IApprovalResponseCommitter
{
    private int _accepted;
    private long _lastUtcTicks = clock.GetTrustedUtcNow().UtcTicks;
    public async Task CommitAsync(ApprovalResponseCommitment selection, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        Validate(selection, ct);
        if (context.Response.HasStarted || selection.MaximumBytes is not (64 * 1024 or 96 * 1024) || bytes.Length > selection.MaximumBytes ||
            selection.PayloadSha256 != Convert.ToHexStringLower(SHA256.HashData(bytes.Span)) ||
            selection.StatusCode is not (200 or 201 or 404) ||
            Interlocked.CompareExchange(ref _accepted, 1, 0) != 0) throw Unavailable();
        context.Response.OnStarting(() => { Validate(selection, ct); return Task.CompletedTask; });
        context.Response.StatusCode = selection.StatusCode; context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.Pragma = "no-cache";
        if (selection.Etag is not null) context.Response.Headers.ETag = selection.Etag;
        context.Response.ContentLength = bytes.Length;
        await context.Response.StartAsync(ct); Validate(selection, ct);
        await context.Response.Body.WriteAsync(bytes, ct); await context.Response.Body.FlushAsync(ct);
    }
    private void Validate(ApprovalResponseCommitment selection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); context.RequestAborted.ThrowIfCancellationRequested();
        var current = LocalAuthenticationEndpoints.CurrentSessionSelection(context); var now = clock.GetTrustedUtcNow();
        var previous = Interlocked.Read(ref _lastUtcTicks);
        if (now == default || now == DateTimeOffset.MaxValue || now.UtcTicks < previous ||
            Interlocked.CompareExchange(ref _lastUtcTicks, now.UtcTicks, previous) != previous) throw Unavailable();
        if (selection is null || selection.EnvironmentId != environmentId || selection.InvocationId == Guid.Empty ||
            selection.ValidUntilUtc == default || selection.ValidUntilUtc == DateTimeOffset.MaxValue ||
            selection.ValidUntilUtc.Offset != TimeSpan.Zero || now.Offset != TimeSpan.Zero || now >= selection.ValidUntilUtc ||
            current is null || current.PrincipalId != subject.PrincipalId || current.BootGeneration != subject.BootGeneration ||
            current.Reference != subject.Reference || !context.Request.IsHttps || context.Request.Host.Value != "localhost:5128" ||
            context.Request.Path.Value != route || (context.Request.QueryString.Value ?? "") != expectedQuery) throw Unavailable();
    }
    private static InvalidOperationException Unavailable() => new("Approval response cannot be handed to this caller.");
    public void Abort() => context.Abort();
}
