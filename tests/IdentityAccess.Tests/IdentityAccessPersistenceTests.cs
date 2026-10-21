using System.Security.Cryptography;
using Kordra.Api;
using Kordra.DbMigrator;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.IdentityAccess.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class IdentityAccessPersistenceTests
{
    [Fact]
    public void Runtime_context_rejects_offline_migration_role()
    {
        var connectionString = CreateConnectionString(database: "kordra", username: "kordra_admin");

        Assert.Throws<InvalidOperationException>(
            () => IdentityAccessDbContextFactory.CreateRuntimeOptions(connectionString));
    }

    [Fact]
    public void Offline_migrator_rejects_runtime_role()
    {
        var connectionString = CreateConnectionString(database: "kordra", username: "kordra_app");

        Assert.Throws<InvalidOperationException>(
            () => IdentityAccessDbContextFactory.CreateOfflineMigrationOptions(connectionString));
    }

    [Theory]
    [InlineData("localhost", 5432, "kordra_test", "kordra_test")]
    [InlineData("127.0.0.1", 5433, "kordra_test", "kordra_test")]
    [InlineData("127.0.0.1", 5432, "kordra", "kordra_test")]
    [InlineData("127.0.0.1", 5432, "kordra_test", "kordra_app")]
    [InlineData("192.0.2.1", 5432, "kordra_test", "kordra_test")]
    public void Test_database_guard_rejects_non_isolated_targets(
        string host,
        int port,
        string database,
        string username)
    {
        var connectionString = CreateConnectionString(host, port, database, username);

        Assert.Throws<InvalidOperationException>(() => TestDatabaseConfiguration.ValidateTarget(connectionString));
    }

    [Theory]
    [InlineData("localhost", 5432, "kordra", "kordra_app")]
    [InlineData("127.0.0.1", 5433, "kordra", "kordra_app")]
    [InlineData("127.0.0.1", 5432, "kordra_test", "kordra_app")]
    [InlineData("127.0.0.1", 5432, "kordra", "kordra_test")]
    [InlineData("192.0.2.1", 5432, "kordra", "kordra_app")]
    public void Runtime_privilege_probe_rejects_non_exact_targets(
        string host,
        int port,
        string database,
        string username)
    {
        var connectionString = CreateConnectionString(host, port, database, username);

        Assert.Throws<InvalidOperationException>(
            () => TestDatabaseConfiguration.ValidateRuntimeProbeTarget(connectionString));
    }

    [Fact]
    public async Task Default_api_bootstrap_keeps_IAM_persistence_and_migration_inactive()
    {
        var references = typeof(Program).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Kordra.DbMigrator", references);

        await using var app = LocalApiHost.Create(port: 0);
        Assert.Null(app.Services.GetService<LocalAuthenticationService>());
        Assert.Null(app.Services.GetService<IDbContextFactory<KordraIdentityDbContext>>());
    }

    [Fact]
    public void Offline_migrator_is_a_separate_host_with_only_IAM_owner_reference()
    {
        var references = typeof(IdentityAccessMigrationCommand).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Kordra.Modules.IdentityAccess", references);
        Assert.DoesNotContain("Kordra.Api", references);
    }

    [Fact]
    public void Model_persists_only_approved_IAM_principal_and_session_columns()
    {
        var options = new DbContextOptionsBuilder<KordraIdentityDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_test")
            .Options;
        using var dbContext = new KordraIdentityDbContext(options);

        var entityTypes = dbContext.Model.GetEntityTypes().ToArray();
        Assert.Equal(
            [typeof(KordraAuthenticationCommand), typeof(KordraIdentityUser), typeof(KordraSession)],
            entityTypes.Select(entity => entity.ClrType).OrderBy(type => type.FullName, StringComparer.Ordinal));

        var command = Assert.Single(entityTypes, entity => entity.ClrType == typeof(KordraAuthenticationCommand));
        Assert.Equal("iam_authentication_commands", command.GetTableName());
        Assert.Equal(
            ["command_id", "command_kind", "created_at_utc", "idempotency_key_hash", "principal_id"],
            GetColumnNames(command));

        var principal = Assert.Single(entityTypes, entity => entity.ClrType == typeof(KordraIdentityUser));
        Assert.Equal("iam_principals", principal.GetTableName());
        Assert.Equal(
            [
                "access_failed_count",
                "account_status",
                "concurrency_stamp",
                "lockout_enabled",
                "lockout_end_utc",
                "normalized_user_name",
                "password_hash",
                "principal_id",
                "revision",
                "security_stamp",
                "user_name"
            ],
            GetColumnNames(principal));

        foreach (var propertyName in new[]
                 {
                     nameof(KordraIdentityUser.Email),
                     nameof(KordraIdentityUser.NormalizedEmail),
                     nameof(KordraIdentityUser.EmailConfirmed),
                     nameof(KordraIdentityUser.PhoneNumber),
                     nameof(KordraIdentityUser.PhoneNumberConfirmed),
                     nameof(KordraIdentityUser.TwoFactorEnabled)
                 })
        {
            Assert.Null(principal.FindProperty(propertyName));
        }

        var session = Assert.Single(entityTypes, entity => entity.ClrType == typeof(KordraSession));
        Assert.Equal("iam_sessions", session.GetTableName());
        Assert.Equal(
            [
                "absolute_expires_at_utc",
                "boot_generation",
                "created_at_utc",
                "is_revoked",
                "last_activity_at_utc",
                "principal_id",
                "principal_revision",
                "revocation_reason",
                "revoked_at_utc",
                "session_id",
                "session_reference_hash"
            ],
            GetColumnNames(session));
        Assert.All(entityTypes, entity => Assert.Equal("app", entity.GetSchema()));
    }

    [Fact]
    public async Task Additive_migration_round_trips_IAM_principal_and_session_only_in_isolated_test_database()
    {
        var connectionString = TestDatabaseConfiguration.TryGetConnectionString();
        if (connectionString is null)
        {
            throw new InvalidOperationException(
                $"Required integration-test configuration is missing: set {TestDatabaseConfiguration.ConnectionEnvironmentVariable} using the private kordra_test locator. Database verification cannot be skipped.");
        }

        var options = new DbContextOptionsBuilder<KordraIdentityDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable(
                KordraIdentityDbContext.MigrationHistoryTableName,
                KordraIdentityDbContext.SchemaName))
            .Options;

        await using var dbContext = new KordraIdentityDbContext(options);
        await dbContext.Database.MigrateAsync();

        var entityTypes = dbContext.Model.GetEntityTypes().ToArray();
        Assert.Equal(
            ["iam_authentication_commands", "iam_principals", "iam_sessions"],
            entityTypes.Select(entity => entity.GetTableName()).Order(StringComparer.Ordinal));
        Assert.All(entityTypes, entity => Assert.Equal("app", entity.GetSchema()));
        Assert.DoesNotContain(
            entityTypes.SelectMany(entity => entity.GetProperties()),
            property => string.Equals(property.Name, "TenantId", StringComparison.OrdinalIgnoreCase));

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var username = $"synthetic-{Guid.NewGuid():N}";
            var principal = new KordraIdentityUser
            {
                UserName = username,
                NormalizedUserName = username.ToUpperInvariant(),
                PasswordHash = "synthetic-test-hash",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                AccountStatus = "active",
                Revision = 1
            };

            var session = new KordraSession
            {
                SessionReferenceHash = RandomNumberGenerator.GetBytes(32),
                PrincipalId = principal.Id,
                PrincipalRevision = principal.Revision,
                BootGeneration = Guid.NewGuid(),
                CreatedAtUtc = now,
                LastActivityAtUtc = now,
                AbsoluteExpiresAtUtc = now.AddHours(8)
            };
            var command = new KordraAuthenticationCommand
            {
                CommandId = Guid.NewGuid(),
                IdempotencyKeyHash = RandomNumberGenerator.GetBytes(32),
                PrincipalId = principal.Id,
                CommandKind = "logout-all",
                CreatedAtUtc = now
            };

            dbContext.Principals.Add(principal);
            dbContext.Sessions.Add(session);
            dbContext.AuthenticationCommands.Add(command);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            var storedPrincipal = await dbContext.Principals.AsNoTracking()
                .SingleAsync(value => value.Id == principal.Id);
            var storedSession = await dbContext.Sessions.AsNoTracking()
                .SingleAsync(value => value.SessionId == session.SessionId);
            var storedCommand = await dbContext.AuthenticationCommands.AsNoTracking()
                .SingleAsync(value => value.CommandId == command.CommandId);

            Assert.Equal(principal.NormalizedUserName, storedPrincipal.NormalizedUserName);
            Assert.Equal(principal.AccountStatus, storedPrincipal.AccountStatus);
            Assert.Equal(principal.Revision, storedSession.PrincipalRevision);
            Assert.Equal(32, storedSession.SessionReferenceHash.Length);
            Assert.Equal(principal.Id, storedSession.PrincipalId);
            Assert.Null(storedPrincipal.Email);
            Assert.Equal(command.CommandKind, storedCommand.CommandKind);
            Assert.Equal(command.IdempotencyKeyHash, storedCommand.IdempotencyKeyHash);
            Assert.Equal(principal.Id, storedCommand.PrincipalId);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Corrective_migration_removes_inherited_history_privileges_without_changing_application_tables()
    {
        var connectionString = TestDatabaseConfiguration.TryGetConnectionString()
            ?? throw new InvalidOperationException("The isolated IAM database locator is required.");
        var options = new DbContextOptionsBuilder<KordraIdentityDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable(
                KordraIdentityDbContext.MigrationHistoryTableName, KordraIdentityDbContext.SchemaName)).Options;
        await using var dbContext = new KordraIdentityDbContext(options);
        await dbContext.Database.MigrateAsync();
        await dbContext.Database.MigrateAsync();
        Assert.Contains("20261021130000_RestrictIdentityMigrationHistory",
            await dbContext.Database.GetAppliedMigrationsAsync());

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        // Reproduce default-grant leakage on an already-created history table, including PUBLIC.
        await dbContext.Database.ExecuteSqlRawAsync(
            "GRANT ALL PRIVILEGES ON TABLE app.iam_ef_migrations_history TO PUBLIC, kordra_app;");
        var query = "SELECT has_table_privilege('kordra_app', 'app.iam_ef_migrations_history', @privilege)";
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var nativeTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await using var command = new NpgsqlCommand(query, connection, nativeTransaction);
        command.Parameters.AddWithValue("privilege", "UPDATE");
        Assert.True((bool)(await command.ExecuteScalarAsync())!);

        var migration = new RestrictIdentityMigrationHistory();
        foreach (var sql in dbContext.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations))
        {
            await dbContext.Database.ExecuteSqlRawAsync(sql.CommandText);
        }

        foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER" })
        {
            command.Parameters[0].Value = privilege;
            Assert.False((bool)(await command.ExecuteScalarAsync())!, privilege);
        }

        // The correction changes ACLs only; the owner can still track migrations and access IAM rows.
        Assert.Equal(1, await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE app.iam_ef_migrations_history SET \"ProductVersion\" = \"ProductVersion\" WHERE \"MigrationId\" = '20261021130000_RestrictIdentityMigrationHistory'"));
        _ = await dbContext.Principals.CountAsync();
        _ = await dbContext.Sessions.CountAsync();
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Runtime_role_has_no_database_or_schema_DDL_privileges()
    {
        var connectionString = TestDatabaseConfiguration.TryGetRuntimeProbeConnectionString();
        if (connectionString is null)
        {
            throw new InvalidOperationException(
                "Required integration-test configuration is missing: set KORDRA_IAM_RUNTIME_PROBE_CONNECTION_STRING using the private loopback kordra_app locator. Runtime privilege verification cannot be skipped.");
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
        {
            await readOnly.ExecuteNonQueryAsync();
        }

        const string catalogQuery = """
            SELECT current_database(), current_user,
                   has_database_privilege(current_user, current_database(), 'CREATE'),
                   has_schema_privilege(current_user, 'app', 'CREATE'),
                   has_schema_privilege(current_user, 'public', 'CREATE'),
                   has_schema_privilege(current_user, 'app', 'USAGE')
            """;
        await using var command = new NpgsqlCommand(catalogQuery, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("kordra", reader.GetString(0));
        Assert.Equal("kordra_app", reader.GetString(1));
        Assert.False(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
        Assert.False(reader.GetBoolean(4));
        Assert.True(reader.GetBoolean(5));
        Assert.False(await reader.ReadAsync());
    }

    private static string CreateConnectionString(
        string host = "127.0.0.1",
        int port = 5432,
        string database = "kordra_test",
        string username = "kordra_test") =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username
        }.ConnectionString;

    private static string[] GetColumnNames(IEntityType entityType)
    {
        var tableName = Assert.IsType<string>(entityType.GetTableName());
        var table = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        return entityType.GetProperties()
            .Select(property => property.GetColumnName(table))
            .Where(columnName => columnName is not null)
            .Select(columnName => columnName!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
