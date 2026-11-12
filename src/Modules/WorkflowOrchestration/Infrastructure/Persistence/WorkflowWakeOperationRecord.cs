namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>Append-only exact publication binding, not an authority or replay decision.</summary>
public sealed class WorkflowWakeOperationRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid StepId { get; set; }
    public Guid TimerId { get; set; }
    public long Generation { get; set; }
    public int SchedulerAttempt { get; set; }
    public long CustodyEpoch { get; set; }
    public Guid SchedulerOperationId { get; set; }
    public Guid WakeOperationId { get; set; }
    public Guid MessageId { get; set; }
    public string DescriptorHash { get; set; } = "";
    public byte[] Descriptor { get; set; } = [];
}
