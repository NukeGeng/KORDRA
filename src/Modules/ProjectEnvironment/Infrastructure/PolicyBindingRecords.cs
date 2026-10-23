using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ProjectEnvironment.Infrastructure;

public sealed class PolicyBindingRecord
{
    public Guid BindingId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public Guid ScopeId { get; set; }
    public string ScopeKind { get; set; } = "";
    public string Action { get; set; } = "target.read";
    public Guid PolicyId { get; set; }
    public long PolicyRevision { get; set; }
    public long BindingRevision { get; set; }
}
public sealed class PolicyBindingHistoryRecord
{
    public Guid BindingId { get; set; }
    public long BindingRevision { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public Guid ScopeId { get; set; }
    public string ScopeKind { get; set; } = "";
    public string Action { get; set; } = "target.read";
    public Guid PolicyId { get; set; }
    public long PolicyRevision { get; set; }
    public Guid ManifestId { get; set; }
    public byte[] ContentHash { get; set; } = [];
}
public static class PolicyBindingModel
{
    public static void Configure(ModelBuilder model)
    {
        var b = model.Entity<PolicyBindingRecord>();
        b.ToTable("prj_policy_bindings", t => t.HasCheckConstraint("ck_prj_policy_binding", "binding_revision > 0 AND policy_revision > 0 AND action = 'target.read' AND policy_id <> '00000000-0000-0000-0000-000000000000' AND ((scope_kind = 'environment' AND environment_id IS NOT NULL) OR (scope_kind = 'project_without_environment' AND environment_id IS NULL))"));
        b.HasKey(x => x.BindingId); b.Property(x => x.BindingId).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.BindingId, x.TenantId, x.ProjectId, x.ScopeKind, x.ScopeId, x.Action });
        b.ToTable(t => t.HasCheckConstraint("ck_prj_policy_binding_scope_identity", "scope_id = COALESCE(environment_id, project_id)"));
        b.Property(x => x.BindingRevision).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.EnvironmentId, x.Action }).IsUnique().HasFilter("scope_kind = 'environment'");
        b.HasIndex(x => new { x.TenantId, x.ProjectId, x.Action }).IsUnique().HasFilter("scope_kind = 'project_without_environment'");
        b.HasOne<ProjectRecord>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EnvironmentRecord>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.EnvironmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var h = model.Entity<PolicyBindingHistoryRecord>();
        h.ToTable("prj_policy_binding_history", t => t.HasCheckConstraint("ck_prj_policy_binding_history", "binding_revision > 0 AND policy_revision > 0 AND octet_length(content_hash) = 32 AND action = 'target.read' AND ((scope_kind = 'environment' AND environment_id IS NOT NULL) OR (scope_kind = 'project_without_environment' AND environment_id IS NULL))"));
        h.HasKey(x => new { x.BindingId, x.BindingRevision });
        h.ToTable(t => t.HasCheckConstraint("ck_prj_policy_binding_history_scope_identity", "scope_id = COALESCE(environment_id, project_id)"));
        h.HasOne<PolicyBindingRecord>().WithMany().HasForeignKey(x => new { x.BindingId, x.TenantId, x.ProjectId, x.ScopeKind, x.ScopeId, x.Action })
            .HasPrincipalKey(x => new { x.BindingId, x.TenantId, x.ProjectId, x.ScopeKind, x.ScopeId, x.Action }).OnDelete(DeleteBehavior.Restrict);
        foreach (var type in new[] { typeof(PolicyBindingRecord), typeof(PolicyBindingHistoryRecord) })
        {
            var entity = model.Entity(type); entity.Property("ScopeKind").HasMaxLength(32); entity.Property("Action").HasMaxLength(32);
        }
    }
}
