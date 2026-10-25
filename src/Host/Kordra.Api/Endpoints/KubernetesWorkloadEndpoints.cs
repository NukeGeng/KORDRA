using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Api;

public static class KubernetesWorkloadEndpoints
{
    public static void Map(WebApplication app) => app.MapGet("/api/v1/targets/{targetId:guid}/workloads",
        (HttpContext context, Guid targetId, KubernetesObservationRegistry registry, CredentialBroker broker, IAuthorityClock clock, ProtectedReadAdmission admission) =>
            Handle(context, targetId, registry, broker, clock, admission));

    public static Task Handle(HttpContext context, Guid target, KubernetesObservationRegistry registry, CredentialBroker broker, IAuthorityClock clock, ProtectedReadAdmission admission)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (LocalAuthenticationEndpoints.CurrentSessionSelection(context) is null) { context.Response.StatusCode = 401; return Task.CompletedTask; }
        if (!registry.TryGet(target, out var trust)) { context.Response.StatusCode = 403; return Task.CompletedTask; }
        return CredentialReadHttpHandler.Handle(context, target,
            new CredentialTargetRead(trust.InstallationId, broker, new KubernetesObservationConsumer(trust, clock), trust.ConfigurationDigest), admission);
    }
}
