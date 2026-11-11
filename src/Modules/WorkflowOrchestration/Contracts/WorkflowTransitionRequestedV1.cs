namespace Kordra.Modules.WorkflowOrchestration.Contracts;

public enum WorkflowTransitionKind { Checkpoint = 1, TimerDue, Cancel }

/// <summary>Closed business data; neither possession nor a custody epoch grants current authority.</summary>
public sealed record WorkflowTransitionRequestedV1
{
    public Guid InstanceId { get; }
    public Guid StepId { get; }
    public long ExpectedVersion { get; }
    public long CustodyEpoch { get; }
    public Guid OperationId { get; }
    public string SpecificationHash { get; }
    public string TemplateHash { get; }
    public WorkflowTransitionKind TransitionKind { get; }
    public Guid? TimerId { get; }
    public long? Generation { get; }

    public WorkflowTransitionRequestedV1(Guid instanceId, Guid stepId, long expectedVersion, long custodyEpoch,
        Guid operationId, string specificationHash, string templateHash, WorkflowTransitionKind transitionKind,
        Guid? timerId, long? generation)
    {
        if (instanceId == Guid.Empty || stepId == Guid.Empty || operationId == Guid.Empty || expectedVersion < 1 ||
            custodyEpoch < 1 || !ValidHash(specificationHash) || !ValidHash(templateHash) || !Enum.IsDefined(transitionKind))
            throw new ArgumentException("Invalid workflow transition binding.");
        if (transitionKind == WorkflowTransitionKind.TimerDue
            ? timerId is null || timerId == Guid.Empty || generation is null || generation < 1
            : timerId is not null || generation is not null)
            throw new ArgumentException("Timer identity/generation is required only for TimerDue.");
        InstanceId = instanceId; StepId = stepId; ExpectedVersion = expectedVersion; CustodyEpoch = custodyEpoch;
        OperationId = operationId; SpecificationHash = specificationHash; TemplateHash = templateHash;
        TransitionKind = transitionKind; TimerId = timerId; Generation = generation;
    }
    private static bool ValidHash(string value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public override string ToString() => "WorkflowTransitionRequestedV1 [private binding, not authority]";
}
