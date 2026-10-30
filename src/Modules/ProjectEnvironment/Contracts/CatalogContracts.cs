namespace Kordra.Modules.ProjectEnvironment.Contracts;

// Internal owner commands, not HTTP envelopes or transferable permits.
public sealed record CatalogCommand(string Key, string Operation, string Kind, Guid TenantId,
    Guid ProjectId = default, Guid ResourceId = default, long ExpectedRevision = 0,
    string? Name = null, string? Classification = null, long? PolicyRevision = null);
public sealed record CatalogFact(Guid Id, Guid TenantId, Guid ProjectId, string Kind, string Name,
    string Status, long Revision, string? Classification = null, long? PolicyRevision = null);
public sealed record CatalogMutationResult(string Status, CatalogFact? Resource = null);
public sealed record PreparedCatalogChange(string Status, CatalogFact? Resource = null, bool IsReplay = false);

public enum EnvironmentSelection { ResolvedAbsent, ResolvedPresent }
public sealed record ProjectOwnership(Guid TenantId, Guid ProjectId, long ProjectRevision,
    EnvironmentSelection Selection, Guid? EnvironmentId, long? EnvironmentRevision,
    string? Classification, long? PolicyRevision, PolicyBinding? PolicyBinding = null);
public sealed record TargetAssociation(Guid TargetId, Guid TenantId, Guid ProjectId, Guid? EnvironmentId, long Revision);

public sealed record ProjectIdentity(Guid TenantId, Guid ProjectId, long Revision);

/// <summary>Current owner lookup with no caller tenant. Missing/inactive/ambiguous identity returns null.</summary>
public interface IProjectIdentityReader
{
    Task<ProjectIdentity?> ReadCurrentProjectUnderAdmissionAsync(Guid projectId, CancellationToken ct = default);
}

/// <summary>Authoritative owner reads under the common admission gate; null is not environment absence.</summary>
public interface IProjectOwnershipReader
{
    Task<ProjectOwnership?> ResolveOwnershipAsync(Guid tenantId, Guid projectId, Guid? environmentId, CancellationToken cancellationToken = default);
    Task<TargetAssociation?> ReadAssociationAsync(Guid tenantId, Guid targetId, CancellationToken cancellationToken = default);
}

/// <summary>T06 trusted session/tenant/permission resolution under held admission; no positive default.</summary>
public interface ICatalogAuthority
{
    Task<Guid?> ResolvePrincipalAsync(Guid tenantId, Guid projectId, Guid? environmentId, string permission, CancellationToken cancellationToken = default);
}
public interface IAuthenticatedCatalogSubjectReader
{
    Task<Guid?> CurrentPrincipalUnderAdmissionAsync(CancellationToken ct = default);
}

/// <summary>Trusted policy owner validates an exact applicable association, never an arbitrary positive integer.</summary>
public interface IEnvironmentPolicyReader
{
    Task<bool> IsApplicableAsync(Guid tenantId, Guid projectId, string classification, long policyRevision, CancellationToken cancellationToken = default);
}

/// <summary>Neutral owner facts for a release policy binding; never an authorization or provider permit.</summary>
public sealed record ReleasePolicyEnvironment(Guid TenantId, Guid ProjectId, long ProjectRevision,
    Guid EnvironmentId, long EnvironmentRevision, string Classification);

public interface IReleasePolicyEnvironmentReader
{
    // Caller must hold shared admission or stopped installation custody.
    Task<ReleasePolicyEnvironment?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId,
        Guid environmentId, CancellationToken cancellationToken = default);
}

/// <summary>Current PRJ facts for one release selection; no authorization or provider access is implied.</summary>
public sealed record ReleaseCatalogSelection(Guid TenantId, Guid ProjectId, long ProjectRevision,
    Guid ApplicationId, long ApplicationRevision, Guid EnvironmentId, long EnvironmentRevision,
    string Classification, Guid TargetId, long AssociationRevision);

public interface IReleaseCatalogReader
{
    Task<ReleaseCatalogSelection?> ReadUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid applicationId,
        Guid environmentId, Guid targetId, CancellationToken cancellationToken = default);
}

/// <summary>PRJ-owned environment identity with no caller-supplied tenant or project. Not an authorization.</summary>
public interface IApprovalEnvironmentIdentityReader
{
    // Caller holds admission; missing, inactive or ambiguous IDs return null, owner failures propagate.
    Task<ReleasePolicyEnvironment?> ReadCurrentUnderAdmissionAsync(Guid environmentId, CancellationToken cancellationToken = default);
}
