using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kordra.ArtifactManagement.Domain;

/// <summary>Exact requested platform, never a wildcard or a runtime compatibility assertion.</summary>
public sealed record ArtifactPlatform
{
    private ArtifactPlatform(string os, string architecture, string? variant) =>
        (Os, Architecture, Variant) = (os, architecture, variant);

    public string Os { get; }
    public string Architecture { get; }
    public string? Variant { get; }

    public static ArtifactPlatform Create(string os, string architecture, string? variant)
    {
        if (os != "linux" || architecture is not ("amd64" or "arm64") ||
            (variant is not null && (architecture != "arm64" || variant != "v8")))
        {
            throw new FormatException("Unsupported artifact platform.");
        }

        return new(os, architecture, variant);
    }
}

/// <summary>A user's unverified declaration, not evidence from a CI provider.</summary>
public sealed record ArtifactSourceClaim
{
    private ArtifactSourceClaim(string repository, string commit, string? buildRef) =>
        (SourceRepository, SourceCommit, BuildRef) = (repository, commit, buildRef);

    public string SourceRepository { get; }
    public string SourceCommit { get; }
    public string? BuildRef { get; }

    public static ArtifactSourceClaim Create(string repository, string commit, string? buildRef)
    {
        if (repository is null || repository.Length is < 1 or > 128 ||
            !repository.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '/' or '-') ||
            repository.Split('/').Any(segment => segment is "" or "." or "..") ||
            commit is null || commit.Length is not (40 or 64) ||
            !commit.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9') ||
            (buildRef is not null && (buildRef.Length is < 1 or > 128 || !RegistrationEncoding.IsKeyCharacterString(buildRef))))
        {
            throw new FormatException("Invalid artifact source declaration.");
        }

        return new(repository, commit, buildRef);
    }
}

/// <summary>
/// Immutable project content identity. Validating IDs does not establish project ownership,
/// permissions, registry existence, verified provenance or deployment eligibility.
/// </summary>
public sealed record ArtifactIdentity
{
    private static readonly Regex RepositoryPattern = new(
        @"\A[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*(/[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*)*\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private ArtifactIdentity(Guid tenantId, Guid projectId, Guid registryId, string repository,
        OCIImageDigest digest, ArtifactPlatform platform) =>
        (TenantId, ProjectId, RegistryId, Repository, RootDigest, Platform) =
        (tenantId, projectId, registryId, repository, digest, platform);

    public Guid TenantId { get; }
    public Guid ProjectId { get; }
    public Guid RegistryId { get; }
    public string Repository { get; }
    public OCIImageDigest RootDigest { get; }
    public ArtifactPlatform Platform { get; }

    public static ArtifactIdentity Create(Guid tenantId, Guid projectId, Guid registryId,
        string repository, OCIImageDigest digest, ArtifactPlatform platform)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || registryId == Guid.Empty ||
            repository is null || repository.Length is < 1 or > 255 || !RepositoryPattern.IsMatch(repository) ||
            digest is null || platform is null)
        {
            throw new FormatException("Invalid artifact identity.");
        }

        return new(tenantId, projectId, registryId, repository, digest, platform);
    }

    public string ComputeIdentityHash() => RegistrationEncoding.Hash("artifact-identity-v1\n", writer =>
    {
        writer.WriteStartObject();
        WriteFields(writer);
        writer.WriteEndObject();
    });

    internal void WriteFields(Utf8JsonWriter writer)
    {
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("projectId", ProjectId);
        writer.WriteString("tenantId", TenantId);
        writer.WriteString("registryId", RegistryId);
        writer.WriteString("repository", Repository);
        writer.WriteString("rootDigest", RootDigest.Value);
        writer.WriteStartObject("platform");
        writer.WriteString("os", Platform.Os);
        writer.WriteString("architecture", Platform.Architecture);
        writer.WriteString("variant", Platform.Variant);
        writer.WriteEndObject();
    }
}

/// <summary>
/// Exact business input and scoped idempotency hashes. Correlation IDs and authority snapshots
/// deliberately have no place here; current authorization is still required at every boundary.
/// </summary>
public sealed class ArtifactRegistrationRequest
{
    public ArtifactRegistrationRequest(ArtifactIdentity identity, ArtifactSourceClaim? sourceClaim)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Identity = identity;
        SourceClaim = sourceClaim;
    }

    public ArtifactIdentity Identity { get; }
    public ArtifactSourceClaim? SourceClaim { get; }

    public string ComputeBusinessHash() => RegistrationEncoding.Hash("artifact-registration-request-v1\n", writer =>
    {
        writer.WriteStartObject();
        Identity.WriteFields(writer);
        if (SourceClaim is null)
        {
            writer.WriteNull("sourceClaim");
        }
        else
        {
            writer.WriteStartObject("sourceClaim");
            writer.WriteString("sourceRepository", SourceClaim.SourceRepository);
            writer.WriteString("sourceCommit", SourceClaim.SourceCommit);
            writer.WriteString("buildRef", SourceClaim.BuildRef);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    });

    public string ComputeIdempotencyKeyHash(Guid installationId, Guid principalId, string key)
    {
        if (installationId == Guid.Empty || principalId == Guid.Empty || key is null ||
            key.Length is < 16 or > 128 || !RegistrationEncoding.IsKeyCharacterString(key))
        {
            throw new FormatException("Invalid artifact registration key scope.");
        }

        return RegistrationEncoding.Hash("artifact-registration-key-v1\n", writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("installationId", installationId);
            writer.WriteString("tenantId", Identity.TenantId);
            writer.WriteString("projectId", Identity.ProjectId);
            writer.WriteString("principalId", principalId);
            writer.WriteString("action", "artifact.register");
            writer.WriteString("key", key);
            writer.WriteEndObject();
        });
    }
}

public enum ArtifactRegistrationState { Pending, Registered, Failed, Abandoned }
public enum ArtifactRegistrationFailure
{
    UnsupportedImage, RegistryUnavailable, AuthorityLost, InvalidCredential, Cancelled, IntegrityConflict, AttemptExpired
}

/// <summary>
/// Immutable staged receipt state. It does not prove a durable commit, authorize recovery,
/// or replace the store's atomic compare/uniqueness checks and same-transaction audit.
/// </summary>
public sealed class ArtifactRegistrationReceipt
{
    private ArtifactRegistrationReceipt(Guid id, Guid installationId, Guid bootId, Guid invocationId,
        Guid principalId, ArtifactRegistrationRequest request, string keyHash, DateTimeOffset createdAtUtc,
        ArtifactRegistrationState state, Guid? artifactId = null, DateTimeOffset? completedAtUtc = null,
        DateTimeOffset? attemptVerifiedAtUtc = null, string? safeReason = null)
    {
        (Id, InstallationId, BootId, InvocationId, PrincipalId, Request, KeyHash, CreatedAtUtc,
            State, ArtifactId, CompletedAtUtc, AttemptVerifiedAtUtc, SafeReason) =
            (id, installationId, bootId, invocationId, principalId, request, keyHash, createdAtUtc,
                state, artifactId, completedAtUtc, attemptVerifiedAtUtc, safeReason);
    }

    public Guid Id { get; }
    public Guid InstallationId { get; }
    public Guid BootId { get; }
    public Guid InvocationId { get; }
    public Guid PrincipalId { get; }
    public ArtifactRegistrationRequest Request { get; }
    public string KeyHash { get; }
    public string BusinessHash => Request.ComputeBusinessHash();
    public DateTimeOffset CreatedAtUtc { get; }
    public ArtifactRegistrationState State { get; }
    public Guid? ArtifactId { get; }
    public DateTimeOffset? CompletedAtUtc { get; }
    public DateTimeOffset? AttemptVerifiedAtUtc { get; }
    public string? SafeReason { get; }

    public static ArtifactRegistrationReceipt Pending(Guid id, Guid installationId, Guid bootId,
        Guid invocationId, Guid principalId, ArtifactRegistrationRequest request, string key, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || bootId == Guid.Empty || invocationId == Guid.Empty || request is null)
        {
            throw new FormatException("Invalid artifact registration receipt.");
        }

        RequireUtc(createdAtUtc);
        var keyHash = request.ComputeIdempotencyKeyHash(installationId, principalId, key);
        return new(id, installationId, bootId, invocationId, principalId, request, keyHash,
            createdAtUtc, ArtifactRegistrationState.Pending);
    }

    public ArtifactRegistrationReceipt Register(Guid artifactId, ArtifactIdentity identity,
        DateTimeOffset verifiedAtUtc, DateTimeOffset completedAtUtc)
    {
        RequirePending(completedAtUtc);
        RequireUtc(verifiedAtUtc);
        if (artifactId == Guid.Empty || identity != Request.Identity ||
            verifiedAtUtc < CreatedAtUtc || verifiedAtUtc > completedAtUtc)
        {
            throw new InvalidOperationException("Artifact registration facts do not match the pending attempt.");
        }

        return Next(ArtifactRegistrationState.Registered, completedAtUtc, artifactId, verifiedAtUtc);
    }

    public ArtifactRegistrationReceipt Fail(ArtifactRegistrationFailure failure, DateTimeOffset completedAtUtc)
    {
        RequirePending(completedAtUtc);
        var reason = failure switch
        {
            ArtifactRegistrationFailure.UnsupportedImage => "unsupported_image",
            ArtifactRegistrationFailure.RegistryUnavailable => "registry_unavailable",
            ArtifactRegistrationFailure.AuthorityLost => "authority_lost",
            ArtifactRegistrationFailure.InvalidCredential => "invalid_credential",
            ArtifactRegistrationFailure.Cancelled => "cancelled",
            ArtifactRegistrationFailure.IntegrityConflict => "integrity_conflict",
            ArtifactRegistrationFailure.AttemptExpired => "attempt_expired",
            _ => throw new ArgumentOutOfRangeException(nameof(failure), "Unsupported registration failure.")
        };
        return Next(ArtifactRegistrationState.Failed, completedAtUtc, safeReason: reason);
    }

    /// <summary>The offline caller must separately prove stopped process/quiescence and exact owner/audit state.</summary>
    public ArtifactRegistrationReceipt Abandon(DateTimeOffset completedAtUtc)
    {
        RequirePending(completedAtUtc);
        return Next(ArtifactRegistrationState.Abandoned, completedAtUtc, safeReason: "interrupted_attempt");
    }

    private ArtifactRegistrationReceipt Next(ArtifactRegistrationState state, DateTimeOffset completed,
        Guid? artifactId = null, DateTimeOffset? verified = null, string? safeReason = null) =>
        new(Id, InstallationId, BootId, InvocationId, PrincipalId, Request, KeyHash, CreatedAtUtc,
            state, artifactId, completed, verified, safeReason);

    private void RequirePending(DateTimeOffset completedAtUtc)
    {
        RequireUtc(completedAtUtc);
        if (State != ArtifactRegistrationState.Pending || completedAtUtc < CreatedAtUtc)
        {
            throw new InvalidOperationException("Artifact registration cannot transition from this state or time.");
        }
    }

    private static void RequireUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value == default || value == DateTimeOffset.MaxValue)
        {
            throw new FormatException("A bounded trusted UTC timestamp is required.");
        }
    }
}

internal static class RegistrationEncoding
{
    internal static bool IsKeyCharacterString(string value) =>
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');

    internal static string Hash(string domain, Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetBytes(domain));
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}
