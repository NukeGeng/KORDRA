using System.Text.Json;
using System.Text.RegularExpressions;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>
/// T12.3a gate on actual PostgreSQL (operation-gated, ADR-0025 disposable database + run-scoped roles, D4): least-privilege
/// worker/API logins reach owner state only through owner functions, in one atomic root, with DB01/AU02/fence-race subsets.
/// </summary>
public sealed class OwnerFunctionPocDatabaseTests
{
    private const string ManifestVariable = "KORDRA_OWNER_FN_POC_MANIFEST";
    private const string LocatorVariable = "KORDRA_OWNER_FN_POC_CONNECTION_STRING";

    [Fact]
    public void ExportRunScriptsWithoutDatabaseAccess()
    {
        var scripts = OwnerFunctionPoc.Scripts("0123456789ab");
        Assert.Equal(["baseline-wfl.sql", "baseline-aud.sql", "iam-workflow-authority-v1.sql", "wfl-owner-functions-v1.sql",
            "aud-bound-append-v1.sql"], scripts.Select(x => x.Name));
        Assert.Contains("CREATE TABLE wfl.instances", scripts[0].Sql);
        Assert.Contains("CREATE TABLE app.aud_intents", scripts[1].Sql);
        Assert.All(scripts.Skip(2), x => Assert.Contains("_r0123456789ab", x.Sql));
        if (Environment.GetEnvironmentVariable("KORDRA_OWNER_FN_POC_EXPORT") is not { Length: > 0 } directory) return;
        var suffix = Environment.GetEnvironmentVariable("KORDRA_OWNER_FN_POC_SUFFIX") ?? throw new InvalidOperationException("Run suffix required.");
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in OwnerFunctionPoc.Scripts(suffix)) File.WriteAllText(Path.Combine(directory, name), sql);
    }

    [OwnerFunctionPocDatabaseFact]
    public async Task LeastPrivilegeLoginsReachOwnerStateOnlyThroughOwnerFunctionsInOneRoot()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = budget.Token;
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable(ManifestVariable)!));
        var approved = approval.RootElement;
        Assert.Equal("approved_for_execution", approved.GetProperty("status").GetString());
        var suffix = approved.GetProperty("suffix").GetString()!;
        var database = approved.GetProperty("database").GetString()!;
        Assert.Matches(new Regex("^kordra_test_[0-9a-f]{32}$"), database);
        var scripts = OwnerFunctionPoc.Scripts(suffix);
        foreach (var (name, sql) in scripts)
            Assert.Equal(approved.GetProperty("sha256").GetProperty(name).GetString(), OwnerFunctionPoc.Sha256(sql));

        var locator = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(LocatorVariable)
            ?? throw new InvalidOperationException("Approved fixture locator required.")) { Pooling = false, Multiplexing = false, Timeout = 5 };
        Assert.Equal("127.0.0.1", locator.Host); Assert.Equal(database, locator.Database); Assert.Equal("kordra_test", locator.Username);
        string Login(string placeholder) => new NpgsqlConnectionStringBuilder(locator.ConnectionString)
            { Username = OwnerFunctionPoc.Role(placeholder, suffix), Password = null, ApplicationName = "kordra-t12-3a-poc" }.ConnectionString;
        var workerRole = OwnerFunctionPoc.Role("kordra_worker_runtime", suffix);
        var apiRole = OwnerFunctionPoc.Role("kordra_api_runtime", suffix);

        await using var setup = new NpgsqlConnection(locator.ConnectionString);
        await setup.OpenAsync(ct);
        Assert.Equal(database, await Scalar<string>(setup, "SELECT current_database()"));
        Assert.Equal(0L, await Scalar<long>(setup, "SELECT count(*) FROM pg_namespace WHERE nspname IN ('wfl', 'iam_workflow')"));

        // Run-scoped roles exist with the approved attributes (created by the approved runner, D4).
        foreach (var placeholder in OwnerFunctionPoc.Roles)
        {
            var role = OwnerFunctionPoc.Role(placeholder, suffix);
            await using var command = new NpgsqlCommand("SELECT rolsuper, rolcreaterole, rolcreatedb, rolbypassrls, rolinherit, rolcanlogin, " +
                "rolreplication FROM pg_roles WHERE rolname = @r", setup);
            command.Parameters.AddWithValue("r", role);
            await using var reader = await command.ExecuteReaderAsync(ct);
            Assert.True(await reader.ReadAsync(ct), role);
            Assert.False(reader.GetBoolean(0)); Assert.False(reader.GetBoolean(1)); Assert.False(reader.GetBoolean(2));
            Assert.False(reader.GetBoolean(3)); Assert.False(reader.GetBoolean(4)); Assert.False(reader.GetBoolean(6));
            Assert.Equal(placeholder.EndsWith("_runtime", StringComparison.Ordinal), reader.GetBoolean(5));
        }

        await using (var ddl = await setup.BeginTransactionAsync(ct))
        {
            foreach (var (_, sql) in scripts) await Execute(setup, sql, ct, ddl);
            await ddl.CommitAsync(ct);
        }

        // Owner-side function metadata: definer, fixed search_path, run-scoped owner, no PUBLIC execute.
        await using (var command = new NpgsqlCommand("SELECT p.oid::regprocedure::text, p.prosecdef, p.proconfig, pg_get_userbyid(p.proowner), " +
            "has_function_privilege('public', p.oid, 'EXECUTE') FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace " +
            "WHERE n.nspname IN ('iam_workflow', 'wfl', 'app') AND p.prokind = 'f'", setup))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            var count = 0;
            while (await reader.ReadAsync(ct))
            {
                count++;
                Assert.True(reader.GetBoolean(1), reader.GetString(0));
                Assert.Contains("search_path=pg_catalog, pg_temp", (string[])reader.GetValue(2));
                Assert.EndsWith("_owner_r" + suffix, reader.GetString(3));
                Assert.False(reader.GetBoolean(4), reader.GetString(0));
            }
            Assert.Equal(8, count);
        }

        // Fixture seed through the privileged setup login (never granted to a runtime login).
        var installation = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var workerService = Guid.NewGuid();
        var apiService = Guid.NewGuid();
        var boot = Guid.NewGuid();
        await Execute(setup, $"INSERT INTO iam_workflow.authority_fences VALUES ('{installation}', 1, 1)", ct);
        await Execute(setup, $"INSERT INTO iam_workflow.service_logins VALUES ('{workerRole}', '{workerService}', 1, '{installation}', true), " +
            $"('{apiRole}', '{apiService}', 1, '{installation}', true)", ct);

        await using var worker = new NpgsqlConnection(Login("kordra_worker_runtime"));
        await worker.OpenAsync(ct);
        await using var api = new NpgsqlConnection(Login("kordra_api_runtime"));
        await api.OpenAsync(ct);
        Assert.Equal(workerRole, await Scalar<string>(worker, "SELECT session_user::text"));

        // ---- Positive: one atomic worker root, with in-root idempotency ----
        var first = await SeedWorkflow();
        var permit = await SeedPermit(first, workerService);
        var intent = Intent(first, AuditIntentStage.Mutation);
        await using (var root = await worker.BeginTransactionAsync(ct))
        {
            var iam = new WorkflowAuthorityFunctionPort(worker, root);
            Assert.Equal(1L, await iam.AcquireFenceAsync(installation, ct));
            var consumed = await iam.ConsumePermitAsync(Claim(first, permit), ct);
            Assert.False(consumed.Duplicate); Assert.Equal(tenant, consumed.TenantId);
            Assert.True((await iam.ConsumePermitAsync(Claim(first, permit), ct)).Duplicate);
            var wfl = new WorkflowTransitionFunctionPort(worker, root);
            var staged = await wfl.StageTransitionAsync(permit, first.Identity, first.Step, first.Operation, first.Hash, 1, WorkflowState.Running, ct);
            Assert.False(staged.Duplicate); Assert.Equal(2L, staged.Version);
            Assert.True((await wfl.StageTransitionAsync(permit, first.Identity, first.Step, first.Operation, first.Hash, 1, WorkflowState.Running, ct)).Duplicate);
            var aud = new AuditBoundAppendFunctionPort(worker, root);
            var appended = await aud.AppendAsync(permit, first.Operation, first.Hash, intent, ct);
            Assert.False(appended.Duplicate); Assert.Equal(AuditIntentCanonicalizer.ComputeHash(intent), appended.CanonicalHashHex);
            var again = await aud.AppendAsync(permit, first.Operation, first.Hash, intent, ct);
            Assert.True(again.Duplicate); Assert.Equal(appended.RecordId, again.RecordId);
            var floorAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            Assert.Equal(floorAt.UtcTicks / 10, (await iam.AdvanceFloorAsync(installation, floorAt, ct)).UtcTicks / 10);
            await root.CommitAsync(ct);
        }
        await AssertWorkflow(first, WorkflowState.Running, 2, transitions: 1);
        Assert.Equal(1L, await Scalar<long>(setup, $"SELECT count(*) FROM app.aud_intents WHERE tenant_ref = '{tenant}' AND resource_ref = '{first.Identity.InstanceId}'"));
        Assert.Equal(AuditIntentCanonicalizer.ComputeHash(intent),
            Convert.ToHexStringLower(await Scalar<byte[]>(setup, $"SELECT canonical_intent_hash FROM app.aud_intents WHERE intent_id = '{intent.IntentId}'")));
        // Stored occurred time equals the Npgsql (in-process AUD) truncation of 100 ns ticks.
        var occurred = await Scalar<DateTime>(setup, $"SELECT occurred_at_utc FROM app.aud_intents WHERE intent_id = '{intent.IntentId}'");
        Assert.Equal(intent.OccurredAtUtc.UtcTicks - intent.OccurredAtUtc.UtcTicks % 10, occurred.Ticks);
        Assert.Equal(1L, await Scalar<long>(setup, $"SELECT count(*) FROM iam_workflow.permits WHERE permit_id = '{permit}' AND consumed_xact IS NOT NULL"));
        Assert.Equal(1L, await Scalar<long>(setup, $"SELECT count(*) FROM app.iam_authority_clock_floors WHERE installation_id = '{installation}'"));

        // ---- Atomicity: a late owner failure leaves no trace and releases the permit ----
        var second = await SeedWorkflow();
        var secondPermit = await SeedPermit(second, workerService);
        await Assert.ThrowsAsync<PostgresException>(() => InRoot(worker, async root => {
            var iam = new WorkflowAuthorityFunctionPort(worker, root);
            await iam.AcquireFenceAsync(installation, ct);
            await iam.ConsumePermitAsync(Claim(second, secondPermit), ct);
            await new WorkflowTransitionFunctionPort(worker, root).StageTransitionAsync(secondPermit, second.Identity, second.Step,
                second.Operation, second.Hash, 1, WorkflowState.Running, ct);
            await new AuditBoundAppendFunctionPort(worker, root).AppendAsync(secondPermit, second.Operation, second.Hash, Intent(second, AuditIntentStage.Mutation), ct);
            await iam.AdvanceFloorAsync(installation, DateTimeOffset.UtcNow.AddMinutes(5), ct); // KD008
        }));
        await AssertWorkflow(second, WorkflowState.Pending, 1, transitions: 0);
        Assert.Equal(0L, await Scalar<long>(setup, $"SELECT count(*) FROM iam_workflow.permits WHERE permit_id = '{secondPermit}' AND consumed_xact IS NOT NULL"));
        Assert.Equal(0L, await Scalar<long>(setup, $"SELECT count(*) FROM app.aud_intents WHERE resource_ref = '{second.Identity.InstanceId}'"));
        Assert.Equal("KD201", await DeniedInRoot(worker, async root => {
            var iam = new WorkflowAuthorityFunctionPort(worker, root);
            await iam.ConsumePermitAsync(Claim(second, secondPermit), ct);
            await using var bad = new NpgsqlCommand("SELECT * FROM app.aud_append_bound_intent(@p, @o, @h, @i, @x)", worker, root);
            bad.Parameters.AddWithValue("p", secondPermit); bad.Parameters.AddWithValue("o", second.Operation); bad.Parameters.AddWithValue("h", second.Hash);
            bad.Parameters.AddWithValue("i", AuditBoundAppendFunctionPort.CanonicalFor(Intent(second, AuditIntentStage.Mutation)).CanonicalJson);
            bad.Parameters.AddWithValue("x", new byte[32]);
            await bad.ExecuteNonQueryAsync(ct);
        }));

        // ---- DB01: no raw owner access, no DDL, no privilege escalation, for both runtime logins ----
        foreach (var login in new[] { worker, api })
        {
            foreach (var sql in new[] {
                "SELECT * FROM wfl.instances", "SELECT * FROM wfl.transitions",
                $"UPDATE wfl.instances SET version = version + 1 WHERE tenant_id = '{tenant}'",
                $"INSERT INTO wfl.transitions SELECT * FROM wfl.transitions LIMIT 0",
                "SELECT * FROM app.aud_intents", "UPDATE app.aud_intents SET safe_reason = 'x'", "DELETE FROM app.aud_intents",
                "SELECT * FROM iam_workflow.permits", "UPDATE iam_workflow.authority_fences SET epoch = epoch + 1",
                "UPDATE iam_workflow.permits SET consumed_xact = NULL, consumed_at = NULL",
                "INSERT INTO iam_workflow.service_logins SELECT * FROM iam_workflow.service_logins LIMIT 0",
                "SELECT * FROM app.iam_authority_clock_floors",
                "CREATE TABLE public.kordra_poc_probe (i integer)", "CREATE TEMP TABLE kordra_poc_probe (i integer)",
                "CREATE SCHEMA kordra_poc_probe", "CREATE TABLE wfl.kordra_poc_probe (i integer)",
                "DROP FUNCTION wfl.stage_transition(uuid, uuid, uuid, uuid, uuid, uuid, text, bigint, integer)",
                "ALTER FUNCTION iam_workflow.acquire_fence(uuid) SECURITY INVOKER",
                $"SET ROLE {OwnerFunctionPoc.Role("kordra_wfl_owner", suffix)}",
                $"SET SESSION AUTHORIZATION {OwnerFunctionPoc.Role("kordra_iam_owner", suffix)}",
                $"SELECT * FROM iam_workflow.require_consumed('{permit}', '{first.Operation}', '{first.Hash}')" })
                Assert.Contains(await Denied(login, sql), new[] { "42501", "42P01" });
        }
        Assert.Equal("42501", await Denied(worker, $"SELECT iam_workflow.advance_epoch('{installation}', 'probe')"));
        Assert.Equal("42501", await Denied(api, $"SELECT * FROM iam_workflow.consume_permit('{permit}', '{first.Operation}', " +
            $"'{first.Identity.InstanceId}', 1, '{first.Hash}', '{boot}', 0)"));
        Assert.Equal("42501", await Denied(api, $"SELECT * FROM wfl.stage_transition('{permit}', '{installation}', '{tenant}', " +
            $"'{first.Identity.InstanceId}', '{first.Step}', '{first.Operation}', '{first.Hash}', 1, 2)"));
        Assert.Equal("42501", await Denied(api, "SELECT * FROM app.aud_append_bound_intent(gen_random_uuid(), gen_random_uuid(), 'x', '{}', ''::bytea)"));

        // Spoofed GUC/session settings and a hostile search_path do not change owner decisions.
        var third = await SeedWorkflow();
        var thirdPermit = await SeedPermit(third, workerService);
        await Execute(setup, "CREATE FUNCTION public.sha256(bytea) RETURNS bytea LANGUAGE sql IMMUTABLE AS $$ SELECT '\\x00'::bytea $$; " +
            "CREATE FUNCTION public.pg_current_xact_id() RETURNS xid8 LANGUAGE sql AS $$ SELECT '1'::xid8 $$; " +
            "GRANT EXECUTE ON FUNCTION public.sha256(bytea), public.pg_current_xact_id() TO PUBLIC", ct);
        await InRoot(worker, async root => {
            await Execute(worker, $"SET LOCAL search_path = public, pg_catalog; SET LOCAL kordra.tenant_id = '{Guid.NewGuid()}'; " +
                $"SET LOCAL kordra.installation_id = '{Guid.NewGuid()}'", ct, root);
            var iam = new WorkflowAuthorityFunctionPort(worker, root);
            await iam.AcquireFenceAsync(installation, ct);
            await iam.ConsumePermitAsync(Claim(third, thirdPermit), ct);
            await new WorkflowTransitionFunctionPort(worker, root).StageTransitionAsync(thirdPermit, third.Identity, third.Step,
                third.Operation, third.Hash, 1, WorkflowState.Running, ct);
            await new AuditBoundAppendFunctionPort(worker, root).AppendAsync(thirdPermit, third.Operation, third.Hash, Intent(third, AuditIntentStage.Mutation), ct);
            await root.CommitAsync(ct);
        });
        await AssertWorkflow(third, WorkflowState.Running, 2, transitions: 1);
        await Execute(setup, "DROP FUNCTION public.sha256(bytea); DROP FUNCTION public.pg_current_xact_id()", ct);

        // ---- AU02 subset: every mismatched/expired/foreign permit is denied without a state change ----
        var target = await SeedWorkflow();
        var good = await SeedPermit(target, workerService);
        var foreignOperation = Guid.NewGuid();
        var foreign = await SeedPermit(target, apiService, operation: foreignOperation);
        var expiredOperation = Guid.NewGuid();
        var expired = await SeedPermit(target, workerService, operation: expiredOperation, expired: true);
        foreach (var (claim, code) in new (WorkflowPermitClaim, string)[] {
            (Claim(target, good) with { InstanceId = Guid.NewGuid() }, "KD003"),
            (Claim(target, good) with { OperationId = Guid.NewGuid() }, "KD003"),
            (Claim(target, good) with { ExpectedVersion = 2 }, "KD003"),
            (Claim(target, good) with { SpecificationHash = new string('f', 64) }, "KD003"),
            (Claim(target, good) with { BootId = Guid.NewGuid() }, "KD003"),
            (Claim(target, good) with { CustodyEpoch = 1 }, "KD003"),
            (Claim(target, foreign) with { OperationId = foreignOperation }, "KD003"),
            (Claim(target, expired) with { OperationId = expiredOperation }, "KD006") })
        {
            Assert.Equal(code, await DeniedInRoot(worker, async root => {
                var iam = new WorkflowAuthorityFunctionPort(worker, root);
                await iam.AcquireFenceAsync(installation, ct);
                await iam.ConsumePermitAsync(claim, ct);
            }));
        }
        Assert.Equal("KD101", await DeniedInRoot(worker, async root => {
            await new WorkflowAuthorityFunctionPort(worker, root).ConsumePermitAsync(Claim(target, good), ct);
            await new WorkflowTransitionFunctionPort(worker, root).StageTransitionAsync(good,
                new WorkflowIdentity(installation, Guid.NewGuid(), target.Identity.InstanceId), target.Step, target.Operation, target.Hash, 1, WorkflowState.Running, ct);
        }));
        Assert.Equal("KD108", await DeniedInRoot(worker, async root => {
            await new WorkflowAuthorityFunctionPort(worker, root).ConsumePermitAsync(Claim(target, good), ct);
            await new WorkflowTransitionFunctionPort(worker, root).StageTransitionAsync(good, target.Identity, target.Step, target.Operation,
                target.Hash, 1, WorkflowState.Cancelled, ct);
        }));
        Assert.Equal("KD204", await DeniedInRoot(worker, async root => {
            await new WorkflowAuthorityFunctionPort(worker, root).ConsumePermitAsync(Claim(target, good), ct);
            await new AuditBoundAppendFunctionPort(worker, root).AppendAsync(good, target.Operation, target.Hash,
                Intent(target, AuditIntentStage.Mutation) with { TenantRef = Guid.NewGuid() }, ct);
        }));
        // Consumed in another committed root: a new root can neither consume it again nor stage with it.
        await InRoot(worker, async root => {
            await new WorkflowAuthorityFunctionPort(worker, root).ConsumePermitAsync(Claim(target, good), ct);
            await root.CommitAsync(ct);
        });
        Assert.Equal("KD004", await DeniedInRoot(worker, root => new WorkflowAuthorityFunctionPort(worker, root).ConsumePermitAsync(Claim(target, good), ct)));
        Assert.Equal("KD007", await DeniedInRoot(worker, root => new WorkflowTransitionFunctionPort(worker, root).StageTransitionAsync(good,
            target.Identity, target.Step, target.Operation, target.Hash, 1, WorkflowState.Running, ct)));
        await AssertWorkflow(target, WorkflowState.Pending, 1, transitions: 0);

        // ---- Fence: worker holds -> writer waits; writer committed first -> stale permit denied; bounded lock wait ----
        var raced = await SeedWorkflow();
        var racedPermit = await SeedPermit(raced, workerService);
        await using (var held = await worker.BeginTransactionAsync(ct))
        {
            await new WorkflowAuthorityFunctionPort(worker, held).AcquireFenceAsync(installation, ct);
            await using var writerRoot = await api.BeginTransactionAsync(ct);
            var writer = new WorkflowAuthorityFunctionPort(api, writerRoot);
            var waiting = writer.AcquireFenceAsync(installation, ct);
            await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            Assert.False(waiting.IsCompleted);
            await held.RollbackAsync(ct);
            Assert.Equal(1L, await waiting);
            Assert.Equal(2L, await writer.AdvanceEpochAsync(installation, "poc.revoke", ct));
            await writerRoot.CommitAsync(ct);
        }
        Assert.Equal("KD005", await DeniedInRoot(worker, async root => {
            var iam = new WorkflowAuthorityFunctionPort(worker, root);
            Assert.Equal(2L, await iam.AcquireFenceAsync(installation, ct));
            await iam.ConsumePermitAsync(Claim(raced, racedPermit), ct);
        }));
        await using (var held = await worker.BeginTransactionAsync(ct))
        {
            await new WorkflowAuthorityFunctionPort(worker, held).AcquireFenceAsync(installation, ct);
            var started = System.Diagnostics.Stopwatch.StartNew();
            Assert.Equal("55P03", await DeniedInRoot(api, root => new WorkflowAuthorityFunctionPort(api, root).AcquireFenceAsync(installation, ct)));
            Assert.InRange(started.Elapsed, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(3));
            await held.RollbackAsync(ct);
        }
        await AssertWorkflow(raced, WorkflowState.Pending, 1, transitions: 0);

        // ---- Changed content for a recorded operation/intent conflicts ----
        var conflict = await SeedWorkflow();
        var conflictPermit = await SeedPermit(conflict, workerService, epoch: 2);
        var conflictIntent = Intent(conflict, AuditIntentStage.Mutation);
        Assert.Equal("KD103", await DeniedInRoot(worker, async root => {
            var iam = new WorkflowAuthorityFunctionPort(worker, root);
            await iam.ConsumePermitAsync(Claim(conflict, conflictPermit), ct);
            var wfl = new WorkflowTransitionFunctionPort(worker, root);
            await wfl.StageTransitionAsync(conflictPermit, conflict.Identity, conflict.Step, conflict.Operation, conflict.Hash, 1, WorkflowState.Running, ct);
            await wfl.StageTransitionAsync(conflictPermit, conflict.Identity, conflict.Step, conflict.Operation, conflict.Hash, 1, WorkflowState.BlockedAuthority, ct);
        }));
        Assert.Equal("KD203", await DeniedInRoot(worker, async root => {
            await new WorkflowAuthorityFunctionPort(worker, root).ConsumePermitAsync(Claim(conflict, conflictPermit), ct);
            var aud = new AuditBoundAppendFunctionPort(worker, root);
            await aud.AppendAsync(conflictPermit, conflict.Operation, conflict.Hash, conflictIntent, ct);
            await aud.AppendAsync(conflictPermit, conflict.Operation, conflict.Hash, conflictIntent with { SafeReason = "worker.other" }, ct);
        }));
        await AssertWorkflow(conflict, WorkflowState.Pending, 1, transitions: 0);

        // ---- local helpers ----
        AuditIntent Intent(Seeded seeded, AuditIntentStage stage) => new(1, Guid.NewGuid(), Guid.NewGuid(), stage, Guid.NewGuid(), tenant,
            seeded.Identity.InstanceId, "workflow.start", "environment", Guid.NewGuid(), 1, 1, "worker.admit",
            DateTimeOffset.UtcNow.AddTicks(7), Guid.NewGuid());

        WorkflowPermitClaim Claim(Seeded seeded, Guid permitId) =>
            new(permitId, seeded.Operation, seeded.Identity.InstanceId, 1, seeded.Hash, boot, 0);

        async Task<Seeded> SeedWorkflow()
        {
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var specification = new WorkflowSpecification(installation, tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
            var step = Guid.NewGuid();
            await using var command = new NpgsqlCommand("INSERT INTO wfl.instances (installation_id, tenant_id, instance_id, request_id, " +
                "specification_hash, specification, state, version, created_at_utc, deadline_utc, deadline_exceeded) VALUES (@i, @t, @n, @r, @h, @s, 1, 1, @c, @d, false); " +
                "INSERT INTO wfl.steps (installation_id, tenant_id, instance_id, step_id, ordinal, state, version) VALUES (@i, @t, @n, @step, 0, 1, 1)", setup);
            command.Parameters.AddWithValue("i", installation); command.Parameters.AddWithValue("t", tenant);
            command.Parameters.AddWithValue("n", specification.InstanceId); command.Parameters.AddWithValue("r", specification.RequestId);
            command.Parameters.AddWithValue("h", specification.SpecificationHash); command.Parameters.AddWithValue("s", specification.WriteCanonical());
            command.Parameters.AddWithValue("c", specification.CreatedAtUtc); command.Parameters.AddWithValue("d", specification.DeadlineUtc);
            command.Parameters.AddWithValue("step", step);
            await command.ExecuteNonQueryAsync(ct);
            return new(new WorkflowIdentity(installation, tenant, specification.InstanceId), step, Guid.NewGuid(), specification.SpecificationHash);
        }

        async Task<Guid> SeedPermit(Seeded seeded, Guid service, Guid? operation = null, bool expired = false, long epoch = 1)
        {
            var id = Guid.NewGuid();
            await using var command = new NpgsqlCommand("INSERT INTO iam_workflow.permits (permit_id, service_id, generation, installation_id, " +
                "tenant_id, instance_id, operation_id, expected_version, specification_hash, transition_kind, authority_epoch, boot_id, " +
                "custody_epoch, issued_at, expires_at) VALUES (@id, @s, 1, @i, @t, @n, @o, 1, @h, 'admit', @e, @b, 0, " +
                (expired ? "clock_timestamp() - interval '3 seconds', clock_timestamp() - interval '2 seconds')"
                         : "clock_timestamp(), clock_timestamp() + interval '2 seconds')"), setup);
            command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("s", service);
            command.Parameters.AddWithValue("i", installation); command.Parameters.AddWithValue("t", tenant);
            command.Parameters.AddWithValue("n", seeded.Identity.InstanceId); command.Parameters.AddWithValue("o", operation ?? seeded.Operation);
            command.Parameters.AddWithValue("h", seeded.Hash); command.Parameters.AddWithValue("e", epoch); command.Parameters.AddWithValue("b", boot);
            await command.ExecuteNonQueryAsync(ct);
            return id;
        }

        async Task AssertWorkflow(Seeded seeded, WorkflowState state, long version, long transitions)
        {
            Assert.Equal((int)state, await Scalar<int>(setup, $"SELECT state FROM wfl.instances WHERE instance_id = '{seeded.Identity.InstanceId}'"));
            Assert.Equal(version, await Scalar<long>(setup, $"SELECT version FROM wfl.instances WHERE instance_id = '{seeded.Identity.InstanceId}'"));
            Assert.Equal(transitions, await Scalar<long>(setup, $"SELECT count(*) FROM wfl.transitions WHERE instance_id = '{seeded.Identity.InstanceId}'"));
        }

        async Task InRoot(NpgsqlConnection connection, Func<NpgsqlTransaction, Task> action)
        {
            await using var root = await connection.BeginTransactionAsync(ct);
            await action(root);
            if (root.Connection is not null) await root.RollbackAsync(ct);
        }

        async Task<string> DeniedInRoot(NpgsqlConnection connection, Func<NpgsqlTransaction, Task> action)
        {
            await using var root = await connection.BeginTransactionAsync(ct);
            var error = await Assert.ThrowsAsync<PostgresException>(() => action(root));
            await root.RollbackAsync(ct);
            return error.SqlState;
        }

        Task<string> Denied(NpgsqlConnection connection, string sql) => DeniedInRoot(connection, root => Execute(connection, sql, ct, root));
    }

    private sealed record Seeded(WorkflowIdentity Identity, Guid Step, Guid Operation, string Hash);

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

public sealed class OwnerFunctionPocDatabaseFactAttribute : FactAttribute
{
    public OwnerFunctionPocDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KORDRA_OWNER_FN_POC_MANIFEST") is not { Length: > 0 })
            Skip = "T12.3a owner-function PoC needs an approved disposable database, run-scoped roles and manifest.";
    }
}
