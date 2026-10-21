using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.AuditCompliance.Infrastructure;

/// <summary>AUD-owned persistence context. No other module's tables are mapped here.</summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string SchemaName = "app";
    public const string MigrationHistoryTableName = "aud_ef_migrations_history";
    public const string EntriesTableName = "aud_intents";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureModel(modelBuilder);
    }

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        var entries = modelBuilder.Entity<AuditEntry>();
        entries.ToTable(EntriesTableName, table =>
        {
            table.HasCheckConstraint("ck_aud_intents_schema_version", "schema_version = 1");
            table.HasCheckConstraint("ck_aud_intents_stage", "stage IN ('U', 'D', 'mutation', 'outcome')");
            table.HasCheckConstraint("ck_aud_intents_hash_length", "octet_length(canonical_intent_hash) = 32");
            table.HasCheckConstraint("ck_aud_intents_canonical_length", "octet_length(canonical_intent) <= 8192");
            table.HasCheckConstraint("ck_aud_intents_revisions", "(policy_revision IS NULL OR policy_revision > 0) AND (authority_revision IS NULL OR authority_revision > 0)");
            table.HasCheckConstraint(
                "ck_aud_intents_result_digest_size",
                "(stage = 'D' AND result_digest IS NOT NULL AND length(result_digest) = 64 AND result_size_bytes IS NOT NULL AND result_size_bytes >= 0) OR (stage <> 'D' AND result_digest IS NULL AND result_size_bytes IS NULL)");
        });
        entries.HasKey(entry => entry.RecordId).HasName("pk_aud_intents");
        entries.Property(entry => entry.RecordId).HasColumnName("record_id").ValueGeneratedNever();
        entries.Property(entry => entry.SchemaVersion).HasColumnName("schema_version").IsRequired();
        entries.Property(entry => entry.IntentId).HasColumnName("intent_id").IsRequired();
        entries.Property(entry => entry.InvocationId).HasColumnName("invocation_id").IsRequired();
        entries.Property(entry => entry.Stage).HasColumnName("stage").HasMaxLength(16).IsRequired();
        entries.Property(entry => entry.SubjectRef).HasColumnName("subject_ref");
        entries.Property(entry => entry.TenantRef).HasColumnName("tenant_ref");
        entries.Property(entry => entry.ResourceRef).HasColumnName("resource_ref");
        entries.Property(entry => entry.Permission).HasColumnName("permission").HasMaxLength(128).IsRequired();
        entries.Property(entry => entry.ScopeKind).HasColumnName("scope_kind").HasMaxLength(32).IsRequired();
        entries.Property(entry => entry.ScopeId).HasColumnName("scope_id");
        entries.Property(entry => entry.PolicyRevision).HasColumnName("policy_revision");
        entries.Property(entry => entry.AuthorityRevision).HasColumnName("authority_revision");
        entries.Property(entry => entry.SafeReason).HasColumnName("safe_reason").HasMaxLength(128).IsRequired();
        entries.Property(entry => entry.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        entries.Property(entry => entry.CorrelationId).HasColumnName("correlation_id").IsRequired();
        entries.Property(entry => entry.ResultDigest).HasColumnName("result_digest").HasMaxLength(64);
        entries.Property(entry => entry.ResultSizeBytes).HasColumnName("result_size_bytes");
        entries.Property(entry => entry.CanonicalIntent).HasColumnName("canonical_intent").HasMaxLength(8192).IsRequired();
        entries.Property(entry => entry.CanonicalIntentHash).HasColumnName("canonical_intent_hash").HasColumnType("bytea").HasMaxLength(32).IsRequired();
        entries.Property(entry => entry.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired();
        entries.HasIndex(entry => entry.IntentId).IsUnique().HasDatabaseName("uq_aud_intents_intent_id");
    }
}
