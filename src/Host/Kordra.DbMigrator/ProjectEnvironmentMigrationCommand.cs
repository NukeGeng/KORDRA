using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.DbMigrator;

/// <summary>Offline entry point for the PRJ-owned migration set only.</summary>
public static class ProjectEnvironmentMigrationCommand
{
    public const string ConnectionEnvironmentVariable = "KORDRA_PROJECPRJVIRONMENT_MIGRATION_CONNECTION_STRING";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length != 1 || !string.Equals(args[0], "project-environment", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: Kordra.DbMigrator project-environment");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Set the private {ConnectionEnvironmentVariable} locator for the approved local PRJ migration target.");
            return 2;
        }

        DbContextOptions<ProjectDbContext> options;
        try
        {
            options = ProjectDbContextFactory.CreateOfflineMigrationOptions(connectionString);
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("The PRJ migration target is not the approved loopback kordra/kordra_admin identity; its value was not displayed.");
            return 2;
        }

        try
        {
            await using var dbContext = new ProjectDbContext(options);
            await dbContext.Database.MigrateAsync(cancellationToken);
            Console.WriteLine("PRJ migrations completed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PRJ migration failed ({exception.GetType().Name}); connection details were not displayed.");
            return 1;
        }
    }
}
