using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.TenantManagement;

namespace Kordra.Api;

public sealed record AuthorityWriter(string Id, Type Owner, string Method, bool OfflineOnly);

/// <summary>Explicit Foundation writer inventory, not a claim of distributed enforcement or static call-graph proof.</summary>
public static class AuthorityWriterRegistry
{
    public static IReadOnlyList<AuthorityWriter> Entries { get; } = Array.AsReadOnly(new AuthorityWriter[]
    {
        new("iam.boot", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.InitializeBootAsync), true),
        new("iam.login", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.LoginAsync), false),
        new("iam.logout", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.LogoutAsync), false),
        new("iam.logout-all", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.LogoutAllAsync), false),
        new("iam.password", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.ChangePasswordAsync), false),
        new("iam.enroll", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.EnrollOfflineAsync), true),
        new("iam.recover", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.RecoverOfflineAsync), true),
        new("iam.disable", typeof(LocalAuthenticationService), nameof(LocalAuthenticationService.DisableOfflineAsync), true),
        new("iam.definition", typeof(RoleDefinitionStore), nameof(RoleDefinitionStore.PublishOfflineAsync), true),
        new("ten.initialize", typeof(TenantMembershipService), nameof(TenantMembershipService.InitializeOfflineAsync), true),
        new("ten.member", typeof(TenantMembershipService), nameof(TenantMembershipService.AddMemberOfflineAsync), true),
        new("ten.grant", typeof(TenantMembershipService), nameof(TenantMembershipService.GrantOfflineAsync), true),
        new("ten.mutate", typeof(TenantMembershipService), nameof(TenantMembershipService.ExecuteAsync), false),
        new("catalog.mutate", typeof(CatalogMutationCoordinator), nameof(CatalogMutationCoordinator.ExecuteAsync), false),
        new("catalog.environment-v2", typeof(CatalogMutationCoordinator), nameof(CatalogMutationCoordinator.ExecuteEnvironmentV2Async), false),
        new("policy.publish-bind", typeof(OfflineResourcePolicyCoordinator), nameof(OfflineResourcePolicyCoordinator.ExecuteAsync), true)
    });

    public static void Validate()
    {
        if (Entries.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != Entries.Count ||
            Entries.Any(x => !x.Owner.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Any(m => m.Name == x.Method))) throw new InvalidOperationException("Foundation authority writer registry is incomplete.");
    }
}
