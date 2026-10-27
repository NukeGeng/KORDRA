namespace Kordra.Modules.IdentityAccess.Contracts;

public sealed record PermissionRule(string Permission, string ScopeKind, string Effect = "allow");
public sealed record RoleDefinitionSnapshot(Guid DefinitionId, long Revision, string Name, IReadOnlyList<PermissionRule> Rules);

/// <summary>IAM-owned authoritative reads. Missing, retired or noncurrent revisions return null.</summary>
public interface IRoleDefinitionReader
{
    Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid definitionId, long exactRevision, CancellationToken cancellationToken = default);
    Task<bool> IsActivePrincipalAsync(Guid principalId, CancellationToken cancellationToken = default);
}

/// <summary>Trusted offline composition must retain the installation OS lock, with API stopped.</summary>
public interface IOfflineProvisioningLease
{
    void RequireHeld();
}

/// <summary>The accepted Foundation permission slice only. Role labels never confer permission.</summary>
public static class FoundationPermissions
{
    public static bool IsValid(PermissionRule rule) => rule is not null && rule.Effect is "allow" or "deny" &&
        rule.Permission switch
        {
            "org.read" or "org.manage" or "membership.assign-role" or "audit.read" => rule.ScopeKind == "organization",
            "project.read" or "project.manage" => rule.ScopeKind is "organization" or "project",
            "target.read" or "target.manage" => rule.ScopeKind is "project" or "environment",
            "artifact.read" or "artifact.register" => rule.ScopeKind == "project",
            _ => false
        };

    public static bool Allows(RoleDefinitionSnapshot definition, string permission, string scopeKind) =>
        definition.Revision > 0 && definition.Rules.Count is > 0 and <= 32 &&
        definition.Rules.All(IsValid) &&
        definition.Rules.Any(rule => rule.Permission == permission && rule.ScopeKind == scopeKind && rule.Effect == "allow") &&
        !definition.Rules.Any(rule => rule.Permission == permission && rule.ScopeKind == scopeKind && rule.Effect == "deny");
}

/// <summary>Shared IAM trusted UTC source; untrusted time throws rather than extending authority.</summary>
public interface IAuthorityClock
{
    DateTimeOffset GetTrustedUtcNow();
}
