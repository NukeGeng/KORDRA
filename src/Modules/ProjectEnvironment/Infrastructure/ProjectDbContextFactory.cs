using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kordra.Modules.ProjectEnvironment.Infrastructure;

/// <summary>Design-time factory and explicit local connection-role boundaries for PRJ persistence.</summary>
public sealed class ProjectDbContextFactory : IDesignTimeDbContextFactory<ProjectDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=127.0.0.1;Port=5432;Database=kordra_design_time;Username=kordra_design_time;Timeout=1";

    public ProjectDbContext CreateDbContext(string[] args) =>
        new(CreateOptions(DesignTimeConnectionString));

    public static DbContextOptions<ProjectDbContext> CreateRuntimeOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra", "kordra_app");
        return CreateOptions(connectionString);
    }

    public static DbContextOptions<ProjectDbContext> CreateOfflineMigrationOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra", "kordra_admin");
        return CreateOptions(connectionString);
    }

    public static DbContextOptions<ProjectDbContext> CreateTestOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra_test", "kordra_test");
        return CreateOptions(connectionString);
    }

    public static void ValidateRuntimeTarget(string connectionString) =>
        ValidateTarget(connectionString, "kordra", "kordra_app");

    public static void ValidateOfflineMigrationTarget(string connectionString) =>
        ValidateTarget(connectionString, "kordra", "kordra_admin");

    private static DbContextOptions<ProjectDbContext> CreateOptions(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ProjectDbContext>();
        options.UseNpgsql(connectionString, postgres =>
            postgres.MigrationsHistoryTable(
                ProjectDbContext.HistoryTable,
                "app")
            .CommandTimeout(2));
        return options.Options;
    }

    public static DbContextOptions<ProjectDbContext> CreateEnlistedOptions(System.Data.Common.DbConnection connection)
    {
        // Exact runtime or isolated test identity only; never an admin connection for owner mutation.
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (parsed.Database == "kordra_test") ValidateTarget(connection.ConnectionString, "kordra_test", "kordra_test");
        else ValidateTarget(connection.ConnectionString, "kordra", "kordra_app");
        return new DbContextOptionsBuilder<ProjectDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(ProjectDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    }

    public static DbContextOptions<ProjectDbContext> CreateOfflineEnlistedOptions(System.Data.Common.DbConnection connection)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (parsed.Database == "kordra_test") ValidateTarget(connection.ConnectionString, "kordra_test", "kordra_test");
        else ValidateTarget(connection.ConnectionString, "kordra", "kordra_admin");
        return new DbContextOptionsBuilder<ProjectDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(ProjectDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    }

    private static void ValidateTarget(string connectionString, string expectedDatabase, string expectedUser)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A private PRJ database connection locator is required.");
        }

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The PRJ database connection locator is invalid; its value was not displayed.");
        }

        if (!string.Equals(parsed.Host, "127.0.0.1", StringComparison.Ordinal) ||
            parsed.Port != 5432 ||
            !string.Equals(parsed.Database, expectedDatabase, StringComparison.Ordinal) ||
            !string.Equals(parsed.Username, expectedUser, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The PRJ database connection target or role is not permitted.");
        }
    }
}
