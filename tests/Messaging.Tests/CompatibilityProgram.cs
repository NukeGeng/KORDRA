using System.Text.Json;
using JasperFx;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Kordra.Modules.Messaging.Infrastructure.Diagnostics;
using Npgsql;
using Wolverine;
using Wolverine.Postgresql;
using Wolverine.RDBMS;
using Wolverine.Runtime;
using Wolverine.Postgresql.Transport;
using MsgScope = Kordra.Modules.Messaging.Contracts.MessageScope;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;

namespace Kordra.Messaging.Tests;

[Wolverine.Attributes.MessageIdentity(NativeWireFixture.TypeId)]
public sealed record NativeCompatibilityMessage(Guid CaseId, Guid LogicalId)
{
    public VerifiedMessageEnvelope? Verified { get; internal init; }
}
public sealed record NativeCompatibilityResult(Guid CaseId, Guid LogicalId);
public sealed record NativeFixtureConfiguration(string Locator, Guid CaseId, string Mode)
{
    public override string ToString() => "NativeFixtureConfiguration [private]";
}

public static class CompatibilityProgram
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "startup-fixture")
            return await StartupReadinessFixture.RunAsync(args[1]);
        if (args.Length == 2 && args[0] == "lifecycle-cleanup-sql" && Guid.TryParseExact(args[1], "N", out var lifecycleCase) && lifecycleCase != Guid.Empty)
        {
            Console.Write(LifecycleBoundaryDatabaseTests.CleanupSql(lifecycleCase));
            return 0;
        }
        if (args.Length == 2 && args[0] == "exhaustion-fixture-ddl" && Guid.TryParseExact(args[1], "N", out var exhaustionCase) && exhaustionCase != Guid.Empty)
        {
            Console.Write(NativeExhaustionDatabaseTests.CreationSql(exhaustionCase));
            return 0;
        }
        if (args.Length == 2 && args[0] == "receipt-fixture-ddl" && Guid.TryParseExact(args[1], "N", out var receiptCase) && receiptCase != Guid.Empty)
        {
            // Review-only output: no connection, credential lookup, provisioning or schema execution.
            Console.Write(ReceiptDatabaseConformanceTests.CreationSql(receiptCase));
            return 0;
        }
        if (args.Length == 2 && args[0] == "ingress-fixture-ddl" && Guid.TryParseExact(args[1], "N", out var ingressCase) && ingressCase != Guid.Empty)
        {
            Console.Write(NativeIngressDatabaseTests.CreationSql(ingressCase));
            return 0;
        }
        if (args.Length == 2 && args[0] == "retry-fixture-ddl" && Guid.TryParseExact(args[1], "N", out var retryCase) && retryCase != Guid.Empty)
        {
            Console.Write(NativeRetryDatabaseTests.CreationSql(retryCase));
            return 0;
        }
        if (args.Length > 0 && args[0] == "codegen")
            return await Builder("Host=127.0.0.1;Database=kordra_test;Username=kordra_test", Guid.Empty, "codegen", false)
                .RunJasperFxCommands(args);
        if (args.Length != 4 || args[0] != "fixture" || args[1] is not ("producer" or "producer-conflict" or "consumer" or "before-commit" or "after-commit" or "commit-reply-loss") ||
            !int.TryParse(args[2], out var slot) || slot is < 4 or > 6 || !Guid.TryParseExact(args[3], "N", out var logicalId) || logicalId == Guid.Empty)
            throw new InvalidOperationException("Explicit approved native fixture command required.");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Environment.GetEnvironmentVariable("KORDRA_MSG_COMPAT_FIXTURE_MANIFEST") ?? throw new InvalidOperationException("Fixture manifest required.")));
        if (manifest.RootElement.GetProperty("status").GetString() != "approved_for_execution") throw new InvalidOperationException("Fixture permission required.");
        var schema = manifest.RootElement.GetProperty("schemas")[slot].GetString()!;
        if (schema.Length != 36 || !schema.StartsWith("t12_", StringComparison.Ordinal) || !Guid.TryParseExact(schema[4..], "N", out var caseId))
            throw new InvalidOperationException("Approved UUID schema required.");
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Private test locator required.");
        using (var check = new NpgsqlConnection(locator))
        using (var model = AuditDbContextFactory.CreateIsolatedTestContext(check, caseId)) { }
        var consumer = args[1] is not ("producer" or "producer-conflict");
        using var host = Builder(locator, caseId, args[1], consumer).Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await host.StartAsync(deadline.Token); Console.WriteLine("NATIVE_FIXTURE_READY");
        if (consumer)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
        else
        {
            var occurredAt = DateTimeOffset.ParseExact(Environment.GetEnvironmentVariable("KORDRA_MSG_COMPAT_OCCURRED_AT_UTC")
                ?? throw new InvalidOperationException("Original fixture creation time required."), "O", System.Globalization.CultureInfo.InvariantCulture);
            var verified = NativeWireFixture.Create(caseId, logicalId, occurredAt, enabled: args[1] != "producer-conflict");
            var wire = new MessageEnvelopeCodec(NativeWireFixture.Registry()).Write(verified.Binding, verified.Delivery, verified.CopyPayload());
            var destination = new Uri("postgresql://nativecompatibility");
            await host.Services.GetRequiredService<IMessageBus>().EndpointFor(destination).SendRawMessageAsync(wire, configure: e =>
            { e.Id = verified.Delivery.MessageId; e.Destination = destination; e.ContentType = MessageEnvelopeCodec.ContentType; e.MessageType = NativeWireFixture.TypeId; });
        }
        await host.StopAsync(); return 0;
    }

    public static IHostBuilder Builder(string locator, Guid caseId, string mode, bool consumer, bool setup = false) =>
        Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(new NativeFixtureConfiguration(locator, caseId, mode));
            if (mode == "codegen") services.AddSingleton<SyntheticQueuedLoggingState>();
        })
        .ConfigureLogging(logging =>
        {
            if (Environment.GetEnvironmentVariable("KORDRA_MSG_COMPAT_SAFE_DIAGNOSTICS") == "1")
                logging.ClearProviders().AddProvider(new MessageDiagnosticLoggingProvider(new FixtureDiagnosticConsole()));
        })
        .UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery(); options.Discovery.IncludeType<NativeCompatibilityHandler>();
            if (mode == "codegen")
            {
                options.Discovery.IncludeType<SyntheticLoggingProbeHandler>();
                options.Discovery.IncludeType<SyntheticQueuedLoggingHandler>();
            }
            options.AddSerializer(new NativeWireFixture.Serializer(caseId));
            options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
            var schema = caseId == Guid.Empty ? "t12_codegen" : "t12_" + caseId.ToString("N");
            options.UsePostgresqlPersistenceAndTransport(locator, schema, schema);
            options.AutoBuildMessageStorageOnStartup = setup ? AutoCreate.CreateOrUpdate : AutoCreate.None;
            options.Durability.MessageIdentity = Wolverine.MessageIdentity.IdAndDestination;
            options.Durability.KeepAfterMessageHandling = TimeSpan.FromDays(7);
            options.PublishMessage<NativeCompatibilityMessage>().ToPostgresqlQueue("nativecompatibility");
            options.PublishMessage<NativeCompatibilityResult>().ToPostgresqlQueue("native_result");
            if (consumer) options.ListenToPostgresqlQueue("nativecompatibility").Sequential();
            if (mode == "ingress")
            {
                options.UnknownMessageBehavior = UnknownMessageBehavior.DeadLetterQueue;
                options.Transports.GetOrCreate<PostgresqlTransport>().Queues["nativecompatibility"].IncomingRules.Add(
                    new NativeMessageIngressRule(NativeWireFixture.Registry(), caseId, MsgScope.Tenant(caseId),
                        new Uri("postgresql://nativecompatibility"), TimeProvider.System));
            }
            options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromSeconds(1);
            options.Durability.NodeReassignmentPollingTime = TimeSpan.FromSeconds(1);
            options.Durability.FirstHealthCheckExecution = TimeSpan.FromSeconds(1);
            options.Durability.HealthCheckPollingTime = TimeSpan.FromSeconds(1);
            options.Durability.StaleNodeTimeout = TimeSpan.FromSeconds(3);
        });
}

public sealed class NativeCompatibilityHandler
{
    public static async Task Handle(NativeCompatibilityMessage message, NativeFixtureConfiguration fixture,
        IMessageContext context, CancellationToken cancellation)
    {
        if (message.CaseId != fixture.CaseId || message.LogicalId == Guid.Empty) throw new InvalidOperationException("Wrong fixture identity.");
        var schema = "t12_" + fixture.CaseId.ToString("N");
        var ownerLocator = new NpgsqlConnectionStringBuilder(fixture.Locator) { Pooling = false, Multiplexing = false }.ConnectionString;
        await using var connection = new NpgsqlConnection(ownerLocator); await connection.OpenAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await using var audit = await AuditTransactionContext.CreateIsolatedTestAsync(connection, transaction, fixture.CaseId, cancellation);
        var native = (MessageContext)context;
        var store = (IMessageDatabase)native.Runtime.Storage;
        var stager = new CallerOwnedMessageStager(connection, transaction, store, schema);
        await native.EnlistInOutboxAsync(new CallerOwnedEnvelopeEnlistment(stager, async () => await audit.RollbackAsync()));
        var incoming = context.Envelope ?? throw new InvalidOperationException("Native inbox identity required.");
        var intent = new AuditIntent(1, incoming.Id, Guid.NewGuid(), AuditIntentStage.Mutation, Guid.NewGuid(), fixture.CaseId,
            fixture.CaseId, "target.read", "target", fixture.CaseId, 1, 1, "synthetic_fixture", DateTimeOffset.UtcNow, fixture.CaseId);
        if ((await audit.StageAsync(intent, cancellation)).Status != AuditTransactionStageStatus.Staged)
            throw new InvalidOperationException("Audit stage failed.");
        var verified = message.Verified ?? throw new InvalidOperationException("Verified native wire fixture required.");
        var claim = await stager.StageConsumptionAsync(CallerOwnedReceiptStore.CreateIsolatedTestStore(connection, transaction, fixture.CaseId),
            NativeWireFixture.Registry(), native, verified, LogicalReceiptOutcome.Completed, DateTimeOffset.UtcNow, cancellation);
        if (claim.Disposition == ReceiptStageDisposition.Staged)
        {
            await using var effect = new NpgsqlCommand($"INSERT INTO {schema}.fixture_effect VALUES (@id)", connection, transaction);
            effect.Parameters.AddWithValue("id", message.LogicalId);
            if (await effect.ExecuteNonQueryAsync(cancellation) != 1) throw new InvalidOperationException("Owner effect did not stage.");
            await context.PublishAsync(new NativeCompatibilityResult(fixture.CaseId, message.LogicalId));
        }
        else if (claim.Disposition == ReceiptStageDisposition.Conflict)
            await CallerOwnedMessageJournal.CreateIsolatedTestJournal(connection, transaction, fixture.CaseId)
                .StageQuarantineAsync(Guid.NewGuid(), fixture.CaseId, MessageQuarantineReason.HashConflict, verified.PayloadHash, DateTimeOffset.UtcNow, cancellation);
        await stager.StageHandledAsync(incoming, DateTimeOffset.UtcNow, cancellation);
        Console.WriteLine("NATIVE_FIXTURE_STAGED");
        if (fixture.Mode == "before-commit") Environment.Exit(81);
        var fault = fixture.Mode == "commit-reply-loss" ? CommitReplyFault.Install(connection) : null;
        var committed = await audit.CommitAsync(cancellation);
        if (fault is not null)
        {
            if (!fault.Dropped || committed.Status != AuditAppendStatus.Unknown) Environment.Exit(84);
            Console.WriteLine("NATIVE_FIXTURE_UNKNOWN_COMMIT");
            Environment.Exit(83);
        }
        if (committed.Status != AuditAppendStatus.Durable) Environment.Exit(83);
        Console.WriteLine("NATIVE_FIXTURE_COMMITTED");
        if (fixture.Mode == "after-commit") Environment.Exit(82);
        incoming.Status = EnvelopeStatus.Handled;
        await native.FlushOutgoingMessagesAsync();
    }
}
