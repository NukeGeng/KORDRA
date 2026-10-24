namespace Kordra.Modules.IdentityAccess.Contracts;

/// <summary>Trusted cookie references only. Never accept from a business request body or log the token.</summary>
public sealed class SessionSelection(Guid principalId, string reference, Guid bootGeneration)
{
    public Guid PrincipalId { get; } = principalId;
    public string Reference { get; } = reference;
    public Guid BootGeneration { get; } = bootGeneration;
    public override string ToString() => "SessionSelection [redacted]";
}
public sealed record CurrentSubject(Guid PrincipalId, Guid SessionId, Guid BootGeneration,
    long PrincipalRevision, DateTimeOffset AbsoluteExpiresAtUtc, DateTimeOffset IdleExpiresAtUtc);
public sealed record ScopedGrant(Guid PrincipalId, Guid TenantId, Guid MembershipId, long TenantRevision,
    long MembershipRevision, Guid AssignmentId, long AssignmentRevision, Guid DefinitionId,
    long DefinitionRevision, string Permission, string ScopeKind, Guid ScopeId, DateTimeOffset? ExpiresAtUtc);
public sealed record TargetAuthority(Guid TargetId, Guid TenantId, Guid ProjectId, long TargetRevision,
    long ProjectRevision, long AssociationRevision, Guid? EnvironmentId, long? EnvironmentRevision,
    string? Classification);
public sealed record ResourcePolicyAuthority(Guid TenantId, Guid ProjectId, Guid? EnvironmentId, string Permission,
    long Revision, Guid PolicyId, Guid BindingId, long BindingRevision, string ContentHash,
    DateTimeOffset EffectiveFromUtc, DateTimeOffset? ExpiresAtUtc, ResourceEligibility Decision);

// Every method is an authoritative current owner read under the caller's already-held common gate.
public interface ICurrentSubjectReader
{
    Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default);
}
public interface IScopedGrantReader
{
    Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission, string scopeKind, Guid scopeId, CancellationToken ct = default);
}
public interface ITargetAuthorityReader
{
    Task<TargetAuthority?> ReadTargetUnderAdmissionAsync(Guid target, CancellationToken ct = default);
}
public interface IResourcePolicyReader
{
    Task<ResourcePolicyAuthority?> ReadPolicyUnderAdmissionAsync(TargetAuthority target, string permission, CancellationToken ct = default);
}
public enum AuthorityStage { Use, Disclosure }
public enum AuthorityCommitStatus { Durable, Rejected, Unavailable, Unknown }
public enum AuthorityCommitPurpose { ReadAdmission, CredentialConsumption }
public sealed record AuthorityCommitment(Guid IntentId, Guid InvocationId, AuthorityStage Stage,
    Guid SubjectId, Guid TenantId, Guid TargetId, string ScopeKind, Guid ScopeId, long PolicyRevision,
    long AuthorityRevision, string BindingDigest, DateTimeOffset OccurredAtUtc,
    string? ResultDigest = null, long? ResultSize = null, AuthorityCommitPurpose Purpose = AuthorityCommitPurpose.ReadAdmission);
public interface IAuthorityCommitter
{
    Task<AuthorityCommitStatus> CommitUnderAdmissionAsync(AuthorityCommitment commitment, CancellationToken ct = default);
}


/// <summary>Server-registered plan only. Never deserialize a plan from an HTTP request.</summary>
public sealed record ProtectedReadPlan(string Kind, Guid InstallationId, string ConsumerId, string? ConsumerConfigurationDigest = null)
{
    public static ProtectedReadPlan Metadata { get; } = new("metadata-v1", Guid.Empty, "");
    public static ProtectedReadPlan Credential(Guid installation) => new("credential-bound-v1", installation, "inventory.readonly.v1");
    public bool IsCredential => Kind == "credential-bound-v1";
    public bool IsValid => IsCredential ? InstallationId != Guid.Empty && ConsumerId == "inventory.readonly.v1" &&
        (ConsumerConfigurationDigest is null || ConsumerConfigurationDigest.Length == 64 && ConsumerConfigurationDigest.All(char.IsAsciiHexDigitLower)) : this == Metadata;
}
public sealed record ReadDependencySelection(ProtectedReadPlan Plan, Guid TenantId, Guid TargetId,
    string? ScopeKind = null, Guid? ScopeId = null);
public sealed record ReadDependencySnapshot(string Kind, ReadDependencySelection Selection, Guid BindingId,
    long Revision, string Digest, DateTimeOffset NotBeforeUtc, DateTimeOffset ExpiresAtUtc);
public interface IProtectedReadDependencyReader
{
    Task<ReadDependencySnapshot?> ReadCurrentUnderAdmissionAsync(ReadDependencySelection selection, CancellationToken ct = default);
}
