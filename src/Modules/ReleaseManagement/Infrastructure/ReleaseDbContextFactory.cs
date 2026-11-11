using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Data.Common;
using Kordra.Modules.ReleaseManagement.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kordra.Modules.ReleaseManagement.Infrastructure;

public sealed class ReleaseDbContextFactory : IDesignTimeDbContextFactory<ReleaseDbContext>
{
    /// <summary>ADR-0025 explicit test selection only; never opens/provisions or grants operation rights.</summary>
    public static DbContextOptions<ReleaseDbContext> CreateDisposableTestOptions(string locator, DisposableTestBinding profile)
    {
        ValidateDisposableTestTarget(locator, profile);
        return Options(locator);
    }

    /// <summary>The caller owns and explicitly enlists this exact disposable test connection.</summary>
    public static DbContextOptions<ReleaseDbContext> CreateDisposableEnlistedTestOptions(System.Data.Common.DbConnection connection,
        DisposableTestBinding profile)
    {
        if (connection is not NpgsqlConnection) throw new InvalidOperationException("Explicit disposable PostgreSQL test connection required.");
        ValidateDisposableTestTarget(connection.ConnectionString, profile);
        return new DbContextOptionsBuilder<ReleaseDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(ReleaseDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    }

    // The runner/profile loader verifies expected source/manifest and separate approval. This projection grants no authority.
    public static void ValidateDisposableTestTarget(string locator, DisposableTestBinding profile)
    {
        var now = DateTimeOffset.UtcNow;
        if (profile.RunId == Guid.Empty || !Hash(profile.SourceSha256) || !Hash(profile.OperationManifestSha256) ||
            profile.ExpiresAtUtc.Offset != TimeSpan.Zero || profile.ExpiresAtUtc == DateTimeOffset.MaxValue || profile.ExpiresAtUtc <= now ||
            string.IsNullOrWhiteSpace(locator)) throw InvalidDisposableTarget();
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new(locator); }
        catch (ArgumentException) { throw InvalidDisposableTarget(); }
        string[] keys = ["Host", "Port", "Database", "Username", "Password", "Pooling", "Multiplexing", "Include Error Detail",
            "Timeout", "Command Timeout", "Enlist", "Application Name", "Persist Security Info"];
        if (parsed.Host != "127.0.0.1" || parsed.Port != 5432 || parsed.Username != "kordra_test" ||
            parsed.Database != "kordra_test_" + profile.RunId.ToString("N") || parsed.Pooling || parsed.Multiplexing ||
            parsed.IncludeErrorDetail || parsed.PersistSecurityInfo || !string.IsNullOrEmpty(parsed.SearchPath) || !string.IsNullOrEmpty(parsed.Options) ||
            parsed.Keys.Cast<string>().Any(key => !keys.Contains(key, StringComparer.OrdinalIgnoreCase))) throw InvalidDisposableTarget();
        static bool Hash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
    private static InvalidOperationException InvalidDisposableTarget() => new("Disposable test target/profile is invalid; locator and profile values omitted.");

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
    public static DbContextOptions<ReleaseDbContext> CreateDisposableOfflineReceiptReadOptions(DbConnection connection,
        IReleaseOfflineCustody custody, DisposableTestBinding profile)
    {
        ArgumentNullException.ThrowIfNull(custody);
        custody.RequireHeld();
        return CreateDisposableEnlistedTestOptions(connection, profile);
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
