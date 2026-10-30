using Kordra.Modules.ReleaseManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ReleaseManagement;

public sealed record ReleaseLineageSummary(Guid ReleaseId, Guid ProjectId, DateTimeOffset CreatedAtUtc);
public sealed record ReleaseRevisionSummary(Guid ReleaseId, Guid RevisionId, long RevisionNumber, long Version,
    string State, string Label, Guid ApplicationId, Guid EnvironmentId, Guid TargetId, Guid ArtifactId,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? FrozenAtUtc, string? SpecHash);
public sealed record ReleasePage<T>(IReadOnlyList<T> Items, Guid? NextAfterId);

/// <summary>Owner snapshot only; contains no transferable authority or HTTP response.</summary>
public sealed class ReleaseRevisionSnapshot
{
    private readonly byte[]? _subject;
    internal ReleaseRevisionSnapshot(ReleaseRevisionRecord row, byte[]? subject, string? hash)
    {
        ReleaseId = row.ReleaseId; RevisionId = row.Id; RevisionNumber = row.RevisionNumber; Version = row.Version;
        State = row.State; CreatorPrincipalId = row.CreatorPrincipalId; Label = row.Label;
        ApplicationId = row.ApplicationId; EnvironmentId = row.EnvironmentId; TargetId = row.TargetId; ArtifactId = row.ArtifactId;
        CreatedAtUtc = row.CreatedAtUtc; UpdatedAtUtc = row.UpdatedAtUtc; FrozenAtUtc = row.FrozenAtUtc;
        RetiredAtUtc = row.RetiredAtUtc; RetiredReasonCode = row.RetiredReasonCode; SpecHash = hash;
        _subject = subject is null ? null : (byte[])subject.Clone();
    }
    public Guid ReleaseId { get; }
    public Guid RevisionId { get; }
    public long RevisionNumber { get; }
    public long Version { get; }
    public string State { get; }
    public Guid CreatorPrincipalId { get; }
    public string Label { get; }
    public Guid ApplicationId { get; }
    public Guid EnvironmentId { get; }
    public Guid TargetId { get; }
    public Guid ArtifactId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; }
    public DateTimeOffset? FrozenAtUtc { get; }
    public DateTimeOffset? RetiredAtUtc { get; }
    public string? RetiredReasonCode { get; }
    public string? SpecHash { get; }
    public byte[]? GetCanonicalSubject() => _subject is null ? null : (byte[])_subject.Clone();
}

/// <summary>Bounded owner projections only. Host owns current permission, admission, audit and response bounds.</summary>
public sealed class ReleaseQueryStore(ReleaseDbContext db)
{
    public async Task<ReleaseRevisionSnapshot?> ReadRevisionUnderAdmissionAsync(Guid installationId, Guid tenantId,
        Guid projectId, Guid revisionId, CancellationToken ct = default)
    {
        if (!ValidScope(installationId, tenantId, projectId) || revisionId == Guid.Empty) return null;
        var row = await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
            x.TenantId == tenantId && x.ProjectId == projectId && x.Id == revisionId, ct);
        if (row is null) return null;
        var intent = row.State == "Draft" ? null : ReleaseIntegrity.ReadFrozenOrRetired(row);
        return new(row, intent?.GetCanonicalBytes(), intent?.SpecHash);
    }

    public async Task<ReleasePage<ReleaseLineageSummary>?> ReadLineagesUnderAdmissionAsync(Guid installationId, Guid tenantId,
        Guid projectId, int limit, Guid? afterId, CancellationToken ct = default)
    {
        if (!ValidScope(installationId, tenantId, projectId) || limit is < 1 or > 50 || afterId == Guid.Empty) return null;
        var query = db.Lineages.AsNoTracking().Where(x => x.InstallationId == installationId && x.TenantId == tenantId && x.ProjectId == projectId);
        if (afterId is { } id)
        {
            var cursor = await query.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (cursor is null) return null;
            query = query.Where(x => x.CreatedAtUtc > cursor.CreatedAtUtc || x.CreatedAtUtc == cursor.CreatedAtUtc && x.Id.CompareTo(id) > 0);
        }
        var rows = await query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(limit + 1).ToArrayAsync(ct);
        var page = rows.Take(limit).Select(x => new ReleaseLineageSummary(x.Id, x.ProjectId, x.CreatedAtUtc)).ToArray();
        return new(Array.AsReadOnly(page), rows.Length > limit ? page[^1].ReleaseId : null);
    }

    public async Task<ReleasePage<ReleaseRevisionSummary>?> ReadRevisionsUnderAdmissionAsync(Guid installationId, Guid tenantId,
        Guid projectId, Guid releaseId, int limit, Guid? afterId, CancellationToken ct = default)
    {
        if (!ValidScope(installationId, tenantId, projectId) || releaseId == Guid.Empty || limit is < 1 or > 50 || afterId == Guid.Empty) return null;
        if (!await db.Lineages.AsNoTracking().AnyAsync(x => x.InstallationId == installationId && x.TenantId == tenantId &&
            x.ProjectId == projectId && x.Id == releaseId, ct)) return null;
        var query = db.Revisions.AsNoTracking().Where(x => x.InstallationId == installationId && x.TenantId == tenantId &&
            x.ProjectId == projectId && x.ReleaseId == releaseId);
        if (afterId is { } id)
        {
            var cursor = await query.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (cursor is null) return null;
            query = query.Where(x => x.CreatedAtUtc > cursor.CreatedAtUtc || x.CreatedAtUtc == cursor.CreatedAtUtc && x.Id.CompareTo(id) > 0);
        }
        var rows = await query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(limit + 1).ToArrayAsync(ct);
        var page = rows.Take(limit).Select(row => new ReleaseRevisionSummary(row.ReleaseId, row.Id, row.RevisionNumber,
            row.Version, row.State, row.Label, row.ApplicationId, row.EnvironmentId, row.TargetId, row.ArtifactId,
            row.CreatedAtUtc, row.FrozenAtUtc, row.State == "Draft" ? null : ReleaseIntegrity.ReadFrozenOrRetired(row).SpecHash)).ToArray();
        return new(Array.AsReadOnly(page), rows.Length > limit ? page[^1].RevisionId : null);
    }
    private static bool ValidScope(Guid installation, Guid tenant, Guid project) => installation != Guid.Empty && tenant != Guid.Empty && project != Guid.Empty;
}
