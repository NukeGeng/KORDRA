using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Domain;

namespace Kordra.Api;

/// <summary>Closed operator selection. Owner references and transaction origin are resolved separately.</summary>
public sealed record WorkflowReconciliationSelection(Guid RequestId, Guid OperationId, long ExpectedVersion,
    WorkflowReconciliationKind Kind, string EvidenceHash)
{
    public WorkflowReconciliationCommand BindToOwner(WorkflowSpecification specification, Guid currentActorId)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return new(specification.InstallationId, specification.TenantId, specification.InstanceId, RequestId,
            currentActorId, OperationId, ExpectedVersion, Kind, EvidenceHash, specification.SpecificationHash);
    }

    internal bool IsValid => RequestId != Guid.Empty && OperationId != Guid.Empty && ExpectedVersion > 0 &&
        Enum.IsDefined(Kind) && EvidenceHash is { Length: 64 } &&
        EvidenceHash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>Immutable evidence for the timer receiver only. This hash does not prove custody or transaction outcome.</summary>
public static class WorkflowReconciliationEvidence
{
    public static string ComputeHash(WorkflowTransactionOrigin origin, WorkflowWakeOperation wake,
        VerifiedMessageEnvelope request, AuditIntent schedulerAudit, VerifiedMessageEnvelope result, AuditIntent completionAudit)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(wake);
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            kind = "workflow-timer-receiver-evidence",
            origin.CaseId,
            origin.FullTransactionId,
            origin.BackendPid,
            origin.BackendStartedUtc,
            origin.ServerStartedUtc,
            wake.DescriptorHash,
            request = request.Delivery,
            requestHash = request.PayloadHash,
            result = result.Delivery,
            resultHash = result.PayloadHash,
            schedulerAudit = AuditIntentCanonicalizer.ComputeHash(schedulerAudit),
            completionAudit = AuditIntentCanonicalizer.ComputeHash(completionAudit)
        })));
    }
}
