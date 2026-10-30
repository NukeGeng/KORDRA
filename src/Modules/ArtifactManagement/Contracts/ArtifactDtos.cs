namespace Kordra.Modules.ArtifactManagement.Contracts;

// Data projections only. They confer no authority and are never accepted as verified input.
public sealed record ArtifactPlatformV1(string Os, string Architecture, string? Variant);
public sealed record ArtifactSourceClaimV1(string SourceRepository, string SourceCommit, string? BuildRef);
public sealed record ArtifactRegistrationRequestV1(int SchemaVersion, Guid CorrelationId, Guid RegistryId,
    string Repository, string Digest, ArtifactPlatformV1 Platform, ArtifactSourceClaimV1? SourceClaim);
public sealed record ArtifactV1(Guid ArtifactId, Guid ProjectId, Guid RegistryId, string Repository,
    string RootDigest, ArtifactPlatformV1 Platform, string RootMediaType, long RootSizeBytes,
    string SelectedManifestDigest, long SelectedManifestSizeBytes, string SelectedManifestMediaType,
    string ConfigDigest, long ConfigSizeBytes, ArtifactPlatformV1 ObservedPlatform, string VariantEvidence,
    string VerifierProfile, DateTimeOffset VerifiedAtUtc, DateTimeOffset CreatedAtUtc,
    string ContentVerification, string LayerAvailability, string SignatureVerification);
public sealed record ArtifactRegistrationV1(Guid RegistrationId, string Status, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc, DateTimeOffset? AttemptVerifiedAtUtc, string? SafeReason,
    ArtifactV1? Artifact, ArtifactSourceClaimV1? SourceClaim, string Provenance);
public sealed record ArtifactSummaryV1(Guid ArtifactId, Guid RegistryId, string Repository,
    string RootDigest, ArtifactPlatformV1 Platform, DateTimeOffset CreatedAtUtc, string ContentVerification);
public sealed record ArtifactRegistrySelectorV1(Guid RegistryId, string DisplayName, string Repository, bool AuthRequired);

/// <summary>Owner-only immutable facts for release selection; the caller holds admission and authorizes the action.</summary>
public interface IReleaseArtifactReader
{
    Task<ArtifactV1?> ReadReleaseArtifactUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid artifactId,
        CancellationToken cancellationToken = default);
}
