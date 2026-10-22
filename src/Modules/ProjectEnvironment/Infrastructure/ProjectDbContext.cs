using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ProjectEnvironment.Infrastructure;

public sealed class ProjectRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "active";
    public long Revision { get; set; } = 1;
}
public sealed class ApplicationRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "active";
    public long Revision { get; set; } = 1;
}
public sealed class EnvironmentRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Classification { get; set; } = "";
    public long PolicyRevision { get; set; }
    public string Status { get; set; } = "active";
    public long Revision { get; set; } = 1;
}
public sealed class ProjectTargetAssociation
{
    public Guid TargetId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public long Revision { get; set; } = 1;
}
public sealed class ProjectCommandReceipt
{
    public string KeyHash { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
}
public sealed class ProjectDbContext(DbContextOptions<ProjectDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "prj_ef_migrations_history";
    public DbSet<ProjectRecord> Projects => Set<ProjectRecord>();
    public DbSet<ApplicationRecord> Applications => Set<ApplicationRecord>();
    public DbSet<EnvironmentRecord> Environments => Set<EnvironmentRecord>();
    public DbSet<ProjectTargetAssociation> Associations => Set<ProjectTargetAssociation>();
    public DbSet<ProjectCommandReceipt> Commands => Set<ProjectCommandReceipt>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("app");
        var project = model.Entity<ProjectRecord>(); project.ToTable("prj_projects"); project.HasKey(x => new { x.TenantId, x.Id });
        var app = model.Entity<ApplicationRecord>(); app.ToTable("prj_applications"); app.HasKey(x => new { x.TenantId, x.Id });
        app.HasOne<ProjectRecord>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        var env = model.Entity<EnvironmentRecord>(); env.ToTable("prj_environments", t => { t.HasCheckConstraint("ck_prj_classification", "classification IN ('Development','Test','Staging','Production')"); t.HasCheckConstraint("ck_prj_policy_revision", "policy_revision > 0"); });
        env.HasKey(x => new { x.TenantId, x.Id }); env.HasAlternateKey(x => new { x.TenantId, x.ProjectId, x.Id });
        env.HasOne<ProjectRecord>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        env.Property(x => x.Classification).HasMaxLength(16).IsRequired();
        foreach (var type in new[] { typeof(ProjectRecord), typeof(ApplicationRecord), typeof(EnvironmentRecord) })
        {
            var builder = model.Entity(type);
            builder.Property("Name").HasMaxLength(128).IsRequired(); builder.Property("Status").HasMaxLength(16).IsRequired(); builder.Property("Revision").IsConcurrencyToken();
            builder.ToTable(t => { t.HasCheckConstraint("ck_" + builder.Metadata.GetTableName() + "_status", "status IN ('active','suspended','deleted')"); t.HasCheckConstraint("ck_" + builder.Metadata.GetTableName() + "_revision", "revision > 0"); });
        }
        var binding = model.Entity<ProjectTargetAssociation>(); binding.ToTable("prj_target_associations", t => t.HasCheckConstraint("ck_prj_association_revision", "revision > 0"));
        binding.HasKey(x => x.TargetId); binding.Property(x => x.Revision).IsConcurrencyToken();
        binding.HasOne<ProjectRecord>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).OnDelete(DeleteBehavior.Restrict);
        binding.HasOne<EnvironmentRecord>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.EnvironmentId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var receipt = model.Entity<ProjectCommandReceipt>(); receipt.ToTable("prj_commands"); receipt.HasKey(x => x.KeyHash);
        receipt.Property(x => x.KeyHash).HasMaxLength(64); receipt.Property(x => x.ContentHash).HasMaxLength(64).IsRequired(); receipt.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
        foreach (var type in model.Model.GetEntityTypes()) foreach (var property in type.GetProperties())
            property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
