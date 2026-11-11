using System.Collections.Concurrent;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Npgsql;
using Wolverine;
using Wolverine.Runtime;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

[Wolverine.Attributes.MessageIdentity(WorkflowFixtureWire.InputType)]
public sealed record WorkflowFixtureIncoming(Guid InstanceId)
{
    public VerifiedMessageEnvelope? Verified { get; internal init; }
}

public sealed class WorkflowFixtureInvocation(WorkflowSpecification specification, AuditIntent audit, VerifiedMessageEnvelope output)
{
    public WorkflowSpecification Specification { get; } = specification;
    public AuditIntent Audit { get; } = audit;
    public VerifiedMessageEnvelope Output { get; } = output;
    public Func<CancellationToken, Task>? BeforeCommitOracle { get; init; }
    public TaskCompletionSource<WorkflowCommitDisposition> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override string ToString() => "WorkflowFixtureInvocation [private]";
}

/// <summary>Test-assembly-only synthetic owner facts. No runtime DI/authority implementation.</summary>
public sealed class WorkflowFixtureState(Guid caseId, string locator)
{
    private readonly ConcurrentDictionary<Guid, WorkflowFixtureInvocation> _invocations = [];
    private readonly string _locator = locator;
    public Guid CaseId { get; } = caseId;
    public FairAsyncReaderWriterAdmissionGate Gate { get; } = new();
    public void Add(WorkflowFixtureInvocation invocation)
    {
        if (invocation.Specification.InstallationId != CaseId || invocation.Specification.TenantId != CaseId ||
            !_invocations.TryAdd(invocation.Specification.InstanceId, invocation))
            throw new InvalidOperationException("Exact fresh synthetic fixture reservation required.");
    }
    public void ReplaceForOracle(WorkflowFixtureInvocation invocation)
    {
        var old = Require(invocation.Specification.InstanceId);
        if (old.Specification != invocation.Specification || old.Audit != invocation.Audit || old.Output != invocation.Output ||
            !_invocations.TryUpdate(invocation.Specification.InstanceId, invocation, old))
            throw new InvalidOperationException("Only the identical pre-seed fixture facts may acquire an observation callback.");
    }
    public WorkflowFixtureInvocation Require(Guid id) => _invocations.TryGetValue(id, out var value)
        ? value : throw new InvalidOperationException("Unknown synthetic fixture reservation.");
    public NpgsqlConnection NewOwnedConnection()
    {
        if (!WorkflowTransactionSchemaTests.ProposedCases.Contains(CaseId.ToString("D"), StringComparer.Ordinal))
            throw new InvalidOperationException("Only the declared proposed fixture cases may construct an owned connection.");
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_locator) { Pooling = false, Multiplexing = false }.ConnectionString);
        IsolatedWorkflowFixture.Validate(connection, CaseId);
        return connection;
    }
    public override string ToString() => "WorkflowFixtureState [private]";
}

public sealed class WorkflowFixtureFailureException() : Exception("Owned workflow fixture failed; inspect its protected test evidence.");

public sealed class WorkflowFixtureIncomingHandler
{
    public static async Task Handle(WorkflowFixtureIncoming message, WorkflowFixtureState state, IMessageContext context, CancellationToken ct)
    {
        var invocation = state.Require(message.InstanceId);
        try
        {
            var verified = message.Verified ?? throw new InvalidOperationException("Verified synthetic input required.");
            await using var connection = state.NewOwnedConnection(); await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            var native = (MessageContext)context;
            await using var root = await IsolatedWorkflowFixture.CreateRoot(connection, transaction, state.CaseId, native, state.Gate, ct);
            var logical = await root.StageConsumptionAsync(WorkflowFixtureWire.Registry(), verified, DateTimeOffset.UtcNow, ct);
            if (logical.Disposition != ReceiptStageDisposition.Staged) throw new InvalidOperationException("Fresh fixture delivery required.");
            await root.StageCreationAsync(invocation.Specification, ct);
            await root.StageAuditAsync(invocation.Audit, ct);
            await root.StageClockFloorAsync(DateTimeOffset.UtcNow, ct);
            await root.StagePublicationAsync(WorkflowFixtureWire.Registry(), invocation.Output, DateTimeOffset.UtcNow, ct);
            await root.StageIncomingHandledAsync(DateTimeOffset.UtcNow, ct);
            if (invocation.BeforeCommitOracle is { } oracle)
            {
                using var observation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                observation.CancelAfter(TimeSpan.FromMilliseconds(200));
                await oracle(observation.Token);
            }
            var result = await root.CommitAsync(ct);
            if (result != WorkflowCommitDisposition.Committed) throw new InvalidOperationException("Fixture root did not confirm commit.");
            await root.FlushConfirmedAsync(); invocation.Completion.TrySetResult(result);
        }
        catch (Exception error)
        {
            invocation.Completion.TrySetException(error);
            throw new WorkflowFixtureFailureException();
        }
    }
}
