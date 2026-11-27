using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>
/// 12.3b on actual PostgreSQL (operation-gated; ADR-0025 disposable database + run-scoped roles, D4): least-privilege API and
/// worker logins issue/consume permits per attempt under the fence; expiry needs a new attempt, one consume per operation;
/// revocation/reauthorization advance the epoch and only the current authority generation admits (KD109); service
/// rotation/retirement; DB01 for every new function.
/// </summary>
public sealed class WorkflowAuthorityWorkerDatabaseTests
{
    private const string ManifestVariable = "KORDRA_WFL_AUTHORITY_MANIFEST";
    private const string LocatorVariable = "KORDRA_WFL_AUTHORITY_CONNECTION_STRING";

    [Fact]
    public void ExportWorkerAuthoritySqlWithoutDatabaseAccess()
    {
        var scripts = Scripts("0123456789ab");
        Assert.Equal(["baseline-wfl.sql", "baseline-aud.sql", "iam-workflow-authority-v3.sql", "wfl-owner-functions-v2.sql",
            "aud-bound-append-v1.sql", "cleanup.sql"], scripts.Select(x => x.Name));
        Assert.Contains("generation = 1 AND expected_version = 0 AND resulting_version = 1", scripts[0].Sql);
        Assert.All(scripts.Skip(2).Take(3), x => Assert.Contains("_r0123456789ab", x.Sql));
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_AUTHORITY_EXPORT") is not { Length: > 0 } directory) return;
        var suffix = Environment.GetEnvironmentVariable("KORDRA_WFL_AUTHORITY_SUFFIX") ?? throw new InvalidOperationException("Run suffix required.");
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in Scripts(suffix)) File.WriteAllText(Path.Combine(directory, "worker-" + name), sql);
    }

    [WorkflowAuthorityDatabaseFact]
    public async Task LeastPrivilegeApiAndWorkerIssueAndConsumeOnlyCurrentAuthority()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = budget.Token;
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable(ManifestVariable)!));
        var approved = approval.RootElement;
        Assert.Equal("approved_for_execution", approved.GetProperty("status").GetString());
        var suffix = approved.GetProperty("suffix").GetString()!;
        var database = approved.GetProperty("database").GetString()!;
        var scripts = Scripts(suffix);
        foreach (var (name, sql) in scripts)
            Assert.Equal(approved.GetProperty("sha256").GetProperty("worker-" + name).GetString(), OwnerFunctionPoc.Sha256(sql));
        var locator = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(LocatorVariable)
            ?? throw new InvalidOperationException("Approved fixture locator required.")) { Pooling = false, Multiplexing = false, Timeout = 5 };
        Assert.Equal("127.0.0.1", locator.Host); Assert.Equal(database, locator.Database); Assert.Equal("kordra_test", locator.Username);
        string Login(string placeholder) => new NpgsqlConnectionStringBuilder(locator.ConnectionString)
            { Username = OwnerFunctionPoc.Role(placeholder, suffix), Password = null, ApplicationName = "kordra-t12-3b" }.ConnectionString;
        var workerRole = OwnerFunctionPoc.Role("kordra_worker_runtime", suffix);
        var apiRole = OwnerFunctionPoc.Role("kordra_api_runtime", suffix);

        await using var setup = new NpgsqlConnection(locator.ConnectionString);
        await setup.OpenAsync(ct);
        Assert.Equal(0L, await Scalar<long>(setup, "SELECT count(*) FROM pg_namespace WHERE nspname IN ('wfl', 'iam_workflow', 'app')"));
        var created = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                foreach (var (name, sql) in scripts.Where(x => x.Name != "cleanup.sql")) await Execute(setup, sql, ct, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            var installation = Guid.NewGuid(); var tenant = Guid.NewGuid(); var environment = Guid.NewGuid();
            var apiService = Guid.NewGuid(); var workerService = Guid.NewGuid(); var boot = Guid.NewGuid(); var actor = Guid.NewGuid();
            // 12.3d D3: the worker's registered certificate pin, presented with every permit request.
            var workerPin = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            Assert.False(await Manifest("bootstrap", apiService, "api", apiRole, "[]"));
            Assert.False(await Manifest("bootstrap", workerService, "worker", workerRole, JsonSerializer.Serialize(new[] { new Dictionary<string, object> {
                ["tenant_id"] = tenant, ["environment_id"] = environment, ["template_id"] = WorkflowSpecification.TemplateId,
                ["template_version"] = WorkflowSpecification.TemplateVersion, ["transition_kind"] = "admit" } })));

            await using var worker = new NpgsqlConnection(Login("kordra_worker_runtime")); await worker.OpenAsync(ct);
            await using var api = new NpgsqlConnection(Login("kordra_api_runtime")); await api.OpenAsync(ct);

            // ---- 1. Issue (idempotent retry) → consume → admit; a consumed operation never gets another permit ----
            var first = await Seed();
            var permit = Request(first);
            var issued = await Issue(permit);
            Assert.False(issued.Duplicate); Assert.Equal(1, issued.Attempt);
            Assert.InRange(issued.ExpiresAtUtc - issued.IssuedAtUtc, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(2));
            Assert.True((await Issue(permit)).Duplicate);
            await Admit(first, permit.PermitId);
            Assert.Equal(((int)Kordra.Modules.WorkflowOrchestration.Domain.WorkflowState.Running, 2L), await StateOf(first));
            Assert.Equal("KD019", await DeniedInRoot(api, root => new WorkflowAuthorityFunctionPort(api, root).IssuePermitAsync(Request(first), ct)));

            // ---- 2. Expired permit: consume denied, a NEW attempt is issued (never renewed), only one consume per operation ----
            var second = await Seed();
            var expiring = Request(second);
            await Issue(expiring);
            await Task.Delay(TimeSpan.FromMilliseconds(2100), ct);
            Assert.Equal("KD006", await DeniedInRoot(worker, root => Consume(worker, root, second, expiring.PermitId)));
            var retry = Request(second);
            Assert.Equal(2, (await Issue(retry)).Attempt);
            await Admit(second, retry.PermitId);
            Assert.Equal(1L, await Scalar<long>(setup, $"SELECT count(*) FROM iam_workflow.permits WHERE operation_id = '{second.Operation}' AND consumed_xact IS NOT NULL"));

            // ---- 3. Revocation and reauthorization advance the epoch; only the current authority generation admits ----
            var third = await Seed();
            var stale = Request(third);
            await Issue(stale);
            var epochBefore = await Epoch();
            await InRoot(api, async root => { Assert.False((await new WorkflowAuthorityFunctionPort(api, root).RevokeDelegationAsync(third.Delegation, "test.revoke", ct)).Duplicate); await root.CommitAsync(ct); });
            Assert.Equal(epochBefore + 1, await Epoch());
            Assert.Equal("KD005", await DeniedInRoot(worker, root => Consume(worker, root, third, stale.PermitId)));
            Assert.Equal("KD018", await DeniedInRoot(api, root => new WorkflowAuthorityFunctionPort(api, root).IssuePermitAsync(Request(third), ct)));
            var reissued = Guid.NewGuid();
            await InRoot(api, async root =>
            {
                var result = await new WorkflowAuthorityFunctionPort(api, root).ReauthorizeDelegationAsync(new(tenant, third.Identity.InstanceId, 1,
                    reissued, actor, Guid.NewGuid(), Guid.NewGuid(), 1, third.Hash, Encoding.UTF8.GetBytes("{\"g\":2}"),
                    DateTimeOffset.UtcNow.AddMinutes(10), "test.reauthorize"), ct);
                Assert.Equal((reissued, 2L, false), (result.DelegationId, result.Generation, result.Duplicate));
                await root.CommitAsync(ct);
            });
            Assert.Equal(epochBefore + 2, await Epoch());
            var current = Request(third);
            await Issue(current);
            // WFL still binds generation 1: a generation-2 permit cannot admit until the WFL owner records generation 2.
            Assert.Equal("KD109", await DeniedInRoot(worker, async root => { await Consume(worker, root, third, current.PermitId);
                await Stage(worker, root, third, current.PermitId); }));
            await SeedBinding(third, 2, reissued, 1, 2);
            await Admit(third, current.PermitId);

            // ---- 4. Fence ordering: a held worker fence makes the issuer wait; release lets it proceed ----
            var fourth = await Seed();
            await using (var held = await worker.BeginTransactionAsync(ct))
            {
                await new WorkflowAuthorityFunctionPort(worker, held).AcquireFenceAsync(installation, ct);
                await using var issuing = await api.BeginTransactionAsync(ct);
                var waiting = new WorkflowAuthorityFunctionPort(api, issuing).IssuePermitAsync(Request(fourth), ct);
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
                Assert.False(waiting.IsCompleted);
                await held.RollbackAsync(ct);
                Assert.False((await waiting).Duplicate);
                await issuing.RollbackAsync(ct);
            }

            // ---- 5. Service bindings: another environment is not admitted for this worker ----
            var foreign = await Seed(Guid.NewGuid());
            Assert.Equal("KD017", await DeniedInRoot(api, root => new WorkflowAuthorityFunctionPort(api, root).IssuePermitAsync(Request(foreign), ct)));

            // ---- 6. DB01 for the 12.3b surface ----
            foreach (var sql in new[] {
                $"SELECT * FROM iam_workflow.create_delegation('{Guid.NewGuid()}', '{tenant}', '{Guid.NewGuid()}', '{actor}', '{actor}', '{actor}', 1, " +
                    $"'{actor}', '{actor}', '{actor}', 1, '{new string('a', 64)}', 'pilot.checkpoint.v1', 1, '{new string('a', 64)}', ARRAY['admit'], '\\x7b7d', sha256('\\x7b7d'), now() + interval '1 minute')",
                $"SELECT * FROM iam_workflow.revoke_delegation('{third.Delegation}', 'probe')",
                $"SELECT * FROM iam_workflow.read_current_delegation('{tenant}', '{third.Identity.InstanceId}')",
                $"SELECT * FROM iam_workflow.issue_permit('{Guid.NewGuid()}', '{workerService}', '{tenant}', '{fourth.Identity.InstanceId}', '{fourth.Step}', '{Guid.NewGuid()}', 1, '{fourth.Hash}', '{WorkflowSpecification.TemplateHash}', 'admit', '{boot}', 0, '{workerPin}')",
                $"SELECT iam_workflow.advance_epoch('{installation}', 'probe')",
                $"SELECT * FROM iam_workflow.apply_service_manifest('{new string('f', 64)}', 'retire', '{installation}', '{workerService}', NULL, NULL, NULL, NULL, NULL, '[]'::jsonb, '{Guid.NewGuid()}')" })
                Assert.Equal("42501", await Denied(worker, sql));
            foreach (var sql in new[] {
                $"SELECT * FROM iam_workflow.consume_permit('{current.PermitId}', '{third.Operation}', '{third.Identity.InstanceId}', 1, '{third.Hash}', '{boot}', 0)",
                $"SELECT * FROM iam_workflow.apply_service_manifest('{new string('f', 64)}', 'retire', '{installation}', '{workerService}', NULL, NULL, NULL, NULL, NULL, '[]'::jsonb, '{Guid.NewGuid()}')" })
                Assert.Equal("42501", await Denied(api, sql));
            foreach (var login in new[] { worker, api })
                foreach (var table in new[] { "delegations", "services", "service_bindings", "service_pins", "service_manifests", "permits", "authority_fences" })
                {
                    Assert.Equal("42501", await Denied(login, $"SELECT * FROM iam_workflow.{table}"));
                    Assert.Equal("42501", await Denied(login, $"DELETE FROM iam_workflow.{table}"));
                }

            // ---- 7. Rotation keeps at most two valid pins; retirement blocks the worker generation ----
            Assert.False(await Manifest("rotate", workerService, null, null, "[]"));
            Assert.Equal("KD023", await DeniedSetup(() => Manifest("rotate", workerService, null, null, "[]")));
            var beforeRetire = await Epoch();
            Assert.False(await Manifest("retire", workerService, null, null, "[]"));
            Assert.Equal(beforeRetire + 1, await Epoch());
            Assert.Equal("KD017", await DeniedInRoot(api, root => new WorkflowAuthorityFunctionPort(api, root).IssuePermitAsync(Request(fourth), ct)));
            Assert.Equal("KD001", await DeniedInRoot(worker, root => new WorkflowAuthorityFunctionPort(worker, root).AcquireFenceAsync(installation, ct)));

            // ---- local helpers ----
            async Task<bool> Manifest(string kind, Guid service, string? serviceKind, string? login, string bindings)
            {
                await using var command = new NpgsqlCommand("SELECT duplicate FROM iam_workflow.apply_service_manifest(@sha, @kind, @installation, " +
                    "@service, @service_kind, @login::name, @pin, clock_timestamp() - interval '1 minute', clock_timestamp() + interval '1 hour', " +
                    "@bindings::jsonb, @audit)", setup);
                command.Parameters.AddWithValue("sha", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
                command.Parameters.AddWithValue("kind", kind); command.Parameters.AddWithValue("installation", installation);
                command.Parameters.AddWithValue("service", service);
                command.Parameters.AddWithValue("service_kind", (object?)serviceKind ?? DBNull.Value);
                command.Parameters.AddWithValue("login", (object?)login ?? DBNull.Value);
                command.Parameters.AddWithValue("pin", kind == "bootstrap" && service == workerService ? workerPin : Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
                command.Parameters.AddWithValue("bindings", bindings); command.Parameters.AddWithValue("audit", Guid.NewGuid());
                return (bool)(await command.ExecuteScalarAsync(ct))!;
            }

            async Task<Seeded> Seed(Guid? environmentOverride = null)
            {
                var now = DateTimeOffset.UtcNow.AddMinutes(-1);
                var specification = new WorkflowSpecification(installation, tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    environmentOverride ?? environment, Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test,
                    now, now.AddHours(1), now.AddMinutes(3));
                var step = Guid.NewGuid();
                await using (var command = new NpgsqlCommand("INSERT INTO wfl.instances (installation_id, tenant_id, instance_id, request_id, " +
                    "specification_hash, specification, state, version, created_at_utc, deadline_utc, deadline_exceeded) VALUES (@i, @t, @n, @r, @h, @s, 1, 1, @c, @d, false); " +
                    "INSERT INTO wfl.steps (installation_id, tenant_id, instance_id, step_id, ordinal, state, version) VALUES (@i, @t, @n, @step, 0, 1, 1)", setup))
                {
                    command.Parameters.AddWithValue("i", installation); command.Parameters.AddWithValue("t", tenant);
                    command.Parameters.AddWithValue("n", specification.InstanceId); command.Parameters.AddWithValue("r", specification.RequestId);
                    command.Parameters.AddWithValue("h", specification.SpecificationHash); command.Parameters.AddWithValue("s", specification.WriteCanonical());
                    command.Parameters.AddWithValue("c", specification.CreatedAtUtc); command.Parameters.AddWithValue("d", specification.DeadlineUtc);
                    command.Parameters.AddWithValue("step", step);
                    await command.ExecuteNonQueryAsync(ct);
                }
                var delegation = Guid.NewGuid();
                await InRoot(api, async root =>
                {
                    var made = await new WorkflowAuthorityFunctionPort(api, root).CreateDelegationAsync(new(delegation, tenant, specification.InstanceId,
                        actor, Guid.NewGuid(), Guid.NewGuid(), 1, specification.ProjectId, specification.EnvironmentId, specification.ReleaseId, 1,
                        specification.ReleaseHash, WorkflowSpecification.TemplateId, WorkflowSpecification.TemplateVersion,
                        specification.SpecificationHash, ["admit", "checkpoint", "timer_due", "cancel"], Encoding.UTF8.GetBytes("{\"g\":1}"),
                        DateTimeOffset.UtcNow.AddMinutes(10)), ct);
                    Assert.Equal((delegation, 1L, false), (made.DelegationId, made.Generation, made.Duplicate));
                    await root.CommitAsync(ct);
                });
                var seeded = new Seeded(new WorkflowIdentity(installation, tenant, specification.InstanceId), step, Guid.NewGuid(),
                    specification.SpecificationHash, delegation, specification.RequestId);
                await SeedBinding(seeded, 1, delegation, 0, 1);
                return seeded;
            }

            async Task SeedBinding(Seeded seeded, long generation, Guid delegation, long expected, long resulting)
            {
                // Fixture-only WFL owner rows (the owner stores write them in the start/reauthorization roots).
                await using var command = new NpgsqlCommand("INSERT INTO wfl.authority_bindings (installation_id, tenant_id, instance_id, binding_id, " +
                    "delegation_id, actor_id, generation, recorded_at_utc, request_id, command_hash, specification_hash, expected_version, resulting_version) " +
                    "VALUES (@i, @t, @n, @b, @d, @a, @g, clock_timestamp(), @r, @c, @h, @e, @x)", setup);
                command.Parameters.AddWithValue("i", installation); command.Parameters.AddWithValue("t", tenant);
                command.Parameters.AddWithValue("n", seeded.Identity.InstanceId); command.Parameters.AddWithValue("b", Guid.NewGuid());
                command.Parameters.AddWithValue("d", delegation); command.Parameters.AddWithValue("a", actor); command.Parameters.AddWithValue("g", generation);
                command.Parameters.AddWithValue("r", generation == 1 ? seeded.Request : Guid.NewGuid());
                command.Parameters.AddWithValue("c", new string('c', 64)); command.Parameters.AddWithValue("h", seeded.Hash);
                command.Parameters.AddWithValue("e", expected); command.Parameters.AddWithValue("x", resulting);
                await command.ExecuteNonQueryAsync(ct);
            }

            WorkflowPermitRequest Request(Seeded seeded) => new(Guid.NewGuid(), workerService, tenant, seeded.Identity.InstanceId, seeded.Step,
                seeded.Operation, 1, seeded.Hash, WorkflowSpecification.TemplateHash, "admit", boot, 0, workerPin);

            async Task<WorkflowPermitIssued> Issue(WorkflowPermitRequest request)
            {
                WorkflowPermitIssued? result = null;
                await InRoot(api, async root => { result = await new WorkflowAuthorityFunctionPort(api, root).IssuePermitAsync(request, ct); await root.CommitAsync(ct); });
                return result!;
            }

            Task Consume(NpgsqlConnection connection, NpgsqlTransaction root, Seeded seeded, Guid permitId) =>
                new WorkflowAuthorityFunctionPort(connection, root).ConsumePermitAsync(new(permitId, seeded.Operation,
                    seeded.Identity.InstanceId, 1, seeded.Hash, boot, 0), ct);

            Task Stage(NpgsqlConnection connection, NpgsqlTransaction root, Seeded seeded, Guid permitId) =>
                new WorkflowTransitionFunctionPort(connection, root).StageTransitionAsync(permitId, seeded.Identity, seeded.Step,
                    seeded.Operation, seeded.Hash, 1, Kordra.Modules.WorkflowOrchestration.Domain.WorkflowState.Running, ct);

            async Task Admit(Seeded seeded, Guid permitId) => await InRoot(worker, async root =>
            {
                await new WorkflowAuthorityFunctionPort(worker, root).AcquireFenceAsync(installation, ct);
                await Consume(worker, root, seeded, permitId);
                await Stage(worker, root, seeded, permitId);
                await new AuditBoundAppendFunctionPort(worker, root).AppendAsync(permitId, seeded.Operation, seeded.Hash,
                    new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation, actor, tenant, seeded.Identity.InstanceId,
                        "workflow.start", "environment", environment, 1, 1, "worker.admit", DateTimeOffset.UtcNow, Guid.NewGuid()), ct);
                await root.CommitAsync(ct);
            });

            async Task<(int State, long Version)> StateOf(Seeded seeded)
            {
                await using var command = new NpgsqlCommand($"SELECT state, version FROM wfl.instances WHERE instance_id = '{seeded.Identity.InstanceId}'", setup);
                await using var reader = await command.ExecuteReaderAsync(ct);
                Assert.True(await reader.ReadAsync(ct));
                return (reader.GetInt32(0), reader.GetInt64(1));
            }

            Task<long> Epoch() => Scalar<long>(setup, $"SELECT epoch FROM iam_workflow.authority_fences WHERE installation_id = '{installation}'");

            async Task InRoot(NpgsqlConnection connection, Func<NpgsqlTransaction, Task> action)
            {
                // Disposal rolls back an uncommitted root; a root the action committed is left as is.
                await using var root = await connection.BeginTransactionAsync(ct);
                await action(root);
            }

            async Task<string> DeniedInRoot(NpgsqlConnection connection, Func<NpgsqlTransaction, Task> action)
            {
                await using var root = await connection.BeginTransactionAsync(ct);
                var error = await Assert.ThrowsAsync<PostgresException>(() => action(root));
                await root.RollbackAsync(ct);
                return error.SqlState;
            }

            async Task<string> DeniedSetup(Func<Task> action) => (await Assert.ThrowsAsync<PostgresException>(action)).SqlState;

            Task<string> Denied(NpgsqlConnection connection, string sql) => DeniedInRoot(connection, root => Execute(connection, sql, ct, root));
        }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await Execute(setup, scripts.Single(x => x.Name == "cleanup.sql").Sql, cleanup.Token);
            }
        }
    }

    private sealed record Seeded(WorkflowIdentity Identity, Guid Step, Guid Operation, string Hash, Guid Delegation, Guid Request);

    internal static IReadOnlyList<(string Name, string Sql)> Scripts(string suffix)
    {
        using var wfl = WorkflowDbContext.CreateOfflineOriginalBindingModel();
        using var aud = new AuditDbContext(AuditDbContextFactory.CreateOfflineMigrationOptions(
            "Host=127.0.0.1;Database=kordra;Username=kordra_admin;Timeout=1"));
        return [
            ("baseline-wfl.sql", wfl.Database.GenerateCreateScript()),
            ("baseline-aud.sql", aud.Database.GenerateCreateScript() + "\n" + OwnerFunctionPoc.IamFloorTable),
            ("iam-workflow-authority-v3.sql", OwnerFunctionPoc.ForRun(OwnerFunctionPoc.Sql("iam-workflow-authority-v3"), suffix)),
            ("wfl-owner-functions-v2.sql", OwnerFunctionPoc.ForRun(OwnerFunctionPoc.Sql("wfl-owner-functions-v2"), suffix)),
            ("aud-bound-append-v1.sql", OwnerFunctionPoc.ForRun(OwnerFunctionPoc.Sql("aud-bound-append-v1"), suffix)),
            ("cleanup.sql", "DROP SCHEMA wfl CASCADE;\nDROP SCHEMA iam_workflow CASCADE;\nDROP SCHEMA app CASCADE;\n")
        ];
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? root = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, root);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
