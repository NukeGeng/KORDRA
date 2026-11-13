using System.Security.Cryptography;
using System.Text.Json;

namespace Kordra.Modules.WorkflowOrchestration.Domain;

/// <summary>
/// Immutable reauthorization command data (12.2f). Actor and scope must come from current trusted admission,
/// never from wire authority. Recording it never moves a workflow out of BlockedAuthority.
/// </summary>
public sealed class WorkflowReauthorizationCommand
{
    public Guid InstallationId { get; }
    public Guid TenantId { get; }
    public Guid InstanceId { get; }
    public Guid RequestId { get; }
    public Guid ActorId { get; }
    public long ExpectedVersion { get; }
    public string SpecificationHash { get; }

    public WorkflowReauthorizationCommand(Guid installationId, Guid tenantId, Guid instanceId, Guid requestId,
        Guid actorId, long expectedVersion, string specificationHash)
    {
        if (new[] { installationId, tenantId, instanceId, requestId, actorId }.Any(x => x == Guid.Empty) ||
            expectedVersion is < 1 or long.MaxValue || specificationHash is not { Length: 64 } ||
            specificationHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Exact reauthorization binding and bounded expected version required.");
        InstallationId = installationId; TenantId = tenantId; InstanceId = instanceId; RequestId = requestId;
        ActorId = actorId; ExpectedVersion = expectedVersion; SpecificationHash = specificationHash;
    }

    // Server receipt time and delegation references are not command content: a retry returns its original result.
    public string CommandHash
    {
        get
        {
            using var output = new MemoryStream();
            using (var json = new Utf8JsonWriter(output))
            {
                json.WriteStartObject(); json.WriteNumber("schemaVersion", 1); json.WriteString("kind", "workflow.reauthorize");
                json.WriteString("installationId", InstallationId.ToString("D")); json.WriteString("tenantId", TenantId.ToString("D"));
                json.WriteString("instanceId", InstanceId.ToString("D")); json.WriteString("requestId", RequestId.ToString("D"));
                json.WriteString("actorId", ActorId.ToString("D")); json.WriteNumber("expectedVersion", ExpectedVersion);
                json.WriteString("specificationHash", SpecificationHash); json.WriteEndObject();
            }
            return Convert.ToHexStringLower(SHA256.HashData(output.ToArray()));
        }
    }
    public override string ToString() => "WorkflowReauthorizationCommand [command data, not authority]";
}

/// <summary>Owner facts read under the instance lock. Data only; not proof of authority.</summary>
public sealed record WorkflowReauthorizationSnapshot(WorkflowState State, DateTimeOffset CreatedAtUtc, DateTimeOffset DeadlineUtc,
    bool HasCancellationIntent, bool HasUnsettledCustody, bool HasUnknownStep, bool HasPublishedTimer,
    long CurrentGeneration, Guid CurrentActorId);

/// <summary>Structural reauthorization preconditions only. Current permission, delegation and epoch are separate gates.</summary>
public static class WorkflowReauthorizationRules
{
    /// <returns>The next authority-binding generation.</returns>
    public static long RequireEligible(WorkflowReauthorizationCommand command, WorkflowReauthorizationSnapshot snapshot,
        DateTimeOffset effectiveUtc)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (effectiveUtc == default) throw new ArgumentException("Trusted effective clock required.");
        effectiveUtc = effectiveUtc.ToUniversalTime();
        if (snapshot.State != WorkflowState.BlockedAuthority)
            throw new InvalidOperationException("Reauthorization requires BlockedAuthority.");
        if (snapshot.HasCancellationIntent || snapshot.HasUnsettledCustody || snapshot.HasUnknownStep || snapshot.HasPublishedTimer)
            throw new InvalidOperationException("Reauthorization requires no cancellation or unresolved operation.");
        if (effectiveUtc < snapshot.CreatedAtUtc || effectiveUtc >= snapshot.DeadlineUtc)
            throw new InvalidOperationException("Reauthorization is outside the original workflow window.");
        if (snapshot.CurrentGeneration < 1 || snapshot.CurrentActorId == Guid.Empty)
            throw new InvalidOperationException("Reauthorization requires an existing original authority binding.");
        if (snapshot.CurrentActorId != command.ActorId)
            throw new InvalidOperationException("Only the original actor may reauthorize this workflow.");
        if (snapshot.CurrentGeneration == long.MaxValue)
            throw new InvalidOperationException("Authority binding generation exhausted.");
        return snapshot.CurrentGeneration + 1;
    }
}
