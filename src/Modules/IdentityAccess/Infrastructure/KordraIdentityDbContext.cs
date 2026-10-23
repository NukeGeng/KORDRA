using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.IdentityAccess.Infrastructure;

/// <summary>IAM-owned identity persistence. It deliberately contains no tenant or audit state.</summary>
public sealed class KordraIdentityDbContext(DbContextOptions<KordraIdentityDbContext> options) : DbContext(options)
{
    public const string SchemaName = "app";
    public const string MigrationHistoryTableName = "iam_ef_migrations_history";

    public DbSet<KordraIdentityUser> Principals => Set<KordraIdentityUser>();
    public DbSet<KordraSession> Sessions => Set<KordraSession>();
    public DbSet<KordraAuthenticationCommand> AuthenticationCommands => Set<KordraAuthenticationCommand>();

    public DbSet<KordraAuthorityClockFloor> AuthorityClockFloors => Set<KordraAuthorityClockFloor>();

    public DbSet<KordraRoleDefinition> RoleDefinitions => Set<KordraRoleDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);
        ResourcePolicyModel.Configure(modelBuilder);

        var principals = modelBuilder.Entity<KordraIdentityUser>();
        principals.ToTable("iam_principals", table =>
        {
            table.HasCheckConstraint("ck_iam_principals_account_status", "account_status IN ('active', 'disabled')");
            table.HasCheckConstraint("ck_iam_principals_revision", "revision > 0");
        });
        principals.HasKey(user => user.Id).HasName("pk_iam_principals");
        principals.Property(user => user.Id).HasColumnName("principal_id").ValueGeneratedNever();
        principals.Property(user => user.UserName).HasColumnName("user_name").HasMaxLength(64).IsRequired();
        principals.Property(user => user.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(64).IsRequired();
        principals.Property(user => user.PasswordHash).HasColumnName("password_hash").IsRequired();
        principals.Property(user => user.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(128).IsRequired();
        principals.Property(user => user.ConcurrencyStamp).HasColumnName("concurrency_stamp").HasMaxLength(128).IsRequired().IsConcurrencyToken();
        principals.Property(user => user.AccountStatus).HasColumnName("account_status").HasMaxLength(16).IsRequired();
        principals.Property(user => user.Revision).HasColumnName("revision").HasDefaultValue(1L).IsRequired();
        principals.Property(user => user.LockoutEnd).HasColumnName("lockout_end_utc");
        principals.Property(user => user.LockoutEnabled).HasColumnName("lockout_enabled").IsRequired();
        principals.Property(user => user.AccessFailedCount).HasColumnName("access_failed_count").IsRequired();
        principals.Ignore(user => user.Email);
        principals.Ignore(user => user.NormalizedEmail);
        principals.Ignore(user => user.EmailConfirmed);
        principals.Ignore(user => user.PhoneNumber);
        principals.Ignore(user => user.PhoneNumberConfirmed);
        principals.Ignore(user => user.TwoFactorEnabled);
        principals.HasIndex(user => user.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("uq_iam_principals_normalized_user_name");

        var sessions = modelBuilder.Entity<KordraSession>();
        sessions.ToTable("iam_sessions", table =>
        {
            table.HasCheckConstraint("ck_iam_sessions_hash_length", "octet_length(session_reference_hash) = 32");
            table.HasCheckConstraint("ck_iam_sessions_principal_revision", "principal_revision > 0");
            table.HasCheckConstraint("ck_iam_sessions_expiry_after_creation", "absolute_expires_at_utc > created_at_utc");
            table.HasCheckConstraint("ck_iam_sessions_activity_after_creation", "last_activity_at_utc >= created_at_utc");
            table.HasCheckConstraint(
                "ck_iam_sessions_revocation_state",
                "(is_revoked AND revoked_at_utc IS NOT NULL) OR (NOT is_revoked AND revoked_at_utc IS NULL AND revocation_reason IS NULL)");
        });
        sessions.HasKey(session => session.SessionId).HasName("pk_iam_sessions");
        sessions.Property(session => session.SessionId).HasColumnName("session_id").ValueGeneratedNever();
        sessions.Property(session => session.SessionReferenceHash)
            .HasColumnName("session_reference_hash")
            .HasColumnType("bytea")
            .HasMaxLength(32)
            .IsRequired();
        sessions.Property(session => session.PrincipalId).HasColumnName("principal_id").IsRequired();
        sessions.Property(session => session.PrincipalRevision).HasColumnName("principal_revision").IsRequired();
        sessions.Property(session => session.BootGeneration).HasColumnName("boot_generation").IsRequired();
        sessions.Property(session => session.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        sessions.Property(session => session.LastActivityAtUtc).HasColumnName("last_activity_at_utc").IsRequired();
        sessions.Property(session => session.AbsoluteExpiresAtUtc).HasColumnName("absolute_expires_at_utc").IsRequired();
        sessions.Property(session => session.IsRevoked).HasColumnName("is_revoked").IsRequired();
        sessions.Property(session => session.RevokedAtUtc).HasColumnName("revoked_at_utc");
        sessions.Property(session => session.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(128);
        sessions.HasIndex(session => session.SessionReferenceHash)
            .IsUnique()
            .HasDatabaseName("uq_iam_sessions_session_reference_hash");
        sessions.HasIndex(session => new { session.PrincipalId, session.IsRevoked, session.AbsoluteExpiresAtUtc })
            .HasDatabaseName("ix_iam_sessions_principal_active_expiry");
        sessions.HasOne(session => session.Principal)
            .WithMany()
            .HasForeignKey(session => session.PrincipalId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_iam_sessions_iam_principals_principal_id");

        var commands = modelBuilder.Entity<KordraAuthenticationCommand>();
        commands.ToTable("iam_authentication_commands", table =>
        {
            table.HasCheckConstraint(
                "ck_iam_authentication_commands_hash_length",
                "octet_length(idempotency_key_hash) = 32");
            table.HasCheckConstraint(
                "ck_iam_authentication_commands_kind",
                "command_kind IN ('logout-all', 'password-change')");
        });
        commands.HasKey(command => command.CommandId).HasName("pk_iam_authentication_commands");
        commands.Property(command => command.CommandId).HasColumnName("command_id").ValueGeneratedNever();
        commands.Property(command => command.IdempotencyKeyHash)
            .HasColumnName("idempotency_key_hash")
            .HasColumnType("bytea")
            .HasMaxLength(32)
            .IsRequired();
        commands.Property(command => command.PrincipalId).HasColumnName("principal_id").IsRequired();
        commands.Property(command => command.CommandKind).HasColumnName("command_kind").HasMaxLength(32).IsRequired();
        commands.Property(command => command.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        commands.HasIndex(command => command.IdempotencyKeyHash)
            .IsUnique()
            .HasDatabaseName("uq_iam_authentication_commands_idempotency_key_hash");
        commands.HasIndex(command => command.PrincipalId);
        commands.HasOne<KordraIdentityUser>()
            .WithMany()
            .HasForeignKey(command => command.PrincipalId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_iam_authentication_commands_iam_principals_principal_id");
        var floor = modelBuilder.Entity<KordraAuthorityClockFloor>();
        floor.ToTable("iam_authority_clock_floors"); floor.HasKey(x => x.InstallationId);
        floor.Property(x => x.InstallationId).HasColumnName("installation_id").ValueGeneratedNever();
        floor.Property(x => x.AdmittedUtc).HasColumnName("admitted_utc").IsRequired();
        var definitions = modelBuilder.Entity<KordraRoleDefinition>();
        definitions.ToTable("iam_role_definitions", table => table.HasCheckConstraint("ck_iam_role_definition_revision", "revision > 0"));
        definitions.HasKey(x => new { x.DefinitionId, x.Revision });
        definitions.Property(x => x.DefinitionId).HasColumnName("definition_id").ValueGeneratedNever();
        definitions.Property(x => x.Revision).HasColumnName("revision").ValueGeneratedNever();
        definitions.Property(x => x.ManifestId).HasColumnName("manifest_id");
        definitions.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        definitions.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        definitions.Property(x => x.RulesJson).HasColumnName("rules_json").HasColumnType("jsonb").IsRequired();
        definitions.Property(x => x.IsRetired).HasColumnName("is_retired");
        definitions.HasIndex(x => x.ManifestId).IsUnique();

    }
}
