using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.TenantManagement;

namespace Kordra.Api;

/// <summary>
/// One registered invalidating writer. 12.3c (D4): <paramref name="FenceRaceScenario"/> names the real entry-point PostgreSQL
/// race scenario that proves fence participation; null means the writer does not yet participate (activation blocker).
/// </summary>
public sealed record AuthorityWriter(string Id, Type Owner, string Method, bool OfflineOnly, string? FenceRaceScenario = null);

/// <summary>
/// 12.3c (D6): an authority-changing offline operation with no entry point in this repository, so it cannot have a race test.
/// It blocks activation until its runbook step (stop the API and worker, advance the epoch by offline manifest before restart)
/// is accepted.
/// </summary>
public sealed record AuthorityOperation(string Id, string Runbook, bool RunbookStepAccepted);

/// <summary>Explicit Foundation writer inventory, not a claim of distributed enforcement or static call-graph proof.</summary>
public static class AuthorityWriterRegistry
{
    public static IReadOnlyList<AuthorityWriter> Entries { get; } = Array.AsReadOnly(new AuthorityWriter[]
    {
        new("iam.boot", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.InitializeBootAsync), true, "iam.boot"),
        new("iam.login", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.LoginAsync), false, "iam.login"),
        new("iam.logout", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.LogoutAsync), false, "iam.logout"),
        new("iam.logout-all", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.LogoutAllAsync), false, "iam.logout-all"),
        new("iam.password", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.ChangePasswordAsync), false, "iam.password"),
        new("iam.enroll", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.EnrollOfflineAsync), true, "iam.enroll"),
        new("iam.recover", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.RecoverOfflineAsync), true, "iam.recover"),
        new("iam.disable", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.DisableOfflineAsync), true, "iam.disable"),
        new("iam.definition", typeof(RoleDefinitionStore), nameof(RoleDefinitionStore.PublishOfflineAsync), true, "iam.definition"),
        new("ten.initialize", typeof(TenantMembershipService), nameof(TenantMembershipService.InitializeOfflineAsync), true, "ten.initialize"),
        new("ten.member", typeof(TenantMembershipService), nameof(TenantMembershipService.AddMemberOfflineAsync), true, "ten.member"),
        new("ten.grant", typeof(TenantMembershipService), nameof(TenantMembershipService.GrantOfflineAsync), true, "ten.grant"),
        new("ten.mutate", typeof(TenantMembershipService), nameof(TenantMembershipService.ExecuteAsync), false, "ten.mutate"),
        new("catalog.mutate", typeof(CatalogMutationCoordinator), nameof(CatalogMutationCoordinator.ExecuteAsync), false, "catalog.mutate"),
        new("catalog.environment-v2", typeof(CatalogMutationCoordinator), nameof(CatalogMutationCoordinator.ExecuteEnvironmentV2Async), false, "catalog.environment-v2"),
        new("sec.binding", typeof(OfflineCredentialCoordinator), nameof(OfflineCredentialCoordinator.ExecuteAsync), true, "sec.binding"),
        new("policy.publish-bind", typeof(OfflineResourcePolicyCoordinator), nameof(OfflineResourcePolicyCoordinator.ExecuteAsync), true, "policy.publish-bind"),
        new("approval.policy", typeof(OfflineApprovalPolicyCoordinator), nameof(OfflineApprovalPolicyCoordinator.ApplyAsync), true, "approval.policy"),
        new("release.mutate", typeof(ReleaseMutationTransaction), nameof(ReleaseMutationTransaction.ExecuteAsync), false, "release.mutate"),
        new("approval.mutate", typeof(ApprovalMutationTransaction), nameof(ApprovalMutationTransaction.ExecuteAsync), false, "approval.mutate"),
        // 12.3b IAM functions that lock the fence and advance the epoch themselves (registered by 12.3c-3): the reissue entry
        // names the delegation port, whose only implementation is the IAM reissuer; IAM declares its revoke and manifest methods.
        new("iam.delegation-reissue", typeof(IWorkflowDelegationReissuer), nameof(IWorkflowDelegationReissuer.StageReissueAsync), false, "iam.delegation-reissue")
    }.Concat(IamAuthorityFunctionWriters.Entries.Select(x => new AuthorityWriter(x.Id, x.Owner, x.Method, x.OfflineOnly, x.Id))).ToArray());

    /// <summary>12.3c (D6) operations without an entry point; see <see cref="AuthorityOperation"/>.</summary>
    public static IReadOnlyList<AuthorityOperation> Operations { get; } = Array.AsReadOnly(new AuthorityOperation[]
    {
        new("installation.restore", "docs/operations/backup-restore-runbook.md", false)
    });

    /// <summary>
    /// 12.3c (D3/D6) activation guard: every registered invalidating writer must declare its fence race scenario and every
    /// operation without an entry point needs its accepted runbook step. Throws while anything is uncovered; used by the worker
    /// activation path (12.3e), never as a substitute for the race tests themselves.
    /// </summary>
    public static void RequireEnforcedCoverage()
    {
        Validate();
        var missing = Entries.Where(x => x.FenceRaceScenario is null).Select(x => x.Id)
            .Concat(Operations.Where(x => !x.RunbookStepAccepted).Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Authority writers without fence coverage: " + string.Join(", ", missing));
    }

    public static void Validate()
    {
        var ids = Entries.Select(x => x.Id).Concat(Operations.Select(x => x.Id)).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || Operations.Any(x => string.IsNullOrWhiteSpace(x.Runbook)) ||
            Entries.Any(x => !x.Owner.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Any(m => m.Name == x.Method))) throw new InvalidOperationException("Foundation authority writer registry is incomplete.");
    }
}
