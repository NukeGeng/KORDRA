using System.Data.Common;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Npgsql;

namespace Kordra.Api;

/// <summary>
/// 12.3b (D4) production <see cref="IWorkflowDelegationReissuer"/>: on the reauthorization caller root and under the authority
/// fence, captures fresh session-free evidence for the same original actor, revokes the current delegation, advances the epoch
/// and creates the next generation (<c>iam_workflow.reauthorize_delegation</c>). Returns null (deny, no effects of its own)
/// when the admitted facts are missing or no longer match. Not registered in DI; inactive until 12.3f. 12.3c-3 (D1): the fence is
/// locked on the root before the principal and current-delegation facts are read; the IAM function re-locks it in the same
/// transaction and advances the epoch exactly once.
/// </summary>
public sealed class IamWorkflowDelegationReissuer(IDurablePrincipalReader principals) : IWorkflowDelegationReissuer
{
    public const string ReasonCode = "workflow.reauthorize";

    public async Task<WorkflowDelegationReference?> StageReissueAsync(DbConnection connection, DbTransaction root,
        WorkflowDelegationReissueRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (connection is not NpgsqlConnection npgsql || root is not NpgsqlTransaction transaction ||
            request.Subject is not { } subject || request.StartGrant is not { } start || request.ReadGrant is not { } read ||
            subject.PrincipalId != request.ActorId)
            return null;
        var iam = new WorkflowAuthorityFunctionPort(npgsql, transaction);
        await iam.AcquireFenceAsync(request.InstallationId, ct);
        var principal = await principals.ReadUnderAdmissionAsync(request.ActorId, ct);
        if (principal is null || !principal.Active || principal.PrincipalRevision != subject.PrincipalRevision) return null;
        WorkflowDelegationEvidence evidence;
        DateTimeOffset until;
        try
        {
            evidence = WorkflowDelegationEvidence.Capture(principal, start, read, request.TenantId, request.ProjectId, request.EnvironmentId);
            until = WorkflowDelegationAuthority.ValidUntil(request.NotAfterUtc, evidence, request.PolicyExpiresAtUtc,
                request.ApprovalValidUntilUtc);
        }
        catch (ArgumentException) { return null; }
        var current = await iam.ReadCurrentDelegationAsync(request.TenantId, request.InstanceId, ct);
        if (current is null || current.ActorId != request.ActorId || current.SpecificationHash != request.SpecificationHash ||
            current.ProjectId != request.ProjectId || current.EnvironmentId != request.EnvironmentId ||
            current.ReleaseId != request.ReleaseId || current.ReleaseRevision != request.ReleaseRevision ||
            current.ReleaseHash != request.ReleaseHash)
            return null;
        var reissued = await iam.ReauthorizeDelegationAsync(new(request.TenantId, request.InstanceId, current.Generation,
            Guid.NewGuid(), request.ActorId, subject.SessionId, subject.BootGeneration, subject.PrincipalRevision,
            request.SpecificationHash, WorkflowDelegationEvidenceCodec.Encode(evidence), until, ReasonCode), ct);
        return new(reissued.DelegationId, until, reissued.Generation);
    }
}
