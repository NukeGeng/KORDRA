using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Modules.IdentityAccess.Authorization;

public enum DurableApprovalAuthorityStatus { Current, Invalidated, Expired, Unavailable }
public sealed record DurableApprovalAuthorityResult(DurableApprovalAuthorityStatus Status);

/// <summary>
/// Revalidates previously captured principal and two exact grant snapshots under caller-held
/// admission. This is an eligibility input, never a sessionless action permit.
/// </summary>
public sealed class DurableApprovalAuthorityReader(IDurablePrincipalReader principals, IDurableGrantEvidenceReader grants,
    IRoleDefinitionReader definitions, IAuthorityClock clock)
{
    public async Task<DurableApprovalAuthorityResult> CheckUnderAdmissionAsync(DurablePrincipal expectedPrincipal,
        ScopedGrant first, ScopedGrant second, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!ValidSelection(expectedPrincipal, first, second)) return new(DurableApprovalAuthorityStatus.Unavailable);
        try
        {
            var before = clock.GetTrustedUtcNow();
            if (!ValidTime(before)) return new(DurableApprovalAuthorityStatus.Unavailable);
            if (Expired(first, before) || Expired(second, before)) return new(DurableApprovalAuthorityStatus.Expired);
            var principal = await principals.ReadUnderAdmissionAsync(expectedPrincipal.PrincipalId, ct);
            if (principal is null || !principal.Active || principal != expectedPrincipal) return new(DurableApprovalAuthorityStatus.Invalidated);
            foreach (var expected in new[] { first, second })
            {
                var current = await grants.CompareUnderAdmissionAsync(expected, ct);
                if (current == DurableGrantEvidenceStatus.Invalidated) return new(DurableApprovalAuthorityStatus.Invalidated);
                if (current != DurableGrantEvidenceStatus.Current) return new(DurableApprovalAuthorityStatus.Unavailable);
                if (definitions is not IDurableRoleEvidenceReader durableRoles) return new(DurableApprovalAuthorityStatus.Unavailable);
                var role = await durableRoles.ReadExactUnderAdmissionAsync(expected.DefinitionId, expected.DefinitionRevision, ct);
                if (role.Status == DurableRoleEvidenceStatus.Invalidated) return new(DurableApprovalAuthorityStatus.Invalidated);
                if (role.Status != DurableRoleEvidenceStatus.Current || role.Definition is null) return new(DurableApprovalAuthorityStatus.Unavailable);
                var definition = role.Definition;
                if (definition.DefinitionId != expected.DefinitionId || definition.Revision != expected.DefinitionRevision ||
                    !FoundationPermissions.Allows(definition, expected.Permission, expected.ScopeKind))
                    return new(DurableApprovalAuthorityStatus.Invalidated);
            }
            var after = clock.GetTrustedUtcNow();
            if (!ValidTime(after) || after < before) return new(DurableApprovalAuthorityStatus.Unavailable);
            return new(Expired(first, after) || Expired(second, after)
                ? DurableApprovalAuthorityStatus.Expired : DurableApprovalAuthorityStatus.Current);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new(DurableApprovalAuthorityStatus.Unavailable); }
    }

    private static bool ValidSelection(DurablePrincipal? p, ScopedGrant? a, ScopedGrant? b)
    {
        if (p is null || !p.Active || p.PrincipalId == Guid.Empty || p.PrincipalRevision <= 0 ||
            p.SecurityRevisionHash is not { Length: 64 } || !p.SecurityRevisionHash.All(char.IsAsciiHexDigitLower) ||
            a is null || b is null || a.PrincipalId != p.PrincipalId || b.PrincipalId != p.PrincipalId ||
            a.TenantId == Guid.Empty || a.TenantId != b.TenantId || a.MembershipId == Guid.Empty || a.MembershipId != b.MembershipId ||
            a.TenantRevision <= 0 || a.TenantRevision != b.TenantRevision || a.MembershipRevision <= 0 || a.MembershipRevision != b.MembershipRevision ||
            !ValidGrant(a) || !ValidGrant(b)) return false;
        return a.Permission == "release.request-approval" && a.ScopeKind == "environment" && b.Permission == "release.read" && b.ScopeKind == "project" ||
            a.Permission == "release.approve" && a.ScopeKind == "environment" && b.Permission == "release.approval.read" && b.ScopeKind == "environment" && a.ScopeId == b.ScopeId;
    }
    private static bool ValidGrant(ScopedGrant g) => g.AssignmentId != Guid.Empty && g.AssignmentRevision > 0 &&
        g.DefinitionId != Guid.Empty && g.DefinitionRevision > 0 && g.ScopeId != Guid.Empty &&
        (g.ExpiresAtUtc is null || ValidTime(g.ExpiresAtUtc.Value));
    private static bool ValidTime(DateTimeOffset t) => t.Offset == TimeSpan.Zero && t != default && t != DateTimeOffset.MaxValue;
    private static bool Expired(ScopedGrant grant, DateTimeOffset now) => grant.ExpiresAtUtc is { } expiry && expiry <= now;
}
