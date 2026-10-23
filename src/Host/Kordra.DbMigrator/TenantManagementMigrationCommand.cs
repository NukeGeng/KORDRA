using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.DbMigrator;

/// <summary>Offline entry point for the TEN-owned migration set only.</summary>
public static class TenantManagementMigrationCommand
{
    public const string ConnectionEnvironmentVariable = "KORDRA_TENANTMANAGEMENT_MIGRATION_CONNECTION_STRING";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length != 1 || !string.Equals(args[0], "tenant-management", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: Kordra.DbMigrator tenant-management");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Set the private {ConnectionEnvironmentVariable} locator for the approved local TEN migration target.");
            return 2;
        }

        DbContextOptions<TenantDbContext> options;
        try
        {
            options = TenantDbContextFactory.CreateOfflineMigrationOptions(connectionString);
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("The TEN migration target is not the approved loopback kordra/kordra_admin identity; its value was not displayed.");
            return 2;
        }

        try
        {
            await using var dbContext = new TenantDbContext(options);
            await dbContext.Database.MigrateAsync(cancellationToken);
            Console.WriteLine("TEN migrations completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"TEN migration failed ({exception.GetType().Name}); connection details were not displayed.");
            return 1;
        }
    }
}
