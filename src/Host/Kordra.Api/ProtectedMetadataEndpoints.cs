using System.Text.Json;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

/// <summary>One bounded target metadata read; no provider/credential or inventory capability.</summary>
public static class ProtectedMetadataEndpoints
{
    public static void Map(WebApplication app)
    { AuthorityWriterRegistry.Validate(); app.MapGet("/api/v1/targets/{target:guid}", Handle); }
    private static async Task Handle(HttpContext context, Guid target, ProtectedReadAdmission admission, TargetMetadataRead read)
    {
        context.Response.Headers.CacheControl = "no-store";
        var subject = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        if (subject is null) { context.Response.StatusCode = 401; return; }
        if (context.Request.QueryString.HasValue || context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        { context.Response.StatusCode = 400; return; }
        var outcome = await admission.ExecuteAsync(subject, target, read, new CallerBoundResponseCommitter(context, subject, target), context.RequestAborted);
        if (outcome == ProtectedReadStatus.Completed) return;
        if (outcome == ProtectedReadStatus.Unknown || context.Response.HasStarted) { context.Abort(); return; }
        context.Response.StatusCode = outcome == ProtectedReadStatus.Denied ? 403 : 503;
    }
}

public sealed class TargetMetadataRead(IDbContextFactory<InventoryDbContext> contexts, IProjectOwnershipReader projects) : IProtectedTargetRead
{
    public Task<PrivateReadBuffer> Admit(TypedReadSelection selection, CancellationToken ct) => ReadAsync(selection, ct);
    private async Task<PrivateReadBuffer> ReadAsync(TypedReadSelection selection, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var row = await new TargetRegistryStore(db, projects).ReadCurrentAsync(selection.TenantId, selection.TargetId, ct);
        if (row is null || row.TenantId != selection.TenantId || row.TargetId != selection.TargetId ||
            (row.EnvironmentId is null ? selection.ScopeKind != "project" || selection.ScopeId != row.ProjectId :
                selection.ScopeKind != "environment" || selection.ScopeId != row.EnvironmentId))
            throw new InvalidOperationException("Selected metadata is unavailable.");
        return new(JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, targetId = row.TargetId, projectId = row.ProjectId,
            environmentId = row.EnvironmentId, kind = row.Kind, status = row.Status, revision = row.Revision }), 1);
    }
}
