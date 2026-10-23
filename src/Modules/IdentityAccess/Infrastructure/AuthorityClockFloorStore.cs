using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.IdentityAccess.Infrastructure;

public static class AuthorityClockFloorStore
{
    public static Task StageAsync(KordraIdentityDbContext db, Guid installation, DateTimeOffset now, CancellationToken ct = default)
    {
        if (installation == Guid.Empty || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Clock-floor mutation requires an enlisted IAM transaction and trusted installation.");
        return db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.iam_authority_clock_floors (installation_id, admitted_utc) VALUES ({installation}, {now}) ON CONFLICT (installation_id) DO UPDATE SET admitted_utc = GREATEST(app.iam_authority_clock_floors.admitted_utc, EXCLUDED.admitted_utc)", ct);
    }
}
