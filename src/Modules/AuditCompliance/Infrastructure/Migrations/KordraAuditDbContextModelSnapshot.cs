using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kordra.Modules.AuditCompliance.Infrastructure.Migrations;

[DbContext(typeof(AuditDbContext))]
public sealed class KordraAuditDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.11");
        AuditDbContext.ConfigureModel(modelBuilder);
    }
}
