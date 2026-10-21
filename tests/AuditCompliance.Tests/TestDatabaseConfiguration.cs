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

        ValidateTarget(connectionString);
        return connectionString;
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
