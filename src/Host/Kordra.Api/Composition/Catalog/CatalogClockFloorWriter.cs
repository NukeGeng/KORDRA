using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
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
    private readonly DisposableTestBinding? disposableTest;
    private CatalogClockFloorWriter(LocalAuthenticationConfiguration configuration, IAuthorityClock clock, DisposableTestBinding profile)
        : this(configuration, clock) => disposableTest = profile;
    public static CatalogClockFloorWriter CreateDisposableTestWriter(LocalAuthenticationConfiguration configuration, IAuthorityClock clock,
        DisposableTestBinding profile) => new(configuration, clock, profile);

    public async Task StageAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default)
    {
        if (disposableTest is { } profile) IdentityAccessDbContextFactory.ValidateDisposableTestTarget(connection.ConnectionString, profile);
        else IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(connection.ConnectionString);
        await using var db = new KordraIdentityDbContext(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(connection,
            p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app").CommandTimeout(2)).Options);
        await db.Database.UseTransactionAsync(transaction, ct);
        await AuthorityClockFloorStore.StageAsync(db, configuration.InstallationId, clock.GetTrustedUtcNow(), ct);
    }
}
