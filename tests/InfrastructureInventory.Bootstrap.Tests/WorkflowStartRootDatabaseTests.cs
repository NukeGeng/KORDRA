using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowStartRootDatabaseTests
{
    private static readonly Guid CaseId = Guid.Parse("5199a689-d4f5-491d-9825-0cc82f2c36bf");
    private const string Schema = "t12_5199a689d4f5491d98250cc82f2c36bf";

    [Fact]
    public void ExportStartRootSqlWithoutOpeningConnection()
    {
        var scripts = Scripts();
        Assert.Equal(5, scripts.Count);
        Assert.All(scripts.Values, sql => Assert.Contains(Schema, sql));
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_START_ROOT_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in scripts) File.WriteAllText(Path.Combine(directory, name), sql);
    }

    [WorkflowStartRootFact]
    public async Task AdmittedStartRequiresExactAuditPublicationAndCurrentAuthority()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_START_ROOT_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private start root fixture locator required.");
        var scripts = Scripts();
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("KORDRA_WFL_START_ROOT_MANIFEST")!));
        Assert.Equal("approved_for_execution", approval.RootElement.GetProperty("status").GetString());
        Assert.Equal(CaseId, approval.RootElement.GetProperty("caseId").GetGuid());
        foreach (var (name, sql) in scripts)
            Assert.Equal(approval.RootElement.GetProperty("sha256").GetProperty(name).GetString(),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));

        await using var setup = IsolatedWorkflowFixture.Connection(locator, CaseId);
        await setup.OpenAsync(ct);
        await using (var check = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", setup))
        {
            check.Parameters.AddWithValue("schema", Schema);
            Assert.Equal(0L, await check.ExecuteScalarAsync(ct));
        }
        var created = false;
        var started = false;
        using var host = WorkflowTransactionSchemaTests.BuildNativeProfile(locator, Schema);
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                await Sql("CREATE SCHEMA " + Schema, ddl);
                foreach (var name in new[] { "wfl.sql", "aud.sql", "msg-owned.sql", "msg-native.sql" })
                    await Sql(scripts[name], ddl);
                await ddl.CommitAsync(ct);
                created = true;
            }
            await host.StartAsync(ct);
            started = true;
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            var now = DateTimeOffset.UtcNow;
            var spec = new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64),
                WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
            var command = new WorkflowStartCommand(CaseId, CaseId, Guid.NewGuid(), spec.ProjectId, spec.EnvironmentId,
                spec.RequestId, spec.ReleaseId, 1, spec.ReleaseHash, WorkflowSpecification.TemplateId,
                WorkflowSpecification.TemplateVersion, spec.DeadlineUtc, "fixture-start-root");

            foreach (var mode in new[] { "audit", "publication", "revoked", "commit", "duplicate" })
            {
                await using var connection = IsolatedWorkflowFixture.Connection(locator, CaseId);
                await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                var gate = new FairAsyncReaderWriterAdmissionGate();
                var current = true;
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedStartTestAsync(connection, transaction,
                    CaseId, new MessageContext(runtime), gate, ct);
                var stored = await root.StageStartAsync(command,
                    mode == "duplicate" ? () => throw new InvalidOperationException("Duplicate allocated again.") : () => spec,
                    _ => Task.FromResult(current), now, ct);
                if (mode == "duplicate")
                {
                    Assert.True(stored.Duplicate);
                    Assert.Equal(spec.InstanceId, stored.InstanceId);
                    continue;
                }
                Assert.False(stored.Duplicate);
                var audit = new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation,
                    command.ActorId, CaseId, spec.InstanceId, "workflow.start", "environment", spec.EnvironmentId,
                    1, 1, "workflow-start-" + command.BusinessHash, now, spec.RequestId);
                if (mode == "audit")
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => root.StageAuditAsync(audit with { SubjectRef = Guid.NewGuid() }, ct));
                    continue;
                }
                await root.StageAuditAsync(audit, ct);
                await root.StageClockFloorAsync(now, ct);
                await using var context = IsolatedWorkflowFixture.CreateStartFixture(connection, CaseId);
                await context.Database.UseTransactionAsync(transaction, ct);
                var step = await context.Set<WorkflowStepRecord>().Where(x => x.InstanceId == spec.InstanceId && x.Ordinal == 0)
                    .Select(x => x.StepId).SingleAsync(ct);
                var payload = new WorkflowTransitionRequestedV1(spec.InstanceId, step, 1, 1, spec.RequestId,
                    mode == "publication" ? new string('b', 64) : spec.SpecificationHash, WorkflowSpecification.TemplateHash,
                    WorkflowTransitionKind.Checkpoint, null, null);
                using var bytes = new MemoryStream();
                using (var writer = new Utf8JsonWriter(bytes)) new WorkflowTransitionRequestedCodec().Write(writer, payload);
                var registry = WorkflowWakePublication.CreateRegistry(new Refuse());
                var codec = new MessageEnvelopeCodec(registry);
                var binding = new MessageBinding(new(CaseId, MessageScope.Tenant(CaseId), spec.RequestId, step, 1,
                    WorkflowWakePublication.TypeId), 1, new("workflow", spec.InstanceId), now, now.AddSeconds(120));
                var wire = codec.Write(binding, new(Guid.NewGuid(), spec.RequestId, Guid.NewGuid()), bytes.ToArray());
                var envelope = codec.Read(wire, MessageEnvelopeCodec.ContentType, CaseId, MessageScope.Tenant(CaseId), now);
                if (mode == "publication")
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => root.StagePublicationAsync(registry, envelope, now, ct));
                    continue;
                }
                await root.StagePublicationAsync(registry, envelope, now, ct);
                current = mode != "revoked";
                Assert.Equal(current ? WorkflowCommitDisposition.Committed : WorkflowCommitDisposition.NoCommitAttempt,
                    await root.CommitAsync(ct));
                Assert.False(gate.IsClosed);
            }

            foreach (var table in new[] { "instances", "request_receipts", "start_receipts", "aud_intents", "publication_receipts" })
            {
                await using var count = new NpgsqlCommand($"SELECT count(*) FROM {Schema}.{table}", setup);
                Assert.Equal(1L, await count.ExecuteScalarAsync(ct));
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (started) await host.StopAsync(cleanup.Token);
            if (created)
            {
                await using var drop = new NpgsqlCommand(scripts["cleanup.sql"], setup);
                await drop.ExecuteNonQueryAsync(cleanup.Token);
            }
        }

        async Task Sql(string sql, NpgsqlTransaction transaction)
        {
            await using var command = new NpgsqlCommand(sql, setup, transaction) { CommandTimeout = 3 };
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static Dictionary<string, string> Scripts()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        using var workflow = WorkflowDbContext.CreateStartFixture(connection, CaseId);
        using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId);
        using var host = WorkflowTransactionSchemaTests.BuildNativeProfile(
            "Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1", Schema);
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(host);
        var database = Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>());
        return new()
        {
            ["wfl.sql"] = workflow.Database.GenerateCreateScript(),
            ["aud.sql"] = audit.Database.GenerateCreateScript(),
            ["msg-owned.sql"] = OwnedMessagingSchema.TablesSql.Replace("msg.", Schema + ".", StringComparison.Ordinal),
            ["msg-native.sql"] = WorkflowTransactionSchemaTests.StableNativeScript(database, Schema),
            ["cleanup.sql"] = $"DROP SCHEMA {Schema} CASCADE;\n"
        };
    }

    private sealed class Refuse : IMessageConsumer<WorkflowTransitionRequestedV1>
    {
        public ValueTask HandleAsync(WorkflowTransitionRequestedV1 message, CancellationToken ct) =>
            throw new InvalidOperationException("No listener allowed in this fixture.");
    }
}

public sealed class WorkflowStartRootFactAttribute : FactAttribute
{
    public WorkflowStartRootFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_START_ROOT_MANIFEST")))
            Skip = "Requires exact approval for isolated start WFL/AUD/MSG transaction.";
    }
}
