namespace Kordra.Modules.InfrastructureInventory.Contracts;

public sealed record KubernetesWorkloadsV1(int SchemaVersion, Guid InvocationId, Guid TargetId,
    string Namespace, DateTimeOffset CapturedAtUtc, DateTimeOffset CompletedAtUtc,
    DateTimeOffset ValidUntilUtc, string Coverage, IReadOnlyList<KubernetesResourceSetV1> ResourceSets);
public sealed record KubernetesResourceSetV1(string Kind, string Status, string Coverage,
    string? ResourceVersion, IReadOnlyList<KubernetesWorkloadItemV1> Items);
public sealed record KubernetesWorkloadItemV1(string Kind, string Name, string Uid,
    long? Generation, long? ObservedGeneration, int? DesiredReplicas, int? ReadyReplicas,
    int? AvailableReplicas, string? PodPhase, bool? PodReady, IReadOnlyList<KubernetesImageV1> Images);
public sealed record KubernetesImageV1(string ContainerName, string? ReportedReference,
    string? DeclaredDigest, string? ReportedRuntimeDigest);
