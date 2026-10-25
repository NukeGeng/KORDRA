using System.Text.Json.Serialization;

namespace Kordra.Modules.InfrastructureInventory.Contracts;

public sealed record NomadWorkloadsV1(int SchemaVersion, Guid InvocationId, Guid TargetId, string Provider,
    string Namespace, string Region, DateTimeOffset CapturedAtUtc, DateTimeOffset CompletedAtUtc,
    DateTimeOffset ValidUntilUtc, string Coverage, IReadOnlyList<NomadResourceSetV1> ResourceSets);
public sealed record NomadResourceSetV1(string Kind, string Status, string Coverage, string? ObservationIndex,
    IReadOnlyList<NomadWorkloadItemV1> Items);
[JsonDerivedType(typeof(NomadJobV1))]
[JsonDerivedType(typeof(NomadAllocationV1))]
public abstract record NomadWorkloadItemV1;
public sealed record NomadJobV1(string Id, string Name, string? Type, string? Status) : NomadWorkloadItemV1;
public sealed record NomadAllocationV1(string Id, string JobId, string TaskGroup, string? ClientStatus,
    string? DesiredStatus) : NomadWorkloadItemV1;
