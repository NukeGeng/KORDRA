namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

internal sealed class WorkflowOperatorDecisionRecord
{
    public Guid InstallationId { get; init; }
    public Guid TenantId { get; init; }
    public Guid InstanceId { get; init; }
    public Guid RequestId { get; init; }
    public Guid ActorId { get; init; }
    public Guid OperationId { get; init; }
    public int Kind { get; init; }
    public long ExpectedVersion { get; init; }
    public long ResultingVersion { get; init; }
    public string CommandHash { get; init; } = "";
    public string EvidenceHash { get; init; } = "";
    public string SpecificationHash { get; init; } = "";
    public DateTimeOffset RecordedAtUtc { get; init; }
}
