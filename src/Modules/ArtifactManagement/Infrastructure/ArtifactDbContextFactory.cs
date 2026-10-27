using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kordra.Modules.ArtifactManagement.Infrastructure;

public sealed class ArtifactDbContextFactory : IDesignTimeDbContextFactory<ArtifactDbContext>
{
    public ArtifactDbContext CreateDbContext(string[] args) => new(Options(
        "Host=127.0.0.1;Port=5432;Database=kordra_design_time;Username=kordra_design_time;Timeout=1"));

    public static DbContextOptions<ArtifactDbContext> CreateRuntimeOptions(string locator) => Validated(locator, "kordra", "kordra_app");
    public static DbContextOptions<ArtifactDbContext> CreateOfflineMigrationOptions(string locator) => Validated(locator, "kordra", "kordra_admin");
    public static DbContextOptions<ArtifactDbContext> CreateTestOptions(string locator) => Validated(locator, "kordra_test", "kordra_test");

    public static DbContextOptions<ArtifactDbContext> CreateEnlistedOptions(DbConnection connection, bool offline = false)
    {
        ValidateEnlistedTarget(connection, offline);
        return new DbContextOptionsBuilder<ArtifactDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(ArtifactDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    }

    public static void ValidateEnlistedTarget(DbConnection connection, bool offline = false)
    {
        if (connection is not NpgsqlConnection) throw new InvalidOperationException("An approved ART PostgreSQL connection is required.");
        var parsed = Parse(connection.ConnectionString);
        Validate(parsed, parsed.Database == "kordra_test" ? "kordra_test" : "kordra",
            parsed.Database == "kordra_test" ? "kordra_test" : offline ? "kordra_admin" : "kordra_app");
    }

    private static DbContextOptions<ArtifactDbContext> Validated(string locator, string database, string user)
    {
        Validate(Parse(locator), database, user);
        return Options(locator);
    }

    private static DbContextOptions<ArtifactDbContext> Options(string locator) =>
        new DbContextOptionsBuilder<ArtifactDbContext>().UseNpgsql(locator,
            postgres => postgres.MigrationsHistoryTable(ArtifactDbContext.HistoryTable, "app").CommandTimeout(2)).Options;

    private static NpgsqlConnectionStringBuilder Parse(string locator)
    {
        if (string.IsNullOrWhiteSpace(locator)) throw new InvalidOperationException("A private ART database locator is required.");
        try { return new(locator); }
        catch (ArgumentException) { throw new InvalidOperationException("The ART database locator is invalid; its value was not displayed."); }
    }

    private static void Validate(NpgsqlConnectionStringBuilder parsed, string database, string user)
    {
        if (parsed.Host != "127.0.0.1" || parsed.Port != 5432 || parsed.Database != database || parsed.Username != user)
            throw new InvalidOperationException("The ART database target or role is not permitted.");
    }
}
