namespace Kordra.Modules.WorkflowOrchestration.Contracts;

/// <summary>Exact retained scheduler binding to inspect. Never authority, terminal-backend proof or replay permission.</summary>
public sealed record WorkflowWakeReceiptExpectation
{
    public WorkflowReceiptExpectation Scheduler { get; }
    public Guid TimerId { get; }
    public long Generation { get; }
    public string DescriptorHash { get; }

    public WorkflowWakeReceiptExpectation(WorkflowReceiptExpectation scheduler, Guid timerId, long generation, string descriptorHash)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (scheduler.OperationId is null || scheduler.StepId is null || timerId == Guid.Empty || generation < 1 ||
            descriptorHash is not { Length: 64 } || descriptorHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Exact scheduler operation, timer generation and canonical descriptor hash required.");
        Scheduler = scheduler; TimerId = timerId; Generation = generation; DescriptorHash = descriptorHash;
    }
    public override string ToString() => "WorkflowWakeReceiptExpectation [private, not authority]";
}

public interface IWorkflowWakeReceiptInspector
{
    Task<WorkflowReceiptInspectionStatus> InspectAsync(WorkflowWakeReceiptExpectation expected, CancellationToken ct = default);
}
