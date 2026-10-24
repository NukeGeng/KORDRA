using Kordra.Modules.ConnectionManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.DbMigrator;

/// <summary>Offline entry point for the SEC-owned migration set only.</summary>
public static class ConnectionManagementMigrationCommand
{
    public const string ConnectionEnvironmentVariable = "KORDRA_CREDENTIAL_MIGRATION_CONNECTION_STRING";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length != 1 || !string.Equals(args[0], "connection-management", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: Kordra.DbMigrator connection-management");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Set the private {ConnectionEnvironmentVariable} locator for the approved local SEC migration target.");
            return 2;
        }

        DbContextOptions<CredentialDbContext> options;
        try
        {
            options = CredentialDbContextFactory.CreateOfflineMigrationOptions(connectionString);
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("The SEC migration target is not the approved loopback kordra/kordra_admin identity; its value was not displayed.");
            return 2;
        }

        try
        {
            await using var dbContext = new CredentialDbContext(options);
            await dbContext.Database.MigrateAsync(cancellationToken);
            Console.WriteLine("SEC migrations completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SEC migration failed ({exception.GetType().Name}); connection details were not displayed.");
            return 1;
        }
    }
}
