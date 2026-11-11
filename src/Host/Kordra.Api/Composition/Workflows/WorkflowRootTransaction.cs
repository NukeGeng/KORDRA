using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Data;
using System.Runtime.CompilerServices;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace Kordra.Api;

/// <summary>
/// Inactive host composition kernel: actual WFL/AUD/MSG share the caller's root.
/// The only factory is the exact isolated-test target. No current authority or production registration is supplied.
/// </summary>
public sealed class WorkflowRootTransaction : IAsyncDisposable
{
    private static readonly ConditionalWeakTable<NpgsqlTransaction, RootOwnership> RootOwners = new();
    private sealed class RootOwnership { public string? Generation { get; set; } }

    private enum Phase { Open, Committing, Committed, Flushed, NoCommitAttempt, Unknown }
    private readonly WorkflowDbContext _workflow;
    private readonly AuditTransactionContext _audit;
    private readonly CallerOwnedMessageStager _messaging;
    private readonly CallerOwnedReceiptStore _receipts;
    private readonly MessageContext _messages;
    private readonly FairAsyncReaderWriterAdmissionGate _admission;
    private Phase _phase;
    private int _operationInProgress;
    private bool _disposed;
    private WorkflowIdentity? _identity;
    private Guid? _ownerOperationId;
    private Guid? _transitionStepId;
    private VerifiedMessageEnvelope? _consumption;
    private CompiledMessageRegistry? _consumptionRegistry;
    private bool _incomingHandled;
    private readonly HashSet<(Guid Operation, Guid Step, long Version, string Type, int Schema, string Hash)> _publications = [];
    private bool _ownerStaged, _auditStaged, _floorStaged, _publicationStaged;

    private WorkflowRootTransaction(WorkflowDbContext workflow, AuditTransactionContext audit,
        CallerOwnedMessageStager messaging, CallerOwnedReceiptStore receipts, MessageContext messages,
        FairAsyncReaderWriterAdmissionGate admission)
    {
        _workflow = workflow; _audit = audit; _messaging = messaging; _receipts = receipts;
        _messages = messages; _admission = admission;
    }

    public static Task<WorkflowRootTransaction> CreateIsolatedTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        CancellationToken ct = default)
        => CreateTestCoreAsync(connection, root, caseId, messages, admission, null, ct);

    public static Task<WorkflowRootTransaction> CreateDisposableTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        DisposableTestBinding profile, CancellationToken ct = default)
        => CreateTestCoreAsync(connection, root, caseId, messages, admission, profile, ct);

    private static async Task<WorkflowRootTransaction> CreateTestCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        DisposableTestBinding? disposable, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(messages); ArgumentNullException.ThrowIfNull(admission);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(root.Connection, connection) ||
            (messages.Transaction is not null && !ReferenceEquals(messages.Transaction, messages)))
            throw new InvalidOperationException("Workflow composition requires an open caller root and an unenlisted native context.");
        // Native received contexts use themselves as the default envelope transaction.
        // Accept only that self binding or null; never replace a foreign owner enlistment.
        var workflow = disposable is { } profile
            ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, profile)
            : WorkflowDbContext.CreateFixture(connection, caseId);
        AuditTransactionContext? audit = null;
        try
        {
            await ClaimCoordinatorAsync(connection, root, ct);
            var schema = workflow.SchemaName;
            var native = messages.Runtime.Storage as IMessageDatabase
                ?? throw new InvalidOperationException("Native PostgreSQL message storage required.");
            var messaging = new CallerOwnedMessageStager(connection, root, native, schema);
            var receipts = disposable is { } receiptProfile
                ? CallerOwnedReceiptStore.CreateDisposableTestStore(connection, root, caseId, receiptProfile)
                : CallerOwnedReceiptStore.CreateIsolatedTestStore(connection, root, caseId);
            await workflow.Database.UseTransactionAsync(root, ct);
            audit = disposable is { } auditProfile
                ? await AuditTransactionContext.CreateDisposableIsolatedTestAsync(connection, root, caseId, auditProfile, ct)
                : await AuditTransactionContext.CreateIsolatedTestAsync(connection, root, caseId, ct);
            var owner = new WorkflowRootTransaction(workflow, audit, messaging, receipts, messages, admission);
            await messages.EnlistInOutboxAsync(new CallerOwnedEnvelopeEnlistment(messaging,
                async () => await owner.RollbackCoreAsync()));
            return owner;
        }
        catch
        {
            if (audit is not null) await audit.DisposeAsync();
            await workflow.DisposeAsync();
            // No actual commit attempt: setup failure never closes shared admission.
            throw;
        }
    }

    private static async Task ClaimCoordinatorAsync(NpgsqlConnection connection, NpgsqlTransaction root, CancellationToken ct)
    {
        // Npgsql may reuse a completed transaction object; bind one coordinator to the
        // actual server transaction generation, not the pooled managed object identity.
        await using var command = new NpgsqlCommand("SELECT pg_current_xact_id()::text", connection, root) { CommandTimeout = 2 };
        var generation = await command.ExecuteScalarAsync(ct) as string
            ?? throw new InvalidOperationException("Caller root identity unavailable.");
        var ownership = RootOwners.GetValue(root, _ => new RootOwnership());
        lock (ownership)
        {
            if (ownership.Generation == generation)
                throw new InvalidOperationException("The caller root already has its sole workflow coordinator.");
            ownership.Generation = generation;
        }
    }

    /// <summary>Provisional logical claim before the owner mutation. It never supplies current authority.</summary>
    public async Task<ReceiptStageResult> StageConsumptionAsync(CompiledMessageRegistry registry,
        VerifiedMessageEnvelope message, DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        try
        {
            if (_ownerStaged || _auditStaged || _publicationStaged || _consumption is not null)
                throw new InvalidOperationException("Logical consumption must precede fresh owner staging exactly once.");
            var binding = message.Binding;
            if (binding.Identity.Scope.TenantId is not { } tenant || binding.Resource?.Kind != "workflow")
                throw new InvalidOperationException("Consumption requires one tenant workflow resource.");
            Bind(new(binding.Identity.InstallationId, tenant, binding.Resource.Id));
            var result = await _messaging.StageConsumptionAsync(_receipts, registry, _messages, message,
                LogicalReceiptOutcome.Completed, effectiveUtc, ct);
            if (result.Disposition != ReceiptStageDisposition.Staged) await RollbackCoreAsync();
            else { _consumption = message; _consumptionRegistry = registry; }
            return result;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    public async Task StageIncomingHandledAsync(DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        try
        {
            if (_consumption is null || _consumptionRegistry is null || !_ownerStaged || !_auditStaged || !_floorStaged || !_publicationStaged)
                throw new InvalidOperationException("Owner, audit, floor and publication must stage before inbox disposition.");
            // Recheck the exact native inbox bytes/identity; a context swapped after claim cannot mark another envelope handled.
            var repeated = await _messaging.StageConsumptionAsync(_receipts, _consumptionRegistry, _messages,
                _consumption, LogicalReceiptOutcome.Completed, effectiveUtc, ct);
            if (repeated.Disposition != ReceiptStageDisposition.ExistingInTransaction)
                throw new InvalidOperationException("Original logical receipt must still belong to this root.");
            await _messaging.StageHandledAsync(_messages.Envelope!, effectiveUtc, ct);
            _incomingHandled = true;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    public async Task<WorkflowStoredResult> StageCreationAsync(WorkflowSpecification specification, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen(); ArgumentNullException.ThrowIfNull(specification);
        try
        {
            if (_ownerStaged || (_consumption is { } incoming && incoming.Binding.Identity.OperationId != specification.RequestId))
                throw new InvalidOperationException("One exact fresh logical owner operation per root required.");
            Bind(new(specification.InstallationId, specification.TenantId, specification.InstanceId));
            var result = await new WorkflowStore(_workflow).StageCreationAsync(specification, ct);
            if (result.Duplicate) await RollbackCoreAsync();
            else { _ownerStaged = true; _ownerOperationId = specification.RequestId; }
            return result;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    public async Task<WorkflowStoredResult> StageTransitionAsync(WorkflowIdentity identity, Guid stepId, Guid operationId,
        string specificationHash, long expectedVersion, WorkflowState nextState, DateTimeOffset recordedAtUtc, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        try
        {
            if (_ownerStaged || (_consumption is { } incoming &&
                (incoming.Binding.Identity.OperationId != operationId || incoming.Binding.Identity.StepId != stepId)))
                throw new InvalidOperationException("Owner transition must match the single logical input operation/step.");
            Bind(identity);
            var result = await new WorkflowStore(_workflow).StageTransitionAsync(identity, stepId, operationId,
                specificationHash, expectedVersion, nextState, recordedAtUtc, ct);
            if (result.Duplicate) await RollbackCoreAsync();
            else { _ownerStaged = true; _ownerOperationId = operationId; _transitionStepId = stepId; }
            return result;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    public async Task StageAuditAsync(AuditIntent intent, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        try
        {
            if (_identity is null || intent.TenantRef != _identity.TenantId || intent.ResourceRef != _identity.InstanceId)
                throw new InvalidOperationException("Audit must bind the staged workflow tenant/instance.");
            var staged = await _audit.StageAsync(intent, ct);
            if (staged.Status != AuditTransactionStageStatus.Staged)
                throw new InvalidOperationException("Fresh workflow root requires a newly staged audit intent; prior AUD alone is insufficient.");
            _auditStaged = true;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    public async Task StageClockFloorAsync(DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        try
        {
            if (_identity is null || effectiveUtc == default) throw new InvalidOperationException("Workflow binding and effective clock required.");
            effectiveUtc = effectiveUtc.ToUniversalTime();
            var floor = await _workflow.Set<WorkflowClockFloorRecord>().SingleOrDefaultAsync(x =>
                x.InstallationId == _identity.InstallationId && x.TenantId == _identity.TenantId, ct);
            if (floor is null) _workflow.Add(new WorkflowClockFloorRecord {
                InstallationId = _identity.InstallationId, TenantId = _identity.TenantId, FloorUtc = effectiveUtc });
            else
            {
                if (effectiveUtc < floor.FloorUtc) throw new InvalidOperationException("Workflow effective clock cannot move backwards.");
                floor.FloorUtc = effectiveUtc;
            }
            await _workflow.SaveChangesAsync(ct); _floorStaged = true;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    public async Task<ReceiptStageResult> StagePublicationAsync(CompiledMessageRegistry registry,
        VerifiedMessageEnvelope message, DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        try
        {
            var identity = message.Binding.Identity;
            if (!_ownerStaged || _identity is null || identity.OperationId != _ownerOperationId ||
                (_transitionStepId is { } step && identity.StepId != step) || identity.InstallationId != _identity.InstallationId || identity.Scope.TenantId != _identity.TenantId ||
                message.Binding.Resource?.Kind != "workflow" || message.Binding.Resource.Id != _identity.InstanceId)
                throw new InvalidOperationException("Publication must bind the staged tenant workflow.");
            var result = await _messaging.StagePublicationAsync(_receipts, registry, _messages, message, effectiveUtc, ct);
            if (result.Disposition == ReceiptStageDisposition.Conflict) throw new InvalidOperationException("Workflow publication hash conflict.");
            var key = (identity.OperationId, identity.StepId, identity.StepVersion, identity.TypeId,
                message.Binding.TypeSchemaVersion, message.PayloadHash);
            if (result.Disposition == ReceiptStageDisposition.Staged) _publications.Add(key);
            else if (!_publications.Contains(key))
                throw new InvalidOperationException("An older publication receipt cannot establish a fresh paired owner mutation; independent reconciliation required.");
            _publicationStaged = true; return result;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    public async Task<WorkflowCommitDisposition> CommitAsync(CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        if (!_ownerStaged || !_auditStaged || !_floorStaged || !_publicationStaged ||
            (_messages.Envelope is not null && (_consumption is null || !_incomingHandled)))
        { await RollbackCoreAsync(); return WorkflowCommitDisposition.NoCommitAttempt; }
        _phase = Phase.Committing;
        AuditAppendOutcome outcome;
        try { outcome = await _audit.CommitAsync(ct); }
        catch
        {
            if (_audit.CommitAttempted) { _phase = Phase.Unknown; _admission.Close(); return WorkflowCommitDisposition.Unknown; }
            _phase = Phase.Open; await RollbackCoreAsync(); return WorkflowCommitDisposition.NoCommitAttempt;
        }
        var result = WorkflowCommitClassification.Classify(outcome.Status, _audit.CommitAttempted);
        _phase = result switch {
            WorkflowCommitDisposition.Committed => Phase.Committed,
            WorkflowCommitDisposition.Unknown => Phase.Unknown,
            _ => Phase.NoCommitAttempt
        };
        if (result == WorkflowCommitDisposition.Unknown) _admission.Close();
        else if (result == WorkflowCommitDisposition.Committed && _incomingHandled && _messages.Envelope is { } incoming)
            incoming.Status = EnvelopeStatus.Handled;
        return result;
    }

    public async Task FlushConfirmedAsync()
    {
        using var operation = EnterOperation();
        if (_phase != Phase.Committed) throw new InvalidOperationException("Only the newly confirmed root commit may flush outgoing messages.");
        await _messages.FlushOutgoingMessagesAsync();
        _phase = Phase.Flushed;
    }

    public async Task RollbackAsync()
    {
        using var operation = EnterOperation();
        await RollbackCoreAsync();
    }

    private async Task RollbackCoreAsync()
    {
        if (_phase == Phase.Committing) throw new InvalidOperationException("The sole commit path is already in progress.");
        if (_phase is Phase.Committed or Phase.Flushed or Phase.Unknown or Phase.NoCommitAttempt) return;
        await _audit.RollbackAsync(); _phase = Phase.NoCommitAttempt;
    }

    private void Bind(WorkflowIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (_identity is not null && _identity != identity) throw new InvalidOperationException("One exact workflow binding per root required.");
        _identity = identity;
    }
    private void RequireOpen()
    {
        if (_phase != Phase.Open) throw new InvalidOperationException("Workflow root is not open for staging or commit.");
    }
    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposed)) return;
        using var operation = EnterOperation();
        try { await RollbackCoreAsync(); await _audit.DisposeAsync(); await _workflow.DisposeAsync(); }
        finally { _disposed = true; }
    }

    private IDisposable EnterOperation()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
            throw new InvalidOperationException("Workflow root operations must be serial; no operation was started.");
        if (Volatile.Read(ref _disposed))
        {
            Volatile.Write(ref _operationInProgress, 0);
            throw new ObjectDisposedException(nameof(WorkflowRootTransaction));
        }
        return new OperationLease(this);
    }
    private sealed class OperationLease(WorkflowRootTransaction owner) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref owner._operationInProgress, 0);
    }
}
