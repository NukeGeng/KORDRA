using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Kordra.Api;
using Kordra.DbMigrator;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using System.Diagnostics;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Kordra.Modules.AuditCompliance.Tests;

public sealed class AuditCompliancePersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Append_deadline_includes_stalled_disposal_and_late_commit_is_resolved_by_lookup(bool denial)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(CreateSingleConnectionPoolString())
        {
            ApplicationName = $"audit-cleanup-{Guid.NewGuid():N}"
        }.ConnectionString;
        await MigrateAsync(connectionString);
        Assert.Equal(AuditAppendStatus.Durable,
            (await CreateStore(connectionString).AppendAsync(AuditIntentCanonicalizerTests.CreateIntent())).Status);
        var cleanup = new DelayedDisposalInterceptor();
        var options = new DbContextOptionsBuilder<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(connectionString))
            .AddInterceptors(cleanup).Options;
        var store = new AuditAppendStore(new AuditTestContextFactory(options));
        var intent = AuditIntentCanonicalizerTests.CreateIntent();
        if (denial)
        {
            intent = intent with { Stage = AuditIntentStage.Outcome, ActionOutcome = AuditActionOutcome.Denied };
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await store.AppendAsync(intent).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            stopwatch.Stop();
            await cleanup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(AuditAppendStatus.Unknown, result.Status);
            Assert.Null(result.Receipt);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(denial ? 500 : 1500));
        }
        finally
        {
            cleanup.Release.TrySetResult();
        }

        // The timed-out operation retained its resources until cleanup; no concurrent early pool return.
        await using var probe = new NpgsqlConnection(connectionString);
        using var probeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await probe.OpenAsync(probeDeadline.Token);
        await probe.CloseAsync();
        var lookup = await CreateStore(connectionString).LookupAsync(intent.IntentId);
        Assert.Equal(AuditLookupStatus.Found, lookup.Status);
        Assert.NotNull(lookup.Receipt);
    }

    private sealed class DelayedDisposalInterceptor : DbConnectionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> ConnectionDisposingAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            Entered.TrySetResult();
            await Release.Task; // Intentionally model a provider cleanup that does not honor cancellation.
            return result;
        }
    }

    [Fact]
    public void Contracts_are_BCL_only_and_expose_only_append_and_exact_lookup()
    {
        var assembly = typeof(IAuditAppendPort).Assembly;
        var references = assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();
        var methods = typeof(IAuditAppendPort).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
        Assert.DoesNotContain("Npgsql", references);
        Assert.Equal(["AppendAsync", "LookupAsync"], methods);
        Assert.Equal(
            ["IntentHash", "IntentId", "RecordId", "RecordedAtUtc"],
            typeof(AuditReceipt).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Default_API_profile_keeps_AUD_persistence_inactive_and_offline_migrator_separate()
    {
        var references = typeof(Program).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Kordra.Modules.AuditCompliance", references);
        Assert.Contains("Kordra.Modules.AuditCompliance.Contracts", references);
        Assert.DoesNotContain("Kordra.DbMigrator", references);

        await using var app = LocalApiHost.Create(port: 0);
        Assert.Null(app.Services.GetService<IAuditAppendPort>());
        Assert.Null(app.Services.GetService<IDbContextFactory<AuditDbContext>>());
    }

    [Fact]
    public void Offline_migrator_is_separate_and_references_the_AUD_owner()
    {
        var references = typeof(AuditComplianceMigrationCommand).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Kordra.Modules.AuditCompliance", references);
        Assert.DoesNotContain("Kordra.Api", references);
    }

    [Fact]
    public void Context_model_contains_only_the_owner_audit_table_and_nonpayload_columns()
    {
        var options = AuditDbContextFactory.CreateTestOptions(
            "Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_test");
        using var dbContext = new AuditDbContext(options);
        var entry = Assert.Single(dbContext.Model.GetEntityTypes());

        Assert.Equal("app", entry.GetSchema());
        Assert.Equal("aud_intents", entry.GetTableName());
        Assert.DoesNotContain(entry.GetProperties(), property =>
            property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entry.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(AuditEntry.IntentId)]));
        Assert.All(entry.GetProperties().Where(property => property.Name.EndsWith("Ref", StringComparison.Ordinal)),
            property => Assert.True(property.IsNullable));
    }

    [Theory]
    [InlineData("localhost", 5432, "kordra_test", "kordra_test")]
    [InlineData("127.0.0.1", 5433, "kordra_test", "kordra_test")]
    [InlineData("127.0.0.1", 5432, "kordra", "kordra_test")]
    [InlineData("127.0.0.1", 5432, "kordra_test", "kordra_app")]
    [InlineData("192.0.2.1", 5432, "kordra_test", "kordra_test")]
    public void Test_database_guard_rejects_every_nonexact_target(
        string host,
        int port,
        string database,
        string username)
    {
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username
        }.ConnectionString;

        Assert.Throws<InvalidOperationException>(() => TestDatabaseConfiguration.ValidateTarget(connectionString));
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateTestOptions(connectionString));
    }

    [Fact]
    public void Runtime_and_migration_factories_reject_each_others_database_roles()
    {
        var app = "Host=127.0.0.1;Port=5432;Database=kordra;Username=kordra_app";
        var admin = "Host=127.0.0.1;Port=5432;Database=kordra;Username=kordra_admin";

        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateRuntimeOptions(admin));
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateOfflineMigrationOptions(app));
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateOfflineMigrationOptions(
            "Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_test"));
    }

    [Fact]
    public async Task Migration_append_replay_conflict_and_exact_lookup_are_durable_in_isolated_test_database()
    {
        var connectionString = TestDatabaseConfiguration.RequireConnectionString();
        var store = CreateStore(connectionString);
        await MigrateAsync(connectionString);

        var intent = AuditIntentCanonicalizerTests.CreateIntent();
        var first = await store.AppendAsync(intent);
        var replay = await store.AppendAsync(intent);
        var conflict = await store.AppendAsync(intent with { SafeReason = "changed-content" });

        Assert.Equal(AuditAppendStatus.Durable, first.Status);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Equal(AuditAppendStatus.Durable, replay.Status);
        Assert.Equal(AuditAppendStatus.Rejected, conflict.Status);
        Assert.Equal("intent_conflict", conflict.ReasonCode);

        var lookup = await store.LookupAsync(intent.IntentId);
        Assert.Equal(AuditLookupStatus.Found, lookup.Status);
        Assert.Equal(first.Receipt, lookup.Receipt);
    }

    [Fact]
    public async Task Concurrent_duplicate_and_changed_content_appends_converge_on_one_intent()
    {
        var connectionString = TestDatabaseConfiguration.RequireConnectionString();
        var store = CreateStore(connectionString);
        await MigrateAsync(connectionString);
        var intent = AuditIntentCanonicalizerTests.CreateIntent();

        var identical = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => store.AppendAsync(intent).AsTask()));
        var concurrentConflict = AuditIntentCanonicalizerTests.CreateIntent();
        var conflicting = await Task.WhenAll(
            store.AppendAsync(concurrentConflict with { SafeReason = "changed-content-a" }).AsTask(),
            store.AppendAsync(concurrentConflict with { SafeReason = "changed-content-b" }).AsTask());

        Assert.All(identical, outcome => Assert.Equal(AuditAppendStatus.Durable, outcome.Status));
        Assert.All(identical, outcome => Assert.Equal(identical[0].Receipt, outcome.Receipt));
        Assert.Single(conflicting, outcome => outcome.Status == AuditAppendStatus.Durable);
        Assert.Single(conflicting, outcome => outcome.Status == AuditAppendStatus.Rejected);
        Assert.All(conflicting.Where(outcome => outcome.Status == AuditAppendStatus.Rejected),
            outcome => Assert.Equal("intent_conflict", outcome.ReasonCode));
    }

    [Fact]
    public async Task Exact_lookup_recovers_a_lost_caller_ack_without_claiming_action_completion()
    {
        var connectionString = TestDatabaseConfiguration.RequireConnectionString();
        var store = CreateStore(connectionString);
        await MigrateAsync(connectionString);
        var intent = AuditIntentCanonicalizerTests.CreateIntent() with
        {
            Stage = AuditIntentStage.Outcome,
            ActionOutcome = AuditActionOutcome.Completed
        };

        _ = await store.AppendAsync(intent); // Model the caller losing the successful append response.
        var recovered = await store.LookupAsync(intent.IntentId);

        Assert.Equal(AuditLookupStatus.Found, recovered.Status);
        Assert.NotNull(recovered.Receipt);
        Assert.Equal(intent.IntentId, recovered.Receipt.IntentId);
        Assert.DoesNotContain(typeof(AuditReceipt).GetProperties(), property =>
            property.Name.Contains("Completed", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Outcome", StringComparison.OrdinalIgnoreCase));

        await using var dbContext = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(connectionString));
        var persisted = await dbContext.Entries.AsNoTracking().SingleAsync(value => value.IntentId == intent.IntentId);
        Assert.Contains("\"actionOutcome\":\"completed\"", persisted.CanonicalIntent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disclosure_persists_only_the_result_digest_and_size()
    {
        const string payload = "synthetic-payload-marker-never-store-this-result";
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var intent = AuditIntentCanonicalizerTests.CreateIntent() with
        {
            Stage = AuditIntentStage.Disclosure,
            ResultDigest = digest,
            ResultSizeBytes = bytes.Length
        };
        var connectionString = TestDatabaseConfiguration.RequireConnectionString();
        var store = CreateStore(connectionString);
        await MigrateAsync(connectionString);

        var outcome = await store.AppendAsync(intent);
        await using var dbContext = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(connectionString));
        var persisted = await dbContext.Entries.AsNoTracking().SingleAsync(value => value.IntentId == intent.IntentId);

        Assert.Equal(AuditAppendStatus.Durable, outcome.Status);
        Assert.Equal(digest, persisted.ResultDigest);
        Assert.Equal(bytes.Length, persisted.ResultSizeBytes);
        Assert.DoesNotContain(payload, persisted.CanonicalIntent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migration_grants_app_only_select_and_insert_on_audit_rows()
    {
        var connectionString = TestDatabaseConfiguration.RequireConnectionString();
        await MigrateAsync(connectionString);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            {
                await readOnly.ExecuteNonQueryAsync();
            }

            const string query = """
                SELECT current_database(), current_user,
                       has_table_privilege('kordra_app', 'app.aud_intents', 'SELECT'),
                       has_table_privilege('kordra_app', 'app.aud_intents', 'INSERT'),
                       has_table_privilege('kordra_app', 'app.aud_intents', 'UPDATE'),
                       has_table_privilege('kordra_app', 'app.aud_intents', 'DELETE'),
                       has_table_privilege('kordra_app', 'app.aud_intents', 'TRUNCATE')
                """;
            await using var command = new NpgsqlCommand(query, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("kordra_test", reader.GetString(0));
            Assert.Equal("kordra_test", reader.GetString(1));
            Assert.True(reader.GetBoolean(2));
            Assert.True(reader.GetBoolean(3));
            Assert.False(reader.GetBoolean(4));
            Assert.False(reader.GetBoolean(5));
            Assert.False(reader.GetBoolean(6));
            Assert.False(await reader.ReadAsync());
        }
    }

    [Fact]
    public async Task Denial_append_is_best_effort_and_bounded_when_audit_table_is_saturated()
    {
        var connectionString = TestDatabaseConfiguration.RequireConnectionString();
        await MigrateAsync(connectionString);
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync();
        await using var transaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand(
                         "LOCK TABLE app.aud_intents IN ACCESS EXCLUSIVE MODE", lockConnection, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        var intent = AuditIntentCanonicalizerTests.CreateIntent() with
        {
            Stage = AuditIntentStage.Outcome,
            ActionOutcome = AuditActionOutcome.Denied,
            SafeReason = "denied"
        };
        var stopwatch = Stopwatch.StartNew();
        var result = await CreateStore(connectionString).AppendAsync(intent);
        stopwatch.Stop();

        await transaction.RollbackAsync();
        Assert.Equal(AuditAppendStatus.Unavailable, result.Status);
        Assert.Null(result.Receipt);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
            $"Denied audit append exceeded the 100 ms budget by returning after {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
        var lookup = await CreateStore(connectionString).LookupAsync(intent.IntentId);
        Assert.Equal(AuditLookupStatus.NotFound, lookup.Status);
    }

    [Fact]
    public async Task Denial_append_is_bounded_when_the_connection_pool_is_saturated()
    {
        var connectionString = CreateSingleConnectionPoolString();
        await MigrateAsync(connectionString);
        await using var heldConnection = new NpgsqlConnection(connectionString);
        await heldConnection.OpenAsync();

        var intent = AuditIntentCanonicalizerTests.CreateIntent() with
        {
            Stage = AuditIntentStage.Outcome,
            ActionOutcome = AuditActionOutcome.Denied,
            SafeReason = "denied"
        };
        var stopwatch = Stopwatch.StartNew();
        var result = await CreateStore(connectionString).AppendAsync(intent);
        stopwatch.Stop();

        Assert.Equal(AuditAppendStatus.Unavailable, result.Status);
        Assert.Null(result.Receipt);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
            $"Denial append exceeded its 100 ms budget while awaiting a pooled connection: {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Theory]
    [InlineData(AuditIntentStage.Use)]
    [InlineData(AuditIntentStage.Disclosure)]
    public async Task Required_use_or_disclosure_append_fails_closed_when_audit_table_is_saturated(
        AuditIntentStage stage)
    {
        var connectionString = TestDatabaseConfiguration.RequireConnectionString();
        await MigrateAsync(connectionString);
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync();
        await using var transaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand(
                         "LOCK TABLE app.aud_intents IN ACCESS EXCLUSIVE MODE", lockConnection, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        var intent = AuditIntentCanonicalizerTests.CreateIntent() with { Stage = stage };
        if (stage == AuditIntentStage.Disclosure)
        {
            intent = intent with { ResultDigest = new string('a', 64), ResultSizeBytes = 12 };
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await CreateStore(connectionString).AppendAsync(intent);
        stopwatch.Stop();
        await transaction.RollbackAsync();

        Assert.Equal(AuditAppendStatus.Unavailable, result.Status);
        Assert.Null(result.Receipt);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(1500),
            $"Required {stage} audit append exceeded its one-second budget: {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
        var lookup = await CreateStore(connectionString).LookupAsync(intent.IntentId);
        Assert.Equal(AuditLookupStatus.NotFound, lookup.Status);
    }

    [Theory]
    [InlineData(AuditIntentStage.Use)]
    [InlineData(AuditIntentStage.Disclosure)]
    public async Task Required_append_is_bounded_when_the_connection_pool_is_saturated(
        AuditIntentStage stage)
    {
        var connectionString = CreateSingleConnectionPoolString();
        await MigrateAsync(connectionString);
        await using var heldConnection = new NpgsqlConnection(connectionString);
        await heldConnection.OpenAsync();

        var intent = AuditIntentCanonicalizerTests.CreateIntent() with { Stage = stage };
        if (stage == AuditIntentStage.Disclosure)
        {
            intent = intent with { ResultDigest = new string('b', 64), ResultSizeBytes = 16 };
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await CreateStore(connectionString).AppendAsync(intent);
        stopwatch.Stop();

        Assert.Equal(AuditAppendStatus.Unavailable, result.Status);
        Assert.Null(result.Receipt);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(1500),
            $"Required {stage} append exceeded its one-second budget while awaiting a pooled connection: {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    }

    internal static async Task MigrateAsync(string connectionString)
    {
        await using var dbContext = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(connectionString));
        await dbContext.Database.MigrateAsync();
    }

    internal static AuditAppendStore CreateStore(string connectionString)
    {
        var options = AuditDbContextFactory.CreateTestOptions(connectionString);
        return new AuditAppendStore(new AuditTestContextFactory(options));
    }

    private static string CreateSingleConnectionPoolString() =>
        new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.RequireConnectionString())
        {
            MaxPoolSize = 1,
            Timeout = 15
        }.ConnectionString;

    private sealed class AuditTestContextFactory(DbContextOptions<AuditDbContext> options)
        : IDbContextFactory<AuditDbContext>
    {
        public AuditDbContext CreateDbContext() => new(options);

        public Task<AuditDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
