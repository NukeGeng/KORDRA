using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

/// <summary>Composition of owner ports, under a caller-held gate; never reads owner tables.</summary>
public sealed class CurrentResourcePolicyReader(IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ProjectDbContext> projects, IAuthorityClock clock) : IResourcePolicyReader, IExactEnvironmentPolicyReader, IEnvironmentPolicyReader
{
    public async Task<ResourcePolicyAuthority?> ReadPolicyUnderAdmissionAsync(TargetAuthority target, string permission, CancellationToken ct = default)
    {
        if (permission != "target.read") return null;
        await using var prj = await projects.CreateDbContextAsync(ct);
        var binding = await new PolicyBindingStore(prj).ReadCurrentUnderAdmissionAsync(target.TenantId, target.ProjectId, target.EnvironmentId, ct);
        if (binding is null) return null;
        await using var iam = await identity.CreateDbContextAsync(ct);
        var policy = await new ResourcePolicyStore(iam).ReadCurrentUnderAdmissionAsync(binding.TenantId, binding.ProjectId, binding.PolicyId, binding.PolicyRevision, ct);
        if (policy is null) return null;
        var selected = new ResourcePolicySelection(binding.BindingId, binding.BindingRevision, binding.TenantId, binding.ProjectId, binding.EnvironmentId,
            binding.ScopeKind, binding.PolicyId, binding.PolicyRevision, target.Classification);
        var decision = ResourcePolicies.Evaluate(policy, selected, clock.GetTrustedUtcNow());
        return new(binding.TenantId, binding.ProjectId, binding.EnvironmentId, permission, policy.Revision, policy.PolicyId,
            binding.BindingId, binding.BindingRevision, policy.ContentHash, policy.Payload.EffectiveFromUtc, policy.Payload.ExpiresAtUtc, decision);
    }

    public async Task<bool> IsApplicableUnderAdmissionAsync(Guid tenantId, Guid projectId, string classification, ResourcePolicyReference reference, CancellationToken ct = default)
    {
        if (!ResourcePolicies.IsClassification(classification)) return false;
        await using var db = await identity.CreateDbContextAsync(ct);
        var policy = await new ResourcePolicyStore(db).ReadCurrentUnderAdmissionAsync(tenantId, projectId, reference.PolicyId, reference.Revision, ct);
        if (policy is null) return false;
        var now = clock.GetTrustedUtcNow(); var p = policy.Payload;
        // Catalog association is not a target.read permission. A deny-only policy remains a valid explicit constraint.
        return p.Status == "active" && p.ScopeKind == "environment" && p.EffectiveFromUtc <= now && !(p.ExpiresAtUtc <= now);
    }

    // Integer-only legacy references cannot establish a current resource policy identity.
    public Task<bool> IsApplicableAsync(Guid tenantId, Guid projectId, string classification, long policyRevision, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
