namespace Kordra.ApprovalGovernance.Domain;

/// <summary>
/// Closed approval history transitions only. A valid transition is not an authorization,
/// eligibility decision, persistence operation, or grant of execution authority.
/// </summary>
public static class ApprovalDecisionTransition
{
    public static ApprovalRecordedState Next(ApprovalRecordedState current, ApprovalDecisionAction action, string reasonCode)
    {
        var validReason = action switch
        {
            ApprovalDecisionAction.Grant => reasonCode == "approved",
            ApprovalDecisionAction.Reject => reasonCode is "changes_required" or "incorrect_artifact" or "incorrect_target" or "policy_denied",
            ApprovalDecisionAction.Revoke => reasonCode is "obsolete" or "requester_cancelled" or "owner_cancelled",
            _ => false
        };
        if (!validReason) throw new ArgumentException("Invalid approval decision reason.", nameof(reasonCode));

        return (current, action) switch
        {
            (ApprovalRecordedState.Pending, ApprovalDecisionAction.Grant) => ApprovalRecordedState.Granted,
            (ApprovalRecordedState.Pending, ApprovalDecisionAction.Reject) => ApprovalRecordedState.Rejected,
            (ApprovalRecordedState.Pending, ApprovalDecisionAction.Revoke) => ApprovalRecordedState.Revoked,
            (ApprovalRecordedState.Granted, ApprovalDecisionAction.Revoke) => ApprovalRecordedState.Revoked,
            _ => throw new InvalidOperationException("Approval decision is not permitted from this recorded state.")
        };
    }
}

public enum ApprovalRecordedState { Pending, Granted, Rejected, Revoked }
public enum ApprovalDecisionAction { Grant, Reject, Revoke }
