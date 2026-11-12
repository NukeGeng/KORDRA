using System.Security.Cryptography;
using System.Text.Json;

namespace Kordra.Modules.WorkflowOrchestration.Domain;

/// <summary>Stable start request before allocation of instance IDs or server timestamps. Trusted scope is supplied by the caller.</summary>
public sealed class WorkflowStartCommand
{
    public Guid InstallationId { get; }
    public Guid TenantId { get; }
    public Guid ActorId { get; }
    public Guid RequestId { get; }
    public string ScopedKeyHash { get; }
    public string BusinessHash { get; }

    public WorkflowStartCommand(Guid installationId, Guid tenantId, Guid actorId, Guid projectId,
        Guid environmentId, Guid requestId, Guid releaseId, long releaseRevision, string releaseHash,
        string templateId, int templateVersion, DateTimeOffset originalDeadlineUtc, string idempotencyKey)
    {
        if (new[] { installationId, tenantId, actorId, projectId, environmentId, requestId, releaseId }.Contains(Guid.Empty) ||
            releaseRevision <= 0 || releaseHash is not { Length: 64 } ||
            releaseHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            templateId != WorkflowSpecification.TemplateId || templateVersion != WorkflowSpecification.TemplateVersion ||
            originalDeadlineUtc.Offset != TimeSpan.Zero || originalDeadlineUtc == default ||
            originalDeadlineUtc == DateTimeOffset.MaxValue ||
            idempotencyKey is not { Length: >= 1 and <= 128 } || idempotencyKey.Any(c => c is < '!' or > '~'))
            throw new ArgumentException("Invalid workflow start binding or idempotency key.");

        (InstallationId, TenantId, ActorId, RequestId) = (installationId, tenantId, actorId, requestId);
        ScopedKeyHash = Hash(new { schemaVersion = 1, action = "workflow.start", installationId, tenantId, actorId, idempotencyKey });
        BusinessHash = Fingerprint(projectId, environmentId, requestId, releaseId, releaseRevision, releaseHash,
            templateId, templateVersion, originalDeadlineUtc);
    }

    public bool Matches(WorkflowSpecification specification) =>
        specification.InstallationId == InstallationId && specification.TenantId == TenantId &&
        BusinessHash == Fingerprint(specification.ProjectId, specification.EnvironmentId, specification.RequestId,
            specification.ReleaseId, specification.ReleaseRevision, specification.ReleaseHash, WorkflowSpecification.TemplateId,
            WorkflowSpecification.TemplateVersion, specification.DeadlineUtc);

    private static string Fingerprint(Guid projectId, Guid environmentId, Guid requestId, Guid releaseId,
        long releaseRevision, string releaseHash, string templateId, int templateVersion, DateTimeOffset originalDeadlineUtc) =>
        Hash(new
        {
            schemaVersion = 1, action = "workflow.start", projectId, environmentId, requestId, releaseId,
            releaseRevision, releaseHash, templateId, templateVersion, originalDeadlineUtc = originalDeadlineUtc.UtcTicks
        });

    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
