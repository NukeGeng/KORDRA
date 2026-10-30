using Kordra.ApprovalGovernance.Domain;
using Xunit;

namespace Kordra.ApprovalGovernance.Domain.Tests;

public sealed class ApprovalEffectiveStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(ApprovalRecordedState.Rejected, ApprovalEffectiveStatus.Rejected, "rejected")]
    [InlineData(ApprovalRecordedState.Revoked, ApprovalEffectiveStatus.Revoked, "revoked")]
    public void TerminalHistoryDoesNotBecomeAGrantWhenDependenciesAreUnavailable(ApprovalRecordedState recorded, ApprovalEffectiveStatus expected, string reason)
        => Assert.Equal(new(expected, reason), ApprovalEffectiveState.Evaluate(recorded, Now, Now, null, ApprovalEvidenceStatus.Unavailable, null, null));

    [Theory]
    [InlineData(ApprovalRecordedState.Pending)]
    [InlineData(ApprovalRecordedState.Granted)]
    public void ExpiryEqualityPrecedesUnavailableOrInvalidatedEvidence(ApprovalRecordedState recorded)
    {
        var grant = recorded == ApprovalRecordedState.Granted ? Now : (DateTimeOffset?)null;
        Assert.Equal(new(ApprovalEffectiveStatus.Expired, "expired"), ApprovalEffectiveState.Evaluate(recorded, Now, Now, grant, ApprovalEvidenceStatus.Unavailable, null, null));
        Assert.Equal(new(ApprovalEffectiveStatus.Expired, "expired"), ApprovalEffectiveState.Evaluate(recorded, Now, Now, grant, ApprovalEvidenceStatus.Invalidated, "policy_changed", false));
    }
    [Fact]
    public void KnownInvalidationBeatsBlockedWindowButUnknownNeverProducesGrant()
    {
        var deadline = Now.AddMinutes(5);
        Assert.Equal(new(ApprovalEffectiveStatus.Invalidated, "assignment_changed"), ApprovalEffectiveState.Evaluate(ApprovalRecordedState.Granted, Now, deadline, deadline, ApprovalEvidenceStatus.Invalidated, "assignment_changed", true));
        Assert.Equal(new(ApprovalEffectiveStatus.Unavailable, null), ApprovalEffectiveState.Evaluate(ApprovalRecordedState.Granted, Now, deadline, deadline, ApprovalEvidenceStatus.Unavailable, null, false));
        Assert.Equal(new(ApprovalEffectiveStatus.Unavailable, null), ApprovalEffectiveState.Evaluate(ApprovalRecordedState.Granted, Now, deadline, deadline, ApprovalEvidenceStatus.Invalidated, "unrecognized", false));
        Assert.Equal(new(ApprovalEffectiveStatus.Unavailable, null), ApprovalEffectiveState.Evaluate(ApprovalRecordedState.Granted, Now, deadline, deadline, ApprovalEvidenceStatus.Current, null, null));
    }
    [Fact]
    public void BlockedPendingRemainsPendingAndInconsistentLiveGrantFailsClosed()
    {
        var deadline = Now.AddMinutes(5);
        Assert.Equal(new(ApprovalEffectiveStatus.Pending, "approval_window_blocked"), ApprovalEffectiveState.Evaluate(ApprovalRecordedState.Pending, Now, deadline, null, ApprovalEvidenceStatus.Current, null, true));
        Assert.Equal(new(ApprovalEffectiveStatus.Unavailable, null), ApprovalEffectiveState.Evaluate(ApprovalRecordedState.Granted, Now, deadline, deadline, ApprovalEvidenceStatus.Current, null, true));
        Assert.Equal(new(ApprovalEffectiveStatus.Granted, "eligible"), ApprovalEffectiveState.Evaluate(ApprovalRecordedState.Granted, Now, deadline, deadline, ApprovalEvidenceStatus.Current, null, false));
    }
    [Fact]
    public void MalformedPersistedDeadlinesDoNotProduceCurrentState()
    {
        var deadline = Now.AddMinutes(5);
        Assert.Equal(new(ApprovalEffectiveStatus.Unavailable, null), ApprovalEffectiveState.Evaluate(ApprovalRecordedState.Granted, Now, deadline, deadline.AddSeconds(1), ApprovalEvidenceStatus.Current, null, false));
        Assert.Equal(new(ApprovalEffectiveStatus.Unavailable, null), ApprovalEffectiveState.Evaluate(ApprovalRecordedState.Pending, Now, deadline, deadline, ApprovalEvidenceStatus.Current, null, false));
        Assert.Equal(new(ApprovalEffectiveStatus.Unavailable, null), ApprovalEffectiveState.Evaluate((ApprovalRecordedState)99, Now, deadline, null, ApprovalEvidenceStatus.Current, null, false));
    }
}
