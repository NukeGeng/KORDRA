using System.Security.Cryptography;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Api;

/// <summary>One caller/project/body/status/deadline, committed only under current artifact D admission.</summary>
public sealed class ArtifactCallerBoundResponseCommitter(HttpContext context, SessionSelection subject,
    Guid projectId, IAuthorityClock clock) : IArtifactResponseCommitter
{
    private int _accepted;
    public async Task CommitAsync(ArtifactResponseCommitment selection, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        Validate(selection, ct);
        if (context.Response.HasStarted || bytes.Length > 65536 ||
            selection.PayloadSha256 != Convert.ToHexStringLower(SHA256.HashData(bytes.Span)) ||
            selection.StatusCode is not (200 or 201 or 404 or 409 or 422 or 503) ||
            Interlocked.CompareExchange(ref _accepted, 1, 0) != 0) throw Unavailable();
        context.Response.OnStarting(() => { Validate(selection, ct); return Task.CompletedTask; });
        context.Response.StatusCode = selection.StatusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.ContentLength = bytes.Length;
        await context.Response.StartAsync(ct);
        Validate(selection, ct);
        await context.Response.Body.WriteAsync(bytes, ct);
        await context.Response.Body.FlushAsync(ct);
    }
    private void Validate(ArtifactResponseCommitment selection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); context.RequestAborted.ThrowIfCancellationRequested();
        var current = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        var now = clock.GetTrustedUtcNow();
        if (selection is null || selection.ProjectId != projectId || selection.InvocationId == Guid.Empty ||
            selection.ValidUntilUtc == default || selection.ValidUntilUtc == DateTimeOffset.MaxValue ||
            selection.ValidUntilUtc.Offset != TimeSpan.Zero || now.Offset != TimeSpan.Zero || now >= selection.ValidUntilUtc ||
            current is null || current.PrincipalId != subject.PrincipalId || current.BootGeneration != subject.BootGeneration ||
            current.Reference != subject.Reference || !context.Request.IsHttps || context.Request.Host.Value != "localhost:5128") throw Unavailable();
    }
    private static InvalidOperationException Unavailable() => new("Artifact response cannot be handed to this caller.");
    public void Abort() => context.Abort();
}
