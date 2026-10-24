using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public static class EnvironmentV2Endpoints
{
    public static void Map(WebApplication app)
    { AuthorityWriterRegistry.Validate(); app.MapPost("/api/v2/projects/{project:guid}/environments", Create); }
    private static async Task Create(HttpContext context, Guid project, CatalogMutationCoordinator mutations,
        IDbContextFactory<ProjectDbContext> projects, FairAsyncReaderWriterAdmissionGate gate, IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl = "no-store";
        Guid correlation = Guid.NewGuid();
        if (LocalAuthenticationEndpoints.CurrentSessionSelection(context) is null) { await Fail(context, 401, correlation); return; }
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await Fail(context, 403, correlation); return; }
        try
        {
            if (!context.Request.HasJsonContentType()) { await Fail(context, 400, correlation); return; }
            var keys = context.Request.Headers["Idempotency-Key"];
            if (keys.Count != 1 || keys[0] is not { Length: >= 16 and <= 128 } key || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '~' or '-')))
            { await Fail(context, 400, correlation); return; }
            var bytes = new byte[128 * 1024 + 1]; var length = 0;
            while (length < bytes.Length)
            { var count = await context.Request.Body.ReadAsync(bytes.AsMemory(length), context.RequestAborted); if (count == 0) break; length += count; }
            if (length > 128 * 1024) { await Fail(context, 400, correlation); return; }
            using var document = JsonDocument.Parse(bytes.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 8 });
            var body = document.RootElement;
            if (!ResourcePolicies.Exact(body, "schemaVersion", "correlationId", "name", "classification", "resourcePolicy") ||
                body.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number || !body.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 2 ||
                body.GetProperty("correlationId").ValueKind != JsonValueKind.String || !body.GetProperty("correlationId").TryGetGuid(out correlation) || correlation == Guid.Empty ||
                body.GetProperty("name").ValueKind != JsonValueKind.String || body.GetProperty("classification").ValueKind != JsonValueKind.String)
            { await Fail(context, 400, correlation); return; }
            var name = body.GetProperty("name").GetString(); var classification = body.GetProperty("classification").GetString();
            var reference = body.GetProperty("resourcePolicy");
            if (!ProjectCatalogStore.ValidName(name) || !ResourcePolicies.IsClassification(classification) || !ResourcePolicies.Exact(reference, "policyId", "revision") ||
                reference.GetProperty("policyId").ValueKind != JsonValueKind.String || !reference.GetProperty("policyId").TryGetGuid(out var policy) || policy == Guid.Empty ||
                reference.GetProperty("revision").ValueKind != JsonValueKind.Number || !reference.GetProperty("revision").TryGetInt64(out var revision) || revision <= 0)
            { await Fail(context, 400, correlation); return; }
            Guid? tenant;
            await using (var admission = await gate.AcquireReadAsync(context.RequestAborted))
            await using (var db = await projects.CreateDbContextAsync(context.RequestAborted))
                tenant = await new PolicyBindingStore(db).ReadProjectTenantSelectionAsync(project, context.RequestAborted);
            if (tenant is null) { await Fail(context, 403, correlation); return; }
            var result = await mutations.ExecuteEnvironmentV2Async(new(key, tenant.Value, project, name!, classification!, new(policy, revision), correlation), context.RequestAborted);
            if (result.Status != "success" || result.Environment is not { } environment || result.PolicyBinding is not { } binding)
            {
                await Fail(context, result.Status switch { "conflict" => 409, "invalid" => 400, "denied" => 403, _ => 503 }, correlation);
                return;
            }
            context.Response.StatusCode = 201; context.Response.Headers.ETag = '"' + environment.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + '"';
            await context.Response.WriteAsJsonAsync(new { schemaVersion = 2, correlationId = correlation,
                environment = new { id = environment.Id, projectId = environment.ProjectId, name = environment.Name, classification = environment.Classification, status = environment.Status, revision = environment.Revision },
                policyBinding = new { bindingId = binding.BindingId, bindingRevision = binding.BindingRevision, policyId = binding.PolicyId, policyRevision = binding.PolicyRevision } }, context.RequestAborted);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            // Invalid JSON field types are never interpreted as an authority or a wildcard.
            if (!context.Response.HasStarted) await Fail(context, exception is JsonException or FormatException ? 400 : 503, correlation);
            else context.Abort();
        }
        catch
        { if (!context.Response.HasStarted) await Fail(context, 503, correlation); else context.Abort(); }
    }
    private static Task Fail(HttpContext context, int status, Guid correlation)
    {
        if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested) { context.Abort(); return Task.CompletedTask; }
        context.Response.StatusCode = status;
        var code = status switch { 400 => "INVALID_REQUEST", 401 => "SESSION_INVALID", 403 => "REQUEST_REJECTED", 409 => "REQUEST_CONFLICT", _ => "AUTHORITY_UNAVAILABLE" };
        return context.Response.WriteAsJsonAsync(new { schemaVersion = 2, correlationId = correlation == Guid.Empty ? Guid.NewGuid() : correlation,
            error = new { code, message = "Request could not be accepted." } }, context.RequestAborted);
    }
}
