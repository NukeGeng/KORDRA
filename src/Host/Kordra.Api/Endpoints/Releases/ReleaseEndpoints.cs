using System.Text.Json;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace Kordra.Api;

public static class ReleaseEndpoints
{
    public static void Map(WebApplication app)
    {
        AuthorityWriterRegistry.Validate();
        app.MapPost("/api/v1/projects/{project}/releases", (HttpContext c, string project) => Mutate(c, project, null, "create-draft"));
        app.MapPost("/api/v1/projects/{project}/releases/{resource}/revisions", (HttpContext c, string project, string resource) => Mutate(c, project, resource, "next-draft"));
        app.MapPut("/api/v1/projects/{project}/release-revisions/{resource}/draft", (HttpContext c, string project, string resource) => Mutate(c, project, resource, "replace-draft"));
        app.MapPost("/api/v1/projects/{project}/release-revisions/{resource}/freeze", (HttpContext c, string project, string resource) => Mutate(c, project, resource, "freeze"));
        app.MapPost("/api/v1/projects/{project}/release-revisions/{resource}/retire", (HttpContext c, string project, string resource) => Mutate(c, project, resource, "retire"));
        app.MapGet("/api/v1/projects/{project}/releases", (HttpContext c, string project) => Read(c, project, null, ReleaseReadKind.Lineages));
        app.MapGet("/api/v1/projects/{project}/releases/{resource}/revisions", (HttpContext c, string project, string resource) => Read(c, project, resource, ReleaseReadKind.Revisions));
        app.MapGet("/api/v1/projects/{project}/release-revisions/{resource}", (HttpContext c, string project, string resource) => Read(c, project, resource, ReleaseReadKind.Detail));
    }
    private static async Task Mutate(HttpContext context, string project, string? resource, string action)
    {
        NoStore(context); var correlation = Guid.NewGuid(); var subject = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        if (subject is null) { await Fail(context, 401, correlation); return; }
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            if (context.Request.Query.Count != 0 || !context.Request.HasJsonContentType() || context.Request.Headers.ContentEncoding.Count != 0 ||
                !ReleaseRequestParser.ValidFraming(context.Request) || context.Request.Headers["Idempotency-Key"] is not { Count: 1 } keys ||
                !ArtifactRequestParser.ValidKey(keys[0])) { await Fail(context, 400, correlation); return; }
            var body = await ReleaseRequestParser.ReadBodyAsync(context.Request.Body, context.RequestAborted);
            var parsed = ReleaseRequestParser.Parse(action, project, resource, body, context.Request.Headers.IfMatch);
            correlation = parsed.CorrelationId;
            var response = new ReleaseCallerBoundResponseCommitter(context, subject, parsed.Command.ProjectId, parsed.Command.Route,
                context.RequestServices.GetRequiredService<IAuthorityClock>());
            var result = await context.RequestServices.GetRequiredService<ReleaseMutationExecutionCoordinator>()
                .ExecuteAsync(subject, parsed.Command, keys[0]!, correlation, response, context.RequestAborted);
            await Complete(context, result, correlation);
        }
        catch (AntiforgeryValidationException) { await Fail(context, 403, correlation); }
        catch (ReleaseBodyLimitException) { await Fail(context, 413, correlation); }
        catch (ReleasePreconditionException) { await Fail(context, 412, correlation); }
        catch (Exception error) when (error is FormatException or JsonException) { await Fail(context, 400, correlation); }
        catch { await Fail(context, 503, correlation); }
    }
    private static async Task Read(HttpContext context, string project, string? resource, ReleaseReadKind kind)
    {
        NoStore(context); var correlation = Guid.NewGuid(); var subject = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        if (subject is null) { await Fail(context, 401, correlation); return; }
        try
        {
            if (!ReleaseRequestParser.GuidValue(project, out var projectId) ||
                resource is not null && !ReleaseRequestParser.GuidValue(resource, out _) ||
                context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            { await Fail(context, 400, correlation); return; }
            var page = kind == ReleaseReadKind.Detail
                ? context.Request.Query.Count == 0 ? new ParsedReleasePage(50, null) : throw new FormatException()
                : ReleaseRequestParser.ParsePage(context.Request.Query);
            var query = new ReleaseReadQuery(kind, correlation, resource is null ? null : Guid.ParseExact(resource, "D"), page.Limit, page.AfterId);
            var response = new ReleaseCallerBoundResponseCommitter(context, subject, projectId, context.Request.Path.Value!,
                context.RequestServices.GetRequiredService<IAuthorityClock>(), context.Request.QueryString.Value ?? "");
            var result = await context.RequestServices.GetRequiredService<ReleaseReadCoordinator>().ExecuteAsync(subject, projectId, query, response, context.RequestAborted);
            await Complete(context, result, correlation);
        }
        catch (FormatException) { await Fail(context, 400, correlation); }
        catch { await Fail(context, 503, correlation); }
    }
    private static Task Complete(HttpContext context, string status, Guid correlation)
    {
        if (status == "completed") return Task.CompletedTask;
        if (status == "unknown") { context.Abort(); return Task.CompletedTask; }
        return Fail(context, status switch { "invalid" => 400, "denied" => 403, "conflict" => 409, "precondition" => 412, "ineligible" => 422, _ => 503 }, correlation);
    }
    private static void NoStore(HttpContext context) { context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.Pragma = "no-cache"; }
    private static Task Fail(HttpContext context, int status, Guid correlation)
    {
        NoStore(context); if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested) { context.Abort(); return Task.CompletedTask; }
        context.Response.StatusCode = status;
        var code = status switch { 400 => "invalid_request", 401 => "session_invalid", 403 => "request_denied", 409 => "conflict", 412 => "precondition_failed", 413 => "body_too_large", 422 => "qualification_required", _ => "release_unavailable" };
        return context.Response.WriteAsJsonAsync(new { schemaVersion = 1, correlationId = correlation.ToString("D"), code }, context.RequestAborted);
    }
}
