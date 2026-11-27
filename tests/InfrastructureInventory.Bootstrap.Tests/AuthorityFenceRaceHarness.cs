using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>
/// 12.3c (D4) shared PostgreSQL race harness for an approved disposable database whose owner schemas are already migrated:
/// applies the reviewed IAM v2 fence artifact, registers the fixture login as an <c>api</c> service and a run-scoped worker
/// service, and creates a probe delegation. <see cref="RaceAsync"/> holds the installation fence on another root, requires the
/// writer entry point to be observed waiting on that lock, releases it, then checks the epoch delta and that a permit issued
/// before the writer is rejected with <c>KD005</c> exactly when the writer advanced.
/// </summary>
internal sealed class AuthorityFenceRaceHarness : IAsyncDisposable
{
    private readonly string locator;
    private readonly string workerLogin;
    private readonly NpgsqlConnection setup;
    private readonly CancellationToken ct;
    private readonly Guid workerService = Guid.NewGuid(), tenant = Guid.NewGuid(), environment = Guid.NewGuid(), probeInstance = Guid.NewGuid();
    private readonly string specHash = new('a', 64);
    // 12.3d D3: the worker's registered certificate pin, presented with every probe permit.
    private readonly string workerPin = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private readonly HashSet<string> proven = new(StringComparer.Ordinal);

    private AuthorityFenceRaceHarness(string locator, string workerLogin, NpgsqlConnection setup, CancellationToken ct)
    {
        this.locator = locator; this.workerLogin = workerLogin; this.setup = setup; this.ct = ct;
    }

    internal Guid InstallationId { get; } = Guid.NewGuid();
    internal Guid WorkerServiceId => workerService;
    internal IReadOnlyCollection<string> Proven => proven;

    internal static async Task<AuthorityFenceRaceHarness> StartAsync(string locator, string suffix, string fenceScript, CancellationToken ct)
    {
        var workerLogin = new NpgsqlConnectionStringBuilder(locator)
            { Username = OwnerFunctionPoc.Role("kordra_worker_runtime", suffix), Password = null }.ConnectionString;
        var setup = new NpgsqlConnection(locator);
        var harness = new AuthorityFenceRaceHarness(locator, workerLogin, setup, ct);
        try
        {
            await setup.OpenAsync(ct);
            await using (var command = new NpgsqlCommand(fenceScript, setup)) await command.ExecuteNonQueryAsync(ct);
            await harness.ManifestAsync(Guid.NewGuid(), "api", "kordra_test", "[]");
            await harness.ManifestAsync(harness.workerService, "worker", OwnerFunctionPoc.Role("kordra_worker_runtime", suffix), JsonSerializer.Serialize(new[] {
                new Dictionary<string, object> { ["tenant_id"] = harness.tenant, ["environment_id"] = harness.environment,
                    ["template_id"] = WorkflowSpecification.TemplateId, ["template_version"] = WorkflowSpecification.TemplateVersion,
                    ["transition_kind"] = "admit" } }));
            await harness.InApiRootAsync(async (connection, root) => await new WorkflowAuthorityFunctionPort(connection, root).CreateDelegationAsync(new(
                Guid.NewGuid(), harness.tenant, harness.probeInstance, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(),
                harness.environment, Guid.NewGuid(), 1, new string('b', 64), WorkflowSpecification.TemplateId, WorkflowSpecification.TemplateVersion,
                harness.specHash, ["admit"], Encoding.UTF8.GetBytes("{\"probe\":1}"), DateTimeOffset.UtcNow.AddMinutes(30)), ct));
            return harness;
        }
        catch { await harness.DisposeAsync(); throw; }
    }

    internal Task<long> EpochAsync() => Scalar<long>($"SELECT epoch FROM iam_workflow.authority_fences WHERE installation_id = '{InstallationId}'");

    /// <summary>(a) waits on the held fence, then (b) advances by exactly one with KD005 for an older permit, or (c) leaves it.</summary>
    internal async Task RaceAsync(string scenario, bool advances, Func<Task<bool>> writer)
    {
        var permit = await IssueProbeAsync();
        var before = await EpochAsync();
        await using (var holder = new NpgsqlConnection(locator))
        {
            await holder.OpenAsync(ct);
            await using var held = await holder.BeginTransactionAsync(ct);
            await new WorkflowAuthorityFunctionPort(holder, held).AcquireFenceAsync(InstallationId, ct);
            var running = writer();
            // The writer must be observed WAITING on the fence lock before the holder releases it (lock_timeout 1 s).
            var waited = false;
            for (var poll = 0; poll < 150 && !running.IsCompleted; poll++)
            {
                if (await Scalar<long>("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() " +
                    "AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()") > 0) { waited = true; break; }
                await Task.Delay(20, ct);
            }
            Assert.True(waited, scenario + " did not wait for the held fence");
            Assert.False(running.IsCompleted, scenario);
            await held.RollbackAsync(ct);
            Assert.True(await running, scenario + " entry point did not return its expected result");
        }
        Assert.Equal(before + (advances ? 1 : 0), await EpochAsync());
        var consumed = await ConsumeAsync(permit);
        // consume_permit checks the epoch before expiry: an advance always yields KD005; without one the permit is never
        // rejected for its epoch (it may only have expired if the writer was slow).
        if (advances) Assert.Equal("KD005", consumed); else Assert.NotEqual("KD005", consumed);
        proven.Add(scenario);
    }

    private async Task ManifestAsync(Guid service, string kind, string login, string bindings)
    {
        await using var command = new NpgsqlCommand("SELECT * FROM iam_workflow.apply_service_manifest(@sha, 'bootstrap', @installation, " +
            "@service, @kind, @login::name, @pin, clock_timestamp() - interval '1 minute', clock_timestamp() + interval '1 hour', @bindings::jsonb, @audit)", setup);
        command.Parameters.AddWithValue("sha", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        command.Parameters.AddWithValue("installation", InstallationId); command.Parameters.AddWithValue("service", service);
        command.Parameters.AddWithValue("kind", kind); command.Parameters.AddWithValue("login", login);
        command.Parameters.AddWithValue("pin", service == workerService ? workerPin : Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        command.Parameters.AddWithValue("bindings", bindings); command.Parameters.AddWithValue("audit", Guid.NewGuid());
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task InApiRootAsync(Func<NpgsqlConnection, NpgsqlTransaction, Task> action)
    {
        await using var connection = new NpgsqlConnection(locator);
        await connection.OpenAsync(ct);
        await using var root = await connection.BeginTransactionAsync(ct);
        await action(connection, root);
        await root.CommitAsync(ct);
    }

    private async Task<(Guid Permit, Guid Operation)> IssueProbeAsync()
    {
        var operation = Guid.NewGuid(); var permit = Guid.NewGuid();
        await InApiRootAsync(async (connection, root) => await new WorkflowAuthorityFunctionPort(connection, root).IssuePermitAsync(new(permit,
            workerService, tenant, probeInstance, Guid.NewGuid(), operation, 1, specHash, WorkflowSpecification.TemplateHash, "admit",
            InstallationId, 0, workerPin), ct));
        return (permit, operation);
    }

    private async Task<string?> ConsumeAsync((Guid Permit, Guid Operation) probe)
    {
        await using var worker = new NpgsqlConnection(workerLogin);
        await worker.OpenAsync(ct);
        await using var root = await worker.BeginTransactionAsync(ct);
        try
        {
            await new WorkflowAuthorityFunctionPort(worker, root).ConsumePermitAsync(new(probe.Permit, probe.Operation, probeInstance, 1,
                specHash, InstallationId, 0), ct);
            await root.RollbackAsync(ct);
            return null;
        }
        catch (PostgresException error) { return error.SqlState; }
    }

    private async Task<T> Scalar<T>(string sql)
    {
        await using var command = new NpgsqlCommand(sql, setup);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    public ValueTask DisposeAsync() => setup.DisposeAsync();
}
