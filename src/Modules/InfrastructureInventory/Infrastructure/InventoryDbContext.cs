using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.InfrastructureInventory.Infrastructure;

public sealed class TargetRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "active";
    public long Revision { get; set; } = 1;
    public long AssociationRevision { get; set; } = 1;
}
public sealed class TargetCommandReceipt
{
    public string KeyHash { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
}
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "inv_ef_migrations_history";
    public DbSet<TargetRecord> Targets => Set<TargetRecord>();
    public DbSet<TargetCommandReceipt> Commands => Set<TargetCommandReceipt>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("app");
        var target = model.Entity<TargetRecord>(); target.ToTable("inv_targets", t => { t.HasCheckConstraint("ck_inv_status", "status IN ('active','disabled','deleted')"); t.HasCheckConstraint("ck_inv_revisions", "revision > 0 AND association_revision > 0"); });
        target.HasKey(x => x.Id); target.Property(x => x.Name).HasMaxLength(128).IsRequired(); target.Property(x => x.Kind).HasMaxLength(64).IsRequired(); target.Property(x => x.Status).HasMaxLength(16).IsRequired(); target.Property(x => x.Revision).IsConcurrencyToken();
        var receipt = model.Entity<TargetCommandReceipt>(); receipt.ToTable("inv_commands"); receipt.HasKey(x => x.KeyHash);
        receipt.Property(x => x.KeyHash).HasMaxLength(64); receipt.Property(x => x.ContentHash).HasMaxLength(64).IsRequired(); receipt.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
        foreach (var type in model.Model.GetEntityTypes()) foreach (var property in type.GetProperties())
            property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
