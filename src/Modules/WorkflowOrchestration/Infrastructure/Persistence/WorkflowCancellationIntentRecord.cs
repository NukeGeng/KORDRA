namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>Append-only owner cancellation receipt; no undo or confirmed terminal effect.</summary>
public sealed class WorkflowCancellationIntentRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid RequestId { get; set; }
    public Guid ActorId { get; set; }
    public string SpecificationHash { get; set; } = "";
    public string CommandHash { get; set; } = "";
    public long ExpectedVersion { get; set; }
    public long ResultingVersion { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
