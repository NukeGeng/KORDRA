using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ApprovalGovernance;

/// <summary>Detached owner facts for host coordination, never a wire response or authority to disclose.</summary>
public sealed record ApprovalRequestOwnerSnapshot(ApprovalRequestRecord Request, IReadOnlyList<ApprovalDecisionRecord> Decisions);
public sealed record ApprovalRequestOwnerPage(IReadOnlyList<ApprovalRequestRecord> Requests, Guid? NextAfterId);

/// <summary>Caller holds admission, authorizes the environment, computes current eligibility and audits disclosure.</summary>
public sealed class ApprovalRequestQueryStore(ApprovalDbContext db)
{
    public async Task<ApprovalRequestOwnerSnapshot?> ReadUnderAdmissionAsync(Guid installation, Guid tenant,
        Guid project, Guid environment, Guid requestId, CancellationToken ct = default)
    {
        if (!Scope(installation, tenant, project, environment) || requestId == Guid.Empty) return null;
        var row = await Query(installation, tenant, project, environment).SingleOrDefaultAsync(x => x.Id == requestId, ct);
        if (row is null) return null;
        var decisions = await db.Decisions.AsNoTracking().Where(x => x.InstallationId == installation &&
            x.TenantId == tenant && x.RequestId == row.Id).OrderBy(x => x.RequestVersion).Take(3).ToArrayAsync(ct);
        if (decisions.Length > 2) throw new InvalidDataException("Approval decision history exceeds its bound.");
        return new(row, Array.AsReadOnly(decisions));
    }

    public async Task<ApprovalRequestOwnerPage?> ReadPageUnderAdmissionAsync(Guid installation, Guid tenant,
        Guid project, Guid environment, int limit, Guid? afterId, CancellationToken ct = default)
    {
        if (!Scope(installation, tenant, project, environment) || limit is < 1 or > 50 || afterId == Guid.Empty) return null;
        var query = Query(installation, tenant, project, environment);
        if (afterId is { } id)
        {
            var cursor = await query.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (cursor is null) return null;
            query = query.Where(x => x.CreatedAtUtc > cursor.CreatedAtUtc ||
                x.CreatedAtUtc == cursor.CreatedAtUtc && x.Id.CompareTo(id) > 0);
        }
        var rows = await query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(limit + 1).ToArrayAsync(ct);
        var items = rows.Take(limit).ToArray();
        return new(Array.AsReadOnly(items), rows.Length > limit ? items[^1].Id : null);
    }

    private IQueryable<ApprovalRequestRecord> Query(Guid installation, Guid tenant, Guid project, Guid environment) =>
        db.Requests.AsNoTracking().Where(x => x.InstallationId == installation && x.TenantId == tenant &&
            x.ProjectId == project && x.EnvironmentId == environment);
    private static bool Scope(Guid installation, Guid tenant, Guid project, Guid environment) =>
        installation != Guid.Empty && tenant != Guid.Empty && project != Guid.Empty && environment != Guid.Empty;
}
