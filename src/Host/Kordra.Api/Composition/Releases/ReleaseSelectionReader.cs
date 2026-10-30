using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Kordra.ApprovalGovernance.Domain;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.Modules.ArtifactManagement.Contracts;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.ReleaseManagement.Domain;

namespace Kordra.Api;

/// <summary>Candidate data only; it is not an authorization, audit receipt or deployment capability.</summary>
public sealed record ReleaseSelectionResult(string Status, ReleaseIntent? Intent = null, DateTimeOffset? EligibilityExpiresAtUtc = null, ReleaseApprovalPolicy? Policy = null);

/// <summary>Composes exact owner facts under caller-held admission. Not registered as an API route.</summary>
public sealed class ReleaseSelectionReader(IReleaseCatalogReader catalog, IReleaseTargetReader targets,
    IReleaseArtifactReader artifacts, IReleaseApprovalPolicyReader policies)
{
    public async Task<ReleaseSelectionResult> ReadUnderAdmissionAsync(Guid installationId, Guid tenantId, Guid projectId,
        ReleaseSelection selection, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        if (installationId == Guid.Empty || tenantId == Guid.Empty || projectId == Guid.Empty || selection is null ||
            nowUtc.Offset != TimeSpan.Zero || nowUtc == default) throw new ArgumentException("Invalid release selection scope.");
        var prj = await catalog.ReadUnderAdmissionAsync(tenantId, projectId, selection.ApplicationId, selection.EnvironmentId, selection.TargetId, ct);
        if (prj is null) return new("owner_missing");
        var target = await targets.ReadUnderAdmissionAsync(tenantId, projectId, selection.EnvironmentId, selection.TargetId, ct);
        var artifact = await artifacts.ReadReleaseArtifactUnderAdmissionAsync(tenantId, projectId, selection.ArtifactId, ct);
        if (target is null || artifact is null) return new("owner_missing");
        if (prj.TenantId != tenantId || prj.ProjectId != projectId || prj.ApplicationId != selection.ApplicationId ||
            prj.EnvironmentId != selection.EnvironmentId || prj.TargetId != selection.TargetId || target.TenantId != tenantId ||
            target.ProjectId != projectId || target.EnvironmentId != selection.EnvironmentId || target.TargetId != selection.TargetId ||
            target.AssociationRevision != prj.AssociationRevision || artifact.ArtifactId != selection.ArtifactId || artifact.ProjectId != projectId)
            return new("owner_mismatch");
        if (prj.Classification is not ("Development" or "Test")) return new("qualification_required");
        var binding = await policies.ReadCurrentUnderAdmissionAsync(installationId, tenantId, projectId, selection.EnvironmentId, ct);
        if (binding is null) return new("policy_missing");
        if (binding.InstallationId != installationId || binding.TenantId != tenantId || binding.ProjectId != projectId ||
            binding.EnvironmentId != selection.EnvironmentId) return new("owner_mismatch");
        var payload = binding.GetCanonicalPayload();
        ReleaseApprovalPolicy policy;
        try { policy = ReleaseApprovalPolicy.Parse(payload); }
        catch (ArgumentException error) { throw new InvalidDataException("Approval policy content is invalid.", error); }
        if (!CryptographicOperations.FixedTimeEquals(payload, policy.GetCanonicalBytes()) || policy.PayloadHash != binding.PayloadHash)
            throw new InvalidDataException("Approval policy integrity failed.");
        var rule = policy.FindApplicableAllowRule(prj.Classification, nowUtc);
        if (rule is null || !rule.AllowUnattestedProvenance)
        {
            var boundary = policy.Status == "active" && nowUtc < policy.EffectiveFromUtc ? policy.EffectiveFromUtc :
                policy.ExpiresAtUtc is { } expiry && nowUtc < expiry ? expiry : (DateTimeOffset?)null;
            return new("policy_denied", EligibilityExpiresAtUtc: boundary);
        }
        var value = new
        {
            schemaVersion = 1,
            subjectKind = "release-intent-v1",
            tenantId = tenantId.ToString("D"),
            project = new { id = projectId.ToString("D"), revision = prj.ProjectRevision },
            application = new { id = prj.ApplicationId.ToString("D"), revision = prj.ApplicationRevision },
            environment = new { id = prj.EnvironmentId.ToString("D"), revision = prj.EnvironmentRevision, classification = prj.Classification },
            target = new { id = target.TargetId.ToString("D"), kind = target.Kind, revision = target.Revision, associationRevision = target.AssociationRevision },
            artifact = new
            {
                artifactId = artifact.ArtifactId.ToString("D"),
                projectId = artifact.ProjectId.ToString("D"),
                registryId = artifact.RegistryId.ToString("D"),
                repository = artifact.Repository,
                rootDigest = artifact.RootDigest,
                platform = new { os = artifact.Platform.Os, architecture = artifact.Platform.Architecture, variant = artifact.Platform.Variant },
                rootMediaType = artifact.RootMediaType,
                rootSizeBytes = artifact.RootSizeBytes,
                selectedManifestDigest = artifact.SelectedManifestDigest,
                selectedManifestSizeBytes = artifact.SelectedManifestSizeBytes,
                selectedManifestMediaType = artifact.SelectedManifestMediaType,
                configDigest = artifact.ConfigDigest,
                configSizeBytes = artifact.ConfigSizeBytes,
                observedPlatform = new { os = artifact.ObservedPlatform.Os, architecture = artifact.ObservedPlatform.Architecture, variant = artifact.ObservedPlatform.Variant },
                variantEvidence = artifact.VariantEvidence,
                verifierProfile = artifact.VerifierProfile,
                verifiedAtUtc = ExactTime(artifact.VerifiedAtUtc),
                createdAtUtc = ExactTime(artifact.CreatedAtUtc),
                contentVerification = artifact.ContentVerification,
                layerAvailability = artifact.LayerAvailability,
                signatureVerification = artifact.SignatureVerification
            },
            approvalPolicy = new
            {
                policyId = binding.PolicyId.ToString("D"),
                policyRevision = binding.PolicyRevision,
                payloadHash = binding.PayloadHash,
                bindingId = binding.BindingId.ToString("D"),
                bindingRevision = binding.BindingRevision
            },
            qualification = new { profile = "pilot", provenanceClass = "unattested", executionAuthorized = false }
        };
        try { return new("eligible", ReleaseIntent.FromOwnerFacts(JsonSerializer.SerializeToUtf8Bytes(value)), policy.ExpiresAtUtc, policy); }
        catch (ArgumentException error) { throw new InvalidDataException("Current owner facts are invalid.", error); }
    }
    private static string ExactTime(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0) throw new InvalidDataException("Owner timestamp precision is invalid.");
        return value.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    }
}
