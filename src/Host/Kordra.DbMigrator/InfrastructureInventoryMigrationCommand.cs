using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.DbMigrator;

/// <summary>Offline entry point for the INV-owned migration set only.</summary>
public static class InfrastructureInventoryMigrationCommand
{
    public const string ConnectionEnvironmentVariable = "KORDRA_INVENTORY_MIGRATION_CONNECTION_STRING";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length != 1 || !string.Equals(args[0], "infrastructure-inventory", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: Kordra.DbMigrator infrastructure-inventory");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Set the private {ConnectionEnvironmentVariable} locator for the approved local INV migration target.");
            return 2;
        }

        DbContextOptions<InventoryDbContext> options;
        try
        {
            options = InventoryDbContextFactory.CreateOfflineMigrationOptions(connectionString);
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("The INV migration target is not the approved loopback kordra/kordra_admin identity; its value was not displayed.");
            return 2;
        }

        try
        {
            await using var dbContext = new InventoryDbContext(options);
            await dbContext.Database.MigrateAsync(cancellationToken);
            Console.WriteLine("INV migrations completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"INV migration failed ({exception.GetType().Name}); connection details were not displayed.");
            return 1;
        }
    }
}
