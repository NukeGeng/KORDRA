using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Modules.IdentityAccess.Authorization;

/// <summary>Session-free evidence captured from a current, authorized action. Never an action permit.</summary>
public sealed record ApprovalAuthorityEvidence
{
    private ApprovalAuthorityEvidence(DurablePrincipal principal, ScopedGrant environmentGrant, ScopedGrant companionGrant)
        => (Principal, EnvironmentGrant, CompanionGrant) = (principal, environmentGrant, companionGrant);
    public DurablePrincipal Principal { get; }
    public ScopedGrant EnvironmentGrant { get; }
    public ScopedGrant CompanionGrant { get; }

    public static ApprovalAuthorityEvidence CaptureRequester(DurablePrincipal principal, ScopedGrant requestGrant,
        ScopedGrant readGrant, Guid tenantId, Guid projectId, Guid environmentId) =>
        Capture(principal, requestGrant, readGrant, tenantId, environmentId, projectId,
            "release.request-approval", "release.read", "project");

    public static ApprovalAuthorityEvidence CaptureApprover(DurablePrincipal principal, ScopedGrant approveGrant,
        ScopedGrant approvalReadGrant, Guid tenantId, Guid environmentId) =>
        Capture(principal, approveGrant, approvalReadGrant, tenantId, environmentId, environmentId,
            "release.approve", "release.approval.read", "environment");

    // Transient revoke authorization facts only; these are deliberately excluded from the durable requester/approver codec.
    public static ApprovalAuthorityEvidence CaptureRevoker(DurablePrincipal principal, ScopedGrant revokeGrant,
        ScopedGrant approvalReadGrant, Guid tenantId, Guid environmentId) =>
        Capture(principal, revokeGrant, approvalReadGrant, tenantId, environmentId, environmentId,
            "release.approval.revoke", "release.approval.read", "environment");

    // Transient display authority only; excluded from durable requester/approver encoding.
    public static ApprovalAuthorityEvidence CaptureReader(DurablePrincipal principal, ScopedGrant readGrant,
        Guid tenantId, Guid environmentId) => Capture(principal, readGrant, readGrant, tenantId, environmentId, environmentId,
            "release.approval.read", "release.approval.read", "environment");

    private static ApprovalAuthorityEvidence Capture(DurablePrincipal principal, ScopedGrant first, ScopedGrant second,
        Guid tenantId, Guid environmentId, Guid companionScope, string firstPermission, string secondPermission, string secondScope)
    {
        if (principal is null || first is null || second is null || !principal.Active || principal.PrincipalId == Guid.Empty ||
            principal.PrincipalRevision <= 0 || principal.SecurityRevisionHash is not { Length: 64 } ||
            !principal.SecurityRevisionHash.All(char.IsAsciiHexDigitLower) || tenantId == Guid.Empty || environmentId == Guid.Empty || companionScope == Guid.Empty ||
            first.PrincipalId != principal.PrincipalId || second.PrincipalId != principal.PrincipalId ||
            first.TenantId != tenantId || second.TenantId != tenantId || first.MembershipId == Guid.Empty ||
            first.MembershipId != second.MembershipId || first.TenantRevision <= 0 || first.TenantRevision != second.TenantRevision ||
            first.MembershipRevision <= 0 || first.MembershipRevision != second.MembershipRevision ||
            first.Permission != firstPermission || first.ScopeKind != "environment" || first.ScopeId != environmentId ||
            second.Permission != secondPermission || second.ScopeKind != secondScope || second.ScopeId != companionScope ||
            !Valid(first) || !Valid(second))
            throw new ArgumentException("Invalid approval authority evidence.");
        return new(principal, first, second);
    }
    private static bool Valid(ScopedGrant grant) => grant.AssignmentId != Guid.Empty && grant.AssignmentRevision > 0 &&
        grant.DefinitionId != Guid.Empty && grant.DefinitionRevision > 0 &&
        (grant.ExpiresAtUtc is null || grant.ExpiresAtUtc.Value.Offset == TimeSpan.Zero &&
            grant.ExpiresAtUtc.Value != default && grant.ExpiresAtUtc.Value != DateTimeOffset.MaxValue);
}
