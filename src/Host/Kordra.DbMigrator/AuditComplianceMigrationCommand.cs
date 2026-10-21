using Kordra.Modules.AuditCompliance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.DbMigrator;

/// <summary>Offline entry point for AUD-owned additive migrations only.</summary>
public static class AuditComplianceMigrationCommand
{
    public const string ConnectionEnvironmentVariable = "KORDRA_AUDITCOMPLIANCE_MIGRATION_CONNECTION_STRING";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length != 1 || !string.Equals(args[0], "audit-compliance", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: Kordra.DbMigrator audit-compliance");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Set the private {ConnectionEnvironmentVariable} locator for the approved local AUD migration target.");
            return 2;
        }

        DbContextOptions<AuditDbContext> options;
        try
        {
            options = AuditDbContextFactory.CreateOfflineMigrationOptions(connectionString);
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("The AUD migration target is not loopback PostgreSQL database kordra with role kordra_admin; its value was not displayed.");
            return 2;
        }

        try
        {
            await using var dbContext = new AuditDbContext(options);
            await dbContext.Database.MigrateAsync(cancellationToken);
            Console.WriteLine("AUD migrations completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AUD migration failed ({exception.GetType().Name}); connection details were not displayed.");
            return 1;
        }
    }
}
