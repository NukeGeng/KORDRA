using Kordra.Api;
using Kordra.Modules.AuditCompliance.Contracts;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowCommitClassificationTests
{
    [Theory]
    [InlineData(AuditAppendStatus.Durable, false, WorkflowCommitDisposition.NoCommitAttempt)]
    [InlineData(AuditAppendStatus.Rejected, false, WorkflowCommitDisposition.NoCommitAttempt)]
    [InlineData(AuditAppendStatus.Unavailable, false, WorkflowCommitDisposition.NoCommitAttempt)]
    [InlineData(AuditAppendStatus.Unknown, false, WorkflowCommitDisposition.NoCommitAttempt)]
    [InlineData(AuditAppendStatus.Durable, true, WorkflowCommitDisposition.Committed)]
    [InlineData(AuditAppendStatus.Rejected, true, WorkflowCommitDisposition.Unknown)]
    [InlineData(AuditAppendStatus.Unavailable, true, WorkflowCommitDisposition.Unknown)]
    [InlineData(AuditAppendStatus.Unknown, true, WorkflowCommitDisposition.Unknown)]
    public void OnlyANewConfirmedRootCommitAllowsFlush(AuditAppendStatus audit, bool attempted, WorkflowCommitDisposition expected)
    {
        // AUD can return an existing durable intent after this caller root rolled back.
        // Such an intent is not WFL/MSG atomicity and must not authorize native delivery.
        Assert.Equal(expected, WorkflowCommitClassification.Classify(audit, attempted));
    }
}
