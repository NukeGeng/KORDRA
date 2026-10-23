using System.Data.Common;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public interface ICatalogClockFloorWriter
{
    Task StageAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default);
}
public sealed class CatalogClockFloorWriter(LocalAuthenticationConfiguration configuration, IAuthorityClock clock) : ICatalogClockFloorWriter
{
    public async Task StageAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default)
    {
        IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(connection.ConnectionString);
        await using var db = new KordraIdentityDbContext(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(connection,
            p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app").CommandTimeout(2)).Options);
        await db.Database.UseTransactionAsync(transaction, ct);
        await AuthorityClockFloorStore.StageAsync(db, configuration.InstallationId, clock.GetTrustedUtcNow(), ct);
    }
}
