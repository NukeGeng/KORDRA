namespace Kordra.Modules.WorkflowOrchestration.Contracts;

public sealed record WorkflowMetadataPage(Guid InstanceId, long Version, int State, bool DeadlineExceeded,
    DateTimeOffset DeadlineUtc, IReadOnlyList<WorkflowStepMetadata> Steps, IReadOnlyList<WorkflowTransitionMetadata> History,
    long? NextBeforeVersion);

public sealed record WorkflowStepMetadata(Guid StepId, int Ordinal, long Version, int State,
    DateTimeOffset? DueAtUtc);

public sealed record WorkflowTransitionMetadata(long Version, Guid StepId, int State, DateTimeOffset RecordedAtUtc);
