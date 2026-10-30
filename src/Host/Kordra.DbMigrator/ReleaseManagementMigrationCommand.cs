using Kordra.Modules.ReleaseManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.DbMigrator;

/// <summary>Offline REL migration; the caller holds exclusive installation custody.</summary>
public static class ReleaseManagementMigrationCommand
{
    public const string ConnectionEnvironmentVariable = "KORDRA_RELEASE_MIGRATION_CONNECTION_STRING";
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args is not ["release-management"]) return 2;
        var locator = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(locator))
        { Console.Error.WriteLine($"Set private {ConnectionEnvironmentVariable} for the approved offline REL target."); return 2; }
        DbContextOptions<ReleaseDbContext> options;
        try { options = ReleaseDbContextFactory.CreateOfflineMigrationOptions(locator); }
        catch (InvalidOperationException)
        { Console.Error.WriteLine("REL migration target or role is not approved; locator is not displayed."); return 2; }
        try
        {
            await using var db = new ReleaseDbContext(options); await db.Database.MigrateAsync(cancellationToken);
            Console.WriteLine("REL migrations completed."); return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine($"REL migration failed ({error.GetType().Name}); locator is not displayed."); return 1; }
    }
}
