using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;

namespace Kordra.Api;

/// <summary>Opaque current start evidence. Not delegation, permission to commit, or an allocated instance.</summary>
public sealed class WorkflowStartAdmission
{
    internal WorkflowStartAdmission(Guid installationId, WorkflowStartSelection selection, Guid requestId, Guid invocationId,
        WorkflowStartEligibility eligibility, WorkflowActionPolicyFacts policy, DateTimeOffset observed, DateTimeOffset until)
    {
        InstallationId = installationId; Selection = selection; RequestId = requestId; InvocationId = invocationId;
        Eligibility = eligibility; Policy = policy; ObservedAtUtc = observed; ValidUntilUtc = until;
    }
    public Guid InstallationId { get; }
    public WorkflowStartSelection Selection { get; }
    public Guid RequestId { get; }
    public Guid InvocationId { get; }
    public WorkflowStartEligibility Eligibility { get; }
    public WorkflowActionPolicyFacts Policy { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public DateTimeOffset ValidUntilUtc { get; }

    public WorkflowStartCommand BindCommand(DateTimeOffset originalDeadlineUtc, string idempotencyKey) =>
        new(InstallationId, Selection.TenantId, Eligibility.WorkflowAuthority.Subject.PrincipalId,
            Selection.ProjectId, Selection.EnvironmentId, RequestId, Selection.ReleaseId, Selection.ReleaseRevision,
            Selection.ReleaseHash, Selection.TemplateId, Selection.TemplateVersion, originalDeadlineUtc, idempotencyKey);

    /// <summary>Validates a freshly allocated specification, not an existing idempotency receipt.</summary>
    public bool MatchesSpecification(WorkflowSpecification specification, WorkflowStartCommand command) =>
        MatchesOwnerBinding(specification, command) && specification.CreatedAtUtc >= ObservedAtUtc && specification.CreatedAtUtc < ValidUntilUtc;

    /// <summary>Also applies to the original canonical receipt; its historical creation time is not a new allocation.</summary>
    public bool MatchesOwnerBinding(WorkflowSpecification specification, WorkflowStartCommand command) =>
        specification is not null && command is not null && command.InstallationId == InstallationId &&
        command.TenantId == Selection.TenantId && command.ActorId == Eligibility.WorkflowAuthority.Subject.PrincipalId &&
        command.RequestId == RequestId && command.Matches(specification) &&
        specification.InstallationId == InstallationId && specification.TenantId == Selection.TenantId &&
        specification.RequestId == RequestId && specification.ProjectId == Selection.ProjectId &&
        specification.EnvironmentId == Selection.EnvironmentId && specification.ReleaseId == Selection.ReleaseId &&
        specification.ReleaseRevision == Selection.ReleaseRevision && specification.ReleaseHash == Selection.ReleaseHash &&
        specification.ApplicationId == Eligibility.ApplicationId && specification.TargetId == Eligibility.TargetId &&
        specification.Environment == Eligibility.Classification;

    public AuditIntent CreateMutationAudit(WorkflowSpecification specification, WorkflowStartCommand command, Guid intentId)
    {
        if (intentId == Guid.Empty || !MatchesSpecification(specification, command))
            throw new ArgumentException("Start audit requires the exact admitted specification and command.");
        return new(1, intentId, InvocationId, AuditIntentStage.Mutation, command.ActorId, Selection.TenantId,
            specification.InstanceId, "workflow.start", "environment", Selection.EnvironmentId,
            Policy.Policy.PolicyRevision, Eligibility.WorkflowAuthority.Grant.AssignmentRevision,
            "workflow-start-" + command.BusinessHash, specification.CreatedAtUtc, RequestId);
    }

    public bool IsCurrentSuccessor(WorkflowStartAdmission? current) =>
        current is not null && current.InstallationId == InstallationId && current.Selection == Selection &&
        current.RequestId == RequestId && current.InvocationId == InvocationId &&
        current.ObservedAtUtc >= ObservedAtUtc && current.ObservedAtUtc < ValidUntilUtc && current.ObservedAtUtc < current.ValidUntilUtc &&
        SameSubject(Eligibility.WorkflowAuthority.Subject, current.Eligibility.WorkflowAuthority.Subject) &&
        SameSubject(Eligibility.ReleaseAuthority.Subject, current.Eligibility.ReleaseAuthority.Subject) &&
        Eligibility.WorkflowAuthority.Grant == current.Eligibility.WorkflowAuthority.Grant &&
        Eligibility.ReleaseAuthority.Grant == current.Eligibility.ReleaseAuthority.Grant &&
        Eligibility.WorkflowAuthority.Project == current.Eligibility.WorkflowAuthority.Project &&
        Eligibility.WorkflowAuthority.Environment == current.Eligibility.WorkflowAuthority.Environment &&
        Eligibility.ReleaseAuthority.Project == current.Eligibility.ReleaseAuthority.Project &&
        Eligibility.ApprovalId == current.Eligibility.ApprovalId && Eligibility.ApprovalVersion == current.Eligibility.ApprovalVersion &&
        Eligibility.ApplicationId == current.Eligibility.ApplicationId && Eligibility.TargetId == current.Eligibility.TargetId &&
        Eligibility.ReleaseOwnerVersion == current.Eligibility.ReleaseOwnerVersion && Eligibility.Classification == current.Eligibility.Classification &&
        Policy.ProjectRevision == current.Policy.ProjectRevision && Policy.EnvironmentRevision == current.Policy.EnvironmentRevision &&
        Policy.Classification == current.Policy.Classification && Policy.Policy.BindingId == current.Policy.Policy.BindingId &&
        Policy.Policy.BindingRevision == current.Policy.Policy.BindingRevision && Policy.Policy.PolicyId == current.Policy.Policy.PolicyId &&
        Policy.Policy.PolicyRevision == current.Policy.Policy.PolicyRevision && Policy.Policy.AuthorityDigest == current.Policy.Policy.AuthorityDigest;

    internal static bool SameSubject(CurrentSubject a, CurrentSubject b) => a.PrincipalId == b.PrincipalId &&
        a.SessionId == b.SessionId && a.BootGeneration == b.BootGeneration && a.PrincipalRevision == b.PrincipalRevision;
    public override string ToString() => "WorkflowStartAdmission [private current owner facts]";
}

/// <summary>Caller retains common admission. Combines exact REL/APR eligibility and environment workflow.start policy.</summary>
public sealed class WorkflowStartAdmissionReader(Guid installationId, IWorkflowStartEligibilityReader eligibility,
    WorkflowActionPolicyReader policies, IAuthorityClock clock)
{
    public async Task<WorkflowStartAdmission?> ReadUnderAdmissionAsync(SessionSelection session, WorkflowStartSelection selection,
        Guid requestId, Guid invocationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (installationId == Guid.Empty || session is null || session.PrincipalId == Guid.Empty || session.BootGeneration == Guid.Empty ||
            selection is not { IsValid: true } || requestId == Guid.Empty || invocationId == Guid.Empty) return null;
        try
        {
            var before = clock.GetTrustedUtcNow();
            if (!FiniteUtc(before)) return null;
            var facts = await eligibility.ReadUnderAdmissionAsync(session, selection, invocationId, ct);
            if (!Bound(facts, installationId, session, selection, invocationId)) return null;
            var policy = await policies.ReadStartAllowedUnderAdmissionAsync(new(selection.TenantId, selection.ProjectId,
                selection.EnvironmentId, requestId), ct);
            if (policy is null || policy.ProjectRevision != facts!.WorkflowAuthority.Project.Revision ||
                policy.EnvironmentRevision != facts.WorkflowAuthority.Environment.Revision ||
                policy.Classification != facts.Classification.ToString()) return null;
            var after = clock.GetTrustedUtcNow(); ct.ThrowIfCancellationRequested();
            var until = new[] { facts.ValidUntilUtc, facts.WorkflowAuthority.ValidUntilUtc,
                facts.ReleaseAuthority.ValidUntilUtc, policy.Policy.ValidUntilUtc }.Min();
            if (!FiniteUtc(after) || after < before || !FiniteUtc(until) || after >= until) return null;
            return new(installationId, selection, requestId, invocationId, facts, policy, after, until);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static bool Bound(WorkflowStartEligibility? facts, Guid installation, SessionSelection session, WorkflowStartSelection selected, Guid invocation)
    {
        if (facts is null || facts.InstallationId != installation || facts.Selection != selected || facts.ApprovalId == Guid.Empty || facts.ApprovalVersion <= 0 || facts.ApplicationId == Guid.Empty ||
            facts.TargetId == Guid.Empty || facts.ReleaseOwnerVersion <= 0 || !Enum.IsDefined(facts.Classification)) return false;
        var workflow = facts.WorkflowAuthority; var release = facts.ReleaseAuthority;
        return workflow is not null && release is not null && workflow.Subject.PrincipalId == session.PrincipalId &&
            workflow.Subject.BootGeneration == session.BootGeneration && WorkflowStartAdmission.SameSubject(workflow.Subject, release.Subject) &&
            release.InvocationId == invocation && workflow.Project == release.Project && workflow.Project.TenantId == selected.TenantId &&
            workflow.Project.ProjectId == selected.ProjectId && workflow.Environment.TenantId == selected.TenantId &&
            workflow.Environment.ProjectId == selected.ProjectId && workflow.Environment.EnvironmentId == selected.EnvironmentId &&
            workflow.Grant.PrincipalId == session.PrincipalId && workflow.Grant.TenantId == selected.TenantId &&
            workflow.Grant.Permission == "workflow.start" && workflow.Grant.ScopeKind == "environment" && workflow.Grant.ScopeId == selected.EnvironmentId &&
            release.Grant.PrincipalId == session.PrincipalId && release.Grant.TenantId == selected.TenantId &&
            release.Grant.Permission == "release.read" && release.Grant.ScopeKind == "project" && release.Grant.ScopeId == selected.ProjectId;
    }
    private static bool FiniteUtc(DateTimeOffset time) => time.Offset == TimeSpan.Zero && time != default && time != DateTimeOffset.MaxValue;
}
