using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

/// <summary>
/// 12.2f owner store on actual PostgreSQL (operation-gated). Generation 1 is fixture data (D5: start writes it in 12.3b).
/// </summary>
public sealed class WorkflowReauthorizationDatabaseTests
{
    private static readonly Guid CaseId = Guid.Parse("898b9390-5b13-4ea5-a613-e747a88c23e7");
    private const string Schema = "t12_898b93905b134ea5a613e747a88c23e7";

    [Fact]
    public void ExportReviewedSqlWithoutDatabaseAccess()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        var scripts = Scripts(connection, offlineMetadata: true);
        Assert.All(scripts.Values, sql => Assert.Contains(Schema, sql));
        Assert.DoesNotContain("wfl.", scripts["expand.sql"]);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_REAUTH_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in scripts) File.WriteAllText(Path.Combine(directory, name), sql);
    }

    [WorkflowReauthorizationDatabaseFact]
    public async Task BlockedWorkflowRecordsOneGenerationIdempotentlyAndNeverResumesThroughGenericTransition()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_REAUTH_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved fixture locator required.");
        var cs = new NpgsqlConnectionStringBuilder(locator) { Pooling = false, Multiplexing = false }.ConnectionString;
        await using var setup = WorkflowModelFixture.Connection(cs, CaseId);
        var scripts = Scripts(setup);
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("KORDRA_WFL_REAUTH_MANIFEST")!));
        var approved = approval.RootElement;
        Assert.Equal("approved_for_execution", approved.GetProperty("status").GetString());
        Assert.Equal(CaseId, approved.GetProperty("caseId").GetGuid());
        Assert.Equal(Schema, approved.GetProperty("schema").GetString());
        foreach (var (name, sql) in scripts)
            Assert.Equal(approved.GetProperty("sha256").GetProperty(name).GetString(),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
        await setup.OpenAsync(ct);
        Assert.Equal(0, await NamespaceCount(setup, ct));
        var created = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                foreach (var name in new[] { "create.sql", "baseline.sql", "expand.sql" })
                    await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct);
                created = true;
            }
            // Seven-digit canonical times (PR29-WFL-TIMESTAMP-002) must still bind.
            var clock = DateTimeOffset.UtcNow;
            var now = new DateTimeOffset(clock.UtcTicks - clock.UtcTicks % 10 - 3, TimeSpan.Zero);
            var spec = new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64),
                WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
            var identity = new WorkflowIdentity(CaseId, CaseId, spec.InstanceId);
            var actor = Guid.NewGuid();
            var request = Guid.NewGuid();
            var at = now.AddMinutes(1);
            WorkflowReauthorizationCommand Make(long version, Guid? who = null, Guid? key = null, Guid? tenant = null) =>
                new(CaseId, tenant ?? CaseId, spec.InstanceId, key ?? request, who ?? actor, version, spec.SpecificationHash);
            var delegation = new WorkflowDelegationReference(Guid.NewGuid(), now.AddMinutes(30));

            await using var writer = WorkflowModelFixture.Connection(cs, CaseId);
            await writer.OpenAsync(ct);
            await Root(async (context, _) => { await new WorkflowStore(context).StageCreationAsync(spec, now, ct); return true; }, commit: true);
            await SeedOriginalBinding();
            await AssertState(WorkflowState.Pending, 1, 1);

            // Not yet blocked: Pending is not eligible even for the original actor.
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(1), commit: false));
            var step = await FirstStep();
            await Root(async (context, _) => {
                await new WorkflowStore(context).StageTransitionAsync(identity, step, Guid.NewGuid(), spec.SpecificationHash, 1,
                    WorkflowState.BlockedAuthority, at, ct); return true; }, commit: true);
            await AssertState(WorkflowState.BlockedAuthority, 2, 1);

            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(2, who: Guid.NewGuid()), commit: false));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(2, tenant: Guid.NewGuid()), commit: false));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Stage(Make(1, key: Guid.NewGuid()), commit: false));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(2), commit: false, effective: spec.DeadlineUtc));
            await Assert.ThrowsAsync<ArgumentException>(() => Stage(Make(2), commit: false, reference: delegation with { DelegationId = Guid.Empty }));
            // Unresolved work inside the same root still rejects, then rolls back.
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(2), commit: false,
                before: $"UPDATE {Schema}.steps SET state={(int)WorkflowStepState.Unknown} WHERE step_id='{step:D}'"));
            await AssertState(WorkflowState.BlockedAuthority, 2, 1);

            var rolledBack = await Stage(Make(2), commit: false);
            Assert.False(rolledBack.Duplicate); Assert.Equal(2, rolledBack.Generation);
            await AssertState(WorkflowState.BlockedAuthority, 2, 1);

            var first = await Stage(Make(2), commit: true);
            Assert.False(first.Duplicate); Assert.Equal(2, first.Generation); Assert.Equal(3, first.ResultingVersion);
            await AssertState(WorkflowState.BlockedAuthority, 3, 2);
            var duplicate = await Stage(Make(2), commit: true, reference: new WorkflowDelegationReference(Guid.NewGuid(), now.AddMinutes(5)));
            Assert.True(duplicate.Duplicate); Assert.Equal(first.BindingId, duplicate.BindingId); Assert.Equal(first.Generation, duplicate.Generation);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(3), commit: false));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Stage(Make(2, key: Guid.NewGuid()), commit: false));
            await AssertState(WorkflowState.BlockedAuthority, 3, 2);

            // Generic transition can no longer resume work; narrowing edges remain.
            await Assert.ThrowsAsync<InvalidOperationException>(() => Root(async (context, _) => {
                await new WorkflowStore(context).StageTransitionAsync(identity, step, Guid.NewGuid(), spec.SpecificationHash, 3,
                    WorkflowState.Running, at.AddMinutes(1), ct); return true; }, commit: false));
            await Root(async (context, _) => {
                await new WorkflowStore(context).StageTransitionAsync(identity, step, Guid.NewGuid(), spec.SpecificationHash, 3,
                    WorkflowState.Expired, at.AddMinutes(1), ct); return true; }, commit: false);
            await AssertState(WorkflowState.BlockedAuthority, 3, 2);
            Assert.Equal(spec.WriteCanonical(), await Canonical());

            // PR32 review: a concurrent execution of one request blocks on the owner lock until the first root commits,
            // then observes the recorded receipt (so the coordinator never reissues delegation twice).
            var raced = Make(3, key: Guid.NewGuid());
            await using var rival = WorkflowModelFixture.Connection(cs, CaseId);
            await rival.OpenAsync(ct);
            await using (var holderContext = WorkflowModelFixture.CreateAuthorizationFixture(writer, CaseId))
            await using (var holder = await writer.BeginTransactionAsync(ct))
            {
                await holderContext.Database.UseTransactionAsync(holder, ct);
                var holderStore = new WorkflowAuthorityBindingStore(holderContext);
                Assert.True(await holderStore.LockOwnerAsync(raced, ct));
                Assert.Null(await holderStore.ReadRecordedAsync(raced, ct));
                await using var rivalContext = WorkflowModelFixture.CreateAuthorizationFixture(rival, CaseId);
                await using var rivalRoot = await rival.BeginTransactionAsync(ct);
                await rivalContext.Database.UseTransactionAsync(rivalRoot, ct);
                var rivalStore = new WorkflowAuthorityBindingStore(rivalContext);
                var waiting = rivalStore.LockOwnerAsync(raced, ct);
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                Assert.False(waiting.IsCompleted);
                var won = await holderStore.StageReauthorizationAsync(raced, delegation, at, ct);
                Assert.False(won.Duplicate); Assert.Equal(3, won.Generation);
                await holder.CommitAsync(ct);
                Assert.True(await waiting);
                var observed = await rivalStore.ReadRecordedAsync(raced, ct);
                Assert.NotNull(observed); Assert.True(observed!.Duplicate); Assert.Equal(won.BindingId, observed.BindingId);
                await rivalRoot.RollbackAsync(ct);
            }
            await AssertState(WorkflowState.BlockedAuthority, 4, 3);

            async Task<WorkflowAuthorityBindingResult> Stage(WorkflowReauthorizationCommand command, bool commit,
                DateTimeOffset? effective = null, WorkflowDelegationReference? reference = null, string? before = null)
            {
                WorkflowAuthorityBindingResult? result = null;
                await Root(async (context, root) => {
                    if (before is not null) await Execute(writer, before, ct, root);
                    result = await new WorkflowAuthorityBindingStore(context).StageReauthorizationAsync(command,
                        reference ?? delegation, effective ?? at, ct);
                    return true;
                }, commit);
                return result!;
            }

            async Task Root(Func<WorkflowDbContext, NpgsqlTransaction, Task<bool>> action, bool commit)
            {
                await using var context = WorkflowModelFixture.CreateAuthorizationFixture(writer, CaseId);
                await using var root = await writer.BeginTransactionAsync(ct);
                await context.Database.UseTransactionAsync(root, ct);
                await action(context, root);
                if (commit) await root.CommitAsync(ct); else await root.RollbackAsync(ct);
            }

            async Task SeedOriginalBinding()
            {
                // Fixture-only generation 1 (12.3b writes it in the start root). Direct SQL bypasses the owner guard on purpose.
                await using var command = new NpgsqlCommand($"INSERT INTO {Schema}.authority_bindings (installation_id, tenant_id, instance_id, " +
                    "binding_id, delegation_id, actor_id, generation, recorded_at_utc, request_id, command_hash, specification_hash, " +
                    "expected_version, resulting_version) VALUES (@id, @id, @instance, @binding, @delegation, @actor, 1, @at, @request, " +
                    "@hash, @spec, 1, 2)", setup);
                command.Parameters.AddWithValue("id", CaseId); command.Parameters.AddWithValue("instance", spec.InstanceId);
                command.Parameters.AddWithValue("binding", Guid.NewGuid()); command.Parameters.AddWithValue("delegation", Guid.NewGuid());
                command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("at", now);
                command.Parameters.AddWithValue("request", spec.RequestId); command.Parameters.AddWithValue("hash", new string('c', 64));
                command.Parameters.AddWithValue("spec", spec.SpecificationHash);
                Assert.Equal(1, await command.ExecuteNonQueryAsync(ct));
            }

            async Task<Guid> FirstStep()
            {
                await using var command = new NpgsqlCommand($"SELECT step_id FROM {Schema}.steps WHERE instance_id=@id AND ordinal=0", setup);
                command.Parameters.AddWithValue("id", spec.InstanceId);
                return (Guid)(await command.ExecuteScalarAsync(ct))!;
            }

            async Task<byte[]> Canonical()
            {
                await using var command = new NpgsqlCommand($"SELECT specification FROM {Schema}.instances WHERE instance_id=@id", setup);
                command.Parameters.AddWithValue("id", spec.InstanceId);
                return (byte[])(await command.ExecuteScalarAsync(ct))!;
            }

            async Task AssertState(WorkflowState state, long version, long bindings)
            {
                await using var command = new NpgsqlCommand($"SELECT state, version, (SELECT count(*) FROM {Schema}.authority_bindings " +
                    $"WHERE instance_id=@id), deadline_exceeded FROM {Schema}.instances WHERE instance_id=@id", setup);
                command.Parameters.AddWithValue("id", spec.InstanceId);
                await using var reader = await command.ExecuteReaderAsync(ct);
                Assert.True(await reader.ReadAsync(ct));
                Assert.Equal((int)state, reader.GetInt32(0)); Assert.Equal(version, reader.GetInt64(1));
                Assert.Equal(bindings, reader.GetInt64(2)); Assert.False(reader.GetBoolean(3));
            }
        }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0, await NamespaceCount(setup, cleanup.Token));
            }
        }
    }

    internal static Dictionary<string, string> Scripts(NpgsqlConnection connection, bool offlineMetadata = false)
    {
        using var baseline = offlineMetadata ? WorkflowDbContext.CreateReconciliationFixture(connection, CaseId)
            : WorkflowModelFixture.CreateReconciliationFixture(connection, CaseId);
        return new()
        {
            ["create.sql"] = $"CREATE SCHEMA {Schema};\n",
            ["baseline.sql"] = baseline.Database.GenerateCreateScript(),
            ["expand.sql"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v6-authorization.sql"))
                .Replace("wfl.", Schema + ".", StringComparison.Ordinal),
            ["cleanup.sql"] = $"DROP SCHEMA {Schema} CASCADE;\n"
        };
    }

    private static async Task<long> NamespaceCount(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", connection);
        command.Parameters.AddWithValue("schema", Schema);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? root = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, root) { CommandTimeout = 3 };
        await command.ExecuteNonQueryAsync(ct);
    }
}

public sealed class WorkflowReauthorizationDatabaseFactAttribute : FactAttribute
{
    public WorkflowReauthorizationDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_REAUTH_MANIFEST")))
            Skip = "Requires exact approval for the 12.2f reauthorization PostgreSQL fixture.";
    }
}
