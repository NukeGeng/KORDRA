namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

internal sealed class WorkflowStartReceiptRecord
{
    public Guid InstallationId { get; init; }
    public Guid TenantId { get; init; }
    public Guid ActorId { get; init; }
    public Guid RequestId { get; init; }
    public string ScopedKeyHash { get; init; } = "";
    public string BusinessHash { get; init; } = "";
}
