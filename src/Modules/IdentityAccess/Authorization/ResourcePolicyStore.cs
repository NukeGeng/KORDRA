using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.IdentityAccess.Authorization;

/// <summary>IAM owner only. Caller holds admission; staged writes require an enlisted transaction and offline lease.</summary>
public sealed class ResourcePolicyStore(KordraIdentityDbContext db) : ICurrentResourcePolicyReader
{
    public async Task<ResourcePolicySnapshot?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId,
        Guid policyId, long exactRevision, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || policyId == Guid.Empty || exactRevision <= 0) return null;
        var found = await (from identity in db.Set<ResourcePolicyIdentity>().AsNoTracking()
                           join head in db.Set<ResourcePolicyHead>().AsNoTracking() on identity.PolicyId equals head.PolicyId
                           join revision in db.Set<ResourcePolicyRevision>().AsNoTracking() on new { head.PolicyId, Revision = head.CurrentRevision } equals new { revision.PolicyId, revision.Revision }
                           where identity.PolicyId == policyId && identity.TenantId == tenantId && identity.ProjectId == projectId && head.CurrentRevision == exactRevision
                           select revision).SingleOrDefaultAsync(ct);
        if (found is null) return null;
        var rules = await db.Set<ResourcePolicyRule>().AsNoTracking().Where(x => x.PolicyId == policyId && x.Revision == exactRevision)
            .OrderBy(x => x.Case).ThenBy(x => x.Effect).Take(9).Select(x => new ResourceRule(x.Case, x.Effect)).ToArrayAsync(ct);
        var payload = new ResourcePolicyPayload(found.SchemaVersion, found.Action, found.ScopeKind, found.Status,
            found.EffectiveFromUtc, found.ExpiresAtUtc, Array.AsReadOnly(rules));
        if (!ResourcePolicies.IsValid(payload) || found.ContentHash.Length != 32 || ResourcePolicies.Hash(payload) != Convert.ToHexStringLower(found.ContentHash)) return null;
        return new(policyId, tenantId, projectId, exactRevision, payload, Convert.ToHexStringLower(found.ContentHash));
    }

    public async Task<ResourcePolicySnapshot?> StagePublicationAsync(IOfflineProvisioningLease lease, Guid tenantId, Guid projectId,
        Guid? policyId, long expected, ResourcePolicyPayload payload, DateTimeOffset now, CancellationToken ct = default)
    {
        lease.RequireHeld();
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Policy publication requires an enlisted owner transaction.");
        if (tenantId == Guid.Empty || projectId == Guid.Empty || !ResourcePolicies.IsValid(payload) || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MinValue || now == DateTimeOffset.MaxValue ||
            payload.EffectiveFromUtc > now || payload.Status == "active" && payload.ExpiresAtUtc <= now ||
            expected < 0 || expected == long.MaxValue || (policyId is null ? expected != 0 : policyId == Guid.Empty || expected == 0)) return null;
        ResourcePolicyHead? head = null;
        payload = ResourcePolicies.Parse(System.Text.Encoding.UTF8.GetString(ResourcePolicies.CanonicalBytes(payload)))!;
        if (policyId is { } existing)
        {
            if (await ReadCurrentUnderAdmissionAsync(tenantId, projectId, existing, expected, ct) is null) return null;
            head = await db.Set<ResourcePolicyHead>().SingleAsync(x => x.PolicyId == existing, ct);
        }
        var id = policyId ?? Guid.NewGuid(); var revision = checked(expected + 1);
        if (policyId is null) db.Add(new ResourcePolicyIdentity { PolicyId = id, TenantId = tenantId, ProjectId = projectId });
        db.Add(new ResourcePolicyRevision { PolicyId = id, Revision = revision, SchemaVersion = payload.SchemaVersion,
            Action = payload.Action, ScopeKind = payload.ScopeKind, Status = payload.Status, EffectiveFromUtc = payload.EffectiveFromUtc,
            ExpiresAtUtc = payload.ExpiresAtUtc, ContentHash = Convert.FromHexString(ResourcePolicies.Hash(payload)), PublishedAtUtc = now.AddTicks(-(now.Ticks % 10)) });
        foreach (var r in payload.Rules) db.Add(new ResourcePolicyRule { PolicyId = id, Revision = revision, Case = r.Case, Effect = r.Effect });
        if (head is null) db.Add(new ResourcePolicyHead { PolicyId = id, CurrentRevision = revision }); else head.CurrentRevision = revision;
        // Copy through the canonical parser so caller-owned rule lists cannot mutate the staged result.
        var copied = ResourcePolicies.Parse(System.Text.Encoding.UTF8.GetString(ResourcePolicies.CanonicalBytes(payload)))!;
        return new(id, tenantId, projectId, revision, copied, ResourcePolicies.Hash(copied));
    }

    public async Task<ResourcePolicyCommandRecord?> ReadCommandUnderAdmissionAsync(Guid installation, Guid manifest, CancellationToken ct = default)
    {
        var row = await db.Set<ResourcePolicyCommandReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.InstallationId == installation && x.ManifestId == manifest, ct);
        return row is null ? null : new(Convert.ToHexStringLower(row.ContentHash), row.ResultJson, row.AuditIntentId, row.AuditIntentHash);
    }
    public void StageCommand(Guid installation, Guid manifest, ResourcePolicyCommandRecord result)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Receipt requires the enlisted owner transaction.");
        db.Add(new ResourcePolicyCommandReceipt { InstallationId = installation, ManifestId = manifest,
            ContentHash = Convert.FromHexString(result.ContentHash), ResultJson = result.ResultJson, AuditIntentId = result.AuditIntentId,
            AuditIntentHash = result.AuditIntentHash });
    }
    public Task PersistAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
    public Task StageClockFloorAsync(Guid installation, DateTimeOffset now, CancellationToken ct = default) => AuthorityClockFloorStore.StageAsync(db, installation, now, ct);
}
