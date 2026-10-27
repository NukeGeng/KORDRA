using System.Text.Json;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace Kordra.Api;

public static class ArtifactEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/v1/projects/{project}/artifacts", Register);
        app.MapGet("/api/v1/projects/{project}/artifacts", (HttpContext c, string project) => Read(c, project, ArtifactQueryKind.List));
        app.MapGet("/api/v1/projects/{project}/artifacts/{resource}", (HttpContext c, string project, string resource) => Read(c, project, ArtifactQueryKind.Detail, resource));
        app.MapGet("/api/v1/projects/{project}/artifact-registrations/{resource}", (HttpContext c, string project, string resource) => Read(c, project, ArtifactQueryKind.Registration, resource));
        app.MapGet("/api/v1/projects/{project}/artifact-registries", (HttpContext c, string project) => Read(c, project, ArtifactQueryKind.Registries));
    }

    private static async Task Register(HttpContext context, string project)
    {
        NoStore(context); var correlation = Guid.NewGuid();
        var subject = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        if (subject is null) { await Fail(context, 401, correlation); return; }
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            if (!ArtifactRequestParser.TryGuid(project, out var projectId) || context.Request.Query.Count != 0 ||
                !context.Request.HasJsonContentType() || context.Request.Headers.ContentEncoding.Count != 0 ||
                context.Request.Headers["Idempotency-Key"] is not { Count: 1 } keys || !ArtifactRequestParser.ValidKey(keys[0]))
            { await Fail(context, 400, correlation); return; }
            var bytes = await ArtifactRequestParser.ReadBodyAsync(context.Request.Body, context.RequestAborted);
            var command = ArtifactRequestParser.Parse(bytes); correlation = command.CorrelationId;
            var response = new ArtifactCallerBoundResponseCommitter(context, subject, projectId,
                context.RequestServices.GetRequiredService<IAuthorityClock>());
            var status = await context.RequestServices.GetRequiredService<ArtifactRegistrationCoordinator>()
                .ExecuteAsync(subject, projectId, command, keys[0]!, response, context.RequestAborted);
            await Complete(context, status, correlation);
        }
        catch (AntiforgeryValidationException) { await Fail(context, 403, correlation); }
        catch (ArtifactBodyLimitException) { await Fail(context, 413, correlation); }
        catch (Exception e) when (e is JsonException or FormatException) { await Fail(context, 400, correlation); }
        catch { await Fail(context, 503, correlation); }
    }

    private static async Task Read(HttpContext context, string project, ArtifactQueryKind kind, string? resource = null)
    {
        NoStore(context); var correlation = Guid.NewGuid();
        var subject = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        if (subject is null) { await Fail(context, 401, correlation); return; }
        try
        {
            if (!ArtifactRequestParser.TryGuid(project, out var projectId) ||
                resource is not null && !ArtifactRequestParser.TryGuid(resource, out _ ) ||
                context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            { await Fail(context, 400, correlation); return; }
            var query = ArtifactRequestParser.ParseQuery(context.Request.Query, kind, correlation,
                resource is null ? null : Guid.Parse(resource));
            var response = new ArtifactCallerBoundResponseCommitter(context, subject, projectId,
                context.RequestServices.GetRequiredService<IAuthorityClock>());
            var status = await context.RequestServices.GetRequiredService<ArtifactReadCoordinator>()
                .ExecuteAsync(subject, projectId, query, response, context.RequestAborted);
            await Complete(context, status, correlation);
        }
        catch (FormatException) { await Fail(context, 400, correlation); }
        catch { await Fail(context, 503, correlation); }
    }

    private static Task Complete(HttpContext context, ArtifactExecutionStatus status, Guid correlation)
    {
        if (status == ArtifactExecutionStatus.Completed) return Task.CompletedTask;
        if (status == ArtifactExecutionStatus.Unknown) { context.Abort(); return Task.CompletedTask; }
        return Fail(context, status == ArtifactExecutionStatus.Denied ? 403 : 503, correlation);
    }

    internal static void NoStore(HttpContext context)
    { context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.Pragma = "no-cache"; }

    internal static Task Fail(HttpContext context, int status, Guid correlation, CancellationToken? ct = null)
    {
        NoStore(context);
        if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested && ct is null)
        { context.Abort(); return Task.CompletedTask; }
        context.Response.StatusCode = status;
        var code = status switch { 400 => "invalid_request", 413 => "body_too_large", 401 => "session_invalid", 403 => "request_denied", _ => "artifact_unavailable" };
        return context.Response.WriteAsJsonAsync(new { schemaVersion = 1, correlationId = correlation, code, registration = (object?)null }, ct ?? context.RequestAborted);
    }
}
