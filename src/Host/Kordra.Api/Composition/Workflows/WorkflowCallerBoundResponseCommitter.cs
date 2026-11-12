using System.Security.Cryptography;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Api;

/// <summary>Bounded local response handoff for a single workflow query. No route is registered here.</summary>
public sealed class WorkflowCallerBoundResponseCommitter(HttpContext context, SessionSelection subject,
    WorkflowReadScope scope, long? beforeVersion, string expectedPath, string expectedQuery,
    IAuthorityClock clock, FairAsyncReaderWriterAdmissionGate gate) : IWorkflowReadResponseCommitter
{
    private int accepted;

    public async Task CommitAsync(WorkflowReadCommitment commitment, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        Validate(commitment, ct);
        if (context.Response.HasStarted || commitment.StatusCode is not (200 or 404) ||
            payload.Length is < 1 or > 256 * 1024 || commitment.PayloadSize != payload.Length ||
            commitment.PayloadSha256 != Convert.ToHexStringLower(SHA256.HashData(payload.Span)) ||
            Interlocked.CompareExchange(ref accepted, 1, 0) != 0)
            throw Unavailable();

        context.Response.OnStarting(() => { Validate(commitment, ct); return Task.CompletedTask; });
        context.Response.StatusCode = commitment.StatusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentLength = payload.Length;
        await context.Response.StartAsync(ct);
        Validate(commitment, ct);
        await context.Response.Body.WriteAsync(payload, ct);
        await context.Response.Body.FlushAsync(ct);
    }

    public void Abort() => context.Abort();

    private void Validate(WorkflowReadCommitment commitment, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.RequestAborted.ThrowIfCancellationRequested();
        var current = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        var now = clock.GetTrustedUtcNow();
        if (gate.IsClosed || commitment.InvocationId == Guid.Empty || commitment.Scope != scope ||
            commitment.BeforeVersion != beforeVersion || commitment.ValidUntilUtc == DateTimeOffset.MaxValue ||
            commitment.ValidUntilUtc.Offset != TimeSpan.Zero || now == default || now.Offset != TimeSpan.Zero ||
            now >= commitment.ValidUntilUtc || current is null || current.PrincipalId != subject.PrincipalId ||
            current.BootGeneration != subject.BootGeneration || current.Reference != subject.Reference ||
            !context.Request.IsHttps || context.Request.Host.Value != "localhost:5128" ||
            context.Request.Path.Value != expectedPath || (context.Request.QueryString.Value ?? "") != expectedQuery)
            throw Unavailable();
    }

    private static InvalidOperationException Unavailable() => new("Workflow response cannot be handed to this caller.");
}
