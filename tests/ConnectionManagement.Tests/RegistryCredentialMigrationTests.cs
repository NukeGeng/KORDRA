using System.Text.RegularExpressions;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Kordra.Credentials.Tests;

public sealed class RegistryCredentialMigrationTests
{
    private const string Previous = "20261024043626_AddCredentialReceiptEvidence";
    private const string Current = "20261025140200_RegistryCredentialAllocation";

    [Theory]
    [InlineData("success")]
    [InlineData("partial-failure")]
    [InlineData("conflicting-history")]
    public async Task Nonempty_legacy_backfill_preserves_rows_and_partial_failure_rolls_back(string mode)
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private isolated kordra_test locator required.");
        // The owner factory rejects any database/user other than the isolated test target.
        await using var db = new CredentialDbContext(CredentialDbContextFactory.CreateTestOptions(locator));
        var migrator = db.GetService<IMigrator>();
        var schema = "t10_sec_" + Guid.NewGuid().ToString("N");
        string Isolate(string sql) => Regex.Replace(sql, @"\bapp\b", schema, RegexOptions.CultureInvariant);
        var oldSql = Isolate(migrator.GenerateScript("0", Previous, MigrationsSqlGenerationOptions.NoTransactions));
        var newSql = Isolate(migrator.GenerateScript(Previous, Current, MigrationsSqlGenerationOptions.NoTransactions));
        await using var connection = new NpgsqlConnection(locator);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // All DDL/data, including the unique schema, roll back when this fixture ends.
        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
        async Task<object?> Scalar(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            return await command.ExecuteScalarAsync();
        }
        await Execute(oldSql);
        var installation = Guid.NewGuid(); var tenant = Guid.NewGuid(); var profile = Guid.NewGuid();
        var reference = Guid.NewGuid(); var target = Guid.NewGuid(); var binding = Guid.NewGuid();
        // All interpolated identifiers/values are fixture-generated GUIDs or a fixed string.
        await Execute($"""
            INSERT INTO {schema}.sec_connection_profiles VALUES
              ('{installation}','{tenant}','{profile}','{target}','inventory.readonly.v1','target.read','macos-keychain-file-v1','opaque-bytes-v1');
            INSERT INTO {schema}.sec_secret_references VALUES
              ('{installation}','{tenant}','{reference}','{profile}','{Guid.NewGuid()}');
            INSERT INTO {schema}.sec_binding_revisions VALUES
              ('{installation}','{tenant}','{binding}',1,'{target}','inventory.readonly.v1','target.read','{profile}','{reference}',
               'active','2026-10-25T00:00:00Z','2026-10-26T00:00:00Z','{Guid.NewGuid()}');
            INSERT INTO {schema}.sec_binding_heads VALUES
              ('{installation}','{tenant}','{target}','inventory.readonly.v1','target.read','{binding}',1);
            """);
        string LegacyRows() => $"""
            SELECT jsonb_build_array(
              (SELECT jsonb_agg(to_jsonb(r)) FROM {schema}.sec_connection_profiles r),
              (SELECT jsonb_agg(to_jsonb(r)) FROM {schema}.sec_secret_references r),
              (SELECT jsonb_agg(to_jsonb(r)) FROM {schema}.sec_binding_revisions r),
              (SELECT jsonb_agg(to_jsonb(r)) FROM {schema}.sec_binding_heads r))::text
            """;
        if (mode == "conflicting-history")
        {
            // Simulate an inconsistent imported history only in this new transactional schema.
            // Never disable a constraint on the shared SEC tables or a real installation.
            var otherTenant = Guid.NewGuid(); var otherProfile = Guid.NewGuid();
            await Execute($"""
                DROP INDEX {schema}."IX_sec_secret_references_installation_id_reference_id";
                INSERT INTO {schema}.sec_connection_profiles VALUES
                  ('{installation}','{otherTenant}','{otherProfile}','{target}','inventory.readonly.v1','target.read','macos-keychain-file-v1','opaque-bytes-v1');
                INSERT INTO {schema}.sec_secret_references VALUES
                  ('{installation}','{otherTenant}','{reference}','{otherProfile}','{Guid.NewGuid()}');
                """);
        }
        var before = await Scalar(LegacyRows());
        await transaction.SaveAsync("before_allocation");
        if (mode == "conflicting-history")
        {
            var conflict = await Assert.ThrowsAsync<PostgresException>(() => Execute(newSql));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, conflict.SqlState);
            await transaction.RollbackAsync("before_allocation");
            Assert.Equal(DBNull.Value, await Scalar($"SELECT to_regclass('{schema}.sec_native_reference_allocations')::text"));
            Assert.Equal(before, await Scalar(LegacyRows()));
            await transaction.RollbackAsync();
            await using var cleanupCheck = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname = @schema", connection);
            cleanupCheck.Parameters.AddWithValue("schema", schema);
            Assert.Equal(0L, await cleanupCheck.ExecuteScalarAsync());
            return;
        }
        await Execute(newSql);
        Assert.Equal(before, await Scalar(LegacyRows()));
        Assert.True((bool)(await Scalar($"""
            SELECT owner_kind = 'inventory' AND tenant_id = '{tenant}' AND inventory_profile_id = '{profile}' AND registry_profile_id IS NULL
            FROM {schema}.sec_native_reference_allocations WHERE installation_id = '{installation}' AND reference_id = '{reference}'
            """))!);
        Assert.True((bool)(await Scalar($"""
            SELECT has_table_privilege('kordra_app','{schema}.sec_registry_binding_heads','SELECT')
              AND NOT has_table_privilege('kordra_app','{schema}.sec_registry_binding_heads','UPDATE')
              AND NOT has_table_privilege('kordra_app','{schema}.sec_registry_command_receipts','SELECT')
            """))!);
        if (mode == "partial-failure")
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => Execute("SELECT 1 / 0"));
            Assert.Equal(PostgresErrorCodes.DivisionByZero, error.SqlState);
            await transaction.RollbackAsync("before_allocation");
            Assert.Equal(DBNull.Value, await Scalar($"SELECT to_regclass('{schema}.sec_native_reference_allocations')::text"));
            Assert.Equal(before, await Scalar(LegacyRows()));
            // A bounded explicit rerun is safe after the confirmed transactional rollback.
            await Execute(newSql);
            Assert.Equal(1L, await Scalar($"SELECT count(*) FROM {schema}.sec_native_reference_allocations"));
        }
        Assert.Throws<NotSupportedException>(() => migrator.GenerateScript(Current, Previous));
        await transaction.RollbackAsync();
        await using var check = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname = @schema", connection);
        check.Parameters.AddWithValue("schema", schema);
        Assert.Equal(0L, await check.ExecuteScalarAsync());
    }
}
