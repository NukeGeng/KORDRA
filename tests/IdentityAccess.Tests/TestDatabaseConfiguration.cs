using Npgsql;

namespace Kordra.Modules.IdentityAccess.Tests;

/// <summary>Fail-closed connection guard for synthetic IAM integration data.</summary>
public static class TestDatabaseConfiguration
{
    public const string ConnectionEnvironmentVariable = "KORDRA_IAM_TEST_CONNECTION_STRING";

    public static string? TryGetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        ValidateTarget(connectionString);
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
