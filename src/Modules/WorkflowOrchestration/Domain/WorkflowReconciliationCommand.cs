using System.Security.Cryptography;
using System.Text.Json;

namespace Kordra.Modules.WorkflowOrchestration.Domain;

public enum WorkflowReconciliationKind { BindMatchingCommittedReceipt = 1, ConfirmKnownRollback = 2 }

/// <summary>Closed operator intent. Current authority, custody and resolved evidence are checked independently.</summary>
public sealed class WorkflowReconciliationCommand
{
    public Guid InstallationId { get; }
    public Guid TenantId { get; }
    public Guid InstanceId { get; }
    public Guid RequestId { get; }
    public Guid ActorId { get; }
    public Guid OperationId { get; }
    public long ExpectedVersion { get; }
    public WorkflowReconciliationKind Kind { get; }
    public string EvidenceHash { get; }
    public string SpecificationHash { get; }
    public string CommandHash { get; }

    public WorkflowReconciliationCommand(Guid installationId, Guid tenantId, Guid instanceId, Guid requestId,
        Guid actorId, Guid operationId, long expectedVersion, WorkflowReconciliationKind kind,
        string evidenceHash, string specificationHash)
    {
        if (new[] { installationId, tenantId, instanceId, requestId, actorId, operationId }.Contains(Guid.Empty) ||
            expectedVersion is < 1 or long.MaxValue || !Enum.IsDefined(kind) ||
            !ValidHash(evidenceHash) || !ValidHash(specificationHash))
            throw new ArgumentException("Invalid workflow reconciliation command.");
        InstallationId = installationId;
        TenantId = tenantId;
        InstanceId = instanceId;
        RequestId = requestId;
        ActorId = actorId;
        OperationId = operationId;
        ExpectedVersion = expectedVersion;
        Kind = kind;
        EvidenceHash = evidenceHash;
        SpecificationHash = specificationHash;
        CommandHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, action = "workflow.reconcile", installationId, tenantId, instanceId, requestId,
            actorId, operationId, expectedVersion, kind = kind.ToString(), evidenceHash, specificationHash
        })));
    }

    private static bool ValidHash(string hash) => hash is { Length: 64 } &&
        hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public override string ToString() => "WorkflowReconciliationCommand [private intent]";
}
