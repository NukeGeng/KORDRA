namespace Kordra.Modules.ProjectEnvironment.Contracts;

public sealed record ResourcePolicyReference(Guid PolicyId, long Revision);
public sealed record PolicyBinding(Guid BindingId, Guid TenantId, Guid ProjectId, Guid? EnvironmentId,
    string ScopeKind, string Action, Guid PolicyId, long PolicyRevision, long BindingRevision);
public sealed record PolicyBindingChange(Guid? BindingId, string ScopeKind, Guid? EnvironmentId, long ExpectedBindingRevision, string Action = "target.read");
public interface IPolicyBindingReader
{
    Task<PolicyBinding?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid? environmentId, CancellationToken ct = default);
}
public interface IExactEnvironmentPolicyReader
{
    Task<bool> IsApplicableUnderAdmissionAsync(Guid tenantId, Guid projectId, string classification,
        ResourcePolicyReference policy, CancellationToken ct = default);
}

/// <summary>Current binding for one exact action. No fallback to another action.</summary>
public interface IActionPolicyBindingReader
{
    Task<PolicyBinding?> ReadCurrentForActionUnderAdmissionAsync(Guid tenantId, Guid projectId,
        Guid? environmentId, string action, CancellationToken ct = default);
}
