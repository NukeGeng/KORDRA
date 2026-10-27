using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kordra.Modules.TenantManagement.Infrastructure;

/// <summary>Design-time factory and explicit local connection-role boundaries for TEN persistence.</summary>
public sealed class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=127.0.0.1;Port=5432;Database=kordra_design_time;Username=kordra_design_time;Timeout=1";

    public TenantDbContext CreateDbContext(string[] args) =>
        new(CreateOptions(DesignTimeConnectionString));

    public static DbContextOptions<TenantDbContext> CreateRuntimeOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra", "kordra_app");
        return CreateOptions(connectionString);
    }

    public static DbContextOptions<TenantDbContext> CreateOfflineMigrationOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra", "kordra_admin");
        return CreateOptions(connectionString);
    }

    public static DbContextOptions<TenantDbContext> CreateTestOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra_test", "kordra_test");
        return CreateOptions(connectionString);
    }

    public static void ValidateRuntimeTarget(string connectionString) =>
        ValidateTarget(connectionString, "kordra", "kordra_app");

    public static void ValidateOfflineMigrationTarget(string connectionString) =>
        ValidateTarget(connectionString, "kordra", "kordra_admin");

    private static DbContextOptions<TenantDbContext> CreateOptions(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>();
        options.UseNpgsql(connectionString, postgres =>
            postgres.MigrationsHistoryTable(
                TenantDbContext.HistoryTable,
                "app")
            .CommandTimeout(2));
        return options.Options;
    }

    public static DbContextOptions<TenantDbContext> CreateOfflineEnlistedOptions(System.Data.Common.DbConnection connection)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (parsed.Database == "kordra_test") ValidateTarget(connection.ConnectionString, "kordra_test", "kordra_test");
        else ValidateTarget(connection.ConnectionString, "kordra", "kordra_admin");
        return new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(TenantDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    }

    private static void ValidateTarget(string connectionString, string expectedDatabase, string expectedUser)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A private TEN database connection locator is required.");
        }

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The TEN database connection locator is invalid; its value was not displayed.");
        }

        if (!string.Equals(parsed.Host, "127.0.0.1", StringComparison.Ordinal) ||
            parsed.Port != 5432 ||
            !string.Equals(parsed.Database, expectedDatabase, StringComparison.Ordinal) ||
            !string.Equals(parsed.Username, expectedUser, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The TEN database connection target or role is not permitted.");
        }
    }
}
