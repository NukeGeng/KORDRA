namespace Kordra.Modules.IdentityAccess.Contracts;

/// <summary>Current project owner facts, not a tenant supplied by a business request.</summary>
public sealed record ReleaseProjectAuthority(Guid TenantId, Guid ProjectId, long Revision);

public interface IReleaseProjectAuthorityReader
{
    Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid projectId, CancellationToken ct = default);
}

public sealed record ApprovalEnvironmentAuthority(Guid TenantId, Guid ProjectId, Guid EnvironmentId, long Revision);
public interface IApprovalEnvironmentAuthorityReader
{
    Task<ApprovalEnvironmentAuthority?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid environmentId, CancellationToken ct = default);
}
