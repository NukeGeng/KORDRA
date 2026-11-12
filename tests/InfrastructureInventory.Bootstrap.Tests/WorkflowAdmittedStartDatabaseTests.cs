using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
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
using Weasel.Core.Migrations;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

[Collection(LocalHostEnvironmentCollection.Name)]
public sealed class WorkflowAdmittedStartDatabaseTests
{
    private static Guid CaseId => WorkflowStartOwnerFixture.CaseId;
    private static string Schema => WorkflowStartOwnerFixture.Schema;

    [Fact]
    public void ExportCompleteStartOwnerSqlWithoutOpeningConnectionsOrStartingHost()
    {
        var scripts = Scripts();
        Assert.Equal(12, scripts.Count);
        Assert.All(scripts.Values, sql => Assert.Contains(Schema, sql));
        foreach (var script in scripts.Values)
        {
            Assert.DoesNotContain("CREATE ROLE", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CREATE DATABASE", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"app\".", script);
        }
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_ADMITTED_START_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in scripts) File.WriteAllText(Path.Combine(directory, name), sql);
    }

    [Fact]
    public void SyntheticApprovalPolicyUsesExplicitTestSelfApprovalOnly()
    {
        var policy = WorkflowStartOwnerFixture.ApprovalPolicy(new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero));
        using var document = JsonDocument.Parse(policy.GetCanonicalBytes());
        var rule = Assert.Single(document.RootElement.GetProperty("rules").EnumerateArray());
        Assert.Equal("Test", rule.GetProperty("classification").GetString());
        Assert.Equal("self_allowed", rule.GetProperty("separationOfDuties").GetString());
        Assert.True(rule.GetProperty("allowUnattestedProvenance").GetBoolean());
        Assert.Equal(300, rule.GetProperty("grantLifetimeSeconds").GetInt32());
    }

    [WorkflowAdmittedStartFact]
    public async Task ActualCurrentOwnersBindTypedStartAuditAndOriginalReceipt()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_ADMITTED_START_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Exact private fixture locator required.");
        var builder = new NpgsqlConnectionStringBuilder(locator);
        Assert.Equal("127.0.0.1", builder.Host); Assert.Equal(5432, builder.Port);
        Assert.Equal(IsolatedWorkflowFixture.ExpectedDatabase, builder.Database); Assert.Equal("kordra_test", builder.Username);
        var scripts = Scripts();
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("KORDRA_WFL_ADMITTED_START_MANIFEST")!));
        Assert.Equal("approved_for_execution", approval.RootElement.GetProperty("status").GetString());
        Assert.Equal(CaseId, approval.RootElement.GetProperty("caseId").GetGuid());
        foreach (var (name, sql) in scripts)
            Assert.Equal(approval.RootElement.GetProperty("sha256").GetProperty(name).GetString(),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
        await using var setup = IsolatedWorkflowFixture.Connection(locator, CaseId); await setup.OpenAsync(ct);
        await using (var check = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", setup))
        { check.Parameters.AddWithValue("schema", Schema); Assert.Equal(0L, await check.ExecuteScalarAsync(ct)); }
        using var host = WorkflowTransactionSchemaTests.BuildNativeProfile(locator, Schema);
        var created = false; var started = false;
        WorkflowStartOwnerFixture? owners = null;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                await Sql("CREATE SCHEMA " + Schema, ddl);
                foreach (var (name, script) in scripts.Where(x => x.Key != "cleanup.sql")) await Sql(script, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            var gate = new FairAsyncReaderWriterAdmissionGate();
            owners = await WorkflowStartOwnerFixture.SeedAsync(locator, gate, ct);
            await host.StartAsync(ct); started = true;
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            var requestId = Guid.NewGuid(); var invocation = Guid.NewGuid();
            var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
            WorkflowSpecification? committed = null;
            // Every read here uses the concrete REL/APR/current authority implementation. No fake eligibility result.
            foreach (var mode in new[] { "application", "target", "classification", "invocation", "policy", "assignment", "commit", "duplicate" })
            {
                await using var held = await gate.AcquireReadAsync(ct);
                var admitted = await owners.Reader.ReadUnderAdmissionAsync(owners.Session, owners.Selection, requestId, invocation, ct);
                Assert.NotNull(admitted);
                var command = admitted.BindCommand(deadline, "synthetic-admitted-start");
                var now = DateTimeOffset.UtcNow;
                WorkflowSpecification? spec = null;
                WorkflowSpecification Allocate()
                {
                    if (mode == "duplicate") throw new InvalidOperationException("Duplicate allocated a second specification.");
                    return spec = new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), requestId, owners.ProjectId,
                    mode == "application" ? Guid.NewGuid() : owners.ApplicationId, owners.EnvironmentId,
                    mode == "target" ? Guid.NewGuid() : owners.TargetId, owners.ReleaseId, 1, owners.Selection.ReleaseHash,
                    mode == "classification" ? WorkflowEnvironment.Development : WorkflowEnvironment.Test,
                    now, deadline, now.AddSeconds(30));
                }
                await using var connection = IsolatedWorkflowFixture.Connection(locator, CaseId); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedStartTestAsync(connection, transaction,
                    CaseId, new MessageContext(runtime), gate, ct);
                Task<WorkflowStoredResult> Stage() => root.StageAdmittedStartAsync(admitted, owners.Reader, owners.Session,
                    deadline, "synthetic-admitted-start", Allocate, now, ct);
                if (mode is "application" or "target" or "classification")
                { await Assert.ThrowsAsync<InvalidOperationException>(Stage); continue; }
                var stored = await Stage();
                if (mode == "duplicate")
                { Assert.True(stored.Duplicate); Assert.Equal(committed!.InstanceId, stored.InstanceId); continue; }
                Assert.False(stored.Duplicate);
                Assert.NotNull(spec);
                var audit = admitted.CreateMutationAudit(spec, command, Guid.NewGuid());
                if (mode is "invocation" or "policy" or "assignment")
                {
                    var altered = mode switch
                    {
                        "invocation" => audit with { InvocationId = Guid.NewGuid() },
                        "policy" => audit with { PolicyRevision = audit.PolicyRevision + 1 },
                        _ => audit with { AuthorityRevision = audit.AuthorityRevision + 1 }
                    };
                    await Assert.ThrowsAsync<InvalidOperationException>(() => root.StageAuditAsync(altered, ct)); continue;
                }
                await root.StageAuditAsync(audit, ct); await root.StageClockFloorAsync(now, ct);
                await using var workflow = IsolatedWorkflowFixture.CreateStartFixture(connection, CaseId);
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
                Assert.Equal(WorkflowCommitDisposition.Committed, await root.CommitAsync(ct)); committed = spec;
            }
            Assert.NotNull(committed);
            foreach (var table in new[] { "instances", "start_receipts", "aud_intents", "publication_receipts" })
            { await using var count = new NpgsqlCommand($"SELECT count(*) FROM {Schema}.{table}", setup); Assert.Equal(1L, await count.ExecuteScalarAsync(ct)); }
            Assert.False(gate.IsClosed);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (owners is not null) await owners.DisposeAsync();
            if (started) await host.StopAsync(cleanup.Token);
            if (created) { await using var drop = new NpgsqlCommand(scripts["cleanup.sql"], setup); await drop.ExecuteNonQueryAsync(cleanup.Token); }
        }
        async Task Sql(string sql, NpgsqlTransaction transaction)
        { await using var command = new NpgsqlCommand(sql, setup, transaction) { CommandTimeout = 3 }; await command.ExecuteNonQueryAsync(ct); }
    }

    internal static Dictionary<string, string> Scripts()
    {
        var scripts = WorkflowStartOwnerFixture.OwnerScripts();
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        using var workflow = WorkflowDbContext.CreateStartFixture(connection, CaseId);
        using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId);
        using var host = WorkflowTransactionSchemaTests.BuildNativeProfile("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened", Schema);
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(host);
        scripts["wfl.sql"] = workflow.Database.GenerateCreateScript(); scripts["aud.sql"] = audit.Database.GenerateCreateScript();
        scripts["msg-owned.sql"] = OwnedMessagingSchema.TablesSql.Replace("msg.", Schema + ".", StringComparison.Ordinal);
        scripts["msg-native.sql"] = WorkflowTransactionSchemaTests.StableNativeScript(
            Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>()), Schema);
        scripts["cleanup.sql"] = $"DROP SCHEMA {Schema} CASCADE;\n";
        return scripts;
    }
    private sealed class Refuse : IMessageConsumer<WorkflowTransitionRequestedV1>
    { public ValueTask HandleAsync(WorkflowTransitionRequestedV1 message, CancellationToken ct) => throw new InvalidOperationException("No fixture listener."); }
}

public sealed class WorkflowAdmittedStartFactAttribute : FactAttribute
{
    public WorkflowAdmittedStartFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_ADMITTED_START_MANIFEST")))
            Skip = "Requires exact approved integrated owner/start-root fixture manifest.";
    }
}
