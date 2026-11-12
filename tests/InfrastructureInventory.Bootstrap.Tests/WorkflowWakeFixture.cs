using System.Collections.Concurrent;
using JasperFx;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Weasel.Core;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.Runtime;
using Wolverine.Runtime.Serialization;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

[Wolverine.Attributes.MessageIdentity(WorkflowWakePublication.TypeId)]
public sealed record WorkflowWakeFixtureInput(Guid InstanceId)
{
    public VerifiedMessageEnvelope? Verified { get; internal init; }
}

/// <summary>Server-side synthetic facts only; never populated from wire authority.</summary>
public sealed class WorkflowWakeFixtureInvocation(WorkflowSpecification specification, WorkflowWakeOperation wake, AuditIntent audit,
    Guid workerId, Guid workerBootId, Guid resultMessageId)
{
    private int _handlerCalls;
    public int HandlerCalls => Volatile.Read(ref _handlerCalls);
    internal void RecordHandlerCall() => Interlocked.Increment(ref _handlerCalls);
    public WorkflowSpecification Specification { get; } = specification;
    public WorkflowWakeOperation Wake { get; } = wake;
    public AuditIntent Audit { get; } = audit;
    public Guid WorkerId { get; } = workerId;
    public Guid WorkerBootId { get; } = workerBootId;
    public Guid ResultMessageId { get; } = resultMessageId;
    public Func<CancellationToken, Task>? BeforeCommitOracle { get; init; }
    public Func<CancellationToken, Task>? BeforeOwnerStage { get; init; }
    public Func<CancellationToken, Task>? AfterCommitBeforeFlush { get; init; }
    // Test-owned hook after all stages; never decoded from a wire request or registered in Product.
    public Func<NpgsqlConnection, WorkflowTransactionOrigin, CancellationToken, Task>? BeforeCommitFault { get; init; }
    public Action<WorkflowCommitDisposition>? CommitOutcomeObserved { get; init; }
    public TaskCompletionSource<WorkflowCommitDisposition> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override string ToString() => "WorkflowWakeFixtureInvocation [private]";
}

public sealed class WorkflowWakeFixtureState(Guid caseId, string locator)
{
    private readonly ConcurrentDictionary<Guid, WorkflowWakeFixtureInvocation> _invocations = [];
    public Guid CaseId { get; } = caseId;
    public FairAsyncReaderWriterAdmissionGate Gate { get; } = new();
    public void Add(WorkflowWakeFixtureInvocation invocation)
    {
        if (CaseId == Guid.Empty || invocation.Specification.InstallationId != CaseId || invocation.Specification.TenantId != CaseId ||
            invocation.Wake.InstanceId != invocation.Specification.InstanceId || invocation.Wake.SpecificationHash != invocation.Specification.SpecificationHash ||
            invocation.WorkerId == Guid.Empty || invocation.WorkerBootId == Guid.Empty || invocation.ResultMessageId == Guid.Empty ||
            !_invocations.TryAdd(invocation.Wake.WakeOperationId, invocation))
            throw new InvalidOperationException("Exact fresh synthetic wake fixture reservation required.");
    }
    public WorkflowWakeFixtureInvocation Require(Guid operation) => _invocations.TryGetValue(operation, out var value)
        ? value : throw new InvalidOperationException("Unknown reserved synthetic wake operation.");
    public NpgsqlConnection NewOwnedConnection()
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(locator) { Pooling = false, Multiplexing = false }.ConnectionString);
        _ = IsolatedWorkflowFixture.Validate(connection, CaseId); return connection;
    }
    public override string ToString() => "WorkflowWakeFixtureState [private]";
}

public sealed class WorkflowWakeFixtureFailureException() : Exception("Owned native wake fixture failed; inspect protected test evidence.");

public sealed class WorkflowWakeFixtureHandler
{
    public static async Task Handle(WorkflowWakeFixtureInput input, WorkflowWakeFixtureState state, IMessageContext messages, CancellationToken ct)
    {
        var verified = input.Verified ?? throw new InvalidOperationException("Verified native wake required.");
        var delivery = WorkflowWakePublication.ReadDelivery(verified);
        var invocation = state.Require(delivery.Request.OperationId);
        invocation.RecordHandlerCall();
        try
        {
            var now = DateTimeOffset.UtcNow;
            await using var admission = await state.Gate.AcquireReadAsync(ct);
            await using var connection = state.NewOwnedConnection(); await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction,
                state.CaseId, (MessageContext)messages, state.Gate, ct);
            var logical = await root.StageConsumptionAsync(WorkflowWakePublication.CreateRegistry(new RefuseRequest()), verified, now, ct);
            // Same-envelope ACK-loss recovery belongs to native inbox dedup. Never replay an existing logical claim here.
            if (logical.Disposition != ReceiptStageDisposition.Staged)
                throw new InvalidOperationException("Fresh native timer logical claim required; existing/conflicting input cannot execute again.");
            if (invocation.BeforeOwnerStage is { } beforeOwner) await beforeOwner(ct);
            var completed = await root.StageTimerDueCompletionAsync(invocation.WorkerId, invocation.WorkerBootId, now, ct);
            if (completed.Duplicate) throw new InvalidOperationException("Fresh native logical claim cannot pair with an older owner transition.");
            await root.StageAuditAsync(invocation.Audit, ct); await root.StageClockFloorAsync(now, ct);
            var results = WorkflowTransitionResultPublication.CreateRegistry(new RefuseResult());
            var outgoing = WorkflowTransitionResultPublication.PrepareTimerDue(completed.Operation, completed.WorkflowVersion,
                invocation.ResultMessageId, results, now);
            await root.StagePublicationAsync(results, outgoing, now, ct);
            await root.StageIncomingHandledAsync(now, ct);
            if (invocation.BeforeCommitOracle is { } oracle) await oracle(ct);
            if (invocation.BeforeCommitFault is { } fault)
                await fault(connection, await root.CaptureTimerCommitOriginAsync(ct), ct);
            var outcome = await root.CommitAsync(ct);
            invocation.CommitOutcomeObserved?.Invoke(outcome);
            if (outcome != WorkflowCommitDisposition.Committed)
                throw new InvalidOperationException("Native wake root did not confirm a new commit.");
            if (invocation.AfterCommitBeforeFlush is { } afterCommit) await afterCommit(ct);
            await root.FlushConfirmedAsync(); invocation.Completion.TrySetResult(outcome);
        }
        catch (Exception error)
        {
            invocation.Completion.TrySetException(error);
            throw new WorkflowWakeFixtureFailureException();
        }
    }
    internal sealed class RefuseRequest : IMessageConsumer<WorkflowTransitionRequestedV1>
    {
        public ValueTask HandleAsync(WorkflowTransitionRequestedV1 message, CancellationToken ct)
            => throw new InvalidOperationException("Schema registry cannot execute native fixture effects.");
    }
    internal sealed class RefuseResult : IMessageConsumer<WorkflowTransitionRecordedV1>
    {
        public ValueTask HandleAsync(WorkflowTransitionRecordedV1 message, CancellationToken ct)
            => throw new InvalidOperationException("Result schema has no transition callback.");
    }
}

internal sealed class WorkflowWakeFixtureSerializer(WorkflowWakeFixtureState state) : IMessageSerializer
{
    private static CompiledMessageRegistry Requests() => WorkflowWakePublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseRequest());
    public string ContentType => MessageEnvelopeCodec.ContentType;
    public byte[] Write(Envelope envelope) => WriteMessage(envelope.Message!);
    public byte[] WriteMessage(object message)
    {
        if (message is not WorkflowWakeFixtureInput { Verified: { } verified })
            throw new InvalidOperationException("Only verified native wake fixture wire can be serialized.");
        return new MessageEnvelopeCodec(Requests()).Write(verified.Binding, verified.Delivery, verified.CopyPayload());
    }
    public object ReadFromData(byte[] data) => throw new InvalidOperationException("Envelope-aware native wake decoding required.");
    public object ReadFromData(Type type, Envelope envelope)
    {
        if (type != typeof(WorkflowWakeFixtureInput) || envelope.Data is null)
            throw new InvalidOperationException("Exact compiled native wake frame required.");
        var verified = new MessageEnvelopeCodec(Requests()).Read(envelope.Data, envelope.ContentType ?? "", state.CaseId,
            MessageScope.Tenant(state.CaseId), DateTimeOffset.UtcNow);
        var delivery = WorkflowWakePublication.ReadDelivery(verified);
        var invocation = state.Require(delivery.Request.OperationId);
        if (verified.Delivery.MessageId != envelope.Id || delivery.Identity.InstanceId != invocation.Specification.InstanceId ||
            delivery.Request.SpecificationHash != invocation.Specification.SpecificationHash)
            throw new InvalidOperationException("Native delivery differs from the reserved owner fixture facts.");
        return new WorkflowWakeFixtureInput(delivery.Identity.InstanceId) { Verified = verified };
    }
}

public static class WorkflowWakeFixtureProfile
{
    /// <summary>Test assembly only. Building metadata is not permission to start this host.</summary>
    public static IHostBuilder Builder(string locator, string schema, WorkflowWakeFixtureState state, bool listen,
        bool includeLegacyForCodegen = false, bool guardedIngress = false, bool processRecovery = false)
        => Host.CreateDefaultBuilder().ConfigureServices(services => {
            services.AddSingleton(state);
            if (includeLegacyForCodegen) services.AddSingleton(new WorkflowFixtureState(Guid.Empty, ""));
        }).UseWolverine(options => {
            options.Discovery.DisableConventionalDiscovery(); options.Discovery.IncludeType<WorkflowWakeFixtureHandler>();
            if (includeLegacyForCodegen) options.Discovery.IncludeType<WorkflowFixtureIncomingHandler>();
            options.AddSerializer(new WorkflowWakeFixtureSerializer(state));
            options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
            options.UsePostgresqlPersistenceAndTransport(locator, schema, schema);
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
            options.Durability.MessageIdentity = Wolverine.MessageIdentity.IdAndDestination;
            options.Durability.KeepAfterMessageHandling = TimeSpan.FromDays(7);
            options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromMinutes(1);
            options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMinutes(1);
            if (processRecovery)
            {
                options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromSeconds(1);
                options.Durability.NodeReassignmentPollingTime = TimeSpan.FromSeconds(1);
                options.Durability.FirstHealthCheckExecution = TimeSpan.FromSeconds(1);
                options.Durability.HealthCheckPollingTime = TimeSpan.FromSeconds(1);
                options.Durability.StaleNodeTimeout = TimeSpan.FromSeconds(3);
                options.Durability.ScheduledJobFirstExecution = TimeSpan.FromSeconds(1);
                options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
                options.Durability.OrphanedMessageSweepPollingTime = TimeSpan.FromSeconds(1);
            }
            options.PublishMessage<WorkflowWakeFixtureInput>().ToPostgresqlQueue(WorkflowWakePublication.Route);
            options.PublishMessage<WorkflowTransitionRecordedV1>().ToPostgresqlQueue(WorkflowTransitionResultPublication.Route);
            if (listen)
            {
                options.ListenToPostgresqlQueue(WorkflowWakePublication.Route).Sequential();
                if (guardedIngress)
                {
                    var transport = options.Transports.GetOrCreate<Wolverine.Postgresql.Transport.PostgresqlTransport>();
                    transport.Queues[transport.SanitizeIdentifier(WorkflowWakePublication.Route)].IncomingRules.Add(new NativeMessageIngressRule(
                            WorkflowWakePublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseRequest()),
                            state.CaseId, MessageScope.Tenant(state.CaseId),
                            new Uri("postgresql://" + transport.SanitizeIdentifier(WorkflowWakePublication.Route)), TimeProvider.System, transport));
                    options.UnknownMessageBehavior = UnknownMessageBehavior.DeadLetterQueue;
                }
            }
            options.OnException<WorkflowWakeFixtureFailureException>().Discard();
        });
}
