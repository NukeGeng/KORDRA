namespace Kordra.Modules.WorkflowOrchestration.Contracts;

/// <summary>Owner-resolved concurrency facts, not service authentication or permission.</summary>
public sealed record WorkflowTimerCustodyExpectation
{
    public Guid WorkerId { get; }
    public Guid WorkerBootId { get; }
    public long Epoch { get; }
    public long Version { get; }
    public WorkflowTimerCustodyExpectation(Guid workerId, Guid workerBootId, long epoch, long version)
    {
        if (workerId == Guid.Empty || workerBootId == Guid.Empty || epoch < 1 || version < 1)
            throw new ArgumentException("Invalid timer custody expectation.");
        WorkerId = workerId; WorkerBootId = workerBootId; Epoch = epoch; Version = version;
    }
    public override string ToString() => "WorkflowTimerCustodyExpectation [private, not authority]";
}

public sealed record WorkflowTimerScheduleRequest
{
    public WorkflowIdentity Identity { get; }
    public Guid StepId { get; }
    public Guid OperationId { get; }
    public string SpecificationHash { get; }
    public long ExpectedWorkflowVersion { get; }
    public long ExpectedStepVersion { get; }
    public WorkflowTimerCustodyExpectation Custody { get; }
    public DateTimeOffset RecordedAtUtc { get; }
    public WorkflowTimerScheduleRequest(WorkflowIdentity identity, Guid stepId, Guid operationId,
        string specificationHash, long expectedWorkflowVersion, long expectedStepVersion,
        WorkflowTimerCustodyExpectation custody, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(custody);
        if (stepId == Guid.Empty || operationId == Guid.Empty || expectedWorkflowVersion is < 1 or long.MaxValue ||
            expectedStepVersion is < 1 or long.MaxValue || recordedAtUtc == default ||
            specificationHash is not { Length: 64 } || specificationHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Invalid waiting-timer binding.");
        Identity = identity; StepId = stepId; OperationId = operationId; SpecificationHash = specificationHash;
        ExpectedWorkflowVersion = expectedWorkflowVersion; ExpectedStepVersion = expectedStepVersion;
        Custody = custody; RecordedAtUtc = recordedAtUtc.ToUniversalTime();
    }
    public override string ToString() => "WorkflowTimerScheduleRequest [private, not authority]";
}

public sealed record WorkflowTimerScheduleResult(Guid TimerId, long Generation, long WorkflowVersion, bool Duplicate);

/// <summary>Read-only bounded discovery. Possession cannot dispatch, renew or authorize a wake.</summary>
public sealed record WorkflowDueTimerSnapshot(WorkflowIdentity Identity, Guid StepId, Guid TimerId,
    long Generation, long Version, DateTimeOffset DueAtUtc, int SchedulerAttempts)
{
    public override string ToString() => "WorkflowDueTimerSnapshot [private, not authority]";
}
