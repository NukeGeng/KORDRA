using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kordra.Modules.AuditCompliance.Infrastructure;

/// <summary>Design-time factory and exact local database-role boundaries for AUD persistence.</summary>
public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=127.0.0.1;Port=5432;Database=kordra_design_time;Username=kordra_design_time;Timeout=1";

    public AuditDbContext CreateDbContext(string[] args) => new(CreateOptions(DesignTimeConnectionString));

    public static DbContextOptions<AuditDbContext> CreateRuntimeOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra", "kordra_app");
        return CreateOptions(connectionString);
    }

    public static DbContextOptions<AuditDbContext> CreateOfflineMigrationOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra", "kordra_admin");
        return CreateOptions(connectionString);
    }

    public static DbContextOptions<AuditDbContext> CreateTestOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra_test", "kordra_test");
        return CreateOptions(connectionString);
    }

    public static void ValidateRuntimeTarget(string connectionString) =>
        ValidateTarget(connectionString, "kordra", "kordra_app");

    public static void ValidateOfflineMigrationTarget(string connectionString) =>
        ValidateTarget(connectionString, "kordra", "kordra_admin");

    public static void ValidateTestTarget(string connectionString) =>
        ValidateTarget(connectionString, "kordra_test", "kordra_test");

    internal static DbContextOptions<AuditDbContext> CreateTransactionOptions(System.Data.Common.DbConnection connection)
    {
        ValidateTransactionTarget(connection.ConnectionString);
        return CreateOptions(connection);
    }

    internal static void ValidateTransactionTarget(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("An approved local AUD transaction connection is required.");
        }

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The AUD transaction target is invalid; its value was not displayed.");
        }

        var runtime = string.Equals(parsed.Database, "kordra", StringComparison.Ordinal) &&
            string.Equals(parsed.Username, "kordra_app", StringComparison.Ordinal);
        var test = string.Equals(parsed.Database, "kordra_test", StringComparison.Ordinal) &&
            string.Equals(parsed.Username, "kordra_test", StringComparison.Ordinal);
        if (!string.Equals(parsed.Host, "127.0.0.1", StringComparison.Ordinal) || parsed.Port != 5432 || !(runtime || test))
        {
            throw new InvalidOperationException("The AUD transaction target or role is not permitted.");
        }
    }

    internal static DbContextOptions<AuditDbContext> CreateOfflineTransactionOptions(System.Data.Common.DbConnection connection)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (parsed.Host != "127.0.0.1" || parsed.Port != 5432 ||
            !(parsed.Database == "kordra" && parsed.Username == "kordra_admin" || parsed.Database == "kordra_test" && parsed.Username == "kordra_test"))
            throw new InvalidOperationException("The offline AUD transaction target or role is not permitted.");
        return CreateOptions(connection);
    }

    /// <summary>Neutral receipt inspection on the stopped owner's approved shared connection; never grants a runtime append.</summary>
    public static DbContextOptions<AuditDbContext> CreateOfflineReceiptReadOptions(System.Data.Common.DbConnection connection,
        Kordra.Modules.AuditCompliance.Contracts.IOfflineAuditLease custody)
    {
        custody.RequireHeld();
        return CreateOfflineTransactionOptions(connection);
    }

    private static DbContextOptions<AuditDbContext> CreateOptions(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>();
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable(
            AuditDbContext.MigrationHistoryTableName,
            AuditDbContext.SchemaName));
        return options.Options;
    }

    private static DbContextOptions<AuditDbContext> CreateOptions(System.Data.Common.DbConnection connection)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>();
        options.UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable(
            AuditDbContext.MigrationHistoryTableName,
            AuditDbContext.SchemaName));
        return options.Options;
    }

    private static void ValidateTarget(string connectionString, string expectedDatabase, string expectedUser)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A private AUD database connection locator is required.");
        }

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The AUD database connection locator is invalid; its value was not displayed.");
        }

        if (!string.Equals(parsed.Host, "127.0.0.1", StringComparison.Ordinal) ||
            parsed.Port != 5432 ||
            !string.Equals(parsed.Database, expectedDatabase, StringComparison.Ordinal) ||
            !string.Equals(parsed.Username, expectedUser, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The AUD database connection target or role is not permitted.");
        }
    }
}
