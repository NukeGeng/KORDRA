using System.Data.Common;
using Kordra.Modules.AuditCompliance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.TenantManagement.Infrastructure;

public sealed class TenantOrganization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public long Revision { get; set; } = 1;
}
public sealed class TenantMembership
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PrincipalId { get; set; }
    public string Status { get; set; } = "active";
    public long Revision { get; set; } = 1;
}
public sealed class TenantAssignment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid DefinitionId { get; set; }
    public long DefinitionRevision { get; set; }
    public string ScopeKind { get; set; } = string.Empty;
    public Guid ScopeId { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string Status { get; set; } = "active";
    public long Revision { get; set; } = 1;
    public bool IsFounding { get; set; }
    public Guid GrantorPrincipalId { get; set; }
    public string ProvenanceJson { get; set; } = "[]";
}
public sealed class TenantCommandReceipt
{
    public string KeyHash { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid ResourceId { get; set; }
    public long Revision { get; set; }
}

public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "ten_ef_migrations_history";
    public DbSet<TenantOrganization> Organizations => Set<TenantOrganization>();
    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();
    public DbSet<TenantAssignment> Assignments => Set<TenantAssignment>();
    public DbSet<TenantCommandReceipt> Commands => Set<TenantCommandReceipt>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("app");
        var org = model.Entity<TenantOrganization>();
        org.ToTable("ten_organizations", t => { t.HasCheckConstraint("ck_ten_org_status", "status IN ('active','suspended')"); t.HasCheckConstraint("ck_ten_org_revision", "revision > 0"); });
        org.HasKey(x => x.Id); org.Property(x => x.Name).HasMaxLength(128).IsRequired(); org.Property(x => x.Status).HasMaxLength(16).IsRequired(); org.Property(x => x.Revision).IsConcurrencyToken();
        var member = model.Entity<TenantMembership>();
        member.ToTable("ten_memberships", t => { t.HasCheckConstraint("ck_ten_member_status", "status IN ('active','revoked')"); t.HasCheckConstraint("ck_ten_member_revision", "revision > 0"); });
        member.HasKey(x => new { x.TenantId, x.Id }); member.HasIndex(x => new { x.TenantId, x.PrincipalId }).IsUnique(); member.Property(x => x.Status).HasMaxLength(16).IsRequired(); member.Property(x => x.Revision).IsConcurrencyToken();
        member.HasOne<TenantOrganization>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        var assignment = model.Entity<TenantAssignment>();
        assignment.ToTable("ten_assignments", t => { t.HasCheckConstraint("ck_ten_assignment_status", "status IN ('active','revoked')"); t.HasCheckConstraint("ck_ten_assignment_revision", "revision > 0 AND definition_revision > 0"); t.HasCheckConstraint("ck_ten_scope", "scope_kind IN ('organization','project','environment')"); });
        assignment.HasKey(x => new { x.TenantId, x.Id }); assignment.Property(x => x.Status).HasMaxLength(16).IsRequired(); assignment.Property(x => x.ScopeKind).HasMaxLength(16).IsRequired(); assignment.Property(x => x.Revision).IsConcurrencyToken(); assignment.Property(x => x.ProvenanceJson).HasColumnType("jsonb").IsRequired();
        assignment.HasOne<TenantMembership>().WithMany().HasForeignKey(x => new { x.TenantId, x.MembershipId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        assignment.HasIndex(x => new { x.TenantId, x.MembershipId, x.ScopeKind, x.ScopeId });
        var commands = model.Entity<TenantCommandReceipt>(); commands.ToTable("ten_commands"); commands.HasKey(x => x.KeyHash); commands.Property(x => x.KeyHash).HasMaxLength(64); commands.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        foreach (var type in model.Model.GetEntityTypes())
            foreach (var property in type.GetProperties())
                property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}

/// <summary>Composition enlists AUD in this owner's transaction; setup never commits.</summary>
public interface ITenantAuditTransactionFactory
{
    ValueTask<ITenantAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken = default);
}
public interface ITenantAuditTransaction : IAsyncDisposable
{
    Task<AuditAppendOutcome> CommitAsync(AuditIntent intent, Func<CancellationToken, Task<bool>> saveOwnerAsync, CancellationToken cancellationToken = default);
}
