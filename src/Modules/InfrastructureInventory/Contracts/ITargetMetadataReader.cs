namespace Kordra.Modules.InfrastructureInventory.Contracts;

/// <summary>Immutable server selection admitted at U. Values never establish authority.</summary>
public sealed record TargetMetadataReadSelection(Guid InvocationId, Guid TenantId, Guid TargetId,
    string ScopeKind, Guid ScopeId);

/// <summary>
/// Observation-only port. The caller owns current authorization, U/D audit and disclosure.
/// Implementations must honor cancellation and must not mutate targets or resolve credentials.
/// </summary>
public interface ITargetMetadataReader
{
    Task<TargetMetadata?> ReadAsync(TargetMetadataReadSelection selection, CancellationToken cancellationToken = default);
}
