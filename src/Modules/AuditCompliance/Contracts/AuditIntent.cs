namespace Kordra.Modules.AuditCompliance.Contracts;

/// <summary>Stages are the approved audit v1 admission and owner-mutation boundaries.</summary>
public enum AuditIntentStage
{
    Use,
    Disclosure,
    Mutation,
    Outcome
}

/// <summary>The observed result of a protected stage, recorded separately from its intent.</summary>
public enum AuditActionOutcome
{
    Completed,
    Denied,
    Cancelled,
    Unknown
}

/// <summary>Nonsecret metadata for one immutable audit intent. No operation result payload is accepted.</summary>
public sealed record AuditIntent(
    int SchemaVersion,
    Guid IntentId,
    Guid InvocationId,
    AuditIntentStage Stage,
    Guid? SubjectRef,
    Guid? TenantRef,
    Guid? ResourceRef,
    string Permission,
    string ScopeKind,
    Guid? ScopeId,
    long? PolicyRevision,
    long? AuthorityRevision,
    string SafeReason,
    DateTimeOffset OccurredAtUtc,
    Guid CorrelationId,
    string? ResultDigest = null,
    long? ResultSizeBytes = null,
    AuditActionOutcome? ActionOutcome = null);
