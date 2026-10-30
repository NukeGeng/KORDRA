using System.Text.Json;

namespace Kordra.ReleaseManagement.Domain;

/// <summary>
/// Closed, immutable business command. Tenant, actor and new server IDs are not accepted from wire bodies.
/// The command and hashes are not authority, audit evidence or proof of commit.
/// </summary>
public sealed class ReleaseMutationCommand
{
    private ReleaseMutationCommand(Guid projectId, string action, string route, Guid? releaseId, Guid? revisionId,
        Guid? baseRevisionId, string? label, ReleaseSelection? selection, long? expectedVersion,
        string? expectedSelectionHash, string? reasonCode, ReadOnlyMemory<byte> body, string? ifMatch)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Invalid project.");
        ProjectId = projectId; Action = action; Route = route; ReleaseId = releaseId; RevisionId = revisionId;
        BaseRevisionId = baseRevisionId; Label = label; Selection = selection; ExpectedVersion = expectedVersion;
        ExpectedSelectionHash = expectedSelectionHash; ReasonCode = reasonCode; IfMatch = ifMatch;
        BusinessHash = ReleaseCommandHash.Body(action, body, ifMatch);
    }

    public Guid ProjectId { get; }
    public string Action { get; }
    public string Route { get; }
    public Guid? ReleaseId { get; }
    public Guid? RevisionId { get; }
    public Guid? BaseRevisionId { get; }
    public string? Label { get; }
    public ReleaseSelection? Selection { get; }
    public long? ExpectedVersion { get; }
    public string? ExpectedSelectionHash { get; }
    public string? ReasonCode { get; }
    public string? IfMatch { get; }
    public string BusinessHash { get; }
    public string Permission => Action == "retire" ? "release.retire" : "release.create";
    public string ScopedKeyHash(Guid installationId, Guid tenantId, Guid actorId, string key) =>
        ReleaseCommandHash.ScopedKey(installationId, tenantId, actorId, Action, Route, key);

    public static ReleaseMutationCommand Create(Guid projectId, string label, ReleaseSelection selection)
    {
        ValidateDraft(label, selection);
        return new(projectId, "create-draft", Prefix(projectId) + "/releases", null, null, null, label, selection, null,
            null, null, DraftBody(label, selection), null);
    }
    public static ReleaseMutationCommand Next(Guid projectId, Guid releaseId, Guid baseRevisionId, string label, ReleaseSelection selection)
    {
        ValidateDraft(label, selection); RequireId(releaseId); RequireId(baseRevisionId);
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            baseRevisionId = baseRevisionId.ToString("D"),
            label,
            applicationId = selection.ApplicationId.ToString("D"),
            environmentId = selection.EnvironmentId.ToString("D"),
            targetId = selection.TargetId.ToString("D"),
            artifactId = selection.ArtifactId.ToString("D")
        });
        return new(projectId, "next-draft", Prefix(projectId) + $"/releases/{releaseId:D}/revisions", releaseId, null,
            baseRevisionId, label, selection, null, null, null, body, null);
    }
    public static ReleaseMutationCommand Replace(Guid projectId, Guid revisionId, long version, string ifMatch,
        string label, ReleaseSelection selection)
    {
        ValidateDraft(label, selection); ValidateEtag(revisionId, version, ifMatch);
        return new(projectId, "replace-draft", RevisionRoute(projectId, revisionId, "draft"), null, revisionId, null,
            label, selection, version, null, null, DraftBody(label, selection), ifMatch);
    }
    public static ReleaseMutationCommand Freeze(Guid projectId, Guid revisionId, long version, string ifMatch, string expectedSelectionHash)
    {
        ValidateEtag(revisionId, version, ifMatch);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, expectedSelectionHash });
        return new(projectId, "freeze", RevisionRoute(projectId, revisionId, "freeze"), null, revisionId, null,
            null, null, version, expectedSelectionHash, null, body, ifMatch);
    }
    public static ReleaseMutationCommand Retire(Guid projectId, Guid revisionId, long version, string ifMatch, string reasonCode)
    {
        ValidateEtag(revisionId, version, ifMatch);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, reasonCode });
        return new(projectId, "retire", RevisionRoute(projectId, revisionId, "retire"), null, revisionId, null,
            null, null, version, null, reasonCode, body, ifMatch);
    }
    private static byte[] DraftBody(string label, ReleaseSelection selection) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = 1,
        label,
        applicationId = selection.ApplicationId.ToString("D"),
        environmentId = selection.EnvironmentId.ToString("D"),
        targetId = selection.TargetId.ToString("D"),
        artifactId = selection.ArtifactId.ToString("D")
    });
    private static void ValidateDraft(string label, ReleaseSelection selection)
    { ArgumentNullException.ThrowIfNull(selection); ReleaseDraft.ValidateLabel(label); }
    private static string Prefix(Guid projectId) { RequireId(projectId); return $"/api/v1/projects/{projectId:D}"; }
    private static string RevisionRoute(Guid projectId, Guid revisionId, string operation)
    { RequireId(revisionId); return Prefix(projectId) + $"/release-revisions/{revisionId:D}/{operation}"; }
    private static void RequireId(Guid value) { if (value == Guid.Empty) throw new ArgumentException("Invalid command identity."); }
    private static void ValidateEtag(Guid revisionId, long version, string ifMatch)
    {
        RequireId(revisionId);
        if (version <= 0 || ifMatch != $"\"rel-v1:{revisionId:D}:{version}\"")
            throw new ArgumentException("Version precondition does not match the revision.");
    }
}
