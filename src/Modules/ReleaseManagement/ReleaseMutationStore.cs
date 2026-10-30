using System.Text.Json;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.ReleaseManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ReleaseManagement;

/// <summary>Known stale or mismatched owner state; this is not a successful mutation.</summary>
public sealed class ReleaseMutationConflictException(string message) : InvalidOperationException(message);

/// <summary>
/// Stages REL owner changes only. The host must hold write admission, resolve current owner facts,
/// enforce permissions, stage AUD and floor, and commit the shared transaction. These methods do not
/// save, authorize, commit, disclose or perform external effects.
/// </summary>
public sealed class ReleaseMutationStore(ReleaseDbContext db)
{
    public void StageFirstDraft(Guid installationId, ReleaseDraft draft, DateTimeOffset now)
    {
        RequireTransaction(); ValidateTime(now);
        if (installationId == Guid.Empty || draft.RevisionNumber != 1 || draft.Version != 1)
            throw new ArgumentException("Invalid initial release revision.");
        db.Lineages.Add(new()
        {
            Id = draft.ReleaseId,
            InstallationId = installationId,
            TenantId = draft.TenantId,
            ProjectId = draft.ProjectId,
            CreatedAtUtc = now
        });
        db.Revisions.Add(NewRevision(installationId, draft, now));
    }

    public async Task StageNextDraftAsync(Guid installationId, Guid tenantId, Guid projectId, Guid baseRevisionId,
        ReleaseDraft draft, DateTimeOffset now, CancellationToken ct = default)
    {
        RequireTransaction(); ValidateTime(now);
        if (installationId == Guid.Empty || tenantId == Guid.Empty || projectId == Guid.Empty || baseRevisionId == Guid.Empty ||
            draft.TenantId != tenantId || draft.ProjectId != projectId || draft.Version != 1)
            throw new ArgumentException("Invalid next revision identity.");
        var baseline = await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
            x.TenantId == tenantId && x.ProjectId == projectId && x.Id == baseRevisionId && x.ReleaseId == draft.ReleaseId, ct);
        if (baseline is null || baseline.State is not ("Frozen" or "Retired"))
            throw new ReleaseMutationConflictException("Base revision is not frozen or retired in this scope.");
        var latest = await db.Revisions.Where(x => x.InstallationId == installationId && x.TenantId == tenantId &&
            x.ProjectId == projectId && x.ReleaseId == draft.ReleaseId).MaxAsync(x => x.RevisionNumber, ct);
        if (latest == long.MaxValue || draft.RevisionNumber != latest + 1)
            throw new ReleaseMutationConflictException("Revision number is stale.");
        db.Revisions.Add(NewRevision(installationId, draft, now));
    }

    public async Task StageReplaceDraftAsync(Guid installationId, Guid tenantId, Guid projectId, Guid revisionId,
        Guid actorId, long expectedVersion, string label, ReleaseSelection selection, DateTimeOffset now,
        CancellationToken ct = default)
    {
        RequireTransaction(); ValidateTime(now);
        var row = await FindDraft(installationId, tenantId, projectId, revisionId, ct);
        if (actorId != row.CreatorPrincipalId || expectedVersion != row.Version || row.Version == long.MaxValue)
            throw new ReleaseMutationConflictException("Draft creator or version does not match.");
        var draft = Restore(row).Replace(actorId, expectedVersion, label, selection);
        if (now < row.UpdatedAtUtc) throw new InvalidOperationException("Clock precedes last update.");
        row.Version = draft.Version; row.Label = draft.Label; row.ApplicationId = selection.ApplicationId;
        row.EnvironmentId = selection.EnvironmentId; row.TargetId = selection.TargetId; row.ArtifactId = selection.ArtifactId;
        row.UpdatedAtUtc = now;
    }

    public async Task StageFreezeAsync(Guid installationId, Guid tenantId, Guid projectId, Guid revisionId,
        Guid actorId, long expectedVersion, string expectedSelectionHash, ReleaseIntent currentOwnerIntent,
        DateTimeOffset now, CancellationToken ct = default)
    {
        RequireTransaction(); ValidateTime(now);
        ArgumentNullException.ThrowIfNull(currentOwnerIntent);
        var row = await FindDraft(installationId, tenantId, projectId, revisionId, ct);
        if (actorId != row.CreatorPrincipalId || row.Version != expectedVersion || row.Version == long.MaxValue || now < row.UpdatedAtUtc)
            throw new ReleaseMutationConflictException("Draft creator, version or clock does not match.");
        if (!ReleasePreviewHash.Matches(row.Id, row.Version, currentOwnerIntent.SpecHash, expectedSelectionHash))
            throw new ReleaseMutationConflictException("Reviewed selection changed.");
        using var document = JsonDocument.Parse(currentOwnerIntent.GetCanonicalBytes());
        var root = document.RootElement;
        if (root.GetProperty("tenantId").GetGuid() != row.TenantId || root.GetProperty("project").GetProperty("id").GetGuid() != row.ProjectId ||
            root.GetProperty("application").GetProperty("id").GetGuid() != row.ApplicationId ||
            root.GetProperty("environment").GetProperty("id").GetGuid() != row.EnvironmentId ||
            root.GetProperty("target").GetProperty("id").GetGuid() != row.TargetId ||
            root.GetProperty("artifact").GetProperty("artifactId").GetGuid() != row.ArtifactId)
            throw new ReleaseMutationConflictException("Current owner selection does not match the draft.");
        row.State = "Frozen"; row.Version++; row.SpecBytes = currentOwnerIntent.GetCanonicalBytes();
        row.SpecHash = Convert.FromHexString(currentOwnerIntent.SpecHash); row.UpdatedAtUtc = now; row.FrozenAtUtc = now;
    }

    public async Task StageRetireAsync(Guid installationId, Guid tenantId, Guid projectId, Guid revisionId,
        long expectedVersion, string reasonCode, DateTimeOffset now, CancellationToken ct = default)
    {
        RequireTransaction(); ValidateScope(installationId, tenantId, projectId, revisionId); ValidateTime(now);
        if (reasonCode is not ("obsolete" or "operator_cancelled")) throw new ArgumentException("Unknown retirement reason.");
        var row = await db.Revisions.SingleOrDefaultAsync(x => x.InstallationId == installationId && x.TenantId == tenantId &&
            x.ProjectId == projectId && x.Id == revisionId, ct);
        if (row is null || row.State != "Frozen" || row.Version != expectedVersion || row.Version == long.MaxValue || now < row.UpdatedAtUtc)
            throw new ReleaseMutationConflictException("Frozen revision, version or clock does not match.");
        row.State = "Retired"; row.Version++; row.UpdatedAtUtc = now; row.RetiredAtUtc = now; row.RetiredReasonCode = reasonCode;
    }

    private async Task<ReleaseRevisionRecord> FindDraft(Guid installationId, Guid tenantId, Guid projectId, Guid revisionId, CancellationToken ct)
    {
        ValidateScope(installationId, tenantId, projectId, revisionId);
        var row = await db.Revisions.SingleOrDefaultAsync(x => x.InstallationId == installationId && x.TenantId == tenantId &&
            x.ProjectId == projectId && x.Id == revisionId, ct);
        return row is { State: "Draft" } ? row : throw new ReleaseMutationConflictException("Draft revision not found in this scope.");
    }
    private static ReleaseDraft Restore(ReleaseRevisionRecord row) => ReleaseDraft.Restore(row.TenantId, row.ProjectId,
        row.ReleaseId, row.Id, row.RevisionNumber, row.CreatorPrincipalId, row.Version, row.Label,
        ReleaseSelection.Create(row.ApplicationId, row.EnvironmentId, row.TargetId, row.ArtifactId));
    private static ReleaseRevisionRecord NewRevision(Guid installationId, ReleaseDraft draft, DateTimeOffset now) => new()
    {
        Id = draft.RevisionId,
        InstallationId = installationId,
        TenantId = draft.TenantId,
        ProjectId = draft.ProjectId,
        ReleaseId = draft.ReleaseId,
        RevisionNumber = draft.RevisionNumber,
        Version = draft.Version,
        CreatorPrincipalId = draft.CreatorPrincipalId,
        Label = draft.Label,
        ApplicationId = draft.Selection.ApplicationId,
        EnvironmentId = draft.Selection.EnvironmentId,
        TargetId = draft.Selection.TargetId,
        ArtifactId = draft.Selection.ArtifactId,
        State = "Draft",
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };
    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("REL mutation requires the caller's shared transaction.");
    }
    private static void ValidateScope(Guid installationId, Guid tenantId, Guid projectId, Guid revisionId)
    {
        if (installationId == Guid.Empty || tenantId == Guid.Empty || projectId == Guid.Empty || revisionId == Guid.Empty)
            throw new ArgumentException("Invalid release scope.");
    }
    private static void ValidateTime(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value == default) throw new ArgumentException("A UTC observation time is required.");
    }
}
