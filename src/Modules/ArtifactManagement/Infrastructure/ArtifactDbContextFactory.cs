using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kordra.Modules.ArtifactManagement.Infrastructure;

public sealed class ArtifactDbContextFactory : IDesignTimeDbContextFactory<ArtifactDbContext>
{
    /// <summary>ADR-0025 explicit test selection only; never opens/provisions or grants operation rights.</summary>
    public static DbContextOptions<ArtifactDbContext> CreateDisposableTestOptions(string locator, DisposableTestBinding profile)
    {
        ValidateDisposableTestTarget(locator, profile);
        return Options(locator);
    }

    /// <summary>The caller owns and explicitly enlists this exact disposable test connection.</summary>
    public static DbContextOptions<ArtifactDbContext> CreateDisposableEnlistedTestOptions(System.Data.Common.DbConnection connection,
        DisposableTestBinding profile)
    {
        if (connection is not NpgsqlConnection) throw new InvalidOperationException("Explicit disposable PostgreSQL test connection required.");
        ValidateDisposableTestTarget(connection.ConnectionString, profile);
        return new DbContextOptionsBuilder<ArtifactDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(ArtifactDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
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
