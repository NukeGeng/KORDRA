using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.IdentityAccess.Infrastructure;

public sealed class ResourcePolicyIdentity
{
    public Guid PolicyId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
}
public sealed class ResourcePolicyRevision
{
    public Guid PolicyId { get; set; }
    public long Revision { get; set; }
    public int SchemaVersion { get; set; }
    public string Action { get; set; } = "";
    public string ScopeKind { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset EffectiveFromUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public byte[] ContentHash { get; set; } = [];
    public DateTimeOffset PublishedAtUtc { get; set; }
}
public sealed class ResourcePolicyRule
{
    public Guid PolicyId { get; set; }
    public long Revision { get; set; }
    public string Case { get; set; } = "";
    public string Effect { get; set; } = "";
}
public sealed class ResourcePolicyHead
{
    public Guid PolicyId { get; set; }
    public long CurrentRevision { get; set; }
}
public sealed class ResourcePolicyCommandReceipt
{
    public Guid InstallationId { get; set; }
    public Guid ManifestId { get; set; }
    public byte[] ContentHash { get; set; } = [];
    public string ResultJson { get; set; } = "";
    public Guid AuditIntentId { get; set; }
    public string AuditIntentHash { get; set; } = "";
}
public static class ResourcePolicyModel
{
    public static void Configure(ModelBuilder model)
    {
        var identity = model.Entity<ResourcePolicyIdentity>();
        identity.ToTable("iam_resource_policies", t => t.HasCheckConstraint("ck_iam_resource_policy_identity", "policy_id <> '00000000-0000-0000-0000-000000000000' AND tenant_id <> '00000000-0000-0000-0000-000000000000' AND project_id <> '00000000-0000-0000-0000-000000000000'"));
        identity.HasKey(x => x.PolicyId); identity.Property(x => x.PolicyId).ValueGeneratedNever();
        identity.HasAlternateKey(x => new { x.TenantId, x.ProjectId, x.PolicyId });
        var revision = model.Entity<ResourcePolicyRevision>();
        revision.ToTable("iam_resource_policy_revisions", t =>
        {
            t.HasCheckConstraint("ck_iam_resource_policy_payload", "revision > 0 AND schema_version = 1 AND action = 'target.read' AND scope_kind IN ('environment','project_without_environment') AND status IN ('active','inactive') AND octet_length(content_hash) = 32 AND isfinite(effective_from_utc) AND isfinite(published_at_utc) AND (expires_at_utc IS NULL OR (isfinite(expires_at_utc) AND expires_at_utc > effective_from_utc))");
        });
        revision.HasKey(x => new { x.PolicyId, x.Revision });
        revision.HasOne<ResourcePolicyIdentity>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        revision.Property(x => x.Action).HasMaxLength(32); revision.Property(x => x.ScopeKind).HasMaxLength(32); revision.Property(x => x.Status).HasMaxLength(16);
        var rule = model.Entity<ResourcePolicyRule>();
        rule.ToTable("iam_resource_policy_rules", t => t.HasCheckConstraint("ck_iam_resource_policy_rule", "revision > 0 AND rule_case IN ('Development','Test','Staging','Production','NoEnvironment') AND effect IN ('allow','deny')"));
        rule.HasKey(x => new { x.PolicyId, x.Revision, x.Case, x.Effect });
        rule.Property(x => x.Case).HasMaxLength(32); rule.Property(x => x.Effect).HasMaxLength(8);
        rule.HasOne<ResourcePolicyRevision>().WithMany().HasForeignKey(x => new { x.PolicyId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
        var head = model.Entity<ResourcePolicyHead>();
        head.ToTable("iam_resource_policy_heads", t => t.HasCheckConstraint("ck_iam_resource_policy_head", "current_revision > 0"));
        head.HasKey(x => x.PolicyId); head.Property(x => x.CurrentRevision).IsConcurrencyToken();
        head.HasOne<ResourcePolicyRevision>().WithMany().HasForeignKey(x => new { x.PolicyId, x.CurrentRevision }).OnDelete(DeleteBehavior.Restrict);
        var receipt = model.Entity<ResourcePolicyCommandReceipt>();
        receipt.ToTable("iam_resource_policy_commands", t => t.HasCheckConstraint("ck_iam_resource_policy_receipt", "octet_length(content_hash) = 32 AND length(audit_intent_hash) = 64"));
        receipt.HasKey(x => new { x.InstallationId, x.ManifestId }); receipt.Property(x => x.ResultJson).HasColumnType("jsonb");
        foreach (var type in new[] { typeof(ResourcePolicyIdentity), typeof(ResourcePolicyRevision), typeof(ResourcePolicyRule), typeof(ResourcePolicyHead), typeof(ResourcePolicyCommandReceipt) })
            foreach (var p in model.Entity(type).Metadata.GetProperties())
                p.SetColumnName(p.Name == "Case" ? "rule_case" : string.Concat(p.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
