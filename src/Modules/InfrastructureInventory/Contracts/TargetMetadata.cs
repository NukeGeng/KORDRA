namespace Kordra.Modules.InfrastructureInventory.Contracts;

public sealed record TargetCommand(string Key, string Operation, Guid TenantId, Guid ProjectId,
    Guid? EnvironmentId = null, Guid TargetId = default, long ExpectedRevision = 0,
    string? Name = null, string? Kind = null);
public sealed record TargetMetadata(Guid TargetId, Guid TenantId, Guid ProjectId, Guid? EnvironmentId,
    string Name, string Kind, string Status, long Revision, long AssociationRevision);
public sealed record TargetMutationResult(string Status, TargetMetadata? Resource = null);
public sealed record PreparedTargetChange(string Status, TargetMetadata? Resource = null, bool IsReplay = false);
