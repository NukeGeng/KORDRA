using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Modules.IdentityAccess.Infrastructure;

public static class AuthorityClockFloorStore
{
    public static Task StageAsync(KordraIdentityDbContext db, Guid installation, DateTimeOffset now, CancellationToken ct = default)
    {
        if (installation == Guid.Empty || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Clock-floor mutation requires an enlisted IAM transaction and trusted installation.");
        return db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.iam_authority_clock_floors (installation_id, admitted_utc) VALUES ({installation}, {now}) ON CONFLICT (installation_id) DO UPDATE SET admitted_utc = GREATEST(app.iam_authority_clock_floors.admitted_utc, EXCLUDED.admitted_utc)", ct);
    }
    /// <summary>Explicit isolated disposable test transaction with a case-owned schema.</summary>
    public static async Task StageDisposableIsolatedTestAsync(KordraIdentityDbContext db, Guid installation,
        DateTimeOffset now, (Guid RunId, string SourceSha256, string OperationManifestSha256, DateTimeOffset ExpiresAtUtc) profile,
        CancellationToken ct = default)
    {
        if (installation == Guid.Empty || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Clock-floor mutation requires an enlisted IAM transaction and trusted installation.");
        IdentityAccessDbContextFactory.ValidateDisposableTestTarget(db.Database.GetDbConnection().ConnectionString, profile);
        await StageCaseSchemaAsync(db, installation, now, ct);
    }

    /// <summary>
    /// Explicit legacy isolated <c>kordra_test</c> transaction whose IAM model lives in the case-owned schema.
    /// Never writes the runtime <c>app</c> schema; production callers keep <see cref="StageAsync"/>.
    /// </summary>
    public static async Task StageIsolatedTestAsync(KordraIdentityDbContext db, Guid installation, DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (installation == Guid.Empty || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Clock-floor mutation requires an enlisted IAM transaction and trusted installation.");
        IdentityAccessDbContextFactory.ValidateIsolatedTestTarget(db.Database.GetDbConnection().ConnectionString);
        await StageCaseSchemaAsync(db, installation, now, ct);
    }

    // The schema is derived only from the trusted installation GUID, never from caller text.
    private static async Task StageCaseSchemaAsync(KordraIdentityDbContext db, Guid installation, DateTimeOffset now,
        CancellationToken ct)
    {
        var schema = "t12_" + installation.ToString("N");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"INSERT INTO {schema}.iam_authority_clock_floors (installation_id, admitted_utc) VALUES (@installation, @now) " +
            $"ON CONFLICT (installation_id) DO UPDATE SET admitted_utc = GREATEST({schema}.iam_authority_clock_floors.admitted_utc, EXCLUDED.admitted_utc)";
        var installationParameter = command.CreateParameter();
        installationParameter.ParameterName = "installation";
        installationParameter.Value = installation;
        command.Parameters.Add(installationParameter);
        var timeParameter = command.CreateParameter();
        timeParameter.ParameterName = "now";
        timeParameter.Value = now;
        command.Parameters.Add(timeParameter);
        await command.ExecuteNonQueryAsync(ct);
    }
}
