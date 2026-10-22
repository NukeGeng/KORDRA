namespace Kordra.Modules.TenantManagement.Contracts;

public sealed record TenantScope(string Kind, Guid Id);
public sealed record SelfMembership(Guid TenantId, Guid MembershipId, long Revision);
public sealed record TenantCommand(string Key, string Kind, Guid TenantId, Guid MembershipId,
    long ExpectedRevision, Guid DefinitionId = default, long DefinitionRevision = 0,
    TenantScope? Scope = null, DateTimeOffset? ExpiresAtUtc = null, Guid AssignmentId = default);
public sealed record TenantCommandResult(string Status, Guid TenantId = default, Guid ResourceId = default, long Revision = 0);
public sealed record EffectiveAssignment(Guid AssignmentId, long Revision, Guid DefinitionId,
    long DefinitionRevision, DateTimeOffset? ExpiresAtUtc);

/// <summary>Trusted server/session adapter only; no principal argument is accepted from a command.</summary>
public interface ITenantSubjectReader
{
    Task<Guid?> CurrentPrincipalAsync(CancellationToken cancellationToken = default);
}

/// <summary>PRJ/INV owner composition validates the exact current tenant/resource association.</summary>
public interface ITenantScopeOwner
{
    Task<bool> BelongsToTenantAsync(Guid tenantId, TenantScope scope, CancellationToken cancellationToken = default);
}
