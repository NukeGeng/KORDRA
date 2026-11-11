using System.Data.Common;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Testing;
using Npgsql;

namespace Kordra.Modules.IdentityAccess.Tests;

/// <summary>Fail-closed connection guard for synthetic IAM integration data.</summary>
public static class TestDatabaseConfiguration
{
    public const string ConnectionEnvironmentVariable = "KORDRA_IAM_TEST_CONNECTION_STRING";

    internal static string ExpectedFixtureDatabase => RegressionPostgreSqlSelection.LoadRunnerProfile()?.Database ?? "kordra_test";
    internal static void ValidateFixtureLocator(string locator)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        if (profile is { } selected) selected.ValidateLocator(locator, DateTimeOffset.UtcNow);
        else ValidateTarget(locator);
    }
    internal static AuditDbContext IsolatedAuditContext(DbConnection connection, Guid caseId)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is { } p ? AuditDbContextFactory.CreateDisposableIsolatedTestContext(connection, caseId, p.OwnerBinding)
            : AuditDbContextFactory.CreateIsolatedTestContext(connection, caseId);
    }
    internal static Task<AuditTransactionContext> IsolatedAuditTransactionAsync(DbConnection connection, DbTransaction root,
        Guid caseId, CancellationToken ct = default)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is { } p ? AuditTransactionContext.CreateDisposableIsolatedTestAsync(connection, root, caseId, p.OwnerBinding, ct)
            : AuditTransactionContext.CreateIsolatedTestAsync(connection, root, caseId, ct);
    }

    public static string? TryGetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        if (profile is null) ValidateTarget(connectionString);
        else profile.ValidateLocator(connectionString, DateTimeOffset.UtcNow);
        return connectionString;
    }

    public static string? TryGetRuntimeProbeConnectionString()
    {
        const string variable = "KORDRA_IAM_RUNTIME_PROBE_CONNECTION_STRING";
        var connectionString = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        ValidateRuntimeProbeTarget(connectionString);
        return connectionString;
    }

    public static void ValidateTarget(string connectionString)
        => ValidateTarget(connectionString, "kordra_test", "kordra_test");

    public static void ValidateRuntimeProbeTarget(string connectionString)
        => ValidateTarget(connectionString, "kordra", "kordra_app");

    private static void ValidateTarget(string connectionString, string expectedDatabase, string expectedUsername)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A private IAM database locator is required.");
        }

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The IAM test database locator is invalid; its value was not displayed.");
        }

        if (!string.Equals(parsed.Host, "127.0.0.1", StringComparison.Ordinal) ||
            parsed.Port != 5432 ||
            !string.Equals(parsed.Database, expectedDatabase, StringComparison.Ordinal) ||
            !string.Equals(parsed.Username, expectedUsername, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The IAM database locator does not match its exact loopback database/role; no connection was opened.");
        }
    }
}
