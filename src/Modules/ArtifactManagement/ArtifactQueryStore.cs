using Kordra.ArtifactManagement.Domain;
using Kordra.Modules.ArtifactManagement.Contracts;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ArtifactManagement;

public sealed record ArtifactReplay(string Status, Guid RegistrationId);
public sealed record ArtifactPage(IReadOnlyList<ArtifactSummaryV1> Items, Guid? NextAfterArtifactId);

/// <summary>Owner-scoped bounded projections. Caller must enforce current project U/D; no provider/SEC dependency.</summary>
public sealed class ArtifactQueryStore(ArtifactDbContext db)
{
    /// <summary>Minimum v1 write capability evidence from owner schema metadata, without history-table grants or DDL.</summary>
    public Task<bool> HasWriteSchemaV1Async(CancellationToken ct = default) => db.Database.SqlQueryRaw<bool>("""
        SELECT to_regclass('app.art_artifacts') IS NOT NULL
          AND to_regclass('app.art_registration_receipts') IS NOT NULL
          AND to_regclass('app.art_provenance_claims') IS NOT NULL
          AND (SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
               JOIN pg_namespace n ON n.oid=c.relnamespace
               WHERE n.nspname='app' AND t.tgenabled IN ('O','A') AND NOT t.tgisinternal
                 AND (c.relname,t.tgname) IN (('art_artifacts','art_artifacts_immutable'),
                   ('art_provenance_claims','art_claims_immutable'), ('art_artifacts','art_artifacts_no_truncate'),
                   ('art_provenance_claims','art_claims_no_truncate'), ('art_registration_receipts','art_receipts_no_truncate'),
                   ('art_registration_receipts','art_receipt_transition'))) = 6
          AND EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace
               WHERE n.nspname='app' AND c.conname='fk_art_claim_registration_artifact' AND c.condeferrable AND c.convalidated)
          AND EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
               JOIN pg_namespace n ON n.oid=c.relnamespace
               WHERE n.nspname='app' AND c.relname='uq_art_content_identity' AND i.indisunique AND i.indisvalid) AS "Value"
        """).SingleAsync(ct);

    public async Task<ArtifactReplay?> FindOwnKeyAsync(ArtifactRegistrationReceipt request, CancellationToken ct = default)
    {
        var identity = request.Request.Identity;
        var found = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(r => r.InstallationId == request.InstallationId &&
            r.TenantId == identity.TenantId && r.ProjectId == identity.ProjectId && r.PrincipalId == request.PrincipalId && r.KeyHash == request.KeyHash, ct);
        return found is null ? null : new(found.BusinessHash != request.BusinessHash ? "key_conflict" :
            found.State == "Pending" ? "in_progress" : "terminal_replay", found.Id);
    }

    public async Task<ArtifactV1?> ReadDetailAsync(Guid tenant, Guid project, Guid artifact, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty || project == Guid.Empty || artifact == Guid.Empty) return null;
        var row = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(a => a.TenantId == tenant && a.ProjectId == project && a.Id == artifact, ct);
        return row?.ToProjection();
    }

    /// <summary>A foreign/nonexistent cursor is absent, never a scope token. At most 51 rows are fetched.</summary>
    public async Task<ArtifactPage?> ReadPageAsync(Guid tenant, Guid project, int limit, Guid? after, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty || project == Guid.Empty || limit is < 1 or > 50 || after == Guid.Empty) return null;
        var query = db.Artifacts.AsNoTracking().Where(a => a.TenantId == tenant && a.ProjectId == project);
        if (after is { } cursorId)
        {
            var cursor = await query.SingleOrDefaultAsync(a => a.Id == cursorId, ct);
            if (cursor is null) return null;
            query = query.Where(a => a.CreatedAtUtc > cursor.CreatedAtUtc || a.CreatedAtUtc == cursor.CreatedAtUtc && a.Id.CompareTo(cursorId) > 0);
        }
        var rows = await query.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id).Take(limit + 1).ToArrayAsync(ct);
        var page = rows.Take(limit).Select(a => new ArtifactSummaryV1(a.Id, a.RegistryId, a.Repository, a.RootDigest,
            ArtifactStorageJson.Platform(a.PlatformJson), a.CreatedAtUtc, "manifest_config_hash_verified")).ToArray();
        return new(Array.AsReadOnly(page), rows.Length > limit ? page[^1].ArtifactId : null);
    }
}
