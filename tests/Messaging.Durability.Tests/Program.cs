using JasperFx;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.ErrorHandling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Wolverine.EntityFrameworkCore.Internals;
using Wolverine.Runtime;

namespace Kordra.Messaging.Durability.Tests;

// Test executable only. Code generation builds source without starting transport or opening a database.
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "codegen")
            return await Builder("Host=127.0.0.1;Database=kordra_test;Username=kordra_test", Guid.Empty, true, false, false)
                .RunJasperFxCommands(args);
        if (args.Length is not (4 or 5) || args[0] != "fixture" ||
            args[1] is not ("consumer" or "consumer-retention" or "consumer-restricted" or "migrator" or "producer" or "producer-before-commit" or "producer-after-commit" or "consumer-before-commit" or "consumer-after-effect" or "consumer-before-effect") ||
            !Guid.TryParseExact(args[2], "N", out var caseId) || !Guid.TryParseExact(args[3], "N", out var logicalId) || logicalId == Guid.Empty)
            throw new InvalidOperationException("Only explicit synthetic fixture commands are supported.");
        var restricted = args[1] == "consumer-restricted";
        var locator = Environment.GetEnvironmentVariable(restricted ? "KORDRA_T12_RESTRICTED_RUNTIME_CONNECTION_STRING" : "KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private fixture locator required.");
        var fixture = restricted ? FixtureDatabaseScope.RestrictedRuntime(locator, caseId) : new FixtureDatabaseScope(locator, caseId);
        var scenario = args.Length == 5 ? args[4] : "normal";
        if (scenario is not ("normal" or "transient" or "expired" or "wrong-case" or "unsupported-schema" or "wrong-tenant" or "unknown-type" or "expires-in-queue"))
            throw new InvalidOperationException("Unknown fixture scenario.");
        var consumer = args[1].StartsWith("consumer", StringComparison.Ordinal);
        using var host = Builder(locator, caseId, consumer, args[1] == "migrator", args[1] == "consumer-after-effect", args[1] == "consumer-before-effect", restricted, args[1] == "consumer-retention", args[1] == "consumer-before-commit").Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(scenario == "transient" ? 110 : 35));
        await host.StartAsync(deadline.Token);
        Console.WriteLine("FIXTURE_READY");
        if (args[1] == "migrator") { await host.StopAsync(); return 0; }
        if (consumer)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
        else
        {
            await using var connection = fixture.CreateConnection(); await connection.OpenAsync(deadline.Token);
            await using var owner = new OwnerContext(new DbContextOptionsBuilder<OwnerContext>().UseNpgsql(connection)
                .ReplaceService<IModelCacheKeyFactory, FixtureModelKey>().Options, fixture.Schema);
            await using var tx = await owner.Database.BeginTransactionAsync(deadline.Token);
            var outbox = host.Services.GetRequiredService<IDbContextOutbox>(); outbox.Enroll(owner);
            owner.Rows.Add(new FixtureRow { Id = Guid.NewGuid(), Value = "synthetic-producer" });
            await owner.SaveChangesAsync(deadline.Token);
            await outbox.PublishAsync(new DeliveryProbe(scenario == "wrong-case" ? Guid.NewGuid() : caseId, logicalId,
                scenario == "unsupported-schema" ? 2 : 1, scenario == "wrong-tenant" ? Guid.NewGuid() : caseId,
                DateTimeOffset.UtcNow.AddSeconds(scenario == "expired" ? -1 : scenario == "expires-in-queue" ? 1 : 120), scenario == "transient"));
            if (args[1] == "producer-before-commit") Environment.Exit(71);
            await tx.CommitAsync(deadline.Token);
            Console.WriteLine("FIXTURE_COMMITTED");
            if (args[1] == "producer-after-commit") Environment.Exit(72);
            await outbox.FlushOutgoingMessagesAsync();
        }
        await host.StopAsync(CancellationToken.None);
        return 0;
    }

    internal static IHostBuilder Builder(string locator, Guid caseId, bool consumer, bool setup, bool crashAfterEffect, bool crashBeforeEffect = false, bool restricted = false, bool shortRetention = false, bool crashBeforeCommit = false) =>
        Host.CreateDefaultBuilder().ConfigureServices(services => services.AddSingleton(new DeliveryState(locator, caseId, crashAfterEffect, crashBeforeEffect, restricted, crashBeforeCommit)))
        .UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<DeliveryProbeHandler>();
            options.UnknownMessageBehavior = UnknownMessageBehavior.DeadLetterQueue;
            options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
            ConfigureStorage(options, locator, caseId == Guid.Empty ? "t12_codegen" : "t12_" + caseId.ToString("N"));
            options.UseEntityFrameworkCoreTransactions();
            options.OnException<FixtureRejectedException>().MoveToErrorQueue();
            options.OnException<TransientFixtureException>().ScheduleRetry(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60));
            options.Durability.KeepAfterMessageHandling = shortRetention ? TimeSpan.FromSeconds(3) : TimeSpan.FromMinutes(10);
            options.Durability.HandledMessageCleanupPollingTime = TimeSpan.FromSeconds(1);
            // Short, explicit fixture recovery periods, not proposed production defaults.
            options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromSeconds(1);
            options.Durability.NodeReassignmentPollingTime = TimeSpan.FromSeconds(1);
            options.Durability.FirstHealthCheckExecution = TimeSpan.FromSeconds(1);
            options.Durability.HealthCheckPollingTime = TimeSpan.FromSeconds(1);
            options.Durability.StaleNodeTimeout = TimeSpan.FromSeconds(3);
            options.Durability.ScheduledJobFirstExecution = TimeSpan.FromSeconds(1);
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
            options.AutoBuildMessageStorageOnStartup = setup ? AutoCreate.CreateOrUpdate : AutoCreate.None;
            options.MaxIncomingEnvelopeDataSize = 16 * 1024;
            options.AddSerializer(new ClosedFixtureSerializer());
            options.PublishMessage<DeliveryProbe>().ToPostgresqlQueue("t12_fixture").DefaultSerializer(new ClosedFixtureSerializer());
            options.PublishMessage<FixtureMessage>().ToPostgresqlQueue("t12_results").DefaultSerializer(new ClosedFixtureSerializer());
            if (consumer) options.ListenToPostgresqlQueue("t12_fixture").Sequential().DefaultSerializer(new ClosedFixtureSerializer());
        });

    internal static void ConfigureStorage(WolverineOptions options, string locator, string schema)
    {
        // Persistence schema does NOT control the native queue tables. Bind both explicitly.
        options.UsePostgresqlPersistenceAndTransport(locator, schema, schema);
    }
}

// Closed synthetic payload: deliberately has no credential/token/kubeconfig or free-form payload field.
public sealed record DeliveryProbe(Guid CaseId, Guid LogicalId, int SchemaVersion, Guid TenantId, DateTimeOffset DeadlineUtc, bool Transient);
public sealed class FixtureRejectedException : Exception;
public sealed class TransientFixtureException : Exception;
public sealed record DeliveryState(string Locator, Guid CaseId, bool CrashAfterEffect, bool CrashBeforeEffect, bool Restricted, bool CrashBeforeCommit)
{
    public override string ToString() => "DeliveryState [private]";
}
public sealed class DeliveryProbeHandler
{
    public static async Task Handle(DeliveryProbe message, DeliveryState state, IMessageContext context, CancellationToken cancellation)
    {
        if (state.CrashBeforeEffect) Environment.Exit(74);
        var fixture = state.Restricted ? FixtureDatabaseScope.RestrictedRuntime(state.Locator, state.CaseId) : new FixtureDatabaseScope(state.Locator, state.CaseId);
        await using var connection = fixture.CreateConnection(); await connection.OpenAsync(cancellation);
        await using (var attempt = new NpgsqlCommand($"INSERT INTO {fixture.Schema}.fixture_attempt(at) VALUES (clock_timestamp())", connection))
            await attempt.ExecuteNonQueryAsync(cancellation);
        if (message.CaseId != state.CaseId || message.CaseId == Guid.Empty || message.LogicalId == Guid.Empty ||
            message.TenantId != state.CaseId || message.SchemaVersion != 1 || message.DeadlineUtc <= DateTimeOffset.UtcNow)
            throw new FixtureRejectedException();
        if (message.Transient) throw new TransientFixtureException();
        await using var owner = new OwnerContext(new DbContextOptionsBuilder<OwnerContext>().UseNpgsql(connection)
            .ReplaceService<IModelCacheKeyFactory, FixtureModelKey>().Options, fixture.Schema);
        await using var transaction = await owner.Database.BeginTransactionAsync(cancellation);
        var messaging = (MessageContext)context;
        var enlistment = new EfCoreEnvelopeTransaction(owner, messaging);
        await messaging.EnlistInOutboxAsync(enlistment);
        await using var command = new NpgsqlCommand($"INSERT INTO {fixture.Schema}.fixture_effect(id) VALUES (@id) ON CONFLICT (id) DO NOTHING", connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("id", message.LogicalId);
        var inserted = await command.ExecuteNonQueryAsync(cancellation);
        if (inserted == 1) await context.EndpointFor(new Uri("postgresql://t12_results")).SendAsync(message);
        if (state.CrashBeforeCommit) Environment.Exit(75);
        // Candidate-specific adapter is confined to this spike: inbox completion, effect and outgoing result share the owner transaction.
        await enlistment.CommitAsync(cancellation, false);
        Console.WriteLine("FIXTURE_EFFECT_COMMITTED");
        if (state.CrashAfterEffect) Environment.Exit(73);
        await messaging.FlushOutgoingMessagesAsync();
    }
}
