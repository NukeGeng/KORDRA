using System.Collections.Frozen;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;

namespace Kordra.Api;

/// <summary>Installation-owned immutable registry; never accepts browser plans or mutable target trust.</summary>
public sealed class NomadObservationRegistry(NomadTrustConfiguration configuration)
{
    private readonly FrozenDictionary<Guid, NomadTargetTrust> _targets = configuration.Targets.ToFrozenDictionary(t => t.TargetId);
    public bool TryGet(Guid target, out NomadTargetTrust trust) => _targets.TryGetValue(target, out trust!);
    public bool Matches(ReadDependencySelection selected) => selected.Plan.IsValid && selected.Plan.IsCredential &&
        _targets.TryGetValue(selected.TargetId, out var t) && selected.Plan.InstallationId == t.InstallationId &&
        selected.TenantId == t.TenantId && selected.ScopeKind == t.ScopeKind && selected.ScopeId == t.ScopeId &&
        selected.Plan.ConsumerConfigurationDigest == t.ConfigurationDigest;
}
