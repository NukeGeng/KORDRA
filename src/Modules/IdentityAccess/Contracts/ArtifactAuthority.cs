namespace Kordra.Modules.IdentityAccess.Contracts;

/// <summary>Current project owner facts, not a tenant supplied by a business request.</summary>
public sealed record ArtifactProjectAuthority(Guid TenantId, Guid ProjectId, long Revision);

public interface IArtifactProjectAuthorityReader
{
    Task<ArtifactProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid projectId, CancellationToken ct = default);
}
