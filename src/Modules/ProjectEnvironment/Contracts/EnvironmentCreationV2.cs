namespace Kordra.Modules.ProjectEnvironment.Contracts;

// Internal command: tenant comes from the trusted project owner, never from the HTTP body.
public sealed record EnvironmentCreateV2Command(string Key, Guid TenantId, Guid ProjectId, string Name,
    string Classification, ResourcePolicyReference ResourcePolicy, Guid CorrelationId);
public sealed record EnvironmentCreationV2Result(string Status, CatalogFact? Environment = null, PolicyBinding? PolicyBinding = null, Guid MutationId = default);
