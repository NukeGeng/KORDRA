namespace Kordra.Modules.WorkflowOrchestration.Contracts;

/// <summary>First compiled result tuple: pure TimerDue completed, workflow still Running. Not deployment verification.</summary>
public sealed record WorkflowTransitionRecordedV1
{
    public Guid InstanceId { get; }
    public Guid StepId { get; }
    public Guid OperationId { get; }
    public long ResultingVersion { get; }
    public string SpecificationHash { get; }
    public string Result => "Completed";
    public string State => "Running";
    public string Reason => "TimerDueElapsed";

    public WorkflowTransitionRecordedV1(Guid instanceId, Guid stepId, Guid operationId, long resultingVersion, string specificationHash)
    {
        if (instanceId == Guid.Empty || stepId == Guid.Empty || operationId == Guid.Empty || resultingVersion < 2 ||
            specificationHash is not { Length: 64 } || specificationHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Invalid closed workflow transition result binding.");
        InstanceId = instanceId; StepId = stepId; OperationId = operationId;
        ResultingVersion = resultingVersion; SpecificationHash = specificationHash;
    }
    public override string ToString() => "WorkflowTransitionRecordedV1 [private, not deployment verification]";
}
