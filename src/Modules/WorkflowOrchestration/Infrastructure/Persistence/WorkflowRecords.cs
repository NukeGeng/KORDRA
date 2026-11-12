namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

// Storage records are owner-internal data shapes, never authorization or HTTP input.
public sealed class WorkflowInstanceRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid RequestId { get; set; }
    public string SpecificationHash { get; set; } = "";
    public byte[] Specification { get; set; } = [];
    public int State { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset DeadlineUtc { get; set; }
    public bool DeadlineExceeded { get; set; }
}

public sealed class WorkflowStepRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid StepId { get; set; }
    public int Ordinal { get; set; }
    public int State { get; set; }
    public long Version { get; set; }
}

public sealed class WorkflowTransitionRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid OperationId { get; set; }
    public Guid StepId { get; set; }
    public long ResultingVersion { get; set; }
    public int ResultingState { get; set; }
    public string SpecificationHash { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed class WorkflowTimerRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid StepId { get; set; }
    public Guid TimerId { get; set; }
    public long Generation { get; set; }
    public long Version { get; set; }
    public int Status { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public int SchedulerAttempts { get; set; }
}

public sealed class WorkflowCustodyRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid StepId { get; set; }
    public Guid OperationId { get; set; }
    public string SpecificationHash { get; set; } = "";
    public Guid WorkerId { get; set; }
    public Guid WorkerBootId { get; set; }
    public long Epoch { get; set; }
    public long Version { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset HeartbeatAtUtc { get; set; }
    public int Phase { get; set; }
    public Guid? LastCommitReceiptId { get; set; }
}

public sealed class WorkflowRecoveryDecisionRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid DecisionId { get; set; }
    public Guid StepId { get; set; }
    public Guid OperationId { get; set; }
    public int Kind { get; set; }
    public string EvidenceHash { get; set; } = "";
    public string SpecificationHash { get; set; } = "";
    public long ResultingVersion { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed class WorkflowRequestReceiptRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid RequestId { get; set; }
    public string ContentHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed class WorkflowAuthorityBindingRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid BindingId { get; set; }
    public Guid DelegationId { get; set; }
    public Guid ActorId { get; set; }
    public long Generation { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    // Revision 6 (12.2f) reauthorization receipt; ignored by revisions 1–5.
    public Guid RequestId { get; set; }
    public string CommandHash { get; set; } = "";
    public string SpecificationHash { get; set; } = "";
    public long ExpectedVersion { get; set; }
    public long ResultingVersion { get; set; }
}

public sealed class WorkflowClockFloorRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public DateTimeOffset FloorUtc { get; set; }
}

