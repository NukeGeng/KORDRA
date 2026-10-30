using System.Data.Common;
using Kordra.Modules.ReleaseManagement.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kordra.Modules.ReleaseManagement.Infrastructure;

public sealed class ReleaseDbContextFactory : IDesignTimeDbContextFactory<ReleaseDbContext>
{
    private const string Design = "Host=127.0.0.1;Port=5432;Database=kordra_design_time;Username=kordra_design_time;Timeout=1";
    public ReleaseDbContext CreateDbContext(string[] args) => new(Options(Design));
    public static DbContextOptions<ReleaseDbContext> CreateRuntimeOptions(string locator)
    { Validate(locator, "kordra", "kordra_app"); return Options(locator); }
    public static DbContextOptions<ReleaseDbContext> CreateOfflineMigrationOptions(string locator)
    { Validate(locator, "kordra", "kordra_admin"); return Options(locator); }
    public static DbContextOptions<ReleaseDbContext> CreateTestOptions(string locator)
    { Validate(locator, "kordra_test", "kordra_test"); return Options(locator); }
    public static DbContextOptions<ReleaseDbContext> CreateEnlistedOptions(DbConnection connection)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (parsed.Database == "kordra_test") Validate(connection.ConnectionString, "kordra_test", "kordra_test");
        else Validate(connection.ConnectionString, "kordra", "kordra_app");
        return new DbContextOptionsBuilder<ReleaseDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable(ReleaseDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    }
    public static DbContextOptions<ReleaseDbContext> CreateOfflineReceiptReadOptions(DbConnection connection, IReleaseOfflineCustody custody)
    {
        ArgumentNullException.ThrowIfNull(custody); custody.RequireHeld();
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (parsed.Database == "kordra_test") Validate(connection.ConnectionString, "kordra_test", "kordra_test");
        else Validate(connection.ConnectionString, "kordra", "kordra_admin");
        return new DbContextOptionsBuilder<ReleaseDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable(HistoryTableName, "app").CommandTimeout(2)).Options;
    }
    private const string HistoryTableName = ReleaseDbContext.HistoryTable;
    private static DbContextOptions<ReleaseDbContext> Options(string locator) =>
        new DbContextOptionsBuilder<ReleaseDbContext>().UseNpgsql(locator,
            x => x.MigrationsHistoryTable(ReleaseDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    private static void Validate(string locator, string database, string role)
    {
        NpgsqlConnectionStringBuilder value;
        try { value = new(locator); }
        catch (Exception exception) when (exception is ArgumentException or NullReferenceException)
        { throw new InvalidOperationException("Invalid private release database locator."); }
        if (value.Host != "127.0.0.1" || value.Port != 5432 || value.Database != database || value.Username != role)
            throw new InvalidOperationException("Unapproved release database target or role.");
    }
}
