using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.DbMigrator;

/// <summary>Offline entry point for the IAM-owned migration set only.</summary>
public static class IdentityAccessMigrationCommand
{
    public const string ConnectionEnvironmentVariable = "KORDRA_IDENTITYACCESS_MIGRATION_CONNECTION_STRING";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length != 1 || !string.Equals(args[0], "identity-access", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: Kordra.DbMigrator identity-access");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Set the private {ConnectionEnvironmentVariable} locator for the approved local IAM migration target.");
            return 2;
        }

        DbContextOptions<KordraIdentityDbContext> options;
        try
        {
            options = IdentityAccessDbContextFactory.CreateOfflineMigrationOptions(connectionString);
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("The IAM migration target is not the approved loopback kordra/kordra_admin identity; its value was not displayed.");
            return 2;
        }

        try
        {
            await using var dbContext = new KordraIdentityDbContext(options);
            await dbContext.Database.MigrateAsync(cancellationToken);
            Console.WriteLine("IAM migrations completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"IAM migration failed ({exception.GetType().Name}); connection details were not displayed.");
            return 1;
        }
    }
}
