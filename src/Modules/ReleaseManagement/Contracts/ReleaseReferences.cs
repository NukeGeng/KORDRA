namespace Kordra.Modules.ReleaseManagement.Contracts;

/// <summary>Immutable owner facts for another module; possession never conveys permission or execution authority.</summary>
public sealed class FrozenReleaseReference
{
    private readonly byte[] _subject;
    public FrozenReleaseReference(Guid installationId, Guid tenantId, Guid projectId, Guid releaseId, Guid revisionId, string specHash, byte[] canonicalSubject)
    {
        InstallationId = installationId; TenantId = tenantId; ProjectId = projectId; ReleaseId = releaseId; RevisionId = revisionId;
        SpecHash = specHash; _subject = (byte[])canonicalSubject.Clone();
    }
    public Guid InstallationId { get; }
    public Guid TenantId { get; }
    public Guid ProjectId { get; }
    public Guid ReleaseId { get; }
    public Guid RevisionId { get; }
    public string SpecHash { get; }
    public byte[] GetCanonicalSubject() => (byte[])_subject.Clone();
}
public interface IFrozenReleaseReader
{
    /// <summary>Caller holds admission and independently checks permission, owner facts, policy and eligibility.</summary>
    Task<FrozenReleaseReference?> ReadFrozenUnderAdmissionAsync(Guid installationId, Guid tenantId, Guid projectId, Guid revisionId,
        CancellationToken cancellationToken = default);
}

/// <summary>Host proof that the installation lock is held while all writers are stopped.</summary>
public interface IReleaseOfflineCustody { void RequireHeld(); }
