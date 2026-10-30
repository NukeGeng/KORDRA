using System.Text.Json;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace Kordra.Api;

/// <summary>Canonical approval routes; runtime composition must explicitly opt in after schema/readiness validation.</summary>
public static class ApprovalEndpoints
{
    public static void Map(WebApplication app)
    {
        AuthorityWriterRegistry.Validate();
        app.MapPost("/api/v1/environments/{environment}/release-approvals", (HttpContext c, string environment) => Mutate(c, environment, null, "request"));
        app.MapPost("/api/v1/environments/{environment}/release-approvals/{request}/grant", (HttpContext c, string environment, string request) => Mutate(c, environment, request, "grant"));
        app.MapPost("/api/v1/environments/{environment}/release-approvals/{request}/reject", (HttpContext c, string environment, string request) => Mutate(c, environment, request, "reject"));
        app.MapPost("/api/v1/environments/{environment}/release-approvals/{request}/revoke", (HttpContext c, string environment, string request) => Mutate(c, environment, request, "revoke"));
        app.MapGet("/api/v1/environments/{environment}/release-approvals", (HttpContext c, string environment) => Read(c, environment, null));
        app.MapGet("/api/v1/environments/{environment}/release-approvals/{request}", (HttpContext c, string environment, string request) => Read(c, environment, request));
    }
    private static async Task Mutate(HttpContext context, string environment, string? request, string action)
    {
        NoStore(context); var correlation = Guid.NewGuid(); var subject = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        if (subject is null) { await Fail(context, 401, correlation); return; }
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            if (context.Request.Query.Count != 0 || !context.Request.HasJsonContentType() || context.Request.Headers.ContentEncoding.Count != 0 ||
                !ReleaseRequestParser.ValidFraming(context.Request) || context.Request.Headers["Idempotency-Key"] is not { Count: 1 } keys ||
                !ArtifactRequestParser.ValidKey(keys[0])) { await Fail(context, 400, correlation); return; }
            var bytes = await ReleaseRequestParser.ReadBodyAsync(context.Request.Body, context.RequestAborted);
            var command = ApprovalRequestParser.Parse(action, environment, request, bytes, context.Request.Headers.IfMatch);
            correlation = command.CorrelationId;
            var response = new ApprovalCallerBoundResponseCommitter(context, subject, command.EnvironmentId, command.Resource,
                context.RequestServices.GetRequiredService<IAuthorityClock>());
            var status = await context.RequestServices.GetRequiredService<ApprovalMutationExecutionCoordinator>()
                .ExecuteAsync(subject, Guid.Empty, command, keys[0]!, correlation, response, context.RequestAborted);
            await Complete(context, status, correlation);
        }
        catch (AntiforgeryValidationException) { await Fail(context, 403, correlation); }
        catch (ReleaseBodyLimitException) { await Fail(context, 413, correlation); }
        catch (ReleasePreconditionException) { await Fail(context, 412, correlation); }
        catch (Exception error) when (error is FormatException or JsonException) { await Fail(context, 400, correlation); }
        catch { await Fail(context, 503, correlation); }
    }
    private static async Task Read(HttpContext context, string environment, string? request)
    {
        NoStore(context); var correlation = Guid.NewGuid(); var subject = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        if (subject is null) { await Fail(context, 401, correlation); return; }
        try
        {
            if (!ReleaseRequestParser.GuidValue(environment, out var environmentId) ||
                request is not null && !ReleaseRequestParser.GuidValue(request, out _) ||
                context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            { await Fail(context, 400, correlation); return; }
            var page = request is not null
                ? context.Request.Query.Count == 0 ? new ParsedReleasePage(50, null) : throw new FormatException()
                : ReleaseRequestParser.ParsePage(context.Request.Query);
            var query = new ApprovalReadQuery(correlation, request is null ? null : Guid.ParseExact(request, "D"), page.Limit, page.AfterId);
            var response = new ApprovalCallerBoundResponseCommitter(context, subject, environmentId, context.Request.Path.Value!,
                context.RequestServices.GetRequiredService<IAuthorityClock>(), context.Request.QueryString.Value ?? "");
            var status = await context.RequestServices.GetRequiredService<ApprovalReadCoordinator>()
                .ExecuteAsync(subject, Guid.Empty, environmentId, query, response, context.RequestAborted);
            await Complete(context, status, correlation);
        }
        catch (FormatException) { await Fail(context, 400, correlation); }
        catch { await Fail(context, 503, correlation); }
    }
    private static Task Complete(HttpContext context, string status, Guid correlation)
    {
        if (status == "completed") return Task.CompletedTask;
        if (status == "unknown") { context.Abort(); return Task.CompletedTask; }
        return Fail(context, status switch { "invalid" => 400, "denied" => 403, "not_found" => 404, "conflict" => 409,
            "precondition" => 412, "ineligible" => 422, _ => 503 }, correlation);
    }
    private static void NoStore(HttpContext context) { context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.Pragma = "no-cache"; }
    private static Task Fail(HttpContext context, int status, Guid correlation)
    {
        NoStore(context); if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested) { context.Abort(); return Task.CompletedTask; }
        context.Response.StatusCode = status;
        var code = status switch { 400 => "invalid_request", 401 => "session_invalid", 403 => "request_denied", 404 => "approval_not_found",
            409 => "conflict", 412 => "precondition_failed", 413 => "body_too_large", 422 => "qualification_required", _ => "approval_unavailable" };
        return context.Response.WriteAsJsonAsync(new { schemaVersion = 1, correlationId = correlation.ToString("D"), code }, context.RequestAborted);
    }
}
