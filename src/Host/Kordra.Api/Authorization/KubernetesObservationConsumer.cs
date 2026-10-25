using System.Text.Json;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Kubernetes;

namespace Kordra.Api;

public sealed class KubernetesObservationConsumer(KubernetesTargetTrust trust, IAuthorityClock clock) : CredentialConsumer<PrivateReadBuffer>
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    public override string Id => "inventory.readonly.v1";
    public override async Task<PrivateReadBuffer> ReadAsync(ReadOnlyMemory<byte> credential, CredentialUseContextV1 context, CancellationToken ct)
    {
        if (!context.IsValid || context.Selection.InstallationId != trust.InstallationId || context.Selection.TenantId != trust.TenantId ||
            context.Selection.TargetId != trust.TargetId || context.Selection.ConsumerId != Id || context.ScopeKind != trust.ScopeKind || context.ScopeId != trust.ScopeId)
            throw new InvalidOperationException("Kubernetes observation is unavailable.");
        var result = await new KubernetesWorkloadReader(trust, clock.GetTrustedUtcNow).ReadAsync(context.InvocationId, trust.TargetId, credential, ct);
        return new(JsonSerializer.SerializeToUtf8Bytes(result, Wire), result.ResourceSets.Sum(s => s.Items.Count), result.ValidUntilUtc);
    }
}
