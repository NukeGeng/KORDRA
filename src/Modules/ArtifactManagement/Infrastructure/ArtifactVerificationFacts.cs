using Kordra.ArtifactManagement.Domain;

namespace Kordra.Modules.ArtifactManagement.Infrastructure;

/// <summary>
/// Private verifier output. Only this ART assembly can construct it; a browser/Host DTO
/// is never sufficient. Synthetic construction is exposed only to the friend test assembly.
/// </summary>
public sealed class ArtifactVerificationFacts
{
    internal const string OciManifest = "application/vnd.oci.image.manifest.v1+json";
    internal const string OciIndex = "application/vnd.oci.image.index.v1+json";
    internal const string DockerManifest = "application/vnd.docker.distribution.manifest.v2+json";
    internal const string DockerIndex = "application/vnd.docker.distribution.manifest.list.v2+json";

    internal ArtifactVerificationFacts(ArtifactIdentity identity, string rootMediaType, long rootSize,
        OCIImageDigest selectedDigest, string selectedMediaType, long selectedSize,
        OCIImageDigest configDigest, long configSize, ArtifactPlatform observedPlatform,
        string variantEvidence, DateTimeOffset capturedAtUtc)
    {
        if (identity is null || selectedDigest is null || configDigest is null || observedPlatform is null ||
            rootMediaType is not (OciManifest or OciIndex or DockerManifest or DockerIndex) ||
            selectedMediaType is not (OciManifest or DockerManifest) ||
            rootSize is < 1 or > 1048576 || selectedSize is < 1 or > 1048576 || configSize is < 1 or > 1048576 ||
            observedPlatform.Os != identity.Platform.Os || observedPlatform.Architecture != identity.Platform.Architecture ||
            capturedAtUtc.Offset != TimeSpan.Zero || capturedAtUtc == default || capturedAtUtc == DateTimeOffset.MaxValue)
            throw new InvalidOperationException("Artifact verifier facts are invalid.");
        var single = rootMediaType is OciManifest or DockerManifest;
        if (single && (identity.RootDigest != selectedDigest || rootSize != selectedSize || rootMediaType != selectedMediaType))
            throw new InvalidOperationException("Single-manifest verifier identity does not match.");
        var validVariant = variantEvidence switch
        {
            "none" => identity.Platform.Variant is null && observedPlatform.Variant is null,
            "config" => identity.Platform.Variant == "v8" && observedPlatform.Variant == "v8",
            "descriptor" => !single && identity.Platform.Variant == "v8" && observedPlatform.Variant is null,
            _ => false
        };
        if (!validVariant) throw new InvalidOperationException("Artifact variant evidence does not match.");
        (Identity, RootMediaType, RootSizeBytes, SelectedManifestDigest, SelectedManifestMediaType,
            SelectedManifestSizeBytes, ConfigDigest, ConfigSizeBytes, ObservedPlatform, VariantEvidence, CapturedAtUtc) =
            (identity, rootMediaType, rootSize, selectedDigest, selectedMediaType, selectedSize, configDigest,
                configSize, observedPlatform, variantEvidence, capturedAtUtc);
    }

    public ArtifactIdentity Identity { get; }
    public string RootMediaType { get; }
    public long RootSizeBytes { get; }
    public OCIImageDigest SelectedManifestDigest { get; }
    public string SelectedManifestMediaType { get; }
    public long SelectedManifestSizeBytes { get; }
    public OCIImageDigest ConfigDigest { get; }
    public long ConfigSizeBytes { get; }
    public ArtifactPlatform ObservedPlatform { get; }
    public string VariantEvidence { get; }
    public DateTimeOffset CapturedAtUtc { get; }

    internal ArtifactRecord NewRecord(Guid id, DateTimeOffset createdAtUtc) => new()
    {
        Id = id,
        TenantId = Identity.TenantId,
        ProjectId = Identity.ProjectId,
        RegistryId = Identity.RegistryId,
        Repository = Identity.Repository,
        RootDigest = Identity.RootDigest.Value,
        PlatformJson = ArtifactStorageJson.Platform(Identity.Platform),
        RootMediaType = RootMediaType,
        RootSizeBytes = RootSizeBytes,
        SelectedManifestDigest = SelectedManifestDigest.Value,
        SelectedManifestMediaType = SelectedManifestMediaType,
        SelectedManifestSizeBytes = SelectedManifestSizeBytes,
        ConfigDigest = ConfigDigest.Value,
        ConfigSizeBytes = ConfigSizeBytes,
        ObservedPlatformJson = ArtifactStorageJson.Platform(ObservedPlatform),
        VariantEvidence = VariantEvidence,
        VerifiedAtUtc = CapturedAtUtc,
        CreatedAtUtc = createdAtUtc
    };

    internal bool Matches(ArtifactRecord row) =>
        row.TenantId == Identity.TenantId && row.ProjectId == Identity.ProjectId && row.RegistryId == Identity.RegistryId &&
        row.Repository == Identity.Repository && row.RootDigest == Identity.RootDigest.Value &&
        row.PlatformJson == ArtifactStorageJson.Platform(Identity.Platform) && row.RootMediaType == RootMediaType &&
        row.RootSizeBytes == RootSizeBytes && row.SelectedManifestDigest == SelectedManifestDigest.Value &&
        row.SelectedManifestSizeBytes == SelectedManifestSizeBytes && row.SelectedManifestMediaType == SelectedManifestMediaType &&
        row.ConfigDigest == ConfigDigest.Value && row.ConfigSizeBytes == ConfigSizeBytes &&
        row.ObservedPlatformJson == ArtifactStorageJson.Platform(ObservedPlatform) && row.VariantEvidence == VariantEvidence &&
        row.VerifierProfile == "artifact-registry-v1";
}
