using System.Data.Common;
using Kordra.Testing;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kordra.Modules.AuditCompliance.Tests;

/// <summary>Fail-closed connection guards for synthetic AUD integration data.</summary>
public static class TestDatabaseConfiguration
{
    public const string ConnectionEnvironmentVariable = "KORDRA_AUDIT_TEST_CONNECTION_STRING";

    public static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Required integration-test configuration is missing: set {ConnectionEnvironmentVariable} using the private kordra_test locator. Database verification cannot be skipped.");
        }

        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        if (profile is null) ValidateTarget(connectionString);
        else profile.ValidateLocator(connectionString, DateTimeOffset.UtcNow);
        return connectionString;
    }

    internal static string ExpectedRunnerDatabase => RegressionPostgreSqlSelection.LoadRunnerProfile()?.Database ?? "kordra_test";
    internal static DbContextOptions<AuditDbContext> OptionsForRunner(string locator)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        if (profile is null) return AuditDbContextFactory.CreateTestOptions(locator);
        var parsed = new NpgsqlConnectionStringBuilder(locator);
        return parsed.Pooling
            ? AuditDbContextFactory.CreateDisposableSingleConnectionPoolTestOptions(locator, profile.OwnerBinding)
            : AuditDbContextFactory.CreateDisposableTestOptions(locator, profile.OwnerBinding);
    }
    internal static Task<AuditTransactionContext> TransactionForRunnerAsync(DbConnection connection, DbTransaction transaction,
        CancellationToken cancellationToken = default, TimeProvider? timeProvider = null)
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        return profile is null
            ? AuditTransactionContext.CreateAsync(connection, transaction, cancellationToken, timeProvider)
            : AuditTransactionContext.CreateDisposableTestAsync(connection, transaction, profile.OwnerBinding, cancellationToken, timeProvider);
    }

    public static void ValidateTarget(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A private AUD test database locator is required.");
        }

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The AUD test database locator is invalid; its value was not displayed.");
        }

        if (!string.Equals(parsed.Host, "127.0.0.1", StringComparison.Ordinal) ||
            parsed.Port != 5432 ||
            !string.Equals(parsed.Database, "kordra_test", StringComparison.Ordinal) ||
            !string.Equals(parsed.Username, "kordra_test", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The AUD database locator does not match the exact loopback kordra_test database and role; no connection was opened.");
        }
    }
}
