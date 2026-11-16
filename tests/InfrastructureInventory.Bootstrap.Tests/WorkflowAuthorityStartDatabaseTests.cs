using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Weasel.Core.Migrations;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>
/// 12.3b (D3/D4) on actual PostgreSQL with concrete owners (operation-gated; ADR-0025 disposable database + run-scoped roles):
/// the admitted start root records the IAM delegation and WFL authority generation 1 atomically; admitted reauthorization
/// revokes it, advances the authority epoch and records generation 2 in both owners.
/// </summary>
[Collection(LocalHostEnvironmentCollection.Name)]
public sealed class WorkflowAuthorityStartDatabaseTests
{
    private const string ManifestVariable = "KORDRA_WFL_AUTHORITY_MANIFEST";
    private const string LocatorVariable = "KORDRA_WFL_AUTHORITY_CONNECTION_STRING";
    private static Guid CaseId => WorkflowStartOwnerFixture.CaseId;
    private static string Schema => WorkflowStartOwnerFixture.Schema;

    [Fact]
    public void ExportAuthorityStartSqlWithoutOpeningConnections()
    {
        var scripts = Scripts("0123456789ab");
        Assert.Equal(14, scripts.Count);
        Assert.Contains("CREATE TABLE " + Schema + ".authority_bindings", scripts["wfl.sql"]);
        Assert.Contains("generation = 1 AND expected_version = 0 AND resulting_version = 1", scripts["wfl.sql"]);
        Assert.Contains("CREATE SCHEMA iam_workflow AUTHORIZATION kordra_iam_owner_r0123456789ab", scripts["iam-workflow-authority-v2.sql"]);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_AUTHORITY_EXPORT") is not { Length: > 0 } directory) return;
        var suffix = Environment.GetEnvironmentVariable("KORDRA_WFL_AUTHORITY_SUFFIX") ?? throw new InvalidOperationException("Run suffix required.");
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in Scripts(suffix)) File.WriteAllText(Path.Combine(directory, name), sql);
    }

    [WorkflowAuthorityDatabaseFact]
    public async Task AdmittedStartRecordsGenerationOneAndReauthorizationRecordsGenerationTwoInBothOwners()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable(LocatorVariable) ?? throw new InvalidOperationException("Exact fixture locator required.");
        var builder = new NpgsqlConnectionStringBuilder(locator);
        Assert.Equal("127.0.0.1", builder.Host); Assert.Equal(5432, builder.Port);
        Assert.Equal(IsolatedWorkflowFixture.ExpectedDatabase, builder.Database); Assert.Equal("kordra_test", builder.Username);
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable(ManifestVariable)!));
        Assert.Equal("approved_for_execution", approval.RootElement.GetProperty("status").GetString());
        var suffix = approval.RootElement.GetProperty("suffix").GetString()!;
        var scripts = Scripts(suffix);
        foreach (var (name, sql) in scripts)
            Assert.Equal(approval.RootElement.GetProperty("sha256").GetProperty(name).GetString(),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
        await using var setup = IsolatedWorkflowFixture.Connection(locator, CaseId); await setup.OpenAsync(ct);
        Assert.Equal(0L, await Scalar<long>(setup, $"SELECT count(*) FROM pg_namespace WHERE nspname IN ('{Schema}', 'iam_workflow', 'app')", ct));
        using var host = WorkflowTransactionSchemaTests.BuildNativeProfile(locator, Schema);
        var created = false; var started = false;
        WorkflowStartOwnerFixture? owners = null;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                await Execute(setup, "CREATE SCHEMA " + Schema, ct, ddl);
                foreach (var (_, script) in scripts.Where(x => x.Key != "cleanup.sql")) await Execute(setup, script, ct, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            // Offline owner manifest (D6): the start/reauthorization root login is the installation's API service.
            await Execute(setup, "SELECT * FROM iam_workflow.apply_service_manifest(@sha, 'bootstrap', @installation, @service, 'api', " +
                "'kordra_test'::name, @pin, clock_timestamp() - interval '1 minute', clock_timestamp() + interval '1 hour', '[]'::jsonb, @audit)", ct,
                null, ("sha", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))), ("installation", CaseId), ("service", Guid.NewGuid()),
                ("pin", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))), ("audit", Guid.NewGuid()));
            var gate = new FairAsyncReaderWriterAdmissionGate();
            owners = await WorkflowStartOwnerFixture.SeedAsync(locator, gate, ct);
            await host.StartAsync(ct); started = true;
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            var requestId = Guid.NewGuid(); var invocation = Guid.NewGuid();
            var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
            WorkflowSpecification? committed = null; WorkflowDelegationReference? delegation = null;
            foreach (var mode in new[] { "audit-failure", "commit", "duplicate" })
            {
                await using var held = await gate.AcquireReadAsync(ct);
                var admitted = await owners.Reader.ReadUnderAdmissionAsync(owners.Session, owners.Selection, requestId, invocation, ct);
                Assert.NotNull(admitted);
                var command = admitted.BindCommand(deadline, "synthetic-authority-start");
                var now = DateTimeOffset.UtcNow;
                WorkflowSpecification? spec = null;
                WorkflowSpecification Allocate() => spec = mode == "duplicate"
                    ? throw new InvalidOperationException("Duplicate allocated a second specification.")
                    : new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), requestId, owners.ProjectId, owners.ApplicationId,
                        owners.EnvironmentId, owners.TargetId, owners.ReleaseId, 1, owners.Selection.ReleaseHash, WorkflowEnvironment.Test,
                        now, deadline, now.AddSeconds(30));
                await using var connection = IsolatedWorkflowFixture.Connection(locator, CaseId); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateAuthorityStartTestAsync(connection, transaction, CaseId,
                    new MessageContext(runtime), gate, ct);
                var result = await root.StageAdmittedAuthorityStartAsync(admitted, owners.Reader, owners.Session, deadline,
                    "synthetic-authority-start", Allocate, now, owners.Principals, ct);
                if (mode == "duplicate")
                {
                    Assert.True(result.Start.Duplicate); Assert.Null(result.Delegation);
                    Assert.Equal(committed!.InstanceId, result.Start.InstanceId);
                    continue;
                }
                Assert.False(result.Start.Duplicate); Assert.NotNull(spec); Assert.NotNull(result.Delegation);
                Assert.Equal(1L, result.Delegation.Generation);
                Assert.True(result.Delegation.ValidUntilUtc <= deadline);
                var audit = admitted.CreateMutationAudit(spec, command, Guid.NewGuid());
                if (mode == "audit-failure")
                {
                    // A late failure in the same root leaves neither the WFL start nor the IAM delegation behind.
                    await Assert.ThrowsAsync<InvalidOperationException>(() => root.StageAuditAsync(audit with { PolicyRevision = audit.PolicyRevision + 1 }, ct));
                    continue;
                }
                await root.StageAuditAsync(audit, ct); await root.StageClockFloorAsync(now, ct);
                await using var workflow = IsolatedWorkflowFixture.CreateOriginalBindingFixture(connection, CaseId);
                await workflow.Database.UseTransactionAsync(transaction, ct);
                var step = await workflow.Set<WorkflowStepRecord>().Where(x => x.InstanceId == spec.InstanceId && x.Ordinal == 0)
                    .Select(x => x.StepId).SingleAsync(ct);
                var registry = WorkflowWakePublication.CreateRegistry(new Refuse()); var codec = new MessageEnvelopeCodec(registry);
                var payload = new WorkflowTransitionRequestedV1(spec.InstanceId, step, 1, 1, requestId,
                    spec.SpecificationHash, WorkflowSpecification.TemplateHash, WorkflowTransitionKind.Checkpoint, null, null);
                using var bytes = new MemoryStream();
                using (var writer = new Utf8JsonWriter(bytes)) new WorkflowTransitionRequestedCodec().Write(writer, payload);
                var binding = new MessageBinding(new(CaseId, MessageScope.Tenant(CaseId), requestId, step, 1,
                    WorkflowWakePublication.TypeId), 1, new("workflow", spec.InstanceId), now, now.AddSeconds(120));
                var wire = codec.Write(binding, new(Guid.NewGuid(), requestId, Guid.NewGuid()), bytes.ToArray());
                await root.StagePublicationAsync(registry,
                    codec.Read(wire, MessageEnvelopeCodec.ContentType, CaseId, MessageScope.Tenant(CaseId), now), now, ct);
                Assert.Equal(WorkflowCommitDisposition.Committed, await root.CommitAsync(ct));
                committed = spec; delegation = result.Delegation;
            }
            Assert.NotNull(committed); Assert.NotNull(delegation);
            var instance = committed.InstanceId;
            Assert.Equal(1L, await Scalar<long>(setup, $"SELECT count(*) FROM {Schema}.instances", ct));
            Assert.Equal(1L, await Scalar<long>(setup, "SELECT count(*) FROM iam_workflow.delegations", ct));
            Assert.Equal(1L, await Scalar<long>(setup, $"SELECT count(*) FROM iam_workflow.delegations WHERE instance_id = '{instance}' " +
                $"AND generation = 1 AND actor_id = '{owners.ActorId}' AND revoked_at IS NULL AND delegation_id = '{delegation.DelegationId}' " +
                $"AND allowed_kinds = ARRAY['admit','checkpoint','timer_due','cancel']::text[] AND authority_evidence_sha256 = sha256(authority_evidence)", ct));
            Assert.Equal(1L, await Scalar<long>(setup, $"SELECT count(*) FROM {Schema}.authority_bindings WHERE instance_id = '{instance}' " +
                $"AND generation = 1 AND delegation_id = '{delegation.DelegationId}' AND expected_version = 0 AND resulting_version = 1", ct));
            var epochBefore = await Scalar<long>(setup, $"SELECT epoch FROM iam_workflow.authority_fences WHERE installation_id = '{CaseId}'", ct);

            // Blocked by authority, then admitted reauthorization by the original actor (D4).
            await using (var connection = IsolatedWorkflowFixture.Connection(locator, CaseId))
            {
                await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var workflow = IsolatedWorkflowFixture.CreateOriginalBindingFixture(connection, CaseId);
                await workflow.Database.UseTransactionAsync(transaction, ct);
                var step = await workflow.Set<WorkflowStepRecord>().Where(x => x.InstanceId == instance && x.Ordinal == 0)
                    .Select(x => x.StepId).SingleAsync(ct);
                await new WorkflowStore(workflow).StageTransitionAsync(new WorkflowIdentity(CaseId, CaseId, instance), step, Guid.NewGuid(),
                    committed.SpecificationHash, 1, WorkflowState.BlockedAuthority, DateTimeOffset.UtcNow, ct);
                await transaction.CommitAsync(ct);
            }
            var reauthorization = new WorkflowReauthorizationCommand(CaseId, CaseId, instance, Guid.NewGuid(), owners.ActorId, 2,
                committed.SpecificationHash);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var connection = IsolatedWorkflowFixture.Connection(locator, CaseId); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                var outcome = await IsolatedWorkflowFixture.ReauthorizeAdmitted(connection, transaction, CaseId, reauthorization,
                    owners.ProjectId, owners.EnvironmentId, owners.Session, owners.Reader,
                    new WorkflowReleaseRevisionResolver(owners.ReleaseContexts), new IamWorkflowDelegationReissuer(owners.Principals), gate, ct);
                Assert.NotNull(outcome.Receipt); Assert.Equal(2L, outcome.Receipt.Generation);
                // First attempt commits; the retry returns the original receipt without a new delegation.
                Assert.Equal(attempt == 0 ? WorkflowCommitDisposition.Committed : WorkflowCommitDisposition.NoCommitAttempt, outcome.Disposition);
                Assert.Equal(attempt == 1, outcome.Receipt.Duplicate);
            }
            Assert.Equal(2L, await Scalar<long>(setup, $"SELECT count(*) FROM iam_workflow.delegations WHERE instance_id = '{instance}'", ct));
            Assert.Equal(1L, await Scalar<long>(setup, $"SELECT count(*) FROM iam_workflow.delegations WHERE instance_id = '{instance}' " +
                $"AND generation = 1 AND revoked_at IS NOT NULL AND revoke_reason = 'workflow.reauthorize'", ct));
            var second = await Scalar<Guid>(setup, $"SELECT delegation_id FROM iam_workflow.delegations WHERE instance_id = '{instance}' " +
                "AND generation = 2 AND revoked_at IS NULL", ct);
            Assert.Equal(1L, await Scalar<long>(setup, $"SELECT count(*) FROM {Schema}.authority_bindings WHERE instance_id = '{instance}' " +
                $"AND generation = 2 AND delegation_id = '{second}' AND expected_version = 2 AND resulting_version = 3", ct));
            Assert.Equal(epochBefore + 1, await Scalar<long>(setup, $"SELECT epoch FROM iam_workflow.authority_fences WHERE installation_id = '{CaseId}'", ct));
            Assert.Equal((int)WorkflowState.BlockedAuthority, await Scalar<int>(setup, $"SELECT state FROM {Schema}.instances WHERE instance_id = '{instance}'", ct));
            Assert.False(gate.IsClosed);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (owners is not null) await owners.DisposeAsync();
            if (started) await host.StopAsync(cleanup.Token);
            if (created) await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
        }
    }

    internal static Dictionary<string, string> Scripts(string suffix)
    {
        var scripts = WorkflowStartOwnerFixture.OwnerScripts();
        using var connection = new NpgsqlConnection(
            // Offline only; disposable selection validates the exact unpooled locator shape without opening it.
            $"Host=127.0.0.1;Port=5432;Database={IsolatedWorkflowFixture.ExpectedDatabase};Username=kordra_test;Password=SYNTHETIC;Pooling=false;Multiplexing=false");
        using var workflow = IsolatedWorkflowFixture.CreateOriginalBindingFixture(connection, CaseId);
        using var audit = IsolatedWorkflowFixture.AuditContext(connection, CaseId);
        using var host = WorkflowTransactionSchemaTests.BuildNativeProfile("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened", Schema);
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(host);
        scripts["wfl.sql"] = workflow.Database.GenerateCreateScript(); scripts["aud.sql"] = audit.Database.GenerateCreateScript();
        scripts["msg-owned.sql"] = OwnedMessagingSchema.TablesSql.Replace("msg.", Schema + ".", StringComparison.Ordinal);
        scripts["msg-native.sql"] = WorkflowTransactionSchemaTests.StableNativeScript(
            Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>()), Schema);
        scripts["app-floor.sql"] = "CREATE SCHEMA app;\n" + OwnerFunctionPoc.IamFloorTable;
        scripts["iam-workflow-authority-v2.sql"] = OwnerFunctionPoc.ForRun(OwnerFunctionPoc.Sql("iam-workflow-authority-v2"), suffix);
        scripts["cleanup.sql"] = $"DROP SCHEMA {Schema} CASCADE;\nDROP SCHEMA iam_workflow CASCADE;\nDROP SCHEMA app CASCADE;\n";
        return scripts;
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? root = null,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, root) { CommandTimeout = 5 };
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    private sealed class Refuse : IMessageConsumer<WorkflowTransitionRequestedV1>
    { public ValueTask HandleAsync(WorkflowTransitionRequestedV1 message, CancellationToken ct) => throw new InvalidOperationException("No fixture listener."); }
}

public sealed class WorkflowAuthorityDatabaseFactAttribute : FactAttribute
{
    public WorkflowAuthorityDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_AUTHORITY_MANIFEST")))
            Skip = "Requires the exact approved 12.3b authority fixture manifest, disposable database and run roles.";
    }
}
