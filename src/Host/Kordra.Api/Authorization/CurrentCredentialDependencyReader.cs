using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public sealed class CurrentCredentialDependencyReader(IDbContextFactory<CredentialDbContext> contexts,
    LocalAuthenticationConfiguration installation, IAuthorityClock clock, NomadObservationRegistry? providers = null) : IProtectedReadDependencyReader
{
    public async Task<ReadDependencySnapshot?> ReadCurrentUnderAdmissionAsync(ReadDependencySelection selected, CancellationToken ct = default)
    {
        if (selected is null || !selected.Plan.IsCredential || !selected.Plan.IsValid || selected.Plan.InstallationId != installation.InstallationId) return null;
        if (selected.Plan.ConsumerConfigurationDigest is not null)
        { if (providers is null || !providers.Matches(selected)) return null; }
        else if (selected.ScopeKind is not null || selected.ScopeId is not null) return null;
        await using var db = await contexts.CreateDbContextAsync(ct);
        var binding = await new CredentialBindingStore(db).ReadCurrentUnderAdmissionAsync(new(installation.InstallationId, selected.TenantId, selected.TargetId, selected.Plan.ConsumerId), ct);
        if (binding is null || !binding.IsValidAt(clock.GetTrustedUtcNow())) return null;
        return new("credential-binding", selected, binding.BindingId, binding.Revision, binding.Digest(), binding.NotBeforeUtc, binding.ExpiresAtUtc);
    }
}
