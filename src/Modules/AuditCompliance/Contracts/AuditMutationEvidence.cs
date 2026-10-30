namespace Kordra.Modules.AuditCompliance.Contracts;

/// <summary>Exact owner receipt facts to compare with AUD; possession does not confer permission.</summary>
public sealed record AuditMutationExpectation(Guid IntentId, string IntentHash, Guid SubjectId, Guid TenantId,
    Guid ResourceId, Guid InvocationId, string Permission, string ScopeKind, Guid ScopeId, string SafeReason);
public enum AuditEvidenceStatus { Matched, Missing, Mismatch, Unavailable }
public interface IAuditMutationEvidenceReader
{
    Task<AuditEvidenceStatus> MatchUnderAdmissionAsync(AuditMutationExpectation expectation, CancellationToken ct = default);
}
