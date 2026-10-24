using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ConnectionManagement.Infrastructure;

public sealed class ConnectionProfileRecord
{
    public Guid InstallationId { get; set; } public Guid TenantId { get; set; } public Guid ProfileId { get; set; }
    public Guid TargetId { get; set; } public string ConsumerId { get; set; } = "inventory.readonly.v1";
    public string Operation { get; set; } = "target.read"; public string ProviderKind { get; set; } = "macos-keychain-file-v1";
    public string CredentialFormat { get; set; } = "opaque-bytes-v1";
}
public sealed class SecretReferenceRecord
{
    public Guid InstallationId { get; set; } public Guid TenantId { get; set; } public Guid ReferenceId { get; set; }
    public Guid ProfileId { get; set; } public Guid LocatorId { get; set; }
}
public sealed class CredentialBindingRevisionRecord
{
    public Guid InstallationId { get; set; } public Guid TenantId { get; set; } public Guid BindingId { get; set; }
    public long Revision { get; set; } public Guid TargetId { get; set; } public string ConsumerId { get; set; } = "inventory.readonly.v1";
    public string Operation { get; set; } = "target.read"; public Guid ProfileId { get; set; } public Guid ReferenceId { get; set; }
    public string State { get; set; } = "active"; public DateTimeOffset NotBeforeUtc { get; set; } public DateTimeOffset ExpiresAtUtc { get; set; }
    public Guid ManifestId { get; set; }
}
public sealed class CredentialBindingHeadRecord
{
    public Guid InstallationId { get; set; } public Guid TenantId { get; set; } public Guid TargetId { get; set; }
    public string ConsumerId { get; set; } = "inventory.readonly.v1"; public string Operation { get; set; } = "target.read";
    public Guid BindingId { get; set; } public long Revision { get; set; }
}
public sealed class CredentialCommandReceiptRecord
{
    public Guid InstallationId { get; set; } public Guid ManifestId { get; set; } public string ContentHash { get; set; } = "";
    public string ResultJson { get; set; } = ""; public DateTimeOffset? AuditOccurredAtUtc { get; set; } public Guid AuditIntentId { get; set; } public string AuditHash { get; set; } = "";
}
public sealed class CredentialDbContext(DbContextOptions<CredentialDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "sec_ef_migrations_history";
    public DbSet<ConnectionProfileRecord> Profiles => Set<ConnectionProfileRecord>();
    public DbSet<SecretReferenceRecord> References => Set<SecretReferenceRecord>();
    public DbSet<CredentialBindingHeadRecord> Heads => Set<CredentialBindingHeadRecord>();
    public DbSet<CredentialBindingRevisionRecord> Revisions => Set<CredentialBindingRevisionRecord>();
    public DbSet<CredentialCommandReceiptRecord> Commands => Set<CredentialCommandReceiptRecord>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("app");
        var p = model.Entity<ConnectionProfileRecord>(); p.ToTable("sec_connection_profiles", t => t.HasCheckConstraint("ck_sec_profile_plan",
            "consumer_id = 'inventory.readonly.v1' AND operation = 'target.read' AND provider_kind = 'macos-keychain-file-v1' AND credential_format = 'opaque-bytes-v1'"));
        p.HasKey(x => new { x.InstallationId, x.TenantId, x.ProfileId }); p.HasIndex(x => new { x.InstallationId, x.ProfileId }).IsUnique();
        p.HasAlternateKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.TargetId, x.ConsumerId, x.Operation });
        var r = model.Entity<SecretReferenceRecord>(); r.ToTable("sec_secret_references"); r.HasKey(x => new { x.InstallationId, x.TenantId, x.ReferenceId });
        r.HasIndex(x => new { x.InstallationId, x.ReferenceId }).IsUnique(); r.HasAlternateKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.ReferenceId });
        r.HasOne<ConnectionProfileRecord>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProfileId }).OnDelete(DeleteBehavior.Restrict);
        var v = model.Entity<CredentialBindingRevisionRecord>(); v.ToTable("sec_binding_revisions", t => t.HasCheckConstraint("ck_sec_binding_state",
            "revision > 0 AND state IN ('active','revoked') AND consumer_id = 'inventory.readonly.v1' AND operation = 'target.read' AND isfinite(not_before_utc) AND isfinite(expires_at_utc) AND not_before_utc < expires_at_utc"));
        v.HasKey(x => new { x.InstallationId, x.TenantId, x.BindingId, x.Revision }); v.HasIndex(x => new { x.InstallationId, x.ManifestId }).IsUnique();
        v.HasAlternateKey(x => new { x.InstallationId, x.TenantId, x.TargetId, x.ConsumerId, x.Operation, x.BindingId, x.Revision });
        v.HasOne<ConnectionProfileRecord>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.TargetId, x.ConsumerId, x.Operation })
            .HasPrincipalKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.TargetId, x.ConsumerId, x.Operation }).OnDelete(DeleteBehavior.Restrict);
        v.HasOne<SecretReferenceRecord>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.ReferenceId })
            .HasPrincipalKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.ReferenceId }).OnDelete(DeleteBehavior.Restrict);
        var h = model.Entity<CredentialBindingHeadRecord>(); h.ToTable("sec_binding_heads", t => t.HasCheckConstraint("ck_sec_head_revision", "revision > 0"));
        h.HasKey(x => new { x.InstallationId, x.TenantId, x.TargetId, x.ConsumerId, x.Operation }); h.HasIndex(x => new { x.InstallationId, x.BindingId }).IsUnique();
        h.Property(x => x.Revision).IsConcurrencyToken();
        h.HasOne<CredentialBindingRevisionRecord>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.TargetId, x.ConsumerId, x.Operation, x.BindingId, x.Revision })
            .HasPrincipalKey(x => new { x.InstallationId, x.TenantId, x.TargetId, x.ConsumerId, x.Operation, x.BindingId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
        var c = model.Entity<CredentialCommandReceiptRecord>(); c.ToTable("sec_command_receipts"); c.HasKey(x => new { x.InstallationId, x.ManifestId });
        c.Property(x => x.ContentHash).HasMaxLength(64).IsRequired(); c.Property(x => x.AuditHash).HasMaxLength(64).IsRequired(); c.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
        foreach (var type in model.Model.GetEntityTypes())
        {
            var builder = model.Entity(type.ClrType);
            foreach (var prop in type.GetProperties())
            {
                prop.SetColumnName(string.Concat(prop.Name.Select((ch, i) => char.IsUpper(ch) && i > 0 ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString())));
                if (prop.ClrType == typeof(string) && prop.Name != "ResultJson") builder.Property(prop.Name).HasMaxLength(128).IsRequired();
                if (prop.ClrType == typeof(Guid)) builder.ToTable(t => t.HasCheckConstraint("ck_" + type.GetTableName() + "_" + prop.GetColumnName(), prop.GetColumnName() + " <> '00000000-0000-0000-0000-000000000000'::uuid"));
            }
        }
    }
}
