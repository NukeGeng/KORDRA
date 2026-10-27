using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.DbMigrator;

/// <summary>Offline APR migration; the caller holds exclusive installation custody.</summary>
public static class ApprovalGovernanceMigrationCommand
{
    public const string ConnectionEnvironmentVariable = "KORDRA_APPROVAL_MIGRATION_CONNECTION_STRING";
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args is not ["approval-governance"]) return 2;
        var locator = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(locator))
        {
            Console.Error.WriteLine($"Set private {ConnectionEnvironmentVariable} for the approved offline APR target.");
            return 2;
        }
        DbContextOptions<ApprovalDbContext> options;
        try { options = ApprovalDbContextFactory.CreateOfflineMigrationOptions(locator); }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("APR migration target or role is not approved; locator is not displayed.");
            return 2;
        }
        try
        {
            await using var db = new ApprovalDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            Console.WriteLine("APR migrations completed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"APR migration failed ({error.GetType().Name}); locator is not displayed.");
            return 1;
        }
    }
}
