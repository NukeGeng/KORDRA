using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Npgsql;

namespace Kordra.Api;

/// <summary>Issued permit, or a fixed deny reason code. Never carries the evidence or any secret.</summary>
public sealed record WorkflowPermitIssuance(WorkflowPermitIssued? Permit, string? DenyReason)
{
    public static WorkflowPermitIssuance Denied(string reason) => new(null, reason);
}

/// <summary>
/// 12.3b (D5) API-side permit issuer, inactive (no route, RPC or DI registration; 12.3d adds the mTLS RPC that supplies the
/// authenticated worker service). On the caller's API root it takes the authority fence BEFORE reading any authorization
/// fact, then re-evaluates the current delegation without a browser session: delegation current and unexpired, captured
/// principal/grants/roles still current, originating session not revoked, release/approval/classification still eligible
/// and the environment workflow.start policy still allows. Only then <c>iam_workflow.issue_permit</c>, which (12.3d D3) checks
/// the authenticated certificate pin of the worker service under the same fence. Never holds the root across network I/O;
/// any missing fact fails closed.
/// </summary>
public sealed class WorkflowPermitIssuer(Guid installationId, DurableWorkflowDelegationAuthorityReader authority,
    WorkflowDelegationSessionReader sessions, WorkflowStartEligibilityReader resources, WorkflowActionPolicyReader policies,
    IWorkflowReleaseRevisionResolver revisions, IAuthorityClock clock)
{
    private static bool IsPin(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    public async Task<WorkflowPermitIssuance> IssueUnderFenceAsync(NpgsqlConnection connection, NpgsqlTransaction root,
        WorkflowSpecification specification, WorkflowPermitRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(request);
        if (installationId == Guid.Empty || specification.InstallationId != installationId ||
            specification.TenantId != request.TenantId || specification.InstanceId != request.InstanceId ||
            specification.SpecificationHash != request.SpecificationHash || request.TemplateHash != WorkflowSpecification.TemplateHash ||
            !WorkflowDelegationAuthority.PilotKinds.Contains(request.TransitionKind) || !IsPin(request.SpkiSha256))
            return WorkflowPermitIssuance.Denied("request_invalid");
        var iam = new WorkflowAuthorityFunctionPort(connection, root);
        try
        {
            // Gate → fence → owner facts: the fence is held before any authorization fact is read.
            await iam.AcquireFenceAsync(installationId, ct);
            var delegation = await iam.ReadCurrentDelegationAsync(request.TenantId, request.InstanceId, ct);
            var now = clock.GetTrustedUtcNow();
            if (delegation is null || delegation.Revoked || delegation.ValidUntilUtc <= now)
                return WorkflowPermitIssuance.Denied("delegation_not_current");
            if (delegation.ProjectId != specification.ProjectId || delegation.EnvironmentId != specification.EnvironmentId ||
                delegation.ReleaseId != specification.ReleaseId || delegation.ReleaseRevision != specification.ReleaseRevision ||
                delegation.ReleaseHash != specification.ReleaseHash || delegation.SpecificationHash != specification.SpecificationHash ||
                !delegation.AllowedKinds.Contains(request.TransitionKind))
                return WorkflowPermitIssuance.Denied("binding_conflict");
            WorkflowDelegationEvidence evidence;
            try
            {
                evidence = WorkflowDelegationEvidenceCodec.Decode(delegation.Evidence, specification.TenantId,
                    specification.ProjectId, specification.EnvironmentId);
            }
            catch (ArgumentException) { return WorkflowPermitIssuance.Denied("evidence_invalid"); }
            if (evidence.Principal.PrincipalId != delegation.ActorId || evidence.Principal.PrincipalRevision != delegation.PrincipalRevision)
                return WorkflowPermitIssuance.Denied("evidence_invalid");
            var status = await authority.CheckUnderAdmissionAsync(evidence, ct);
            if (status != WorkflowDelegationAuthorityStatus.Current)
                return WorkflowPermitIssuance.Denied(status == WorkflowDelegationAuthorityStatus.Expired ? "authority_expired" : "authority_not_current");
            if (await sessions.ReadUnderAdmissionAsync(delegation.SessionId, delegation.ActorId, delegation.BootGeneration, ct)
                != WorkflowDelegationSessionStatus.Current)
                return WorkflowPermitIssuance.Denied("session_revoked");
            if (await revisions.ResolveUnderAdmissionAsync(specification, ct) is not { } revisionId)
                return WorkflowPermitIssuance.Denied("release_not_current");
            var selection = WorkflowReauthorizationAdmission.SelectionFor(specification, revisionId);
            var facts = await resources.ReadResourceUnderAdmissionAsync(selection, clock.GetTrustedUtcNow(), ct);
            if (facts is null || facts.ApplicationId != specification.ApplicationId || facts.TargetId != specification.TargetId ||
                facts.Classification != specification.Environment || facts.ApprovalValidUntilUtc <= clock.GetTrustedUtcNow())
                return WorkflowPermitIssuance.Denied("resource_not_current");
            if (await policies.ReadStartAllowedUnderAdmissionAsync(new(specification.TenantId, specification.ProjectId,
                    specification.EnvironmentId, specification.RequestId), ct) is null)
                return WorkflowPermitIssuance.Denied("policy_not_current");
            return new(await iam.IssuePermitAsync(request, ct), null);
        }
        catch (PostgresException error) when (error.SqlState.StartsWith("KD", StringComparison.Ordinal))
        {
            // Owner functions raise fixed reason codes only; never echo values.
            return WorkflowPermitIssuance.Denied(error.MessageText);
        }
    }
}
