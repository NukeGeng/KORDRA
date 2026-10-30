using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kordra.ReleaseManagement.Domain;

/// <summary>Selected owner IDs only. A valid value does not prove ownership or authority.</summary>
public sealed record ReleaseSelection
{
    private ReleaseSelection(Guid applicationId, Guid environmentId, Guid targetId, Guid artifactId) =>
        (ApplicationId, EnvironmentId, TargetId, ArtifactId) = (applicationId, environmentId, targetId, artifactId);
    public Guid ApplicationId { get; }
    public Guid EnvironmentId { get; }
    public Guid TargetId { get; }
    public Guid ArtifactId { get; }

    public static ReleaseSelection Create(Guid applicationId, Guid environmentId, Guid targetId, Guid artifactId)
    {
        if (applicationId == Guid.Empty || environmentId == Guid.Empty || targetId == Guid.Empty || artifactId == Guid.Empty)
            throw new ArgumentException("Release selection IDs must not be empty.");
        return new(applicationId, environmentId, targetId, artifactId);
    }
}

/// <summary>Draft value semantics; Host must independently resolve current permissions and owner facts.</summary>
public sealed record ReleaseDraft
{
    private ReleaseDraft(Guid tenantId, Guid projectId, Guid releaseId, Guid revisionId, long revisionNumber,
        Guid creatorPrincipalId, long version, string label, ReleaseSelection selection) =>
        (TenantId, ProjectId, ReleaseId, RevisionId, RevisionNumber, CreatorPrincipalId, Version, Label, Selection) =
        (tenantId, projectId, releaseId, revisionId, revisionNumber, creatorPrincipalId, version, label, selection);

    public Guid TenantId { get; }
    public Guid ProjectId { get; }
    public Guid ReleaseId { get; }
    public Guid RevisionId { get; }
    public long RevisionNumber { get; }
    public Guid CreatorPrincipalId { get; }
    public long Version { get; }
    public string Label { get; }
    public ReleaseSelection Selection { get; }

    public static ReleaseDraft Create(Guid tenantId, Guid projectId, Guid releaseId, Guid revisionId,
        long revisionNumber, Guid creatorPrincipalId, string label, ReleaseSelection selection)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || releaseId == Guid.Empty || revisionId == Guid.Empty ||
            creatorPrincipalId == Guid.Empty || revisionNumber <= 0 || selection is null)
            throw new ArgumentException("Invalid draft identity.");
        ValidateLabel(label);
        return new(tenantId, projectId, releaseId, revisionId, revisionNumber, creatorPrincipalId, 1, label, selection);
    }

    /// <summary>Rehydrate a Draft's value semantics without conferring authority.</summary>
    public static ReleaseDraft Restore(Guid tenantId, Guid projectId, Guid releaseId, Guid revisionId,
        long revisionNumber, Guid creatorPrincipalId, long version, string label, ReleaseSelection selection)
    {
        var initial = Create(tenantId, projectId, releaseId, revisionId, revisionNumber, creatorPrincipalId, label, selection);
        if (version <= 0) throw new ArgumentException("Invalid draft version.");
        return new(initial.TenantId, initial.ProjectId, initial.ReleaseId, initial.RevisionId, initial.RevisionNumber,
            initial.CreatorPrincipalId, version, initial.Label, initial.Selection);
    }

    /// <summary>Only value-level identity/CAS; current release.create authorization is enforced by Host.</summary>
    public ReleaseDraft Replace(Guid actorPrincipalId, long expectedVersion, string label, ReleaseSelection selection)
    {
        if (actorPrincipalId != CreatorPrincipalId || expectedVersion != Version || Version == long.MaxValue)
            throw new InvalidOperationException("Draft creator or version does not match.");
        ArgumentNullException.ThrowIfNull(selection);
        ValidateLabel(label);
        return new(TenantId, ProjectId, ReleaseId, RevisionId, RevisionNumber, CreatorPrincipalId,
            checked(Version + 1), label, selection);
    }

    internal static void ValidateLabel(string label)
    {
        if (string.IsNullOrEmpty(label) || !label.IsNormalized(NormalizationForm.FormC))
            throw new ArgumentException("Label must be nonempty NFC text.");
        var span = label.AsSpan();
        var count = 0;
        while (!span.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(span, out var rune, out var consumed) != OperationStatus.Done || Rune.IsControl(rune))
                throw new ArgumentException("Label contains invalid or control characters.");
            count++;
            if (count > 128) throw new ArgumentException("Label is too long.");
            span = span[consumed..];
        }
    }
}

/// <summary>Preview comparison only; it does not validate a spec or authorize freezing.</summary>
public static class ReleasePreviewHash
{
    public static string Compute(Guid revisionId, long version, string specHash)
    {
        if (revisionId == Guid.Empty || version <= 0 || !IsHash(specHash)) throw new ArgumentException("Invalid preview identity.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("releaseRevisionId", revisionId.ToString("D"));
            writer.WriteString("specHash", specHash);
            writer.WriteNumber("version", version);
            writer.WriteEndObject();
        }
        var prefix = Encoding.UTF8.GetBytes("release-selection-v1\n");
        var payload = stream.ToArray();
        var input = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(input, 0);
        payload.CopyTo(input, prefix.Length);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    public static bool Matches(Guid revisionId, long version, string specHash, string expectedSelectionHash)
    {
        if (!IsHash(expectedSelectionHash)) return false;
        var actual = Compute(revisionId, version, specHash);
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expectedSelectionHash));
    }

    private static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
