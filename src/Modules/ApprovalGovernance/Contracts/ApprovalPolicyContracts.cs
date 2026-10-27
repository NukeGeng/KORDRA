namespace Kordra.Modules.ApprovalGovernance.Contracts;

/// <summary>Read-only current owner facts. This snapshot does not itself authorize a request.</summary>
public sealed class ReleaseApprovalPolicyBinding
{
    private readonly byte[] _payload;
    public ReleaseApprovalPolicyBinding(Guid installationId, Guid tenantId, Guid projectId, Guid environmentId,
        Guid bindingId, long bindingRevision, Guid policyId, long policyRevision, string payloadHash,
        string bindingDigest, byte[] canonicalPayload)
    {
        InstallationId = installationId; TenantId = tenantId; ProjectId = projectId; EnvironmentId = environmentId;
        BindingId = bindingId; BindingRevision = bindingRevision; PolicyId = policyId; PolicyRevision = policyRevision;
        PayloadHash = payloadHash; BindingDigest = bindingDigest; _payload = (byte[])canonicalPayload.Clone();
    }
    public Guid InstallationId { get; }
    public Guid TenantId { get; }
    public Guid ProjectId { get; }
    public Guid EnvironmentId { get; }
    public Guid BindingId { get; }
    public long BindingRevision { get; }
    public Guid PolicyId { get; }
    public long PolicyRevision { get; }
    public string PayloadHash { get; }
    public string BindingDigest { get; }
    public byte[] GetCanonicalPayload() => (byte[])_payload.Clone();
}
public interface IReleaseApprovalPolicyReader
{
    /// <summary>Caller holds shared admission or stopped-installation custody. Null never grants access.</summary>
    Task<ReleaseApprovalPolicyBinding?> ReadCurrentUnderAdmissionAsync(Guid installationId, Guid tenantId,
        Guid projectId, Guid environmentId, CancellationToken cancellationToken = default);
}
/// <summary>Host adapter for the already-held stopped-installation OS lock.</summary>
public interface IOfflineApprovalCustody { void RequireHeld(); }
/// <summary>Current PRJ facts established under the same held admission/custody, not an authorization permit.</summary>
public sealed record ApprovalEnvironmentEvidence(Guid TenantId, Guid ProjectId, Guid EnvironmentId,
    long ProjectRevision, long EnvironmentRevision, string Classification);
