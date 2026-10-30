namespace Kordra.Modules.ApprovalGovernance.Infrastructure;

// Persistence records only: authority, immutable-subject validation and audit are the caller's responsibility.
public sealed class ApprovalRequestRecord
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EnvironmentId { get; set; }
    public Guid ReleaseRevisionId { get; set; }
    public Guid RequesterPrincipalId { get; set; }
    public byte[] SpecHash { get; set; } = [];
    public byte[] RequesterEvidence { get; set; } = [];
    public long Version { get; set; } = 1;
    public string State { get; set; } = "Pending";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset PendingExpiresAtUtc { get; set; }
    public DateTimeOffset? GrantedAtUtc { get; set; }
    public DateTimeOffset? GrantExpiresAtUtc { get; set; }
    public Guid? ApproverPrincipalId { get; set; }
    public byte[]? ApproverEvidence { get; set; }
}
public sealed class ApprovalDecisionRecord
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid RequestId { get; set; }
    public long RequestVersion { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid ActorPrincipalId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}
public sealed class ApprovalRequestCommandReceipt
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid PrincipalId { get; set; }
    public Guid RequestId { get; set; }
    public string Action { get; set; } = string.Empty;
    public byte[] ScopedKeyHash { get; set; } = [];
    public byte[] BusinessHash { get; set; } = [];
    public Guid AuditIntentId { get; set; }
    public byte[] AuditIntentHash { get; set; } = [];
    public Guid BootId { get; set; }
    public Guid InvocationId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
