using System.Data.Common;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kordra.Modules.ApprovalGovernance.Infrastructure;

public sealed class ApprovalDbContextFactory : IDesignTimeDbContextFactory<ApprovalDbContext>
{
    private const string Design = "Host=127.0.0.1;Port=5432;Database=kordra_design_time;Username=kordra_design_time;Timeout=1";
    public ApprovalDbContext CreateDbContext(string[] args) => new(Options(Design));
    public static DbContextOptions<ApprovalDbContext> CreateRuntimeOptions(string locator)
    { Validate(locator, "kordra", "kordra_app"); return Options(locator); }
    public static DbContextOptions<ApprovalDbContext> CreateOfflineMigrationOptions(string locator)
    { Validate(locator, "kordra", "kordra_admin"); return Options(locator); }
    public static DbContextOptions<ApprovalDbContext> CreateTestOptions(string locator)
    { Validate(locator, "kordra_test", "kordra_test"); return Options(locator); }
    // The caller owns the connection and must explicitly enlist the context in its transaction.
    public static DbContextOptions<ApprovalDbContext> CreateEnlistedOptions(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (parsed.Database == "kordra_test") Validate(connection.ConnectionString, "kordra_test", "kordra_test");
        else Validate(connection.ConnectionString, "kordra", "kordra_app");
        return new DbContextOptionsBuilder<ApprovalDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable(ApprovalDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    }
    public static DbContextOptions<ApprovalDbContext> CreateOfflineReceiptReadOptions(DbConnection connection, IOfflineApprovalCustody custody)
    {
        ArgumentNullException.ThrowIfNull(custody); custody.RequireHeld();
        return CreateOfflineEnlistedOptions(connection);
    }
    public static DbContextOptions<ApprovalDbContext> CreateOfflineEnlistedOptions(DbConnection connection)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (parsed.Database == "kordra_test") Validate(connection.ConnectionString, "kordra_test", "kordra_test");
        else Validate(connection.ConnectionString, "kordra", "kordra_admin");
        return new DbContextOptionsBuilder<ApprovalDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable(ApprovalDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    }
    private static DbContextOptions<ApprovalDbContext> Options(string locator) =>
        new DbContextOptionsBuilder<ApprovalDbContext>().UseNpgsql(locator,
            x => x.MigrationsHistoryTable(ApprovalDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    private static void Validate(string locator, string database, string role)
    {
        NpgsqlConnectionStringBuilder value;
        try { value = new(locator); }
        catch (Exception exception) when (exception is ArgumentException or NullReferenceException)
        { throw new InvalidOperationException("Invalid private approval database locator."); }
        if (value.Host != "127.0.0.1" || value.Port != 5432 || value.Database != database || value.Username != role)
            throw new InvalidOperationException("Unapproved approval database target or role.");
    }
}
