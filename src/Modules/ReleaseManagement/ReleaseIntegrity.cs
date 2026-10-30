using System.Text.Json;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.ReleaseManagement.Domain;

namespace Kordra.Modules.ReleaseManagement;

/// <summary>Exact frozen subject verification; never silently fixes corrupted owner history.</summary>
internal static class ReleaseIntegrity
{
    internal static ReleaseIntent ReadFrozenOrRetired(ReleaseRevisionRecord revision)
    {
        if (revision.State is not ("Frozen" or "Retired") || revision.SpecBytes is null || revision.SpecHash is not { Length: 32 })
            throw new InvalidDataException("Release content is missing.");
        var intent = ReleaseIntent.ReadStored(revision.SpecBytes, Convert.ToHexStringLower(revision.SpecHash));
        using var document = JsonDocument.Parse(intent.GetCanonicalBytes());
        var root = document.RootElement;
        if (root.GetProperty("tenantId").GetGuid() != revision.TenantId ||
            root.GetProperty("project").GetProperty("id").GetGuid() != revision.ProjectId ||
            root.GetProperty("application").GetProperty("id").GetGuid() != revision.ApplicationId ||
            root.GetProperty("environment").GetProperty("id").GetGuid() != revision.EnvironmentId ||
            root.GetProperty("target").GetProperty("id").GetGuid() != revision.TargetId ||
            root.GetProperty("artifact").GetProperty("artifactId").GetGuid() != revision.ArtifactId)
            throw new InvalidDataException("Release owner facts do not match the revision.");
        return intent;
    }
}
