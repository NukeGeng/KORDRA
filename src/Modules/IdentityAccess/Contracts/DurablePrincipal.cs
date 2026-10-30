namespace Kordra.Modules.IdentityAccess.Contracts;

/// <summary>IAM-owned persistent identity facts, not a session or permission to act.</summary>
public sealed record DurablePrincipal(Guid PrincipalId, long PrincipalRevision, bool Active, string SecurityRevisionHash);

public interface IDurablePrincipalReader
{
    // Called under the existing common admission. Null is known absence; failures must propagate.
    Task<DurablePrincipal?> ReadUnderAdmissionAsync(Guid principalId, CancellationToken cancellationToken = default);
}

public enum DurableGrantEvidenceStatus { Current, Invalidated, Unavailable }
public interface IDurableGrantEvidenceReader
{
    // Compares an original snapshot under the existing admission, without granting an action.
    Task<DurableGrantEvidenceStatus> CompareUnderAdmissionAsync(ScopedGrant expected, CancellationToken cancellationToken = default);
}
