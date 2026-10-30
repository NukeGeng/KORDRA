using Kordra.ApprovalGovernance.Domain;
using Xunit;

namespace Kordra.ApprovalGovernance.Domain.Tests;

public sealed class ApprovalDecisionTransitionTests
{
    [Theory]
    [InlineData(ApprovalRecordedState.Pending, ApprovalDecisionAction.Grant, "approved", ApprovalRecordedState.Granted)]
    [InlineData(ApprovalRecordedState.Pending, ApprovalDecisionAction.Reject, "incorrect_artifact", ApprovalRecordedState.Rejected)]
    [InlineData(ApprovalRecordedState.Pending, ApprovalDecisionAction.Revoke, "requester_cancelled", ApprovalRecordedState.Revoked)]
    [InlineData(ApprovalRecordedState.Granted, ApprovalDecisionAction.Revoke, "owner_cancelled", ApprovalRecordedState.Revoked)]
    public void OnlyApprovedTransitionsAdvanceRecordedState(ApprovalRecordedState state, ApprovalDecisionAction action, string reason, ApprovalRecordedState expected)
        => Assert.Equal(expected, ApprovalDecisionTransition.Next(state, action, reason));

    [Theory]
    [InlineData(ApprovalRecordedState.Granted, ApprovalDecisionAction.Grant, "approved")]
    [InlineData(ApprovalRecordedState.Granted, ApprovalDecisionAction.Reject, "policy_denied")]
    [InlineData(ApprovalRecordedState.Rejected, ApprovalDecisionAction.Grant, "approved")]
    [InlineData(ApprovalRecordedState.Rejected, ApprovalDecisionAction.Revoke, "obsolete")]
    [InlineData(ApprovalRecordedState.Revoked, ApprovalDecisionAction.Grant, "approved")]
    [InlineData((ApprovalRecordedState)99, ApprovalDecisionAction.Grant, "approved")]
    public void TerminalOrIncompatibleTransitionsCannotResurrectApproval(ApprovalRecordedState state, ApprovalDecisionAction action, string reason)
        => Assert.Throws<InvalidOperationException>(() => ApprovalDecisionTransition.Next(state, action, reason));

    [Theory]
    [InlineData(ApprovalDecisionAction.Grant, "obsolete")]
    [InlineData(ApprovalDecisionAction.Reject, "approved")]
    [InlineData(ApprovalDecisionAction.Revoke, "policy_denied")]
    [InlineData((ApprovalDecisionAction)99, "approved")]
    [InlineData(ApprovalDecisionAction.Grant, "Approved")]
    [InlineData(ApprovalDecisionAction.Grant, null)]
    public void RejectsUnknownOrWrongActionReason(ApprovalDecisionAction action, string? reason)
        => Assert.Throws<ArgumentException>(() => ApprovalDecisionTransition.Next(ApprovalRecordedState.Pending, action, reason!));
}
