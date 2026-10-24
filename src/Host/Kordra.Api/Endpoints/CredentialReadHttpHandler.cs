using Kordra.Modules.IdentityAccess.Authorization;

namespace Kordra.Api;

/// <summary>Explicit Host-selected credential read handler; no route or consumer registration is enabled by this type.</summary>
public static class CredentialReadHttpHandler
{
    public static async Task Handle(HttpContext context, Guid target, IProtectedCredentialRead read, ProtectedReadAdmission admission)
    {
        context.Response.Headers.CacheControl = "no-store";
        var subject = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
        if (subject is null) { context.Response.StatusCode = 401; return; }
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.QueryString.HasValue || context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        { context.Response.StatusCode = 400; return; }
        var result = await admission.ExecuteCredentialAsync(subject, target, read, new CallerBoundResponseCommitter(context, subject, target), context.RequestAborted);
        if (result == ProtectedReadStatus.Completed) return;
        if (result == ProtectedReadStatus.Unknown || context.Response.HasStarted) { context.Abort(); return; }
        context.Response.StatusCode = result == ProtectedReadStatus.Denied ? 403 : 503;
    }
}
