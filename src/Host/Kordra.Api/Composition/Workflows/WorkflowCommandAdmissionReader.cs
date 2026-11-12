using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Api;

/// <summary>Current local command eligibility. The caller still holds admission and owns U/M/D and transaction effects.</summary>
public sealed class WorkflowCommandAdmission
{
    internal WorkflowCommandAdmission(WorkflowReadScope scope, string action, WorkflowEnvironmentAuthoritySnapshot authority,
        WorkflowReadPolicy policy, DateTimeOffset observedAtUtc, DateTimeOffset validUntilUtc)
    {
        Scope = scope;
        Action = action;
        Authority = authority;
        Policy = policy;
        ObservedAtUtc = observedAtUtc;
        ValidUntilUtc = validUntilUtc;
    }

    public WorkflowReadScope Scope { get; }
    public string Action { get; }
    public WorkflowEnvironmentAuthoritySnapshot Authority { get; }
    public WorkflowReadPolicy Policy { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public DateTimeOffset ValidUntilUtc { get; }

    public bool IsCurrentSuccessor(WorkflowCommandAdmission? current) =>
        current is not null && current.Scope == Scope && current.Action == Action &&
        current.ObservedAtUtc >= ObservedAtUtc && current.ObservedAtUtc < ValidUntilUtc &&
        current.ObservedAtUtc < current.ValidUntilUtc &&
        current.Authority.Subject.PrincipalId == Authority.Subject.PrincipalId &&
        current.Authority.Subject.SessionId == Authority.Subject.SessionId &&
        current.Authority.Subject.BootGeneration == Authority.Subject.BootGeneration &&
        current.Authority.Subject.PrincipalRevision == Authority.Subject.PrincipalRevision &&
        current.Authority.Grant == Authority.Grant && current.Authority.Project == Authority.Project &&
        current.Authority.Environment == Authority.Environment &&
        current.Policy.AuthorityDigest == Policy.AuthorityDigest;

    public override string ToString() => "WorkflowCommandAdmission [private current facts]";
}

public sealed class WorkflowCommandAdmissionReader(WorkflowEnvironmentAuthorityReader authority,
    WorkflowActionPolicyReader policies, IAuthorityClock clock)
{
    public async Task<WorkflowCommandAdmission?> ReadUnderAdmissionAsync(SessionSelection session, WorkflowReadScope scope,
        string action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (session is null || scope is null || !ResourcePolicies.IsWorkflowAction(action)) return null;
        try
        {
            var before = clock.GetTrustedUtcNow();
            if (!ValidUtc(before)) return null;
            var subject = await authority.ReadCurrentUnderAdmissionAsync(session, scope.TenantId, scope.ProjectId,
                scope.EnvironmentId, action, ct);
            if (subject is null) return null;
            var policy = await policies.ReadAllowedUnderAdmissionAsync(scope, action, ct);
            if (policy is null) return null;
            var now = clock.GetTrustedUtcNow();
            ct.ThrowIfCancellationRequested();
            var until = subject.ValidUntilUtc < policy.ValidUntilUtc ? subject.ValidUntilUtc : policy.ValidUntilUtc;
            if (!ValidUtc(now) || now < before || now >= until) return null;
            return new(scope, action, subject, policy, now, until);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static bool ValidUtc(DateTimeOffset time) => time.Offset == TimeSpan.Zero &&
        time != default && time != DateTimeOffset.MaxValue;
}
