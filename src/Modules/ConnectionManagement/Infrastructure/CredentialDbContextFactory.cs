using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kordra.Modules.ConnectionManagement.Infrastructure;

/// <summary>Design-time factory and explicit local connection-role boundaries for SEC persistence.</summary>
public sealed class CredentialDbContextFactory : IDesignTimeDbContextFactory<CredentialDbContext>
{
    /// <summary>ADR-0025 explicit test selection only; never opens/provisions or grants operation rights.</summary>
    public static DbContextOptions<CredentialDbContext> CreateDisposableTestOptions(string locator, DisposableTestBinding profile)
    {
        ValidateDisposableTestTarget(locator, profile);
        return CreateOptions(locator);
    }

    /// <summary>The caller owns and explicitly enlists this exact disposable test connection.</summary>
    public static DbContextOptions<CredentialDbContext> CreateDisposableEnlistedTestOptions(System.Data.Common.DbConnection connection,
        DisposableTestBinding profile)
    {
        if (connection is not NpgsqlConnection) throw new InvalidOperationException("Explicit disposable PostgreSQL test connection required.");
        ValidateDisposableTestTarget(connection.ConnectionString, profile);
        return new DbContextOptionsBuilder<CredentialDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(CredentialDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
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

    private const string DesignTimeConnectionString =
        "Host=127.0.0.1;Port=5432;Database=kordra_design_time;Username=kordra_design_time;Timeout=1";

    public CredentialDbContext CreateDbContext(string[] args) =>
        new(CreateOptions(DesignTimeConnectionString));

    public static DbContextOptions<CredentialDbContext> CreateRuntimeOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra", "kordra_app");
        return CreateOptions(connectionString);
    }

    public static DbContextOptions<CredentialDbContext> CreateOfflineMigrationOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra", "kordra_admin");
        return CreateOptions(connectionString);
    }

    public static DbContextOptions<CredentialDbContext> CreateTestOptions(string connectionString)
    {
        ValidateTarget(connectionString, "kordra_test", "kordra_test");
        return CreateOptions(connectionString);
    }

    public static void ValidateRuntimeTarget(string connectionString) =>
        ValidateTarget(connectionString, "kordra", "kordra_app");

    public static void ValidateOfflineMigrationTarget(string connectionString) =>
        ValidateTarget(connectionString, "kordra", "kordra_admin");

    private static DbContextOptions<CredentialDbContext> CreateOptions(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CredentialDbContext>();
        options.UseNpgsql(connectionString, postgres =>
            postgres.MigrationsHistoryTable(
                CredentialDbContext.HistoryTable,
                "app")
            .CommandTimeout(2));
        return options.Options;
    }

    public static DbContextOptions<CredentialDbContext> CreateOfflineEnlistedOptions(System.Data.Common.DbConnection connection)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        ValidateTarget(connection.ConnectionString, parsed.Database == "kordra_test" ? "kordra_test" : "kordra", parsed.Database == "kordra_test" ? "kordra_test" : "kordra_admin");
        return new DbContextOptionsBuilder<CredentialDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(CredentialDbContext.HistoryTable, "app").CommandTimeout(2)).Options;
    }

    private static void ValidateTarget(string connectionString, string expectedDatabase, string expectedUser)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A private SEC database connection locator is required.");
        }

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The SEC database connection locator is invalid; its value was not displayed.");
        }

        if (!string.Equals(parsed.Host, "127.0.0.1", StringComparison.Ordinal) ||
            parsed.Port != 5432 ||
            !string.Equals(parsed.Database, expectedDatabase, StringComparison.Ordinal) ||
            !string.Equals(parsed.Username, expectedUser, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The SEC database connection target or role is not permitted.");
        }
    }
}
