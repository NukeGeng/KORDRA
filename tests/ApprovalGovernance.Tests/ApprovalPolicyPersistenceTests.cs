using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.ApprovalGovernance;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.ApprovalGovernance.Domain;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Kordra.ApprovalGovernance.Tests;

public sealed class ApprovalPolicyPersistenceTests : IAsyncLifetime
{
    private ApprovalDbContext _db = null!;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction _transaction = null!;
    private readonly Guid _installation = Guid.NewGuid(), _tenant = Guid.NewGuid(), _project = Guid.NewGuid(), _policy = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private isolated kordra_test locator required.");
        _db = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(locator));
        await _db.Database.MigrateAsync();
        _transaction = await _db.Database.BeginTransactionAsync();
        _db.Heads.Add(new() { InstallationId = _installation, TenantId = _tenant, ProjectId = _project, PolicyId = _policy, CurrentRevision = 1 });
        _db.Revisions.Add(new()
        {
            InstallationId = _installation,
            TenantId = _tenant,
            ProjectId = _project,
            PolicyId = _policy,
            Revision = 1,
            CanonicalPayload = [1],
            PayloadHash = new byte[32],
            PublishedAtUtc = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Partial_migration_failure_rolls_back_without_touching_existing_data()
    {
        // A unique synthetic schema inside the already isolated test DB; the outer transaction removes all test DDL.
        var schema = "aprfixture_" + Guid.NewGuid().ToString("N");
        var connection = _db.Database.GetDbConnection();
        async Task Execute(string sql)
        {
            await using var command = connection.CreateCommand(); command.Transaction = _transaction.GetDbTransaction();
            command.CommandText = sql; await command.ExecuteNonQueryAsync();
        }
        await Execute($"CREATE SCHEMA {schema}; CREATE TABLE {schema}.sentinel (value integer NOT NULL); INSERT INTO {schema}.sentinel VALUES (73); CREATE TABLE {schema}.apr_policy_revisions (collision integer);");
        await Execute("SAVEPOINT before_migration");
        var script = _db.GetService<IMigrator>().GenerateScript("0", null, MigrationsSqlGenerationOptions.NoTransactions)
            .Replace("nspname = 'app'", $"nspname = '{schema}'", StringComparison.Ordinal)
            .Replace("CREATE SCHEMA app;", $"CREATE SCHEMA {schema};", StringComparison.Ordinal)
            .Replace("app.", schema + ".", StringComparison.Ordinal);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => Execute(script));
        Assert.Equal(PostgresErrorCodes.DuplicateTable, failure.SqlState);
        await Execute("ROLLBACK TO SAVEPOINT before_migration");
        await using var check = connection.CreateCommand(); check.Transaction = _transaction.GetDbTransaction();
        check.CommandText = $"SELECT (SELECT value FROM {schema}.sentinel), to_regclass('{schema}.apr_policy_heads') IS NULL";
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(73, reader.GetInt32(0)); Assert.True(reader.GetBoolean(1));
    }

    [Fact]
    public async Task Policy_revision_rejects_update()
    {
        var row = await _db.Revisions.SingleAsync(x => x.PolicyId == _policy);
        row.CanonicalPayload = [2];
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.IsType<PostgresException>(error.InnerException);
    }

    [Fact]
    public async Task Immutable_policy_revision_cannot_be_deleted()
    {
        var revision = await _db.Revisions.SingleAsync(x => x.PolicyId == _policy);
        _db.Revisions.Remove(revision);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.IsType<PostgresException>(error.InnerException);
    }

    [Fact]
    public async Task Immutable_history_tables_cannot_be_truncated_even_by_owner()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => _db.Database.ExecuteSqlRawAsync("TRUNCATE app.apr_policy_commands"));
        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
    }

    [Fact]
    public async Task Head_cannot_skip_or_roll_back_revision()
    {
        var head = await _db.Heads.SingleAsync(x => x.PolicyId == _policy);
        head.CurrentRevision = 3;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.IsType<PostgresException>(error.InnerException);
    }

    [Fact]
    public async Task Binding_must_reference_revision_in_same_tenant_and_project()
    {
        _db.Bindings.Add(new ApprovalPolicyBinding
        {
            BindingId = Guid.NewGuid(),
            InstallationId = _installation,
            TenantId = Guid.NewGuid(),
            ProjectId = _project,
            EnvironmentId = Guid.NewGuid(),
            BindingRevision = 1,
            PolicyId = _policy,
            PolicyRevision = 1
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, pg.SqlState);
    }

    [Fact]
    public async Task Runtime_role_can_only_read_policy_tables()
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.Transaction = _transaction.GetDbTransaction();
        command.CommandText = "SELECT has_table_privilege('kordra_app', 'app.apr_policy_heads', 'SELECT'), " +
            "has_table_privilege('kordra_app', 'app.apr_policy_heads', 'INSERT,UPDATE,DELETE,TRUNCATE'), " +
            "has_table_privilege('kordra_app', 'app.apr_policy_revisions', 'UPDATE,DELETE,TRUNCATE'), " +
            "has_table_privilege('kordra_app', 'app.apr_policy_commands', 'SELECT')";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0)); Assert.False(reader.GetBoolean(1)); Assert.False(reader.GetBoolean(2)); Assert.False(reader.GetBoolean(3));
    }

    [Fact]
    public async Task Publication_stages_exact_binding_and_old_binding_does_not_float_to_new_head()
    {
        var installation = Guid.NewGuid(); var tenant = Guid.NewGuid(); var project = Guid.NewGuid();
        var policy = Guid.NewGuid(); var environment = Guid.NewGuid();
        var store = new ApprovalPolicyStore(_db); var custody = new Custody();
        var evidence = new ApprovalEnvironmentEvidence(tenant, project, environment, 1, 2, "Test");
        var manifest = Manifest(installation, tenant, project, policy, environment, 0, true);
        Assert.True(await store.StagePublicationAsync(custody, manifest, [evidence], Now()));
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var bound = await store.ReadCurrentUnderAdmissionAsync(installation, tenant, project, environment);
        Assert.NotNull(bound); Assert.Equal(manifest.Policy.PayloadHash, bound.PayloadHash); Assert.Equal(1, bound.BindingRevision);
        var bytes = bound.GetCanonicalPayload(); bytes[0] = 0;
        Assert.Equal((byte)'{', bound.GetCanonicalPayload()[0]);
        var next = Manifest(installation, tenant, project, policy, environment, 1, false);
        Assert.True(await store.StagePublicationAsync(custody, next, [], Now()));
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        Assert.Null(await store.ReadCurrentUnderAdmissionAsync(installation, tenant, project, environment));
        Assert.Equal(2, await _db.Revisions.CountAsync(x => x.PolicyId == policy));
        Assert.Single(await _db.BindingHistory.Where(x => x.PolicyId == policy).ToArrayAsync());
    }

    [Fact]
    public async Task Rebinding_advances_policy_and_binding_once_without_rewriting_history()
    {
        var installation = Guid.NewGuid(); var tenant = Guid.NewGuid(); var project = Guid.NewGuid();
        var policy = Guid.NewGuid(); var environment = Guid.NewGuid(); var store = new ApprovalPolicyStore(_db);
        var evidence = new ApprovalEnvironmentEvidence(tenant, project, environment, 1, 2, "Test");
        Assert.True(await store.StagePublicationAsync(new Custody(), Manifest(installation, tenant, project, policy, environment, 0, true), [evidence], Now()));
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var first = await store.ReadCurrentUnderAdmissionAsync(installation, tenant, project, environment);
        Assert.NotNull(first);
        var next = Manifest(installation, tenant, project, policy, environment, 1, true, first.BindingId, first.BindingRevision);
        Assert.True(await store.StagePublicationAsync(new Custody(), next, [evidence], Now()));
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var rebound = await store.ReadCurrentUnderAdmissionAsync(installation, tenant, project, environment);
        Assert.NotNull(rebound); Assert.Equal(first.BindingId, rebound.BindingId);
        Assert.Equal(2, rebound.BindingRevision); Assert.Equal(2, rebound.PolicyRevision);
        Assert.Equal(2, await _db.BindingHistory.CountAsync(x => x.InstallationId == installation && x.PolicyId == policy));
        Assert.Equal(2, await _db.Revisions.CountAsync(x => x.InstallationId == installation && x.PolicyId == policy));
    }

    [Fact]
    public async Task Stale_environment_or_wrong_owner_never_stages_any_rows()
    {
        var installation = Guid.NewGuid(); var tenant = Guid.NewGuid(); var project = Guid.NewGuid();
        var policy = Guid.NewGuid(); var environment = Guid.NewGuid(); var store = new ApprovalPolicyStore(_db);
        var manifest = Manifest(installation, tenant, project, policy, environment, 0, true);
        var wrong = new ApprovalEnvironmentEvidence(tenant, project, environment, 1, 3, "Test");
        Assert.False(await store.StagePublicationAsync(new Custody(), manifest, [wrong], Now()));
        Assert.False(_db.ChangeTracker.HasChanges());
        Assert.False(await store.StagePublicationAsync(new Custody(), manifest, [wrong with { TenantId = Guid.NewGuid(), EnvironmentRevision = 2 }], Now()));
        Assert.False(_db.ChangeTracker.HasChanges());
        var released = new Custody { Held = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePublicationAsync(released, manifest, [], Now()));
    }

    [Fact]
    public async Task Sql_rejects_retrograde_binding_revision_and_immutable_history_deletion()
    {
        var installation = Guid.NewGuid(); var tenant = Guid.NewGuid(); var project = Guid.NewGuid();
        var policy = Guid.NewGuid(); var environment = Guid.NewGuid(); var store = new ApprovalPolicyStore(_db);
        var manifest = Manifest(installation, tenant, project, policy, environment, 0, true);
        Assert.True(await store.StagePublicationAsync(new Custody(), manifest,
            [new ApprovalEnvironmentEvidence(tenant, project, environment, 1, 2, "Test")], Now()));
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var binding = await _db.Bindings.SingleAsync(x => x.PolicyId == policy);
        binding.BindingRevision = 3;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.IsType<PostgresException>(error.InnerException);
    }

    private static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow; return now.AddTicks(-(now.Ticks % 10));
    }
    private static ApprovalPolicyManifest Manifest(Guid installation, Guid tenant, Guid project, Guid policy,
        Guid environment, long revision, bool includeBinding, Guid? expectedBindingId = null, long expectedBindingRevision = 0)
    {
        var binding = new[] { new { environmentId = environment.ToString("D"), expectedEnvironmentRevision = 2,
            expectedBindingId = expectedBindingId?.ToString("D"), expectedBindingRevision } };
        var value = new
        {
            schemaVersion = 1,
            manifestId = Guid.NewGuid().ToString("D"),
            installationId = installation.ToString("D"),
            expectedDatabase = "kordra_test",
            expectedRole = "kordra_admin",
            tenantId = tenant.ToString("D"),
            projectId = project.ToString("D"),
            policyId = policy.ToString("D"),
            expectedPolicyRevision = revision,
            payload = new
            {
                schemaVersion = 1,
                purpose = "release-intent-v1",
                status = "active",
                effectiveFromUtc = "2026-10-27T00:00:00.000000Z",
                expiresAtUtc = (string?)null,
                rules = new[] { new { classification = "Test", effect = "allow", separationOfDuties = "independent",
                    allowUnattestedProvenance = true, pendingLifetimeSeconds = 86400, grantLifetimeSeconds = 1800,
                    blockedWindowsUtc = Array.Empty<object>() } }
            },
            bindingChanges = includeBinding ? binding : []
        };
        return ApprovalPolicyManifest.Parse(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
    }
    private sealed class Custody : IOfflineApprovalCustody
    {
        public bool Held { get; init; } = true;
        public void RequireHeld() { if (!Held) throw new InvalidOperationException("Lost custody."); }
    }

    public async Task DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _db.DisposeAsync();
    }
}
