using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Npgsql;
using Xunit;

namespace Kordra.Modules.AuditCompliance.Tests;

public sealed class AuditTransactionEnlistmentTests
{
    [Fact]
    public async Task Commit_failure_returns_unknown_and_exact_lookup_does_not_invent_a_receipt()
    {
        var connectionString = TestDatabaseConfiguration.RequireConnectionString();
        await AuditCompliancePersistenceTests.MigrateAsync(connectionString);
        var suffix = Guid.NewGuid().ToString("N");
        var functionName = $"test_aud_commit_failure_{suffix}";
        var triggerName = $"test_aud_commit_failure_{suffix}";
        var intent = AuditIntentCanonicalizerTests.CreateIntent();

        try
        {
            await InstallDeferredCommitFailureAsync(connectionString, functionName, triggerName, intent.IntentId);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var audit = await AuditTransactionContext.CreateAsync(connection, transaction);
            var staged = await audit.StageAsync(intent);
            Assert.Equal(AuditTransactionStageStatus.Staged, staged.Status);

            var committed = await audit.CommitAsync();
            Assert.Equal(AuditAppendStatus.Unknown, committed.Status);
            Assert.Null(committed.Receipt);

            var lookup = await AuditCompliancePersistenceTests.CreateStore(connectionString)
                .LookupAsync(intent.IntentId);
            Assert.Equal(AuditLookupStatus.NotFound, lookup.Status);
            Assert.Null(lookup.Receipt);
        }
        finally
        {
            await RemoveDeferredCommitFailureAsync(connectionString, functionName, triggerName);
        }
    }

    [Fact]
    public async Task Owner_state_and_audit_commit_together_in_one_postgresql_transaction()
    {
        var connectionString = TestDatabaseConfiguration.RequireConnectionString();
        await AuditCompliancePersistenceTests.MigrateAsync(connectionString);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand(
                         "CREATE TEMP TABLE owner_state_probe (id uuid PRIMARY KEY) ON COMMIT PRESERVE ROWS",
                         connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        var intent = AuditIntentCanonicalizerTests.CreateIntent();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var ownerWrite = new NpgsqlCommand(
                         "INSERT INTO owner_state_probe (id) VALUES (@id)", connection, transaction))
        {
            ownerWrite.Parameters.AddWithValue("id", Guid.NewGuid());
            await ownerWrite.ExecuteNonQueryAsync();
        }

        await using var audit = await AuditTransactionContext.CreateAsync(connection, transaction);
        var staged = await audit.StageAsync(intent);
        Assert.Equal(AuditTransactionStageStatus.Staged, staged.Status);
        Assert.Null(staged.Receipt);

        var committed = await audit.CommitAsync();

        Assert.Equal(AuditAppendStatus.Durable, committed.Status);
        Assert.NotNull(committed.Receipt);
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM owner_state_probe", connection))
        {
            Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
        }

        var lookup = await AuditCompliancePersistenceTests.CreateStore(connectionString).LookupAsync(intent.IntentId);
        Assert.Equal(AuditLookupStatus.Found, lookup.Status);
        Assert.Equal(committed.Receipt, lookup.Receipt);
    }

    [Fact]
    public async Task Failure_before_commit_rolls_back_owner_state_and_leaves_no_audit_receipt()
    {
        var connectionString = TestDatabaseConfiguration.RequireConnectionString();
        await AuditCompliancePersistenceTests.MigrateAsync(connectionString);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand(
                         "CREATE TEMP TABLE owner_state_probe (id uuid PRIMARY KEY) ON COMMIT PRESERVE ROWS",
                         connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        var intent = AuditIntentCanonicalizerTests.CreateIntent();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var ownerWrite = new NpgsqlCommand(
                         "INSERT INTO owner_state_probe (id) VALUES (@id)", connection, transaction))
        {
            ownerWrite.Parameters.AddWithValue("id", Guid.NewGuid());
            await ownerWrite.ExecuteNonQueryAsync();
        }

        await using var audit = await AuditTransactionContext.CreateAsync(connection, transaction);
        var staged = await audit.StageAsync(intent);
        Assert.Equal(AuditTransactionStageStatus.Staged, staged.Status);
        Assert.Null(staged.Receipt);
        await audit.RollbackAsync();

        await using (var count = new NpgsqlCommand("SELECT count(*) FROM owner_state_probe", connection))
        {
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
        }

        var lookup = await AuditCompliancePersistenceTests.CreateStore(connectionString).LookupAsync(intent.IntentId);
        Assert.Equal(AuditLookupStatus.NotFound, lookup.Status);
        Assert.Null(lookup.Receipt);
    }

    private static async Task InstallDeferredCommitFailureAsync(
        string connectionString,
        string functionName,
        string triggerName,
        Guid intentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var functionSql = $"""
            CREATE FUNCTION app.{functionName}() RETURNS trigger
            LANGUAGE plpgsql AS $failure$
            BEGIN
                IF NEW.intent_id = '{intentId:D}'::uuid THEN
                    RAISE EXCEPTION 'synthetic audit commit failure';
                END IF;
                RETURN NEW;
            END
            $failure$;
            """;
        await using (var function = new NpgsqlCommand(functionSql, connection))
        {
            await function.ExecuteNonQueryAsync();
        }

        var triggerSql = $"""
            CREATE CONSTRAINT TRIGGER {triggerName}
            AFTER INSERT ON app.aud_intents
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION app.{functionName}();
            """;
        await using var trigger = new NpgsqlCommand(triggerSql, connection);
        await trigger.ExecuteNonQueryAsync();
    }

    private static async Task RemoveDeferredCommitFailureAsync(
        string connectionString,
        string functionName,
        string triggerName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var trigger = new NpgsqlCommand(
                         $"DROP TRIGGER IF EXISTS {triggerName} ON app.aud_intents", connection))
        {
            await trigger.ExecuteNonQueryAsync();
        }

        await using var function = new NpgsqlCommand($"DROP FUNCTION IF EXISTS app.{functionName}()", connection);
        await function.ExecuteNonQueryAsync();
    }
}
