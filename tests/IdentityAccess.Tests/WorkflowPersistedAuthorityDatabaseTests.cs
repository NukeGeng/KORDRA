using Kordra.Testing;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Contracts;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
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

public sealed class WorkflowPersistedAuthorityDatabaseTests
{
    private static readonly Guid CaseId = Guid.Parse("3fda17ab-92e2-4a56-8977-f69c4b0d8e43");
    private const string Schema = "t12_3fda17ab92e24a568977f69c4b0d8e43";
    private const string Synthetic = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC";

    [Fact]
    public void ExportReadAuditSqlWithoutConnecting()
    {
        using var connection = new NpgsqlConnection(Synthetic);
        var scripts = Scripts(connection, offlineMetadata: true);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_OWNER_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in scripts) File.WriteAllText(Path.Combine(directory, name), sql);
    }

    [Fact]
    public void PrincipalSeedSatisfiesRequiredIamModelFieldsWithoutConnecting()
    {
        using var connection = new NpgsqlConnection(Synthetic);
        using var iam = Identity(connection);
        var principal = CreatePrincipal(Guid.NewGuid(), "synthetic-offline-shape-only");
        var model = iam.Model.FindEntityType(typeof(KordraIdentityUser))!;
        foreach (var field in model.GetProperties().Where(x => !x.IsNullable))
        {
            var value = field.PropertyInfo!.GetValue(principal);
            Assert.True(value is not null, "Missing required synthetic principal field: " + field.Name);
            if (value is string text && field.GetMaxLength() is { } limit) Assert.InRange(text.Length, 1, limit);
        }
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [WorkflowPersistedAuthorityFact]
    public async Task PersistedSessionGrantPolicyProtectReadAndCancellation()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_OWNER_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private approved read fixture locator required.");
        TestDatabaseConfiguration.ValidateFixtureLocator(locator);
        var target = new NpgsqlConnectionStringBuilder(locator) { Pooling = false, Multiplexing = false };
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(TestDatabaseConfiguration.ExpectedFixtureDatabase, target.Database);
        Assert.Equal("kordra_test", target.Username);
        Assert.Equal(5432, target.Port);
        await using var setup = new NpgsqlConnection(target.ConnectionString);
        var scripts = Scripts(setup);
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("KORDRA_WFL_OWNER_MANIFEST")!));
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
                await Sql(scripts["create.sql"], ddl);
                foreach (var name in new[] { "iam.sql", "ten.sql", "prj.sql", "wfl.sql", "aud.sql" }) await Sql(scripts[name], ddl);
                await ddl.CommitAsync(ct);
                created = true;
            }
            var now = DateTimeOffset.UtcNow;
            var spec = new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64),
                WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
            await using (var owner = StartFixture(setup))
            await using (var root = await setup.BeginTransactionAsync(ct))
            {
                await owner.Database.UseTransactionAsync(root, ct);
                await new WorkflowStore(owner).StageCreationAsync(spec, now, ct);
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
                policyId = Guid.Empty;
                foreach (var action in Actions)
                {
                    var published = await new ResourcePolicyStore(iam).StagePublicationAsync(new Lease(), CaseId,
                        spec.ProjectId, null, 0, policyPayload with { Action = action }, policyNow, ct);
                    Assert.NotNull(published);
                    if (action == "workflow.read") policyId = published.PolicyId;
                    await iam.SaveChangesAsync(ct);
                    Assert.NotNull(await new PolicyBindingStore(prj).StageAsync(CaseId, spec.ProjectId,
                        new(null, "environment", spec.EnvironmentId, 0, action),
                        new(published.PolicyId, 1), Guid.NewGuid(), true, ct));
                    await prj.SaveChangesAsync(ct);
                }
                await root.CommitAsync(ct);
            }

            var gate = new FairAsyncReaderWriterAdmissionGate();
            var boot = Guid.NewGuid();
            var iamFactory = new Factory<KordraIdentityDbContext>(() => Identity(NewOwnedConnection()));
            var tenFactory = new Factory<TenantDbContext>(() => Tenants(NewOwnedConnection()));
            var passwordHasher = new PasswordHasher<KordraIdentityUser>(Options.Create(new PasswordHasherOptions { IterationCount = 220_000 }));
            // One synthetic opaque hash satisfies IAM persistence; this fixture never authenticates a password.
            var syntheticHash = passwordHasher.HashPassword(new KordraIdentityUser(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var authentication = new LocalAuthenticationService(iamFactory, new NoAuthenticationWrites(),
                passwordHasher, new(CaseId, boot), gate);
            var definitions = new RoleDefinitionStore(iamFactory, gate, new NoAuthenticationWrites(), new(CaseId, boot), authentication);
            var grants = new TenantMembershipService(tenFactory, definitions, null!, owners, null!, gate, authentication);
            var authority = new WorkflowEnvironmentAuthorityReader(authentication, grants, definitions, owners, owners, authentication);
            var commands = new WorkflowCommandAdmissionReader(authority,
                new WorkflowActionPolicyReader(owners, owners, owners, authentication), authentication);
            var scope = new WorkflowReadScope(CaseId, spec.ProjectId, spec.EnvironmentId, spec.InstanceId);
            await using (var tenant = Tenants(setup))
            {
                tenant.Organizations.Add(new() { Id = CaseId, Name = "Synthetic fixture" });
                await tenant.SaveChangesAsync(ct);
            }
            foreach (var fault in new[] { "revoked-session", "expired-session", "disabled-principal", "revoked-assignment", "expired-assignment", "retired-definition", "wrong-tenant" })
            {
                var candidate = await SeedActor();
                foreach (var action in Actions) Assert.NotNull(await Admit(candidate.Selection, scope, action));
                if (fault == "wrong-tenant")
                    Assert.Null(await Admit(candidate.Selection, scope with { TenantId = Guid.NewGuid() }, "workflow.cancel"));
                else
                {
                    await using var iam = Identity(setup);
                    await using var ten = Tenants(setup);
                    switch (fault)
                    {
                        case "revoked-session":
                            await iam.Sessions.Where(x => x.SessionId == candidate.SessionId).ExecuteUpdateAsync(x => x
                                .SetProperty(y => y.IsRevoked, true).SetProperty(y => y.RevokedAtUtc, now)
                                .SetProperty(y => y.RevocationReason, "synthetic-fixture"), ct); break;
                        case "expired-session":
                            await iam.Sessions.Where(x => x.SessionId == candidate.SessionId)
                                .ExecuteUpdateAsync(x => x.SetProperty(y => y.LastActivityAtUtc, now.AddMinutes(-31)), ct); break;
                        case "disabled-principal":
                            await iam.Principals.Where(x => x.Id == candidate.Selection.PrincipalId).ExecuteUpdateAsync(x => x
                                .SetProperty(y => y.AccountStatus, "disabled").SetProperty(y => y.Revision, 2), ct); break;
                        case "revoked-assignment":
                            await ten.Assignments.Where(x => x.Id == candidate.AssignmentId).ExecuteUpdateAsync(x => x
                                .SetProperty(y => y.Status, "revoked").SetProperty(y => y.Revision, 2), ct); break;
                        case "expired-assignment":
                            await ten.Assignments.Where(x => x.Id == candidate.AssignmentId).ExecuteUpdateAsync(x => x
                                .SetProperty(y => y.ExpiresAtUtc, now.AddSeconds(-1)).SetProperty(y => y.Revision, 2), ct); break;
                        case "retired-definition":
                            iam.RoleDefinitions.Add(new() { DefinitionId = candidate.DefinitionId, Revision = 2,
                                ManifestId = Guid.NewGuid(), ContentHash = new string('c', 64), Name = "Synthetic retired",
                                RulesJson = JsonSerializer.Serialize(Actions.Select(x => new PermissionRule(x, "environment"))), IsRetired = true });
                            await iam.SaveChangesAsync(ct); break;
                    }
                    foreach (var action in Actions) Assert.Null(await Admit(candidate.Selection, scope, action));
                }
            }
            var caller = await SeedActor();
            var unassigned = await SeedActor(false);
            await using (var held = await gate.AcquireReadAsync(ct))
                Assert.NotNull(await authentication.ReadCurrentUnderAdmissionAsync(unassigned.Selection, ct));
            Assert.Null(await Admit(unassigned.Selection, scope, "workflow.cancel"));
            await using (var root = await setup.BeginTransactionAsync(ct))
            {
                var intent = new WorkflowCancellationIntent(CaseId, CaseId, spec.InstanceId, Guid.NewGuid(),
                    caller.Selection.PrincipalId, 1, spec.SpecificationHash);
                var cancelled = RegressionPostgreSqlSelection.LoadRunnerProfile() is { } profile
                    ? await WorkflowCancellationTransaction.ExecuteAdmittedDisposableTestAsync(setup, root, CaseId,
                        intent, spec.ProjectId, spec.EnvironmentId, caller.Selection, commands, gate, profile.OwnerBinding, ct)
                    : await WorkflowCancellationTransaction.ExecuteAdmittedIsolatedAsync(setup, root, CaseId,
                        intent, spec.ProjectId, spec.EnvironmentId, caller.Selection, commands, gate, ct);
                Assert.Equal(WorkflowCommitDisposition.Committed, cancelled.Disposition);
                Assert.NotNull(cancelled.Receipt);
            }
            Assert.Equal(1L, await Scalar($"SELECT count(*) FROM {Schema}.cancellation_intents"));

            async Task<WorkflowCommandAdmission?> Admit(SessionSelection selection, WorkflowReadScope selectedScope, string action)
            {
                await using var held = await gate.AcquireReadAsync(ct);
                return await commands.ReadUnderAdmissionAsync(selection, selectedScope, action, ct);
            }

            async Task<SeededActor> SeedActor(bool assigned = true)
            {
                var principal = Guid.NewGuid(); var sessionId = Guid.NewGuid(); var definition = Guid.NewGuid();
                var assignment = Guid.NewGuid(); var member = Guid.NewGuid(); var reference = RandomNumberGenerator.GetBytes(32);
                var selection = new SessionSelection(principal, Convert.ToBase64String(reference).TrimEnd('=').Replace('+', '-').Replace('/', '_'), boot);
                await using var iam = Identity(setup);
                iam.Principals.Add(CreatePrincipal(principal, syntheticHash));
                iam.Sessions.Add(new() { SessionId = sessionId, PrincipalId = principal, PrincipalRevision = 1,
                    SessionReferenceHash = SHA256.HashData(reference), BootGeneration = boot, CreatedAtUtc = now.AddHours(-1),
                    LastActivityAtUtc = now, AbsoluteExpiresAtUtc = now.AddHours(1) });
                CryptographicOperations.ZeroMemory(reference);
                iam.RoleDefinitions.Add(new() { DefinitionId = definition, Revision = 1, ManifestId = Guid.NewGuid(),
                    ContentHash = new string('a', 64), Name = "Synthetic workflow permissions",
                    RulesJson = JsonSerializer.Serialize(Actions.Select(x => new PermissionRule(x, "environment"))) });
                await iam.SaveChangesAsync(ct);
                if (assigned)
                {
                    await using var tenant = Tenants(setup);
                    tenant.Memberships.Add(new() { Id = member, TenantId = CaseId, PrincipalId = principal });
                    tenant.Assignments.Add(new() { Id = assignment, TenantId = CaseId, MembershipId = member,
                        DefinitionId = definition, DefinitionRevision = 1, ScopeKind = "environment", ScopeId = spec.EnvironmentId,
                        IsFounding = true, GrantorPrincipalId = principal });
                    await tenant.SaveChangesAsync(ct);
                }
                return new(selection, sessionId, definition, assignment);
            }

            foreach (var change in new[] { false, true })
            {
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
                    authority, new WorkflowActionPolicyReader(owners, owners, owners, authentication), auditFactory, gate, authentication);
                var response = new Response(async (commitment, payload) =>
                {
                    Assert.Equal(2L, await Scalar($"SELECT count(*) FROM {Schema}.aud_intents WHERE invocation_id='{commitment.InvocationId:D}'"));
                    Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)), commitment.PayloadSha256);
                });
                var status = await coordinator.ReadAsync(caller.Selection,
                    new(CaseId, spec.ProjectId, spec.EnvironmentId, spec.InstanceId), null, Guid.NewGuid(), response, ct);
                Assert.Equal(change ? WorkflowReadOutcome.Denied : WorkflowReadOutcome.Completed, status);
                Assert.Equal(change ? 0 : 1, response.Commits);
            }
            Assert.Equal(4L, await Scalar($"SELECT count(*) FROM {Schema}.aud_intents"));
        }
        finally
        {
            foreach (var connection in ownedConnections) await connection.DisposeAsync();
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var drop = new NpgsqlCommand(scripts["cleanup.sql"], setup);
                await drop.ExecuteNonQueryAsync(cleanup.Token);
                await using var absent = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", setup);
                absent.Parameters.AddWithValue("schema", Schema);
                Assert.Equal(0L, await absent.ExecuteScalarAsync(cleanup.Token));
            }
        }

        NpgsqlConnection NewOwnedConnection()
        {
            var connection = new NpgsqlConnection(target.ConnectionString);
            ownedConnections.Add(connection);
            return connection;
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

    private static TenantDbContext Tenants(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connection)
            .ReplaceService<IModelCustomizer, Customizer>().Options);

    private static ProjectDbContext Projects(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<ProjectDbContext>().UseNpgsql(connection)
            .ReplaceService<IModelCustomizer, Customizer>().Options);

    private sealed class Lease : IOfflineProvisioningLease { public void RequireHeld() { } }

    private sealed class PolicyOwners(string locator) : IReleasePolicyEnvironmentReader, IActionPolicyBindingReader, ICurrentResourcePolicyReader, ITenantScopeOwner, IReleaseProjectAuthorityReader, IApprovalEnvironmentAuthorityReader
    {
        public async Task<bool> BelongsToTenantAsync(Guid tenant, TenantScope scope, CancellationToken ct = default)
        {
            await using var connection = new NpgsqlConnection(locator); await using var db = Projects(connection);
            return await new PolicyBindingStore(db).ScopeBelongsToTenantUnderAdmissionAsync(tenant, scope.Kind, scope.Id, ct);
        }
        public async Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid project, CancellationToken ct = default)
        {
            await using var connection = new NpgsqlConnection(locator); await using var db = Projects(connection);
            var current = await new ProjectIdentityReader(db).ReadCurrentProjectUnderAdmissionAsync(project, ct);
            return current is null ? null : new(current.TenantId, current.ProjectId, current.Revision);
        }
        async Task<ApprovalEnvironmentAuthority?> IApprovalEnvironmentAuthorityReader.ReadCurrentUnderAdmissionAsync(
            Guid tenant, Guid project, Guid environment, CancellationToken ct)
        {
            var current = await ReadCurrentUnderAdmissionAsync(tenant, project, environment, ct);
            return current is null ? null : new(tenant, project, environment, current.EnvironmentRevision);
        }
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
            : new WorkflowReadCoordinator(installation, identity, workflows, authority, policies, audits, gate, clock);

    private static WorkflowDbContext StartFixture(NpgsqlConnection connection) =>
        RegressionPostgreSqlSelection.LoadRunnerProfile() is { } profile
            ? WorkflowDbContext.CreateDisposableTestFixture(connection, CaseId, 4, profile.OwnerBinding)
            : WorkflowDbContext.CreateStartFixture(connection, CaseId);

    private static Dictionary<string, string> Scripts(NpgsqlConnection connection, bool offlineMetadata = false)
    {
        using var iam = Identity(connection);
        using var prj = Projects(connection);
        using var ten = Tenants(connection);
        using var wfl = offlineMetadata ? WorkflowDbContext.CreateStartFixture(connection, CaseId) : StartFixture(connection);
        using var aud = offlineMetadata ? AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId)
            : TestDatabaseConfiguration.IsolatedAuditContext(connection, CaseId);
        return new()
        {
            ["create.sql"] = $"CREATE SCHEMA {Schema};\n", ["iam.sql"] = iam.Database.GenerateCreateScript(), ["ten.sql"] = ten.Database.GenerateCreateScript(), ["prj.sql"] = prj.Database.GenerateCreateScript(), ["wfl.sql"] = wfl.Database.GenerateCreateScript(),
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

    private static KordraIdentityUser CreatePrincipal(Guid id, string hash) => new()
    {
        Id = id, UserName = "fixture-" + id.ToString("N"), NormalizedUserName = "FIXTURE-" + id.ToString("N"),
        PasswordHash = hash, SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N")
    };

    private static readonly string[] Actions = ["workflow.start", "workflow.read", "workflow.cancel", "workflow.reconcile"];
    private sealed record SeededActor(SessionSelection Selection, Guid SessionId, Guid DefinitionId, Guid AssignmentId);
    private sealed class NoAuthenticationWrites : IIdentityAuditTransactionFactory
    {
        public ValueTask<IIdentityAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fixture calls session verification only, never authentication mutation.");
    }

}

public sealed class WorkflowPersistedAuthorityFactAttribute : FactAttribute
{
    public WorkflowPersistedAuthorityFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_OWNER_MANIFEST")))
            Skip = "Requires exact approval for the persisted-session/grant WFL fixture.";
    }
}
