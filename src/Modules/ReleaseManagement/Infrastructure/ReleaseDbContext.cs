using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ReleaseManagement.Infrastructure;

public sealed class ReleaseLineageRecord
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
public sealed class ReleaseRevisionRecord
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ReleaseId { get; set; }
    public long RevisionNumber { get; set; }
    public long Version { get; set; }
    public Guid CreatorPrincipalId { get; set; }
    public string Label { get; set; } = "";
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public Guid TargetId { get; set; }
    public Guid ArtifactId { get; set; }
    public string State { get; set; } = "Draft";
    public byte[]? SpecBytes { get; set; }
    public byte[]? SpecHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? FrozenAtUtc { get; set; }
    public DateTimeOffset? RetiredAtUtc { get; set; }
    public string? RetiredReasonCode { get; set; }
}
public sealed class ReleaseCommandReceipt
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PrincipalId { get; set; }
    public Guid? BootId { get; set; }
    public Guid? InvocationId { get; set; }
    public Guid ReleaseRevisionId { get; set; }
    public string Action { get; set; } = "";
    public byte[] ScopedKeyHash { get; set; } = [];
    public byte[] BusinessHash { get; set; } = [];
    public Guid AuditIntentId { get; set; }
    public byte[] AuditIntentHash { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ReleaseDbContext(DbContextOptions<ReleaseDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "rel_ef_migrations_history";
    public DbSet<ReleaseLineageRecord> Lineages => Set<ReleaseLineageRecord>();
    public DbSet<ReleaseRevisionRecord> Revisions => Set<ReleaseRevisionRecord>();
    public DbSet<ReleaseCommandReceipt> Commands => Set<ReleaseCommandReceipt>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("app");
        var lineage = model.Entity<ReleaseLineageRecord>();
        lineage.ToTable("rel_lineages", t => t.HasCheckConstraint("ck_rel_lineage", Ids("id", "installation_id", "tenant_id", "project_id") + " AND isfinite(created_at_utc)"));
        lineage.HasKey(x => x.Id); lineage.Property(x => x.Id).ValueGeneratedNever();
        lineage.HasAlternateKey(x => new { x.Id, x.InstallationId, x.TenantId, x.ProjectId });
        var revision = model.Entity<ReleaseRevisionRecord>();
        revision.ToTable("rel_revisions", t =>
        {
            t.HasCheckConstraint("ck_rel_revision_ids", Ids("id", "installation_id", "tenant_id", "project_id", "release_id", "creator_principal_id", "application_id", "environment_id", "target_id", "artifact_id") + " AND revision_number > 0 AND version > 0");
            t.HasCheckConstraint("ck_rel_revision_state", "(state = 'Draft' AND spec_bytes IS NULL AND spec_hash IS NULL AND frozen_at_utc IS NULL AND retired_at_utc IS NULL AND retired_reason_code IS NULL) OR (state = 'Frozen' AND spec_bytes IS NOT NULL AND spec_hash IS NOT NULL AND octet_length(spec_hash) = 32 AND octet_length(spec_bytes) BETWEEN 1 AND 65536 AND frozen_at_utc IS NOT NULL AND retired_at_utc IS NULL AND retired_reason_code IS NULL) OR (state = 'Retired' AND spec_bytes IS NOT NULL AND spec_hash IS NOT NULL AND octet_length(spec_hash) = 32 AND octet_length(spec_bytes) BETWEEN 1 AND 65536 AND frozen_at_utc IS NOT NULL AND retired_at_utc IS NOT NULL AND retired_reason_code IS NOT NULL AND retired_reason_code IN ('obsolete','operator_cancelled'))");
            t.HasCheckConstraint("ck_rel_revision_times", "isfinite(created_at_utc) AND isfinite(updated_at_utc) AND (frozen_at_utc IS NULL OR isfinite(frozen_at_utc)) AND (retired_at_utc IS NULL OR isfinite(retired_at_utc))");
        });
        revision.HasKey(x => x.Id); revision.Property(x => x.Id).ValueGeneratedNever();
        revision.HasAlternateKey(x => new { x.Id, x.InstallationId, x.TenantId, x.ProjectId });
        revision.HasIndex(x => new { x.ReleaseId, x.RevisionNumber }).IsUnique();
        revision.HasIndex(x => x.ReleaseId).IsUnique().HasFilter("state = 'Draft'");
        revision.HasOne<ReleaseLineageRecord>().WithMany().HasForeignKey(x => new { x.ReleaseId, x.InstallationId, x.TenantId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.InstallationId, x.TenantId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        revision.Property(x => x.Version).IsConcurrencyToken(); revision.Property(x => x.Label).HasMaxLength(512).IsRequired();
        revision.Property(x => x.State).HasMaxLength(16); revision.Property(x => x.RetiredReasonCode).HasMaxLength(32);
        var receipt = model.Entity<ReleaseCommandReceipt>();
        receipt.ToTable("rel_commands", t => t.HasCheckConstraint("ck_rel_command", Ids("id", "installation_id", "tenant_id", "project_id", "principal_id", "release_revision_id", "audit_intent_id") + " AND octet_length(scoped_key_hash) = 32 AND octet_length(business_hash) = 32 AND octet_length(audit_intent_hash) = 32 AND isfinite(created_at_utc)"));
        receipt.HasKey(x => x.Id); receipt.Property(x => x.Id).ValueGeneratedNever();
        receipt.HasIndex(x => new { x.InstallationId, x.TenantId, x.PrincipalId, x.Action, x.ScopedKeyHash }).IsUnique();
        receipt.HasOne<ReleaseRevisionRecord>().WithMany().HasForeignKey(x => new { x.ReleaseRevisionId, x.InstallationId, x.TenantId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.InstallationId, x.TenantId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        receipt.Property(x => x.Action).HasMaxLength(32);
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c)
                    ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString())));
    }
    private static string Ids(params string[] names) => string.Join(" AND ", names.Select(name => $"{name} <> '00000000-0000-0000-0000-000000000000'::uuid"));
}
