using Kordra.Testing;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class WorkflowReadAuditDatabaseTests
{
    private static readonly Guid CaseId = Guid.Parse("c043cf3f-74d2-462c-807d-3cd4f1990ecb");
    private const string Schema = "t12_c043cf3f74d2462c807d3cd4f1990ecb";
    private const string Synthetic = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC";

    [Fact]
    public void ExportReadAuditSqlWithoutConnecting()
    {
        using var connection = new NpgsqlConnection(Synthetic);
        var scripts = Scripts(connection, offlineMetadata: true);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_READ_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in scripts) File.WriteAllText(Path.Combine(directory, name), sql);
    }

    [WorkflowReadAuditFact]
    public async Task ReadReturnsOnlyAfterDurableDisclosureAndRejectsChangedPolicy()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_READ_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private approved read fixture locator required.");
        TestDatabaseConfiguration.ValidateFixtureLocator(locator);
        var target = new NpgsqlConnectionStringBuilder(locator) { Pooling = false, Multiplexing = false };
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(TestDatabaseConfiguration.ExpectedFixtureDatabase, target.Database);
        Assert.Equal("kordra_test", target.Username);
        Assert.Equal(5432, target.Port);
        await using var setup = new NpgsqlConnection(target.ConnectionString);
        var scripts = Scripts(setup);
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("KORDRA_WFL_READ_MANIFEST")!));
        Assert.Equal("approved_for_execution", approval.RootElement.GetProperty("status").GetString());
        Assert.Equal(Schema, approval.RootElement.GetProperty("schema").GetString());
        foreach (var (name, sql) in scripts)
            Assert.Equal(approval.RootElement.GetProperty("sha256").GetProperty(name).GetString(),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
        await setup.OpenAsync(ct);
        Assert.Equal(0L, await Scalar("SELECT count(*) FROM pg_namespace WHERE nspname='" + Schema + "'"));
        var ownedConnections = new List<NpgsqlConnection>();
        var created = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                await Sql("CREATE SCHEMA " + Schema, ddl);
                foreach (var name in new[] { "iam.sql", "prj.sql", "wfl.sql", "aud.sql" }) await Sql(scripts[name], ddl);
                await ddl.CommitAsync(ct);
                created = true;
            }
            // PR29-WFL-TIMESTAMP-002: a canonical seven-digit deadline PostgreSQL cannot store must still read.
            var clockNow = DateTimeOffset.UtcNow;
            var now = new DateTimeOffset(clockNow.UtcTicks - clockNow.UtcTicks % 10 - 3, TimeSpan.Zero);
            var spec = new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64),
                WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
            // Same exact environment/policy; its persisted deadline is later moved by 1us to prove real mismatches still reject.
            var mismatched = new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), Guid.NewGuid(), spec.ProjectId,
                spec.ApplicationId, spec.EnvironmentId, spec.TargetId, spec.ReleaseId, spec.ReleaseRevision, spec.ReleaseHash,
                spec.Environment, spec.CreatedAtUtc, spec.DeadlineUtc, spec.DueAtUtc);
            await using (var owner = StartFixture(setup))
            await using (var root = await setup.BeginTransactionAsync(ct))
            {
                await owner.Database.UseTransactionAsync(root, ct);
                await new WorkflowStore(owner).StageCreationAsync(spec, now, ct);
                await new WorkflowStore(owner).StageCreationAsync(mismatched, now, ct);
                await root.CommitAsync(ct);
            }

            var owners = new PolicyOwners(target.ConnectionString);
            var policyNow = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
            var policyPayload = new ResourcePolicyPayload(2, "workflow.read", "environment", "active",
                policyNow.AddMinutes(-1), null, [new("Test", "allow")]);
            Guid policyId;
            await using (var root = await setup.BeginTransactionAsync(ct))
            {
                await using var iam = Identity(setup);
                await using var prj = Projects(setup);
                await iam.Database.UseTransactionAsync(root, ct);
                await prj.Database.UseTransactionAsync(root, ct);
                prj.Projects.Add(new() { Id = spec.ProjectId, TenantId = CaseId, Name = "Synthetic" });
                prj.Environments.Add(new() { Id = spec.EnvironmentId, TenantId = CaseId, ProjectId = spec.ProjectId,
                    Name = "Synthetic", Classification = "Test" });
                await prj.SaveChangesAsync(ct);
                var published = await new ResourcePolicyStore(iam).StagePublicationAsync(new Lease(), CaseId, spec.ProjectId,
                    null, 0, policyPayload, policyNow, ct);
                Assert.NotNull(published);
                policyId = published.PolicyId;
                await iam.SaveChangesAsync(ct);
                Assert.NotNull(await new PolicyBindingStore(prj).StageAsync(CaseId, spec.ProjectId,
                    new(null, "environment", spec.EnvironmentId, 0, "workflow.read"),
                    new(policyId, 1), Guid.NewGuid(), true, ct));
                await prj.SaveChangesAsync(ct);
                await root.CommitAsync(ct);
            }

            // The legacy kordra_test database may already hold a runtime-shaped app schema; it must not change.
            var appFloorBefore = await AppFloor();

            // A real persisted deadline change, even <1us from canonical, fails closed with a durable Use and no Disclosure.
            // It runs while the policy still allows, so only the owner binding check can reject it.
            await using (var damage = new NpgsqlCommand($"UPDATE {Schema}.instances SET deadline_utc=deadline_utc+interval '1 microsecond' " +
                "WHERE instance_id=@instance", setup) { CommandTimeout = 3 })
            {
                damage.Parameters.AddWithValue("instance", mismatched.InstanceId);
                Assert.Equal(1, await damage.ExecuteNonQueryAsync(ct));
            }
            {
                var facts = new Facts(spec);
                var damagedRead = CreateReadCoordinator(CaseId,
                    new Factory<KordraIdentityDbContext>(() => Identity(NewOwnedConnection())),
                    new Factory<WorkflowDbContext>(() => StartFixture(NewOwnedConnection())),
                    new WorkflowEnvironmentAuthorityReader(facts, facts, facts, facts, facts, facts),
                    new WorkflowActionPolicyReader(owners, owners, owners, facts), new AuditFactory(() => Task.CompletedTask),
                    new FairAsyncReaderWriterAdmissionGate(), facts);
                var response = new Response((_, _) => Task.CompletedTask);
                Assert.Equal(WorkflowReadOutcome.Unavailable, await damagedRead.ReadAsync(facts.Selection,
                    new(CaseId, spec.ProjectId, spec.EnvironmentId, mismatched.InstanceId), null, Guid.NewGuid(), response, ct));
                Assert.Equal(0, response.Commits);
                Assert.Equal(1L, await Scalar($"SELECT count(*) FROM {Schema}.aud_intents WHERE stage='U'"));
                Assert.Equal(0L, await Scalar($"SELECT count(*) FROM {Schema}.aud_intents WHERE stage='D'"));
            }

            foreach (var change in new[] { false, true })
            {
                var facts = new Facts(spec);
                var authority = new WorkflowEnvironmentAuthorityReader(facts, facts, facts, facts, facts, facts);
                var auditFactory = new AuditFactory(async () =>
                {
                    if (!change) return;
                    await using var connection = NewOwnedConnection();
                    await connection.OpenAsync(ct);
                    await using var root = await connection.BeginTransactionAsync(ct);
                    await using var iam = Identity(connection);
                    await iam.Database.UseTransactionAsync(root, ct);
                    Assert.NotNull(await new ResourcePolicyStore(iam).StagePublicationAsync(new Lease(), CaseId,
                        spec.ProjectId, policyId, 1, policyPayload with { Rules = [new("Test", "deny")] }, policyNow, ct));
                    await iam.SaveChangesAsync(ct);
                    await root.CommitAsync(ct);
                });
                var coordinator = CreateReadCoordinator(CaseId,
                    new Factory<KordraIdentityDbContext>(() => Identity(NewOwnedConnection())),
                    new Factory<WorkflowDbContext>(() => StartFixture(NewOwnedConnection())),
                    authority, new WorkflowActionPolicyReader(owners, owners, owners, facts), auditFactory, new FairAsyncReaderWriterAdmissionGate(), facts);
                var response = new Response(async (commitment, payload) =>
                {
                    Assert.Equal(2L, await Scalar($"SELECT count(*) FROM {Schema}.aud_intents WHERE invocation_id='{commitment.InvocationId:D}'"));
                    Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)), commitment.PayloadSha256);
                });
                var status = await coordinator.ReadAsync(facts.Selection,
                    new(CaseId, spec.ProjectId, spec.EnvironmentId, spec.InstanceId), null, Guid.NewGuid(), response, ct);
                Assert.Equal(change ? WorkflowReadOutcome.Denied : WorkflowReadOutcome.Completed, status);
                Assert.Equal(change ? 0 : 1, response.Commits);
            }
            Assert.Equal(4L, await Scalar($"SELECT count(*) FROM {Schema}.aud_intents"));
            Assert.Equal(1L, await Scalar($"SELECT count(*) FROM {Schema}.aud_intents WHERE stage='D'"));
            Assert.NotEqual(0, spec.DeadlineUtc.UtcTicks % 10);

            // PR29-IAM-FLOOR-001: in both legacy and disposable fixtures the IAM floor is case-owned, never runtime app.
            Assert.Equal(1L, await Scalar($"SELECT count(*) FROM {Schema}.iam_authority_clock_floors WHERE installation_id='{CaseId:D}'"));
            Assert.Equal(appFloorBefore, await AppFloor());
        }
        finally
        {
            foreach (var connection in ownedConnections) await connection.DisposeAsync();
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var drop = new NpgsqlCommand(scripts["cleanup.sql"], setup);
                await drop.ExecuteNonQueryAsync(cleanup.Token);
            }
        }

        NpgsqlConnection NewOwnedConnection()
        {
            var connection = new NpgsqlConnection(target.ConnectionString);
            ownedConnections.Add(connection);
            return connection;
        }

        async Task<string?> AppFloor()
        {
            await using var exists = new NpgsqlCommand("SELECT to_regclass('app.iam_authority_clock_floors') IS NOT NULL", setup) { CommandTimeout = 3 };
            if (!(bool)(await exists.ExecuteScalarAsync(ct))!) return null;
            await using var read = new NpgsqlCommand("SELECT admitted_utc::text FROM app.iam_authority_clock_floors WHERE installation_id=@id", setup)
                { CommandTimeout = 3 };
            read.Parameters.AddWithValue("id", CaseId);
            return (string?)await read.ExecuteScalarAsync(ct);
        }

        async Task<long> Scalar(string sql)
        {
            await using var command = new NpgsqlCommand(sql, setup) { CommandTimeout = 3 };
            return (long)(await command.ExecuteScalarAsync(ct))!;
        }
        async Task Sql(string sql, NpgsqlTransaction root)
        {
            await using var command = new NpgsqlCommand(sql, setup, root) { CommandTimeout = 3 };
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static KordraIdentityDbContext Identity(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(connection)
            .ReplaceService<IModelCustomizer, Customizer>().Options);

    private static ProjectDbContext Projects(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<ProjectDbContext>().UseNpgsql(connection)
            .ReplaceService<IModelCustomizer, Customizer>().Options);

    private sealed class Lease : IOfflineProvisioningLease { public void RequireHeld() { } }

    private sealed class PolicyOwners(string locator) : IReleasePolicyEnvironmentReader, IActionPolicyBindingReader, ICurrentResourcePolicyReader
    {
        public async Task<ReleasePolicyEnvironment?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project,
            Guid environment, CancellationToken cancellationToken = default)
        {
            await using var connection = new NpgsqlConnection(locator);
            await using var db = Projects(connection);
            return await new ReleasePolicyEnvironmentReader(db).ReadCurrentUnderAdmissionAsync(tenant, project, environment, cancellationToken);
        }
        public async Task<PolicyBinding?> ReadCurrentForActionUnderAdmissionAsync(Guid tenant, Guid project,
            Guid? environment, string action, CancellationToken ct = default)
        {
            await using var connection = new NpgsqlConnection(locator);
            await using var db = Projects(connection);
            return await new PolicyBindingStore(db).ReadCurrentForActionUnderAdmissionAsync(tenant, project, environment, action, ct);
        }
        public async Task<ResourcePolicySnapshot?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project,
            Guid policy, long revision, CancellationToken ct = default)
        {
            await using var connection = new NpgsqlConnection(locator);
            await using var db = Identity(connection);
            return await new ResourcePolicyStore(db).ReadCurrentUnderAdmissionAsync(tenant, project, policy, revision, ct);
        }
    }

    private static WorkflowReadCoordinator CreateReadCoordinator(Guid installation,
        IDbContextFactory<KordraIdentityDbContext> identity, IDbContextFactory<WorkflowDbContext> workflows,
        WorkflowEnvironmentAuthorityReader authority, IWorkflowReadPolicyReader policies,
        IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock) =>
        RegressionPostgreSqlSelection.LoadRunnerProfile() is { } profile
            ? WorkflowReadCoordinator.CreateDisposableTestCoordinator(installation, identity, workflows, authority, policies, audits, gate, clock, profile.OwnerBinding)
            : WorkflowReadCoordinator.CreateIsolatedTestCoordinator(installation, identity, workflows, authority, policies, audits, gate, clock);

    private static WorkflowDbContext StartFixture(NpgsqlConnection connection) =>
        RegressionPostgreSqlSelection.LoadRunnerProfile() is { } profile
            ? WorkflowDbContext.CreateDisposableTestFixture(connection, CaseId, 4, profile.OwnerBinding)
            : WorkflowDbContext.CreateStartFixture(connection, CaseId);

    private static Dictionary<string, string> Scripts(NpgsqlConnection connection, bool offlineMetadata = false)
    {
        using var iam = Identity(connection);
        using var prj = Projects(connection);
        using var wfl = offlineMetadata ? WorkflowDbContext.CreateStartFixture(connection, CaseId) : StartFixture(connection);
        using var aud = offlineMetadata ? AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId)
            : TestDatabaseConfiguration.IsolatedAuditContext(connection, CaseId);
        return new()
        {
            ["iam.sql"] = iam.Database.GenerateCreateScript(), ["prj.sql"] = prj.Database.GenerateCreateScript(), ["wfl.sql"] = wfl.Database.GenerateCreateScript(),
            ["aud.sql"] = aud.Database.GenerateCreateScript(), ["cleanup.sql"] = $"DROP SCHEMA {Schema} CASCADE;\n"
        };
    }

    public sealed class Customizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder model, DbContext context)
        {
            base.Customize(model, context);
            model.HasDefaultSchema(Schema);
            foreach (var entity in model.Model.GetEntityTypes()) entity.SetSchema(Schema);
        }
    }

    private sealed class Factory<T>(Func<T> create) : IDbContextFactory<T> where T : DbContext
    {
        public T CreateDbContext() => create();
    }

    private sealed class Response(Func<WorkflowReadCommitment, byte[], Task> check) : IWorkflowReadResponseCommitter
    {
        public int Commits;
        public async Task CommitAsync(WorkflowReadCommitment commitment, ReadOnlyMemory<byte> payload, CancellationToken ct)
        {
            await check(commitment, payload.ToArray());
            Commits++;
        }
        public void Abort() { }
    }

    private sealed class AuditFactory(Func<Task> afterUseCommit) : IArtifactAuditTransactionFactory
    {
        public async ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction root, CancellationToken ct) =>
            new AuditAdapter(await TestDatabaseConfiguration.IsolatedAuditTransactionAsync(connection, root, CaseId, ct), afterUseCommit);
    }

    private sealed class AuditAdapter(AuditTransactionContext context, Func<Task> afterUseCommit) : IArtifactAuditTransaction
    {
        private AuditIntentStage stage;
        public bool CommitAttempted => context.CommitAttempted;
        public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct)
        {
            stage = intent.Stage;
            return context.StageAsync(intent, ct);
        }
        public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct)
        {
            var result = await context.CommitAsync(ct);
            if (stage == AuditIntentStage.Use && result.Status == AuditAppendStatus.Durable) await afterUseCommit();
            return result;
        }
        public ValueTask DisposeAsync() => context.DisposeAsync();
    }

    private sealed class Facts : ICurrentSubjectReader, IScopedGrantReader, IRoleDefinitionReader,
        IReleaseProjectAuthorityReader, IApprovalEnvironmentAuthorityReader, IAuthorityClock
    {
        private readonly WorkflowSpecification spec;
        public readonly SessionSelection Selection = new(Guid.NewGuid(), "synthetic-session", Guid.NewGuid());
        private readonly DateTimeOffset expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        private readonly Guid session = Guid.NewGuid(), definition = Guid.NewGuid(), assignment = Guid.NewGuid(), membership = Guid.NewGuid();

        public Facts(WorkflowSpecification specification) => spec = specification;
        public DateTimeOffset GetTrustedUtcNow() => DateTimeOffset.UtcNow;
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default) =>
            Task.FromResult<CurrentSubject?>(new(Selection.PrincipalId, session, Selection.BootGeneration, 1, expiry, expiry));
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission, string scopeKind, Guid scopeId, CancellationToken ct = default) =>
            Task.FromResult<ScopedGrant?>(new(principal, tenant, membership, 1, 1, assignment, 1, definition, 1, permission, scopeKind, scopeId, expiry));
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken ct = default) =>
            Task.FromResult<RoleDefinitionSnapshot?>(new(definition, 1, "Synthetic", [new("workflow.read", "environment")]));
        public Task<bool> IsActivePrincipalAsync(Guid principal, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult<ReleaseProjectAuthority?>(new(CaseId, spec.ProjectId, 1));
        public Task<ApprovalEnvironmentAuthority?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid environment, CancellationToken ct = default) =>
            Task.FromResult<ApprovalEnvironmentAuthority?>(new(CaseId, spec.ProjectId, spec.EnvironmentId, 1));

    }
}

public sealed class WorkflowReadAuditFactAttribute : FactAttribute
{
    public WorkflowReadAuditFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_READ_MANIFEST")))
            Skip = "Requires exact approval for the isolated WFL read/AUD fixture.";
    }
}
