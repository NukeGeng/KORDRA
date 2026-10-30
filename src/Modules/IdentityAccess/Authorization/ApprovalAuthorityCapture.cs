using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Modules.IdentityAccess.Authorization;

/// <summary>Transient exact owner selection for U/M/D comparison. Never persist session facts or serialize as a response.</summary>
public sealed class ApprovalAuthorityCaptureResult
{
    internal ApprovalAuthorityCaptureResult(ApprovalAuthorityEvidence evidence, CurrentSubject subject,
        ReleaseProjectAuthority project, ApprovalEnvironmentAuthority environment, DateTimeOffset until) =>
        (Evidence, Subject, Project, Environment, SessionValidUntilUtc) = (evidence, subject, project, environment, until);
    public ApprovalAuthorityEvidence Evidence { get; }
    public CurrentSubject Subject { get; }
    public ReleaseProjectAuthority Project { get; }
    public ApprovalEnvironmentAuthority Environment { get; }
    public DateTimeOffset SessionValidUntilUtc { get; }
    public bool SameAuthorityAs(ApprovalAuthorityCaptureResult? other) => other is not null &&
        Evidence == other.Evidence && Subject == other.Subject && Project == other.Project && Environment == other.Environment &&
        SessionValidUntilUtc == other.SessionValidUntilUtc;
    public override string ToString() => "ApprovalAuthorityCaptureResult [private]";
}

/// <summary>Reads current owners under caller-held admission. No audit, mutation, or action permit.</summary>
public sealed class ApprovalAuthorityCapture(ICurrentSubjectReader sessions, IDurablePrincipalReader principals,
    IScopedGrantReader grants, IRoleDefinitionReader definitions, IReleaseProjectAuthorityReader projects,
    IApprovalEnvironmentAuthorityReader environments, IAuthorityClock clock)
{
    public Task<ApprovalAuthorityCaptureResult?> CaptureRequesterUnderAdmissionAsync(SessionSelection session,
        Guid projectId, Guid environmentId, CancellationToken ct = default) => CaptureAsync(session, projectId, environmentId, "requester", ct);

    public Task<ApprovalAuthorityCaptureResult?> CaptureApproverUnderAdmissionAsync(SessionSelection session,
        Guid projectId, Guid environmentId, CancellationToken ct = default) => CaptureAsync(session, projectId, environmentId, "approver", ct);

    public Task<ApprovalAuthorityCaptureResult?> CaptureRevokerUnderAdmissionAsync(SessionSelection session,
        Guid projectId, Guid environmentId, CancellationToken ct = default) => CaptureAsync(session, projectId, environmentId, "revoker", ct);

    public Task<ApprovalAuthorityCaptureResult?> CaptureReadUnderAdmissionAsync(SessionSelection session,
        Guid projectId, Guid environmentId, CancellationToken ct = default) => CaptureAsync(session, projectId, environmentId, "reader", ct);

    private async Task<ApprovalAuthorityCaptureResult?> CaptureAsync(SessionSelection session,
        Guid projectId, Guid environmentId, string purpose, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (session is null || session.PrincipalId == Guid.Empty || session.BootGeneration == Guid.Empty ||
            projectId == Guid.Empty || environmentId == Guid.Empty) return null;
        try
        {
            var before = clock.GetTrustedUtcNow(); if (!ValidTime(before)) return null;
            var subject = await sessions.ReadCurrentUnderAdmissionAsync(session, ct);
            if (subject is null || subject.PrincipalId != session.PrincipalId || subject.SessionId == Guid.Empty ||
                subject.BootGeneration != session.BootGeneration || subject.PrincipalRevision <= 0 ||
                !ValidTime(subject.AbsoluteExpiresAtUtc) || !ValidTime(subject.IdleExpiresAtUtc)) return null;
            var project = await projects.ReadCurrentUnderAdmissionAsync(projectId, ct);
            if (project is null || project.ProjectId != projectId || project.TenantId == Guid.Empty || project.Revision <= 0) return null;
            var environment = await environments.ReadCurrentUnderAdmissionAsync(project.TenantId, projectId, environmentId, ct);
            if (environment is null || environment.TenantId != project.TenantId || environment.ProjectId != projectId ||
                environment.EnvironmentId != environmentId || environment.Revision <= 0) return null;
            var principal = await principals.ReadUnderAdmissionAsync(subject.PrincipalId, ct);
            if (principal is null || !principal.Active || principal.PrincipalRevision != subject.PrincipalRevision) return null;
            var approver = purpose != "requester";
            var request = await grants.ReadCurrentGrantUnderAdmissionAsync(subject.PrincipalId, project.TenantId,
                purpose == "reader" ? "release.approval.read" : purpose == "revoker" ? "release.approval.revoke" : approver ? "release.approve" : "release.request-approval", "environment", environmentId, ct);
            var read = purpose == "reader" ? request : await grants.ReadCurrentGrantUnderAdmissionAsync(subject.PrincipalId, project.TenantId,
                approver ? "release.approval.read" : "release.read", approver ? "environment" : "project", approver ? environmentId : projectId, ct);
            if (request is null || read is null) return null;
            var evidence = purpose == "reader" ? ApprovalAuthorityEvidence.CaptureReader(principal, request, project.TenantId, environmentId)
                : purpose == "revoker" ? ApprovalAuthorityEvidence.CaptureRevoker(principal, request, read, project.TenantId, environmentId)
                : approver ? ApprovalAuthorityEvidence.CaptureApprover(principal, request, read, project.TenantId, environmentId)
                : ApprovalAuthorityEvidence.CaptureRequester(principal, request, read, project.TenantId, projectId, environmentId);
            foreach (var grant in new[] { request, read })
            {
                var role = await definitions.GetCurrentAsync(grant.DefinitionId, grant.DefinitionRevision, ct);
                if (role is null || role.DefinitionId != grant.DefinitionId || role.Revision != grant.DefinitionRevision ||
                    !FoundationPermissions.Allows(role, grant.Permission, grant.ScopeKind)) return null;
            }
            var until = new[] { subject.AbsoluteExpiresAtUtc, subject.IdleExpiresAtUtc,
                request.ExpiresAtUtc ?? DateTimeOffset.MaxValue, read.ExpiresAtUtc ?? DateTimeOffset.MaxValue }.Min();
            var after = clock.GetTrustedUtcNow(); ct.ThrowIfCancellationRequested();
            if (!ValidTime(after) || after < before || until <= before || until <= after) return null;
            return new(evidence, subject, project, environment, until);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }
    private static bool ValidTime(DateTimeOffset time) => time.Offset == TimeSpan.Zero && time != default && time != DateTimeOffset.MaxValue;
}
