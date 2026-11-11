using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kordra.Modules.AuditCompliance.Infrastructure;

/// <summary>Design-time factory and exact local database-role boundaries for AUD persistence.</summary>
public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    /// <summary>ADR-0025 explicit test selection only; never opens/provisions or grants operation rights.</summary>
    public static DbContextOptions<AuditDbContext> CreateDisposableTestOptions(string locator, DisposableTestBinding profile)
    {
        ValidateDisposableTestTarget(locator, profile);
        return CreateOptions(locator);
    }

    /// <summary>Explicit pool-exhaustion regression seam. Keeps the exact selected target and a single-connection pool;
    /// the ordinary disposable/unpooled and all runtime/default guards remain unchanged.</summary>
    public static DbContextOptions<AuditDbContext> CreateDisposableSingleConnectionPoolTestOptions(string locator, DisposableTestBinding profile)
    {
        ValidateDisposableSingleConnectionPoolTestTarget(locator, profile);
        return CreateOptions(locator); // Keep the actual pooled locator.
    }

    private static void ValidateDisposableSingleConnectionPoolTestTarget(string locator, DisposableTestBinding profile)
    {
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new(locator); }
        catch (ArgumentException) { throw InvalidDisposableTarget(); }
        if (!parsed.Pooling || parsed.MaxPoolSize != 1 || parsed.MinPoolSize != 0 ||
            parsed.Timeout is < 1 or > 15 || parsed.CommandTimeout is < 1 or > 30) throw InvalidDisposableTarget();
        // Validate the original target/session policy, excluding only these separately constrained pool settings.
        var selection = new NpgsqlConnectionStringBuilder(locator) { Pooling = false };
        selection.Remove("Maximum Pool Size"); selection.Remove("Minimum Pool Size");
        ValidateDisposableTestTarget(selection.ConnectionString, profile);
    }

    internal static void ValidateDisposableAppendTestTarget(System.Data.Common.DbConnection connection,
        DisposableTestBinding profile)
    {
        if (connection is not NpgsqlConnection) throw InvalidDisposableTarget();
        var locator = connection.ConnectionString;
        if (new NpgsqlConnectionStringBuilder(locator).Pooling)
            ValidateDisposableSingleConnectionPoolTestTarget(locator, profile);
        else
            ValidateDisposableTestTarget(locator, profile);
    }

    internal static DbContextOptions<AuditDbContext> CreateDisposableAppendTestEnlistedOptions(
        System.Data.Common.DbConnection connection, DisposableTestBinding profile)
    {
        ValidateDisposableAppendTestTarget(connection, profile);
        return CreateEnlistedOptions(connection);
    }

    /// <summary>The caller owns and explicitly enlists this exact disposable test connection.</summary>
    public static DbContextOptions<AuditDbContext> CreateDisposableEnlistedTestOptions(System.Data.Common.DbConnection connection,
        DisposableTestBinding profile)
    {
        if (connection is not NpgsqlConnection) throw new InvalidOperationException("Explicit disposable PostgreSQL test connection required.");
        ValidateDisposableTestTarget(connection.ConnectionString, profile);
        return CreateEnlistedOptions(connection);
    }

    private static DbContextOptions<AuditDbContext> CreateEnlistedOptions(System.Data.Common.DbConnection connection) =>
        new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(AuditDbContext.MigrationHistoryTableName, "app").CommandTimeout(2)).Options;

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

    /// <summary>Explicit disposable database plus a separate isolated schema; never opens/provisions.</summary>
    public static AuditDbContext CreateDisposableIsolatedTestContext(System.Data.Common.DbConnection connection, Guid caseId,
        DisposableTestBinding profile)
    {
        if (connection is not NpgsqlConnection || caseId == Guid.Empty)
            throw new InvalidOperationException("Explicit disposable AUD connection/case required.");
        ValidateDisposableTestTarget(connection.ConnectionString, profile);
        var schema = "t12_" + caseId.ToString("N");
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(AuditDbContext.MigrationHistoryTableName, schema))
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, IsolatedModelKey>();
        return new AuditDbContext(options.Options, schema);
    }

    /// <summary>ADR-0020 isolated test seam; never opens a connection or provisions a schema.</summary>
    public static AuditDbContext CreateIsolatedTestContext(System.Data.Common.DbConnection connection, Guid caseId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (caseId == Guid.Empty) throw new InvalidOperationException("Explicit isolated AUD case required.");
        ValidateTestTarget(connection.ConnectionString);
        var parsed = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (!string.IsNullOrEmpty(parsed.SearchPath) || !string.IsNullOrEmpty(parsed.Options))
            throw new InvalidOperationException("Isolated AUD does not permit session overrides.");
        var schema = "t12_" + caseId.ToString("N");
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connection,
            postgres => postgres.MigrationsHistoryTable(AuditDbContext.MigrationHistoryTableName, schema))
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, IsolatedModelKey>();
        return new AuditDbContext(options.Options, schema);
    }

    public sealed class IsolatedModelKey : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), ((AuditDbContext)context).ModelSchema, designTime);
    }

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
    public static DbContextOptions<AuditDbContext> CreateDisposableOfflineReceiptReadOptions(System.Data.Common.DbConnection connection,
        Kordra.Modules.AuditCompliance.Contracts.IOfflineAuditLease custody, DisposableTestBinding profile)
    {
        ArgumentNullException.ThrowIfNull(custody);
        custody.RequireHeld();
        return CreateDisposableEnlistedTestOptions(connection, profile);
    }

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
