using System.Text.Json;
using System.Text.Json.Serialization;
using Kordra.ArtifactManagement.Domain;
using Kordra.Modules.ArtifactManagement.Contracts;

namespace Kordra.Modules.ArtifactManagement.Infrastructure;

internal static class ArtifactStorageJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    internal sealed record RequestData(Guid TenantId, Guid ProjectId, Guid RegistryId, string Repository,
        string Digest, ArtifactPlatformV1 Platform, ArtifactSourceClaimV1? SourceClaim);

    internal static ArtifactPlatformV1 Platform(string json)
    {
        var value = JsonSerializer.Deserialize<ArtifactPlatformV1>(json, Options)
            ?? throw new InvalidOperationException("Stored artifact platform is invalid.");
        _ = ArtifactPlatform.Create(value.Os, value.Architecture, value.Variant);
        return value;
    }

    internal static string Platform(ArtifactPlatform value) =>
        JsonSerializer.Serialize(new ArtifactPlatformV1(value.Os, value.Architecture, value.Variant), Options);

    internal static ArtifactSourceClaimV1? Claim(ArtifactSourceClaim? value) => value is null ? null
        : new(value.SourceRepository, value.SourceCommit, value.BuildRef);

    internal static string Request(ArtifactRegistrationRequest request)
    {
        var identity = request.Identity;
        return JsonSerializer.Serialize(new RequestData(identity.TenantId, identity.ProjectId, identity.RegistryId,
            identity.Repository, identity.RootDigest.Value,
            new(identity.Platform.Os, identity.Platform.Architecture, identity.Platform.Variant), Claim(request.SourceClaim)), Options);
    }

    internal static ArtifactRegistrationRequest Request(ArtifactRegistrationRecord row)
    {
        var data = JsonSerializer.Deserialize<RequestData>(row.RequestJson, Options)
            ?? throw new InvalidOperationException("Stored registration request is invalid.");
        if (data.Platform is null) throw new InvalidOperationException("Stored registration platform is missing.");
        var identity = ArtifactIdentity.Create(data.TenantId, data.ProjectId, data.RegistryId, data.Repository,
            OCIImageDigest.Parse(data.Digest), ArtifactPlatform.Create(data.Platform.Os, data.Platform.Architecture, data.Platform.Variant));
        var claim = data.SourceClaim is null ? null : ArtifactSourceClaim.Create(data.SourceClaim.SourceRepository,
            data.SourceClaim.SourceCommit, data.SourceClaim.BuildRef);
        var result = new ArtifactRegistrationRequest(identity, claim);
        if (identity.TenantId != row.TenantId || identity.ProjectId != row.ProjectId ||
            identity.ComputeIdentityHash() != row.IdentityHash || result.ComputeBusinessHash() != row.BusinessHash)
            throw new InvalidOperationException("Stored registration identity does not match its immutable receipt.");
        return result;
    }
}
