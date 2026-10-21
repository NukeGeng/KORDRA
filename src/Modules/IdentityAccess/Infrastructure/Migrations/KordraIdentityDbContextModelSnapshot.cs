using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kordra.Modules.IdentityAccess.Infrastructure.Migrations;

[DbContext(typeof(KordraIdentityDbContext))]
public sealed class KordraIdentityDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63)
            .HasDefaultSchema("app");

        modelBuilder.Entity<KordraIdentityUser>(entity =>
        {
            entity.ToTable("iam_principals", "app", table =>
            {
                table.HasCheckConstraint("ck_iam_principals_account_status", "account_status IN ('active', 'disabled')");
                table.HasCheckConstraint("ck_iam_principals_revision", "revision > 0");
            });

            entity.HasKey(user => user.Id).HasName("pk_iam_principals");
            entity.HasIndex(user => user.NormalizedUserName)
                .IsUnique()
                .HasDatabaseName("uq_iam_principals_normalized_user_name");

            entity.Property(user => user.Id).HasColumnName("principal_id").ValueGeneratedNever();
            entity.Property(user => user.UserName).HasColumnName("user_name").HasMaxLength(64).IsRequired();
            entity.Property(user => user.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(64).IsRequired();
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(user => user.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(128).IsRequired();
            entity.Property(user => user.ConcurrencyStamp).HasColumnName("concurrency_stamp").HasMaxLength(128).IsRequired().IsConcurrencyToken();
            entity.Property(user => user.AccountStatus).HasColumnName("account_status").HasMaxLength(16).IsRequired();
            entity.Property(user => user.Revision).HasColumnName("revision").HasDefaultValue(1L).IsRequired();
            entity.Property(user => user.LockoutEnd).HasColumnName("lockout_end_utc");
            entity.Property(user => user.LockoutEnabled).HasColumnName("lockout_enabled").IsRequired();
            entity.Property(user => user.AccessFailedCount).HasColumnName("access_failed_count").IsRequired();
            entity.Ignore(user => user.Email);
            entity.Ignore(user => user.NormalizedEmail);
            entity.Ignore(user => user.EmailConfirmed);
            entity.Ignore(user => user.PhoneNumber);
            entity.Ignore(user => user.PhoneNumberConfirmed);
            entity.Ignore(user => user.TwoFactorEnabled);
        });

        modelBuilder.Entity<KordraSession>(entity =>
        {
            entity.ToTable("iam_sessions", "app", table =>
            {
                table.HasCheckConstraint("ck_iam_sessions_hash_length", "octet_length(session_reference_hash) = 32");
                table.HasCheckConstraint("ck_iam_sessions_principal_revision", "principal_revision > 0");
                table.HasCheckConstraint("ck_iam_sessions_expiry_after_creation", "absolute_expires_at_utc > created_at_utc");
                table.HasCheckConstraint("ck_iam_sessions_activity_after_creation", "last_activity_at_utc >= created_at_utc");
                table.HasCheckConstraint(
                    "ck_iam_sessions_revocation_state",
                    "(is_revoked AND revoked_at_utc IS NOT NULL) OR (NOT is_revoked AND revoked_at_utc IS NULL AND revocation_reason IS NULL)");
            });

            entity.HasKey(session => session.SessionId).HasName("pk_iam_sessions");
            entity.HasIndex(session => session.SessionReferenceHash)
                .IsUnique()
                .HasDatabaseName("uq_iam_sessions_session_reference_hash");
            entity.HasIndex(session => new { session.PrincipalId, session.IsRevoked, session.AbsoluteExpiresAtUtc })
                .HasDatabaseName("ix_iam_sessions_principal_active_expiry");
            entity.Property(session => session.SessionId).HasColumnName("session_id").ValueGeneratedNever();
            entity.Property(session => session.SessionReferenceHash)
                .HasColumnName("session_reference_hash")
                .HasColumnType("bytea")
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(session => session.PrincipalId).HasColumnName("principal_id").IsRequired();
            entity.Property(session => session.PrincipalRevision).HasColumnName("principal_revision").IsRequired();
            entity.Property(session => session.BootGeneration).HasColumnName("boot_generation").IsRequired();
            entity.Property(session => session.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(session => session.LastActivityAtUtc).HasColumnName("last_activity_at_utc").IsRequired();
            entity.Property(session => session.AbsoluteExpiresAtUtc).HasColumnName("absolute_expires_at_utc").IsRequired();
            entity.Property(session => session.IsRevoked).HasColumnName("is_revoked").IsRequired();
            entity.Property(session => session.RevokedAtUtc).HasColumnName("revoked_at_utc");
            entity.Property(session => session.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(128);
            entity.HasOne(session => session.Principal)
                .WithMany()
                .HasForeignKey(session => session.PrincipalId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_iam_sessions_iam_principals_principal_id");
        });
#pragma warning restore 612, 618
    }
}
