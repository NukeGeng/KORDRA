using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.ArtifactManagement.Infrastructure.Registry;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

/// <summary>Invocation-local eligibility, never a reusable permit or public response.</summary>
public sealed class ArtifactAuthorityBinding
{
    internal ArtifactAuthorityBinding(ArtifactProjectAuthoritySnapshot project, RegistryTrust? registry,
        RegistryCredentialBindingSnapshotV1? credential, DateTimeOffset originalUntil, DateTimeOffset validUntil)
    {
        Facts = project; Registry = registry; Credential = credential;
        OriginalUntilUtc = originalUntil; ValidUntilUtc = validUntil;
        Digest = ComputeDigest(project.InvocationId, project.Digest, project.Grant.Permission,
            registry?.ConfigurationDigest, credential?.Digest(), originalUntil);
    }
    public ArtifactProjectAuthoritySnapshot Facts { get; }
    public RegistryTrust? Registry { get; }
    public RegistryCredentialBindingSnapshotV1? Credential { get; }
    public DateTimeOffset OriginalUntilUtc { get; }
    public DateTimeOffset ValidUntilUtc { get; }
    public string Digest { get; }
    public override string ToString() => "ArtifactAuthorityBinding [private]";

    internal static string ComputeDigest(Guid invocation, string projectDigest, string action,
        string? registryDigest, string? credentialDigest, DateTimeOffset originalUntil)
    {
        using var output = new MemoryStream(); output.Write(Encoding.UTF8.GetBytes("artifact-authority-v1\n"));
        using (var w = new Utf8JsonWriter(output))
        {
            w.WriteStartObject(); w.WriteNumber("schemaVersion", 1); w.WriteString("predicate", "artifact-registration-v1");
            w.WriteString("invocationId", invocation); w.WriteString("projectAuthorityDigest", projectDigest);
            w.WriteString("action", action); w.WriteString("registryConfigurationDigest", registryDigest);
            w.WriteString("credentialBindingDigest", credentialDigest);
            w.WriteString("originalExpiresAtUtc", originalUntil.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)); w.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(output.GetBuffer().AsSpan(0, checked((int)output.Length))));
    }
}

/// <summary>Current owner reads only under the caller's held shared gate; no default registry credential.</summary>
public sealed class ArtifactAuthorityResolver(Guid installation, ArtifactProjectAuthorityReader projects,
    IDbContextFactory<CredentialDbContext> credentials, IAuthorityClock clock, RegistryTrustConfiguration? configuration)
{
    public RegistryTrustConfiguration? Configuration => configuration;

    public async Task<ArtifactAuthorityBinding?> ResolveAsync(SessionSelection subject, Guid project, string permission,
        Guid invocation, Guid? registryId = null, string? repository = null, DateTimeOffset? originalUntil = null, CancellationToken ct = default)
    {
        var facts = await projects.ReadCurrentUnderAdmissionAsync(subject, project, permission, invocation, ct);
        if (facts is null || registryId is null && repository is not null || registryId is not null && permission != "artifact.register") return null;
        RegistryTrust? registry = null; RegistryCredentialBindingSnapshotV1? binding = null;
        var until = facts.ValidUntilUtc;
        if (registryId is { } id)
        {
            if (configuration is null || configuration.InstallationId != installation) return null;
            registry = configuration.Registries.SingleOrDefault(r => r.RegistryId == id && r.TenantId == facts.Project.TenantId &&
                r.ProjectId == project && r.Repository == repository);
            if (registry is null) return null;
            if (registry.AuthMode != "anonymous")
            {
                await using var db = await credentials.CreateDbContextAsync(ct);
                binding = await new RegistryCredentialBindingStore(db).ReadCurrentUnderAdmissionAsync(
                    new(installation, facts.Project.TenantId, project, id, registry.Repository), ct);
                if (binding is null || !binding.IsValidAt(clock.GetTrustedUtcNow())) return null;
                if (binding.ExpiresAtUtc < until) until = binding.ExpiresAtUtc;
            }
        }
        var original = originalUntil ?? until;
        if (original < until) until = original;
        if (clock.GetTrustedUtcNow() >= until) return null;
        return new(facts, registry, binding, original, until);
    }

    public Task<ArtifactAuthorityBinding?> RecheckAsync(SessionSelection subject, ArtifactAuthorityBinding original, CancellationToken ct) =>
        ResolveAsync(subject, original.Facts.Project.ProjectId, original.Facts.Grant.Permission, original.Facts.InvocationId,
            original.Registry?.RegistryId, original.Registry?.Repository, original.OriginalUntilUtc, ct);
}
