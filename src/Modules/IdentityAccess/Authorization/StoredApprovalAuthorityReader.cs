namespace Kordra.Modules.IdentityAccess.Authorization;

/// <summary>Rechecks session-free snapshots against current IAM/TEN facts under caller-held admission.
/// This is an eligibility input, not a permit, audit proof, or substitute for reader authorization.</summary>
public sealed class StoredApprovalAuthorityReader(DurableApprovalAuthorityReader authority)
{
    public async Task<DurableApprovalAuthorityStatus> CheckUnderAdmissionAsync(ReadOnlyMemory<byte> encoded,
        string purpose, Guid tenantId, Guid projectId, Guid environmentId, Guid expectedPrincipalId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (expectedPrincipalId == Guid.Empty) return DurableApprovalAuthorityStatus.Unavailable;
        ApprovalAuthorityEvidence evidence;
        try { evidence = ApprovalAuthorityEvidenceCodec.Decode(encoded, purpose, tenantId, projectId, environmentId); }
        catch (ArgumentException) { return DurableApprovalAuthorityStatus.Unavailable; }
        if (evidence.Principal.PrincipalId != expectedPrincipalId) return DurableApprovalAuthorityStatus.Unavailable;
        var result = await authority.CheckUnderAdmissionAsync(evidence.Principal, evidence.EnvironmentGrant, evidence.CompanionGrant, ct);
        return result.Status;
    }
}
