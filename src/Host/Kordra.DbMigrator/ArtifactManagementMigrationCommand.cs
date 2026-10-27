using Kordra.Modules.ArtifactManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.DbMigrator;

/// <summary>Owner-scoped ART migration; the executable holds stopped-installation custody.</summary>
public static class ArtifactManagementMigrationCommand
{
    public const string ConnectionEnvironmentVariable = "KORDRA_ARTIFACT_MIGRATION_CONNECTION_STRING";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args is not ["artifact-management"])
        {
            Console.Error.WriteLine("Usage: Kordra.DbMigrator artifact-management");
            return 2;
        }
        var locator = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(locator))
        {
            Console.Error.WriteLine($"Set the private {ConnectionEnvironmentVariable} locator for the approved local ART migration target.");
            return 2;
        }
        DbContextOptions<ArtifactDbContext> options;
        try { options = ArtifactDbContextFactory.CreateOfflineMigrationOptions(locator); }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("The ART migration target is not the approved loopback kordra/kordra_admin identity; its value was not displayed.");
            return 2;
        }
        try
        {
            await using var context = new ArtifactDbContext(options);
            await context.Database.MigrateAsync(cancellationToken);
            Console.WriteLine("ART migrations completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ART migration failed ({exception.GetType().Name}); connection details were not displayed.");
            return 1;
        }
    }
}
