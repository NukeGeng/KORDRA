using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

namespace Kordra.Api;

/// <summary>
/// 12.3b shared delegation rules (ENG-T12-3B-IAM-AUTHORITY-SCOPE). Pure; grants nothing by itself.
/// </summary>
public static class WorkflowDelegationAuthority
{
    /// <summary>The pilot transition kinds a start delegation allows (service bindings narrow them further).</summary>
    public static IReadOnlyList<string> PilotKinds { get; } = ["admit", "checkpoint", "timer_due", "cancel"];

    /// <summary>
    /// D2: the earliest of the workflow deadline, grant expiries, the policy's own expiry and the approval grant expiry.
    /// Browser-session idle/absolute expiry is deliberately not an input.
    /// </summary>
    public static DateTimeOffset ValidUntil(DateTimeOffset workflowDeadlineUtc, WorkflowDelegationEvidence evidence,
        DateTimeOffset? policyExpiresAtUtc, DateTimeOffset? approvalValidUntilUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var candidates = new List<DateTimeOffset> { workflowDeadlineUtc };
        if (evidence.EarliestGrantExpiryUtc is { } grant) candidates.Add(grant);
        if (policyExpiresAtUtc is { } policy) candidates.Add(policy);
        if (approvalValidUntilUtc is { } approval) candidates.Add(approval);
        if (candidates.Any(x => x.Offset != TimeSpan.Zero || x == default || x == DateTimeOffset.MaxValue))
            throw new ArgumentException("Delegation validity inputs must be finite UTC instants.");
        return candidates.Min();
    }

    /// <summary>Generation-1 draft from the exact admitted start facts and freshly allocated specification.</summary>
    public static WorkflowDelegationDraft StartDraft(WorkflowStartAdmission admitted, WorkflowSpecification specification,
        DurablePrincipal principal, DateTimeOffset effectiveUtc)
    {
        ArgumentNullException.ThrowIfNull(admitted); ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(principal);
        var subject = admitted.Eligibility.WorkflowAuthority.Subject;
        if (principal.PrincipalId != subject.PrincipalId || principal.PrincipalRevision != subject.PrincipalRevision || !principal.Active)
            throw new InvalidOperationException("Delegation principal differs from the admitted subject.");
        var evidence = WorkflowDelegationEvidence.Capture(principal, admitted.Eligibility.WorkflowAuthority.Grant,
            admitted.Eligibility.ReleaseAuthority.Grant, specification.TenantId, specification.ProjectId, specification.EnvironmentId);
        var until = ValidUntil(specification.DeadlineUtc, evidence, admitted.Policy.Policy.PolicyExpiresAtUtc,
            admitted.Eligibility.ApprovalValidUntilUtc);
        if (until <= effectiveUtc) throw new InvalidOperationException("Delegation would already be expired.");
        return new(Guid.NewGuid(), specification.TenantId, specification.InstanceId, subject.PrincipalId, subject.SessionId,
            subject.BootGeneration, subject.PrincipalRevision, specification.ProjectId, specification.EnvironmentId,
            specification.ReleaseId, specification.ReleaseRevision, specification.ReleaseHash, WorkflowSpecification.TemplateId,
            WorkflowSpecification.TemplateVersion, specification.SpecificationHash, PilotKinds,
            WorkflowDelegationEvidenceCodec.Encode(evidence), until);
    }
}

/// <summary>Start receipt plus the generation-1 delegation reference (null for a duplicate start, which creates nothing).</summary>
public sealed record WorkflowAuthorityStartResult(WorkflowStoredResult Start,
    Kordra.Modules.WorkflowOrchestration.Contracts.WorkflowDelegationReference? Delegation);
