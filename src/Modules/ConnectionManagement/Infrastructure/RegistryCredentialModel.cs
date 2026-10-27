using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ConnectionManagement.Infrastructure;

public sealed class NativeReferenceAllocationRecord
{
    public Guid InstallationId { get; set; }
    public Guid ReferenceId { get; set; }
    public Guid TenantId { get; set; }
    public string OwnerKind { get; set; } = "";
    public Guid? InventoryProfileId { get; set; }
    public Guid? RegistryProfileId { get; set; }
}
public sealed class RegistryConnectionProfileRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RegistryId { get; set; }
    public string Repository { get; set; } = "";
    public Guid ProfileId { get; set; }
    public string ConsumerId { get; set; } = "artifact.registry.read.v1";
    public string Operation { get; set; } = "artifact.register";
    public string ProviderKind { get; set; } = "macos-keychain-file-v1";
    public string CredentialFormat { get; set; } = "opaque-bytes-v1";
}
public sealed class RegistrySecretReferenceRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid ReferenceId { get; set; }
    public Guid LocatorId { get; set; }
}
public sealed class RegistryCredentialBindingRevisionRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RegistryId { get; set; }
    public string Repository { get; set; } = "";
    public string ConsumerId { get; set; } = "artifact.registry.read.v1";
    public string Operation { get; set; } = "artifact.register";
    public Guid BindingId { get; set; }
    public long Revision { get; set; }
    public Guid ProfileId { get; set; }
    public Guid ReferenceId { get; set; }
    public string State { get; set; } = "active";
    public DateTimeOffset NotBeforeUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public Guid ManifestId { get; set; }
}
public sealed class RegistryCredentialBindingHeadRecord
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RegistryId { get; set; }
    public string Repository { get; set; } = "";
    public string ConsumerId { get; set; } = "artifact.registry.read.v1";
    public string Operation { get; set; } = "artifact.register";
    public Guid BindingId { get; set; }
    public long Revision { get; set; }
}
public sealed class RegistryCredentialCommandReceiptRecord
{
    public Guid InstallationId { get; set; }
    public Guid ManifestId { get; set; }
    public string ContentHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTimeOffset AuditOccurredAtUtc { get; set; }
    public Guid AuditIntentId { get; set; }
    public string AuditHash { get; set; } = "";
}

public sealed partial class CredentialDbContext
{
    public DbSet<NativeReferenceAllocationRecord> ReferenceAllocations => Set<NativeReferenceAllocationRecord>();
    public DbSet<RegistryConnectionProfileRecord> RegistryProfiles => Set<RegistryConnectionProfileRecord>();
    public DbSet<RegistrySecretReferenceRecord> RegistryReferences => Set<RegistrySecretReferenceRecord>();
    public DbSet<RegistryCredentialBindingRevisionRecord> RegistryRevisions => Set<RegistryCredentialBindingRevisionRecord>();
    public DbSet<RegistryCredentialBindingHeadRecord> RegistryHeads => Set<RegistryCredentialBindingHeadRecord>();
    public DbSet<RegistryCredentialCommandReceiptRecord> RegistryCommands => Set<RegistryCredentialCommandReceiptRecord>();

    private static void ConfigureRegistryModel(ModelBuilder model)
    {
        const string plan = "consumer_id = 'artifact.registry.read.v1' AND operation = 'artifact.register'";
        var profiles = model.Entity<RegistryConnectionProfileRecord>();
        profiles.ToTable("sec_registry_profiles", t =>
        {
            t.HasCheckConstraint("ck_sec_registry_profile_plan", plan + " AND provider_kind = 'macos-keychain-file-v1' AND credential_format = 'opaque-bytes-v1'");
            t.HasCheckConstraint("ck_sec_registry_repository", "repository COLLATE \"C\" ~ '^[a-z0-9]+((\\.|_|__|-+)[a-z0-9]+)*(/[a-z0-9]+((\\.|_|__|-+)[a-z0-9]+)*)*$' AND repository COLLATE \"C\" !~ '[^a-z0-9._/-]'");
        });
        profiles.HasKey(x => new { x.InstallationId, x.TenantId, x.ProfileId });
        profiles.HasIndex(x => new { x.InstallationId, x.ProfileId }).IsUnique();
        profiles.HasAlternateKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.ProjectId, x.RegistryId, x.Repository, x.ConsumerId, x.Operation });

        var allocations = model.Entity<NativeReferenceAllocationRecord>();
        allocations.ToTable("sec_native_reference_allocations", t => t.HasCheckConstraint("ck_sec_reference_owner",
            "(owner_kind = 'inventory' AND inventory_profile_id IS NOT NULL AND registry_profile_id IS NULL) OR (owner_kind = 'registry' AND inventory_profile_id IS NULL AND registry_profile_id IS NOT NULL)"));
        allocations.HasKey(x => new { x.InstallationId, x.ReferenceId });
        allocations.HasIndex(x => new { x.InstallationId, x.ReferenceId, x.TenantId, x.InventoryProfileId }).IsUnique().HasDatabaseName("uq_sec_inventory_reference_owner");
        allocations.HasIndex(x => new { x.InstallationId, x.ReferenceId, x.TenantId, x.RegistryProfileId }).IsUnique().HasDatabaseName("uq_sec_registry_reference_owner");
        allocations.HasOne<ConnectionProfileRecord>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.InventoryProfileId }).OnDelete(DeleteBehavior.Restrict);
        allocations.HasOne<RegistryConnectionProfileRecord>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.RegistryProfileId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SecretReferenceRecord>().HasOne<NativeReferenceAllocationRecord>().WithMany()
            .HasForeignKey(x => new { x.InstallationId, x.ReferenceId }).OnDelete(DeleteBehavior.Restrict);

        var references = model.Entity<RegistrySecretReferenceRecord>();
        references.ToTable("sec_registry_references");
        references.HasKey(x => new { x.InstallationId, x.TenantId, x.ReferenceId });
        references.HasIndex(x => new { x.InstallationId, x.ReferenceId }).IsUnique();
        references.HasAlternateKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.ReferenceId });
        references.HasOne<RegistryConnectionProfileRecord>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProfileId }).OnDelete(DeleteBehavior.Restrict);
        references.HasOne<NativeReferenceAllocationRecord>().WithMany().HasForeignKey(x => new { x.InstallationId, x.ReferenceId }).OnDelete(DeleteBehavior.Restrict);

        var revisions = model.Entity<RegistryCredentialBindingRevisionRecord>();
        revisions.ToTable("sec_registry_binding_revisions", t => t.HasCheckConstraint("ck_sec_registry_binding_state",
            plan + " AND revision > 0 AND state IN ('active','revoked') AND isfinite(not_before_utc) AND isfinite(expires_at_utc) AND not_before_utc < expires_at_utc"));
        revisions.HasKey(x => new { x.InstallationId, x.TenantId, x.BindingId, x.Revision });
        revisions.HasIndex(x => new { x.InstallationId, x.ManifestId }).IsUnique();
        revisions.HasAlternateKey(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.RegistryId, x.Repository, x.ConsumerId, x.Operation, x.BindingId, x.Revision });
        revisions.HasOne<RegistryConnectionProfileRecord>().WithMany()
            .HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.ProjectId, x.RegistryId, x.Repository, x.ConsumerId, x.Operation })
            .HasPrincipalKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.ProjectId, x.RegistryId, x.Repository, x.ConsumerId, x.Operation }).OnDelete(DeleteBehavior.Restrict);
        revisions.HasOne<RegistrySecretReferenceRecord>().WithMany()
            .HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.ReferenceId })
            .HasPrincipalKey(x => new { x.InstallationId, x.TenantId, x.ProfileId, x.ReferenceId }).OnDelete(DeleteBehavior.Restrict);
        var heads = model.Entity<RegistryCredentialBindingHeadRecord>();
        heads.ToTable("sec_registry_binding_heads", t => t.HasCheckConstraint("ck_sec_registry_head", plan + " AND revision > 0"));
        heads.HasKey(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.RegistryId, x.Repository, x.ConsumerId, x.Operation });
        heads.HasIndex(x => new { x.InstallationId, x.BindingId }).IsUnique();
        heads.Property(x => x.Revision).IsConcurrencyToken();
        heads.HasOne<RegistryCredentialBindingRevisionRecord>().WithMany()
            .HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.RegistryId, x.Repository, x.ConsumerId, x.Operation, x.BindingId, x.Revision })
            .HasPrincipalKey(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.RegistryId, x.Repository, x.ConsumerId, x.Operation, x.BindingId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
        var commands = model.Entity<RegistryCredentialCommandReceiptRecord>();
        commands.ToTable("sec_registry_command_receipts", t => t.HasCheckConstraint("ck_sec_registry_command_hashes",
            "content_hash COLLATE \"C\" ~ '^[0-9a-f]{64}$' AND audit_hash COLLATE \"C\" ~ '^[0-9a-f]{64}$' AND isfinite(audit_occurred_at_utc)"));
        commands.HasKey(x => new { x.InstallationId, x.ManifestId });
        commands.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
    }
}
