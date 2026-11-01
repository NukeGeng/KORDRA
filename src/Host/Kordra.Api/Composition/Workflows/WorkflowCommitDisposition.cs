using Kordra.Modules.AuditCompliance.Contracts;

namespace Kordra.Api;

public enum WorkflowCommitDisposition { Committed, NoCommitAttempt, Unknown }

/// <summary>AUD replay is not evidence this root committed. NoCommitAttempt does not prove rollback/backend termination.</summary>
public static class WorkflowCommitClassification
{
    public static WorkflowCommitDisposition Classify(AuditAppendStatus status, bool actualCommitAttempted) =>
        actualCommitAttempted ? status == AuditAppendStatus.Durable ? WorkflowCommitDisposition.Committed : WorkflowCommitDisposition.Unknown
            : WorkflowCommitDisposition.NoCommitAttempt;
}
