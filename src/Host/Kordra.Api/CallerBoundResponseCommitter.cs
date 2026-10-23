using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Api;

/// <summary>One HTTP caller, one exact selected target, one bounded irreversible handoff under D admission.</summary>
public sealed class CallerBoundResponseCommitter(HttpContext context, SessionSelection subject, Guid targetId) : ICallerBoundResponseCommitter
{
    private int _accepted;
    public async Task CommitAsync(TypedReadSelection selection, ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); context.RequestAborted.ThrowIfCancellationRequested();
        var current = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        if (selection.TargetId != targetId || selection.InvocationId == Guid.Empty || buffer.Length > 256 * 1024 ||
            current is null || current.PrincipalId != subject.PrincipalId || current.BootGeneration != subject.BootGeneration || current.Reference != subject.Reference ||
            !context.Request.IsHttps || context.Request.Host.Value != "localhost:5128" || context.Response.HasStarted ||
            Interlocked.CompareExchange(ref _accepted, 1, 0) != 0) throw new InvalidOperationException("Protected response cannot be handed to this caller.");
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.ContentLength = buffer.Length;
        await context.Response.Body.WriteAsync(buffer, ct);
        await context.Response.Body.FlushAsync(ct);
    }
    public void Abort() => context.Abort();
}
