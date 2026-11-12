using System.Security.Cryptography;
using System.Text.Json;

namespace Kordra.Modules.WorkflowOrchestration.Domain;

/// <summary>Immutable cancellation command data. Actor/scope must come from current trusted admission, not wire authority.</summary>
public sealed class WorkflowCancellationIntent
{
    public Guid InstallationId { get; }
    public Guid TenantId { get; }
    public Guid InstanceId { get; }
    public Guid RequestId { get; }
    public Guid ActorId { get; }
    public long ExpectedVersion { get; }
    public string SpecificationHash { get; }
    public const string ReasonCode = "OperatorRequested";

    public WorkflowCancellationIntent(Guid installationId, Guid tenantId, Guid instanceId, Guid requestId,
        Guid actorId, long expectedVersion, string specificationHash)
    {
        if (new[] { installationId, tenantId, instanceId, requestId, actorId }.Any(x => x == Guid.Empty) ||
            expectedVersion is < 1 or long.MaxValue || specificationHash is not { Length: 64 } ||
            specificationHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Exact cancellation binding and bounded expected version required.");
        InstallationId = installationId; TenantId = tenantId; InstanceId = instanceId; RequestId = requestId;
        ActorId = actorId; ExpectedVersion = expectedVersion; SpecificationHash = specificationHash;
    }

    // Server receipt time is not command content: a retry must return its original result, never acquire a new timestamp/hash.
    public string CommandHash
    {
        get
        {
            using var output = new MemoryStream();
            using (var json = new Utf8JsonWriter(output))
            {
                json.WriteStartObject(); json.WriteNumber("schemaVersion", 1); json.WriteString("kind", "workflow.cancel");
                json.WriteString("installationId", InstallationId.ToString("D")); json.WriteString("tenantId", TenantId.ToString("D"));
                json.WriteString("instanceId", InstanceId.ToString("D")); json.WriteString("requestId", RequestId.ToString("D"));
                json.WriteString("actorId", ActorId.ToString("D")); json.WriteNumber("expectedVersion", ExpectedVersion);
                json.WriteString("specificationHash", SpecificationHash); json.WriteString("reasonCode", ReasonCode); json.WriteEndObject();
            }
            return Convert.ToHexStringLower(SHA256.HashData(output.ToArray()));
        }
    }
    public override string ToString() => "WorkflowCancellationIntent [command data, not authority]";
}
