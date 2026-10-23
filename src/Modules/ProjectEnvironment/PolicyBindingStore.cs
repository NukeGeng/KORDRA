using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ProjectEnvironment;

/// <summary>PRJ facts and staged binding writes only; caller supplies trusted validated IAM references.</summary>
public sealed class PolicyBindingStore(ProjectDbContext db) : IPolicyBindingReader
{
    public async Task<Guid?> ReadProjectTenantSelectionAsync(Guid projectId, CancellationToken ct = default)
    {
        // Identity selection only: preserve suspended identities for subject-bound command receipt recovery.
        var tenants = await db.Projects.AsNoTracking().Where(x => x.Id == projectId).Select(x => x.TenantId).Take(2).ToArrayAsync(ct);
        return tenants.Length == 1 ? tenants[0] : null;
    }
    public async Task<ProjectOwnership?> ReadProjectSelectionUnderAdmissionAsync(Guid projectId, CancellationToken ct = default)
    {
        var tenants = await db.Projects.AsNoTracking().Where(x => x.Id == projectId && x.Status == "active").Select(x => x.TenantId).Take(2).ToArrayAsync(ct);
        return tenants.Length == 1 ? await ReadOwnershipUnderAdmissionAsync(tenants[0], projectId, null, ct) : null;
    }
    public async Task<bool> ScopeBelongsToTenantUnderAdmissionAsync(Guid tenant, string kind, Guid scopeId, CancellationToken ct = default)
    {
        if (kind == "project") return await ReadOwnershipUnderAdmissionAsync(tenant, scopeId, null, ct) is not null;
        if (kind != "environment") return false;
        var project = await db.Environments.AsNoTracking().Where(x => x.TenantId == tenant && x.Id == scopeId)
            .Select(x => (Guid?)x.ProjectId).SingleOrDefaultAsync(ct);
        return project is not null && await ReadOwnershipUnderAdmissionAsync(tenant, project.Value, scopeId, ct) is not null;
    }
    public async Task<ProjectOwnership?> ReadOwnershipUnderAdmissionAsync(Guid tenant, Guid project, Guid? environment, CancellationToken ct = default)
    {
        var p = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.Id == project && x.Status == "active", ct);
        if (p is null) return null;
        if (environment is null) return new(tenant, project, p.Revision, EnvironmentSelection.ResolvedAbsent, null, null, null, null);
        var e = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.ProjectId == project && x.Id == environment && x.Status == "active", ct);
        return e is null ? null : new(tenant, project, p.Revision, EnvironmentSelection.ResolvedPresent, e.Id, e.Revision, e.Classification, null);
    }
    public async Task<PolicyBinding?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid? environmentId, CancellationToken ct = default)
    {
        if (await ReadOwnershipUnderAdmissionAsync(tenantId, projectId, environmentId, ct) is null) return null;
        var scope = environmentId is null ? "project_without_environment" : "environment";
        var rows = await db.Set<PolicyBindingRecord>().AsNoTracking().Where(x => x.TenantId == tenantId && x.ProjectId == projectId &&
            x.EnvironmentId == environmentId && x.ScopeKind == scope && x.Action == "target.read").Take(2).ToArrayAsync(ct);
        return rows.Length == 1 ? Fact(rows[0]) : null;
    }
    public async Task<PolicyBinding?> StageAsync(Guid tenant, Guid project, PolicyBindingChange change, ResourcePolicyReference policy,
        Guid manifest, bool allowReplacement, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Binding writes require an enlisted owner transaction.");
        if (change is null || policy is null || policy.PolicyId == Guid.Empty || policy.Revision <= 0 || manifest == Guid.Empty ||
            change.ExpectedBindingRevision < 0 || change.ExpectedBindingRevision == long.MaxValue ||
            (change.EnvironmentId is null ? change.ScopeKind != "project_without_environment" : change.EnvironmentId == Guid.Empty || change.ScopeKind != "environment") ||
            await ReadOwnershipUnderAdmissionAsync(tenant, project, change.EnvironmentId, ct) is null) return null;
        var current = await db.Set<PolicyBindingRecord>().SingleOrDefaultAsync(x => x.TenantId == tenant && x.ProjectId == project && x.EnvironmentId == change.EnvironmentId && x.Action == "target.read", ct);
        if (current is null ? change.BindingId is not null || change.ExpectedBindingRevision != 0 :
            !allowReplacement || current.BindingId != change.BindingId || current.BindingRevision != change.ExpectedBindingRevision || current.ScopeKind != change.ScopeKind) return null;
        if (current is null)
        {
            current = new() { BindingId = Guid.NewGuid(), TenantId = tenant, ProjectId = project, EnvironmentId = change.EnvironmentId, ScopeKind = change.ScopeKind, ScopeId = change.EnvironmentId ?? project };
            db.Add(current);
        }
        current.PolicyId = policy.PolicyId; current.PolicyRevision = policy.Revision; current.BindingRevision = checked(change.ExpectedBindingRevision + 1);
        var fact = Fact(current);
        db.Add(new PolicyBindingHistoryRecord { BindingId = fact.BindingId, BindingRevision = fact.BindingRevision, TenantId = tenant, ProjectId = project,
            EnvironmentId = fact.EnvironmentId, ScopeId = fact.EnvironmentId ?? project, ScopeKind = fact.ScopeKind, Action = fact.Action, PolicyId = fact.PolicyId, PolicyRevision = fact.PolicyRevision,
            ManifestId = manifest, ContentHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fact)) });
        return fact;
    }
    private static PolicyBinding Fact(PolicyBindingRecord b) => new(b.BindingId, b.TenantId, b.ProjectId, b.EnvironmentId,
        b.ScopeKind, b.Action, b.PolicyId, b.PolicyRevision, b.BindingRevision);

    public async Task<(int Count, IReadOnlyList<Guid> BindingIds)> ReadPolicyImpactUnderAdmissionAsync(Guid tenant, Guid project, Guid policy, CancellationToken ct = default)
    {
        var query = db.Set<PolicyBindingRecord>().AsNoTracking().Where(x => x.TenantId == tenant && x.ProjectId == project && x.PolicyId == policy);
        var count = await query.CountAsync(ct);
        var ids = await query.OrderBy(x => x.BindingId).Select(x => x.BindingId).Take(50).ToArrayAsync(ct);
        return (count, Array.AsReadOnly(ids));
    }

    public Task PersistAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
