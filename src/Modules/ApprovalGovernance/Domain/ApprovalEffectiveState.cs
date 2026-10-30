namespace Kordra.ApprovalGovernance.Domain;

public enum ApprovalEffectiveStatus { Pending, Granted, Rejected, Revoked, Expired, Invalidated, Unavailable }
public enum ApprovalEvidenceStatus { Current, Invalidated, Unavailable }
public sealed record ApprovalEffectiveResult(ApprovalEffectiveStatus Status, string? ReasonCode);

/// <summary>
/// Projects recorded history after the caller separately authorizes the reader and obtains
/// current owner evidence. This projection is never an action permit or execution authority.
/// </summary>
public static class ApprovalEffectiveState
{
    public static ApprovalEffectiveResult Evaluate(ApprovalRecordedState recorded, DateTimeOffset nowUtc,
        DateTimeOffset pendingExpiresUtc, DateTimeOffset? grantExpiresUtc,
        ApprovalEvidenceStatus evidence, string? invalidationReason, bool? policyWindowBlocked)
    {
        if (recorded == ApprovalRecordedState.Rejected) return new(ApprovalEffectiveStatus.Rejected, "rejected");
        if (recorded == ApprovalRecordedState.Revoked) return new(ApprovalEffectiveStatus.Revoked, "revoked");
        if (recorded is not (ApprovalRecordedState.Pending or ApprovalRecordedState.Granted) ||
            nowUtc.Offset != TimeSpan.Zero || pendingExpiresUtc.Offset != TimeSpan.Zero ||
            (recorded == ApprovalRecordedState.Pending && grantExpiresUtc is not null) ||
            (recorded == ApprovalRecordedState.Granted && (grantExpiresUtc is null ||
                grantExpiresUtc.Value.Offset != TimeSpan.Zero || grantExpiresUtc.Value > pendingExpiresUtc)))
            return new(ApprovalEffectiveStatus.Unavailable, null);

        var deadline = recorded == ApprovalRecordedState.Pending ? pendingExpiresUtc : grantExpiresUtc!.Value;
        if (nowUtc >= deadline) return new(ApprovalEffectiveStatus.Expired, "expired");
        if (evidence == ApprovalEvidenceStatus.Invalidated)
            return invalidationReason is "release_retired" or "subject_changed" or "policy_changed" or "principal_inactive" or "assignment_changed" or "selection_changed"
                ? new(ApprovalEffectiveStatus.Invalidated, invalidationReason)
                : new(ApprovalEffectiveStatus.Unavailable, null);
        if (evidence != ApprovalEvidenceStatus.Current || invalidationReason is not null || policyWindowBlocked is null)
            return new(ApprovalEffectiveStatus.Unavailable, null);
        if (policyWindowBlocked.Value)
            return recorded == ApprovalRecordedState.Pending
                ? new(ApprovalEffectiveStatus.Pending, "approval_window_blocked")
                : new(ApprovalEffectiveStatus.Unavailable, null); // A valid original grant deadline cannot cross a blocked window.
        return recorded == ApprovalRecordedState.Pending
            ? new(ApprovalEffectiveStatus.Pending, "eligible")
            : new(ApprovalEffectiveStatus.Granted, "eligible");
    }
}
