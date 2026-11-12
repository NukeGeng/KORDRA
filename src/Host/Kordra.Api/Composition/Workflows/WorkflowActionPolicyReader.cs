using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.ProjectEnvironment.Contracts;

namespace Kordra.Api;

/// <summary>Environment policy selection before any workflow instance allocation.</summary>
public sealed record WorkflowStartPolicyScope(Guid TenantId, Guid ProjectId, Guid EnvironmentId, Guid RequestId);
public sealed record WorkflowActionPolicyFacts(WorkflowReadPolicy Policy, long ProjectRevision, long EnvironmentRevision, string Classification);

/// <summary>Fresh owner policy facts under caller-held admission. No registration or permission grant.</summary>
public sealed class WorkflowActionPolicyReader(IReleasePolicyEnvironmentReader environments, IActionPolicyBindingReader bindings,
    ICurrentResourcePolicyReader policies, IAuthorityClock clock) : IWorkflowReadPolicyReader
{
    public Task<WorkflowReadPolicy?> ReadAllowedUnderAdmissionAsync(WorkflowReadScope scope, CancellationToken ct) =>
        ReadAllowedUnderAdmissionAsync(scope, "workflow.read", ct);

    public async Task<WorkflowReadPolicy?> ReadAllowedUnderAdmissionAsync(WorkflowReadScope scope, string action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (scope is null || scope.TenantId == Guid.Empty || scope.ProjectId == Guid.Empty ||
            scope.EnvironmentId == Guid.Empty || scope.InstanceId == Guid.Empty || !ResourcePolicies.IsWorkflowAction(action)) return null;
        return (await ReadCoreAsync(scope.TenantId, scope.ProjectId, scope.EnvironmentId, action, scope, ct))?.Policy;
    }

    public Task<WorkflowActionPolicyFacts?> ReadStartAllowedUnderAdmissionAsync(WorkflowStartPolicyScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (scope is null || scope.TenantId == Guid.Empty || scope.ProjectId == Guid.Empty ||
            scope.EnvironmentId == Guid.Empty || scope.RequestId == Guid.Empty) return Task.FromResult<WorkflowActionPolicyFacts?>(null);
        return ReadCoreAsync(scope.TenantId, scope.ProjectId, scope.EnvironmentId, "workflow.start", scope, ct);
    }

    private async Task<WorkflowActionPolicyFacts?> ReadCoreAsync(Guid tenantId, Guid projectId, Guid environmentId,
        string action, object scope, CancellationToken ct)
    {
        try
        {
            var before = clock.GetTrustedUtcNow();
            if (!ValidUtc(before)) return null;
            var owner = await environments.ReadCurrentUnderAdmissionAsync(tenantId, projectId, environmentId, ct);
            if (owner is null || owner.TenantId != tenantId || owner.ProjectId != projectId ||
                owner.EnvironmentId != environmentId || owner.ProjectRevision <= 0 || owner.EnvironmentRevision <= 0 ||
                owner.Classification is not ("Development" or "Test"))
                return null;
            var binding = await bindings.ReadCurrentForActionUnderAdmissionAsync(tenantId, projectId, environmentId, action, ct);
            if (binding is null || binding.TenantId != tenantId || binding.ProjectId != projectId ||
                binding.EnvironmentId != environmentId || binding.Action != action || binding.ScopeKind != "environment")
                return null;
            var policy = await policies.ReadCurrentUnderAdmissionAsync(tenantId, projectId, binding.PolicyId, binding.PolicyRevision, ct);
            if (policy?.Payload.SchemaVersion != 2) return null;
            var selected = new ResourcePolicySelection(binding.BindingId, binding.BindingRevision, tenantId, projectId,
                environmentId, binding.ScopeKind, binding.PolicyId, binding.PolicyRevision, owner.Classification, action);
            var after = clock.GetTrustedUtcNow();
            ct.ThrowIfCancellationRequested();
            if (!ValidUtc(after) || after < before || ResourcePolicies.Evaluate(policy, selected, after) != ResourceEligibility.Allow)
                return null;
            var until = policy.Payload.ExpiresAtUtc ?? after.AddSeconds(10);
            var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1, scope, action, owner.ProjectRevision, owner.EnvironmentRevision, owner.Classification,
                binding.BindingId, binding.BindingRevision, binding.PolicyId, binding.PolicyRevision, policy.ContentHash
            })));
            return new(new(binding.BindingId, binding.BindingRevision, binding.PolicyId, binding.PolicyRevision, until, digest),
                owner.ProjectRevision, owner.EnvironmentRevision, owner.Classification);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static bool ValidUtc(DateTimeOffset time) => time.Offset == TimeSpan.Zero && time != default && time != DateTimeOffset.MaxValue;
}
