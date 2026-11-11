namespace Kordra.Modules.WorkflowOrchestration.Contracts;

public enum WorkflowReceiptInspectionStatus { MatchingObserved, Conflict, StillUnknown, Unavailable }

/// <summary>Trusted exact-key query data only. Never a permit or a request to replay an operation.</summary>
public sealed record WorkflowReceiptExpectation
{
    public WorkflowIdentity Identity { get; }
    public Guid RequestId { get; }
    public string SpecificationHash { get; }
    public long ResultingVersion { get; }
    public Guid? OperationId { get; }
    public Guid? StepId { get; }
    public WorkflowReceiptExpectation(WorkflowIdentity identity, Guid requestId, string specificationHash,
        long resultingVersion, Guid? operationId = null, Guid? stepId = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (requestId == Guid.Empty || resultingVersion < 1 || specificationHash is not { Length: 64 } ||
            specificationHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            operationId.HasValue != stepId.HasValue || operationId == Guid.Empty || stepId == Guid.Empty ||
            (operationId is null && resultingVersion != 1) || (operationId is not null && resultingVersion < 2))
            throw new ArgumentException("Invalid exact workflow receipt expectation.");
        Identity = identity; RequestId = requestId; SpecificationHash = specificationHash;
        ResultingVersion = resultingVersion; OperationId = operationId; StepId = stepId;
    }
    public override string ToString() => "WorkflowReceiptExpectation [private, not authority]";
}

public interface IWorkflowReceiptInspector
{
    Task<WorkflowReceiptInspectionStatus> InspectAsync(WorkflowReceiptExpectation expected, CancellationToken ct = default);
}
