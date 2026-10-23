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
