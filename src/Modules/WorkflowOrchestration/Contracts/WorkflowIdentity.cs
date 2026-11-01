namespace Kordra.Modules.WorkflowOrchestration.Contracts;

/// <summary>Owner-resolved identity data. This is never an authorization permit.</summary>
public sealed record WorkflowIdentity
{
    public Guid InstallationId { get; }
    public Guid TenantId { get; }
    public Guid InstanceId { get; }
    public WorkflowIdentity(Guid installationId, Guid tenantId, Guid instanceId)
    {
        if (installationId == Guid.Empty || tenantId == Guid.Empty || instanceId == Guid.Empty)
            throw new ArgumentException("Nonempty installation, tenant and instance required.");
        InstallationId = installationId; TenantId = tenantId; InstanceId = instanceId;
    }
}
