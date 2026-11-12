using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Modules.IdentityAccess.Authorization;

/// <summary>Current IAM facts for the caller's held admission. A coordinator still owns U/D and handoff.</summary>
public sealed class WorkflowEnvironmentAuthoritySnapshot
{
    internal WorkflowEnvironmentAuthoritySnapshot(CurrentSubject subject, ScopedGrant grant,
        ReleaseProjectAuthority project, ApprovalEnvironmentAuthority environment, DateTimeOffset validUntilUtc) =>
        (Subject, Grant, Project, Environment, ValidUntilUtc) = (subject, grant, project, environment, validUntilUtc);

    public CurrentSubject Subject { get; }
    public ScopedGrant Grant { get; }
    public ReleaseProjectAuthority Project { get; }
    public ApprovalEnvironmentAuthority Environment { get; }
    public DateTimeOffset ValidUntilUtc { get; }
}

public sealed class WorkflowEnvironmentAuthorityReader(ICurrentSubjectReader subjects, IScopedGrantReader grants,
    IRoleDefinitionReader definitions, IReleaseProjectAuthorityReader projects, IApprovalEnvironmentAuthorityReader environments,
    IAuthorityClock clock)
{
    public async Task<WorkflowEnvironmentAuthoritySnapshot?> ReadCurrentUnderAdmissionAsync(SessionSelection selection,
        Guid tenantId, Guid projectId, Guid environmentId, string permission, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (selection is null || selection.PrincipalId == Guid.Empty || selection.BootGeneration == Guid.Empty ||
            tenantId == Guid.Empty || projectId == Guid.Empty || environmentId == Guid.Empty ||
            permission is not ("workflow.start" or "workflow.read" or "workflow.cancel" or "workflow.reconcile"))
            return null;
        try
        {
            var before = clock.GetTrustedUtcNow();
            if (!FiniteUtc(before)) return null;
            var subject = await subjects.ReadCurrentUnderAdmissionAsync(selection, ct);
            if (subject is null || subject.PrincipalId != selection.PrincipalId || subject.SessionId == Guid.Empty ||
                subject.BootGeneration != selection.BootGeneration || subject.PrincipalRevision <= 0 ||
                !FiniteUtc(subject.AbsoluteExpiresAtUtc) || !FiniteUtc(subject.IdleExpiresAtUtc))
                return null;
            var project = await projects.ReadCurrentUnderAdmissionAsync(projectId, ct);
            if (project is null || project.TenantId != tenantId || project.ProjectId != projectId || project.Revision <= 0)
                return null;
            var environment = await environments.ReadCurrentUnderAdmissionAsync(tenantId, projectId, environmentId, ct);
            if (environment is null || environment.TenantId != tenantId || environment.ProjectId != projectId ||
                environment.EnvironmentId != environmentId || environment.Revision <= 0)
                return null;
            var grant = await grants.ReadCurrentGrantUnderAdmissionAsync(subject.PrincipalId, tenantId,
                permission, "environment", environmentId, ct);
            if (grant is null || grant.PrincipalId != subject.PrincipalId || grant.TenantId != tenantId ||
                grant.Permission != permission || grant.ScopeKind != "environment" || grant.ScopeId != environmentId ||
                grant.MembershipId == Guid.Empty || grant.TenantRevision <= 0 || grant.MembershipRevision <= 0 ||
                grant.AssignmentId == Guid.Empty || grant.AssignmentRevision <= 0 || grant.DefinitionId == Guid.Empty ||
                grant.DefinitionRevision <= 0 || grant.ExpiresAtUtc is { } expiry && !FiniteUtc(expiry))
                return null;
            var definition = await definitions.GetCurrentAsync(grant.DefinitionId, grant.DefinitionRevision, ct);
            if (definition is null || definition.DefinitionId != grant.DefinitionId || definition.Revision != grant.DefinitionRevision ||
                !FoundationPermissions.Allows(definition, permission, "environment"))
                return null;
            var until = new[] { subject.AbsoluteExpiresAtUtc, subject.IdleExpiresAtUtc,
                grant.ExpiresAtUtc ?? DateTimeOffset.MaxValue }.Min();
            var after = clock.GetTrustedUtcNow();
            ct.ThrowIfCancellationRequested();
            if (!FiniteUtc(after) || after < before || until <= after) return null;
            return new(subject, grant, project, environment, until);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static bool FiniteUtc(DateTimeOffset time) => time.Offset == TimeSpan.Zero &&
        time != default && time != DateTimeOffset.MaxValue;
}
