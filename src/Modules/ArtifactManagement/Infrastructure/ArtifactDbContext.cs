using Kordra.Modules.ArtifactManagement.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ArtifactManagement.Infrastructure;

public sealed class ArtifactRecord
{
    public Guid Id { get; internal set; }
    public Guid TenantId { get; internal set; }
    public Guid ProjectId { get; internal set; }
    public Guid RegistryId { get; internal set; }
    public string Repository { get; internal set; } = "";
    public string RootDigest { get; internal set; } = "";
    public string PlatformJson { get; internal set; } = "";
    public string RootMediaType { get; internal set; } = "";
    public long RootSizeBytes { get; internal set; }
    public string SelectedManifestDigest { get; internal set; } = "";
    public long SelectedManifestSizeBytes { get; internal set; }
    public string SelectedManifestMediaType { get; internal set; } = "";
    public string ConfigDigest { get; internal set; } = "";
    public long ConfigSizeBytes { get; internal set; }
    public string ObservedPlatformJson { get; internal set; } = "";
    public string VariantEvidence { get; internal set; } = "";
    public string VerifierProfile { get; internal set; } = "artifact-registry-v1";
    public DateTimeOffset VerifiedAtUtc { get; internal set; }
    public DateTimeOffset CreatedAtUtc { get; internal set; }

    internal ArtifactV1 ToProjection() => new(Id, ProjectId, RegistryId, Repository, RootDigest,
        ArtifactStorageJson.Platform(PlatformJson), RootMediaType, RootSizeBytes, SelectedManifestDigest,
        SelectedManifestSizeBytes, SelectedManifestMediaType, ConfigDigest, ConfigSizeBytes,
        ArtifactStorageJson.Platform(ObservedPlatformJson), VariantEvidence, VerifierProfile, VerifiedAtUtc,
        CreatedAtUtc, "manifest_config_hash_verified", "not_checked", "not_checked");
}

public sealed class ArtifactRegistrationRecord
{
    public Guid Id { get; internal set; }
    public Guid InstallationId { get; internal set; }
    public Guid TenantId { get; internal set; }
    public Guid ProjectId { get; internal set; }
    public Guid PrincipalId { get; internal set; }
    public Guid BootId { get; internal set; }
    public Guid InvocationId { get; internal set; }
    public string KeyHash { get; internal set; } = "";
    public string BusinessHash { get; internal set; } = "";
    public string IdentityHash { get; internal set; } = "";
    public string RequestJson { get; internal set; } = "";
    public string State { get; internal set; } = "Pending";
    public DateTimeOffset CreatedAtUtc { get; internal set; }
    public Guid? ArtifactId { get; internal set; }
    public DateTimeOffset? CompletedAtUtc { get; internal set; }
    public DateTimeOffset? AttemptVerifiedAtUtc { get; internal set; }
    public string? SafeReason { get; internal set; }
}

public sealed class ArtifactProvenanceClaim
{
    public Guid Id { get; internal set; }
    public Guid TenantId { get; internal set; }
    public Guid ProjectId { get; internal set; }
    public Guid ArtifactId { get; internal set; }
    public Guid RegistrationId { get; internal set; }
    public string DeclarationJson { get; internal set; } = "";
    public DateTimeOffset CreatedAtUtc { get; internal set; }
}

/// <summary>ART-owned data only. Host supplies the shared transaction; never migrate during startup.</summary>
public sealed class ArtifactDbContext(DbContextOptions<ArtifactDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "art_ef_migrations_history";
    public DbSet<ArtifactRecord> Artifacts => Set<ArtifactRecord>();
    public DbSet<ArtifactRegistrationRecord> Registrations => Set<ArtifactRegistrationRecord>();
    public DbSet<ArtifactProvenanceClaim> ProvenanceClaims => Set<ArtifactProvenanceClaim>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("app");
        var artifacts = model.Entity<ArtifactRecord>();
        artifacts.ToTable("art_artifacts", table =>
        {
            table.HasCheckConstraint("ck_art_digest", DigestCheck("root_digest") + " AND " + DigestCheck("selected_manifest_digest") + " AND " + DigestCheck("config_digest"));
            table.HasCheckConstraint("ck_art_sizes", "root_size_bytes BETWEEN 1 AND 1048576 AND selected_manifest_size_bytes BETWEEN 1 AND 1048576 AND config_size_bytes BETWEEN 1 AND 1048576");
            table.HasCheckConstraint("ck_art_profile", "verifier_profile = 'artifact-registry-v1' AND variant_evidence IN ('none','config','descriptor')");
            table.HasCheckConstraint("ck_art_times", "isfinite(verified_at_utc) AND isfinite(created_at_utc) AND verified_at_utc <= created_at_utc");
            table.HasCheckConstraint("ck_art_ids", NonemptyIds("id", "tenant_id", "project_id", "registry_id"));
            table.HasCheckConstraint("ck_art_repository", "repository COLLATE \"C\" ~ '^[a-z0-9]+((\\.|_|__|-+)[a-z0-9]+)*(/[a-z0-9]+((\\.|_|__|-+)[a-z0-9]+)*)*$' AND repository COLLATE \"C\" !~ '[^a-z0-9._/-]'");
            table.HasCheckConstraint("ck_art_platform", PlatformCheck());
            table.HasCheckConstraint("ck_art_media_type", MediaTypeCheck());
        });
        artifacts.HasKey(x => x.Id);
        artifacts.HasAlternateKey(x => new { x.Id, x.TenantId, x.ProjectId });
        artifacts.HasIndex(x => new { x.TenantId, x.ProjectId, x.RegistryId, x.Repository, x.RootDigest, x.PlatformJson })
            .IsUnique().HasDatabaseName("uq_art_content_identity");
        artifacts.HasIndex(x => new { x.TenantId, x.ProjectId, x.CreatedAtUtc, x.Id });
        artifacts.Property(x => x.Repository).HasMaxLength(255);
        artifacts.Property(x => x.RootDigest).HasMaxLength(135);
        artifacts.Property(x => x.SelectedManifestDigest).HasMaxLength(135);
        artifacts.Property(x => x.ConfigDigest).HasMaxLength(135);
        artifacts.Property(x => x.PlatformJson).HasMaxLength(96);
        artifacts.Property(x => x.ObservedPlatformJson).HasMaxLength(96);
        artifacts.Property(x => x.RootMediaType).HasMaxLength(128);
        artifacts.Property(x => x.SelectedManifestMediaType).HasMaxLength(128);
        artifacts.Property(x => x.VariantEvidence).HasMaxLength(16);
        artifacts.Property(x => x.VerifierProfile).HasMaxLength(32);

        var registrations = model.Entity<ArtifactRegistrationRecord>();
        registrations.ToTable("art_registration_receipts", table =>
        {
            table.HasCheckConstraint("ck_art_registration_hashes", "key_hash COLLATE \"C\" ~ '^[0-9a-f]{64}$' AND business_hash COLLATE \"C\" ~ '^[0-9a-f]{64}$' AND identity_hash COLLATE \"C\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_art_registration_ids", NonemptyIds("id", "installation_id", "tenant_id", "project_id", "principal_id", "boot_id", "invocation_id"));
            table.HasCheckConstraint("ck_art_registration_state", "(state = 'Pending' AND artifact_id IS NULL AND completed_at_utc IS NULL AND attempt_verified_at_utc IS NULL AND safe_reason IS NULL) OR (state = 'Registered' AND artifact_id IS NOT NULL AND completed_at_utc IS NOT NULL AND attempt_verified_at_utc IS NOT NULL AND safe_reason IS NULL) OR (state IN ('Failed','Abandoned') AND artifact_id IS NULL AND completed_at_utc IS NOT NULL AND attempt_verified_at_utc IS NULL AND safe_reason IS NOT NULL)");
            table.HasCheckConstraint("ck_art_registration_times", "isfinite(created_at_utc) AND (completed_at_utc IS NULL OR (isfinite(completed_at_utc) AND completed_at_utc >= created_at_utc)) AND (attempt_verified_at_utc IS NULL OR (isfinite(attempt_verified_at_utc) AND attempt_verified_at_utc >= created_at_utc AND attempt_verified_at_utc <= completed_at_utc))");
            table.HasCheckConstraint("ck_art_request_json", "jsonb_typeof(request_json) = 'object' AND octet_length(request_json::text) <= 4096");
            table.HasCheckConstraint("ck_art_safe_reason", "safe_reason IS NULL OR safe_reason ~ '^[a-z_]{1,64}$'");
        });
        registrations.HasKey(x => x.Id);
        registrations.HasAlternateKey(x => new { x.Id, x.TenantId, x.ProjectId });
        registrations.HasIndex(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.PrincipalId, x.KeyHash })
            .IsUnique().HasDatabaseName("uq_art_registration_key");
        registrations.HasIndex(x => new { x.Id, x.TenantId, x.ProjectId, x.ArtifactId }).IsUnique()
            .HasDatabaseName("uq_art_registration_artifact_scope");
        registrations.HasOne<ArtifactRecord>().WithMany().HasForeignKey(x => new { x.ArtifactId, x.TenantId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        registrations.Property(x => x.KeyHash).HasMaxLength(64);
        registrations.Property(x => x.BusinessHash).HasMaxLength(64);
        registrations.Property(x => x.IdentityHash).HasMaxLength(64);
        registrations.Property(x => x.RequestJson).HasColumnType("jsonb");
        registrations.Property(x => x.State).HasMaxLength(16);
        registrations.Property(x => x.SafeReason).HasMaxLength(64);

        var claims = model.Entity<ArtifactProvenanceClaim>();
        claims.ToTable("art_provenance_claims", table =>
        {
            table.HasCheckConstraint("ck_art_claim_ids", NonemptyIds("id", "tenant_id", "project_id", "artifact_id", "registration_id"));
            table.HasCheckConstraint("ck_art_claim_json", "jsonb_typeof(declaration_json) = 'object' AND octet_length(declaration_json::text) <= 1024");
            table.HasCheckConstraint("ck_art_claim_time", "isfinite(created_at_utc)");
        });
        claims.HasKey(x => x.Id);
        claims.HasIndex(x => x.RegistrationId).IsUnique();
        claims.Property(x => x.DeclarationJson).HasColumnType("jsonb");
        claims.HasOne<ArtifactRecord>().WithMany().HasForeignKey(x => new { x.ArtifactId, x.TenantId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        claims.HasOne<ArtifactRegistrationRecord>().WithMany().HasForeignKey(x => new { x.RegistrationId, x.TenantId, x.ProjectId })
            .HasPrincipalKey(x => new { x.Id, x.TenantId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c)
                    ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString())));
            }
        }
    }

    private static string DigestCheck(string column) => $"({column} COLLATE \"C\" ~ '^sha256:[0-9a-f]{{64}}$' OR {column} COLLATE \"C\" ~ '^sha512:[0-9a-f]{{128}}$')";
    private static string NonemptyIds(params string[] columns) => string.Join(" AND ", columns.Select(c => $"{c} <> '00000000-0000-0000-0000-000000000000'::uuid"));

    private static string PlatformCheck()
    {
        const string amd = "{\"os\":\"linux\",\"architecture\":\"amd64\",\"variant\":null}";
        const string arm = "{\"os\":\"linux\",\"architecture\":\"arm64\",\"variant\":null}";
        const string v8 = "{\"os\":\"linux\",\"architecture\":\"arm64\",\"variant\":\"v8\"}";
        return $"(variant_evidence = 'none' AND ((platform_json = '{amd}' AND observed_platform_json = '{amd}') OR (platform_json = '{arm}' AND observed_platform_json = '{arm}'))) OR " +
            $"(variant_evidence = 'config' AND platform_json = '{v8}' AND observed_platform_json = '{v8}') OR " +
            $"(variant_evidence = 'descriptor' AND platform_json = '{v8}' AND observed_platform_json = '{arm}' AND root_media_type IN ('{ArtifactVerificationFacts.OciIndex}','{ArtifactVerificationFacts.DockerIndex}'))";
    }

    private static string MediaTypeCheck() =>
        $"selected_manifest_media_type IN ('{ArtifactVerificationFacts.OciManifest}','{ArtifactVerificationFacts.DockerManifest}') AND " +
        $"(root_media_type IN ('{ArtifactVerificationFacts.OciIndex}','{ArtifactVerificationFacts.DockerIndex}') OR " +
        "(root_media_type = selected_manifest_media_type AND root_digest = selected_manifest_digest AND root_size_bytes = selected_manifest_size_bytes))";
}
