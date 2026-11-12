using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Wolverine;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace Kordra.Api;

/// <summary>
/// Inactive host composition kernel: actual WFL/AUD/MSG share the caller's root.
/// Factories accept only the exact isolated-test target. No production registration, delegation or consumer activation is supplied.
/// </summary>
public sealed class WorkflowRootTransaction : IAsyncDisposable
{
    private static readonly ConditionalWeakTable<NpgsqlTransaction, RootOwnership> RootOwners = new();
    private sealed class RootOwnership { public string? Generation { get; set; } }

    private enum Phase { Open, Committing, Committed, Flushed, NoCommitAttempt, Unknown }
    private readonly WorkflowDbContext _workflow;
    private readonly Guid _installationId;
    private readonly DisposableTestBinding? _disposableTest;
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
    private DateTimeOffset? _timerDispatchUtc;
    private WorkflowWakeStoredResult? _completedTimerDue;
    private WorkflowWakeOperation? _stagedTimerWake;
    private AuditIntent? _stagedAudit;
    private WorkflowStartCommand? _startCommand;
    private WorkflowStartAdmission? _startAdmission;
    private WorkflowSpecification? _startSpecification;
    private Func<CancellationToken, Task<bool>>? _startAuthority;
    private VerifiedMessageEnvelope? _stagedTimerPublication;
    private WorkflowOperationBudget? _timerBudget;
    private WorkflowTransactionOrigin? _timerOrigin;
    private WorkflowWakeSuccessorEvidence? _successorEvidence;
    private VerifiedMessageEnvelope? _consumption;
    private CompiledMessageRegistry? _consumptionRegistry;
    private bool _incomingHandled;
    private readonly HashSet<(Guid Operation, Guid Step, long Version, string Type, int Schema, string Hash)> _publications = [];
    private bool _ownerStaged, _auditStaged, _floorStaged, _publicationStaged;

    private WorkflowRootTransaction(WorkflowDbContext workflow, AuditTransactionContext audit,
        CallerOwnedMessageStager messaging, CallerOwnedReceiptStore receipts, MessageContext messages,
        FairAsyncReaderWriterAdmissionGate admission, Guid installationId, DisposableTestBinding? disposableTest = null)
    {
        _workflow = workflow; _audit = audit; _messaging = messaging; _receipts = receipts;
        _messages = messages; _admission = admission; _installationId = installationId; _disposableTest = disposableTest;
    }

    public static Task<WorkflowRootTransaction> CreateIsolatedTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        CancellationToken ct = default)
        => CreateIsolatedCoreAsync(connection, root, caseId, messages, admission, false, false, ct);

    public static Task<WorkflowRootTransaction> CreateIsolatedWakeTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        CancellationToken ct = default)
        => CreateIsolatedCoreAsync(connection, root, caseId, messages, admission, true, false, ct);

    public static Task<WorkflowRootTransaction> CreateIsolatedCancellationTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        CancellationToken ct = default)
        => CreateIsolatedCoreAsync(connection, root, caseId, messages, admission, true, true, ct);

    public static Task<WorkflowRootTransaction> CreateIsolatedStartTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        CancellationToken ct = default)
        => CreateIsolatedCoreAsync(connection, root, caseId, messages, admission, true, true, ct, true);

    public static Task<WorkflowRootTransaction> CreateDisposableTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        DisposableTestBinding profile, CancellationToken ct = default)
        => CreateIsolatedCoreAsync(connection, root, caseId, messages, admission, false, false, ct, false, profile);

    public static Task<WorkflowRootTransaction> CreateDisposableWakeTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        DisposableTestBinding profile, CancellationToken ct = default)
        => CreateIsolatedCoreAsync(connection, root, caseId, messages, admission, true, false, ct, false, profile);

    public static Task<WorkflowRootTransaction> CreateDisposableCancellationTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        DisposableTestBinding profile, CancellationToken ct = default)
        => CreateIsolatedCoreAsync(connection, root, caseId, messages, admission, true, true, ct, false, profile);

    public static Task<WorkflowRootTransaction> CreateDisposableStartTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        DisposableTestBinding profile, CancellationToken ct = default)
        => CreateIsolatedCoreAsync(connection, root, caseId, messages, admission, true, true, ct, true, profile);

    private static async Task<WorkflowRootTransaction> CreateIsolatedCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, MessageContext messages, FairAsyncReaderWriterAdmissionGate admission,
        bool wakeOperations, bool cancellationIntents, CancellationToken ct, bool startReceipts = false, DisposableTestBinding? disposableTest = null)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(messages); ArgumentNullException.ThrowIfNull(admission);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(root.Connection, connection) ||
            (messages.Transaction is not null && !ReferenceEquals(messages.Transaction, messages)))
            throw new InvalidOperationException("Workflow composition requires an open caller root and an unenlisted native context.");
        // Native received contexts use themselves as the default envelope transaction.
        // Accept only that self binding or null; never replace a foreign owner enlistment.
        var workflow = disposableTest is { } selected
            ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, startReceipts ? 4 : cancellationIntents ? 3 : wakeOperations ? 2 : 1, selected)
            : startReceipts ? WorkflowDbContext.CreateStartFixture(connection, caseId) : cancellationIntents ? WorkflowDbContext.CreateCancellationFixture(connection, caseId) :
            wakeOperations ? WorkflowDbContext.CreateWakeFixture(connection, caseId) : WorkflowDbContext.CreateFixture(connection, caseId);
        AuditTransactionContext? audit = null;
        try
        {
            await ClaimCoordinatorAsync(connection, root, ct);
            var schema = workflow.SchemaName;
            var native = messages.Runtime.Storage as IMessageDatabase
                ?? throw new InvalidOperationException("Native PostgreSQL message storage required.");
            var messaging = new CallerOwnedMessageStager(connection, root, native, schema);
            var receipts = disposableTest is { } receiptProfile
                ? CallerOwnedReceiptStore.CreateDisposableTestStore(connection, root, caseId, receiptProfile)
                : CallerOwnedReceiptStore.CreateIsolatedTestStore(connection, root, caseId);
            await workflow.Database.UseTransactionAsync(root, ct);
            audit = disposableTest is { } auditProfile
                ? await AuditTransactionContext.CreateDisposableIsolatedTestAsync(connection, root, caseId, auditProfile, ct)
                : await AuditTransactionContext.CreateIsolatedTestAsync(connection, root, caseId, ct);
            var owner = new WorkflowRootTransaction(workflow, audit, messaging, receipts, messages, admission, caseId, disposableTest);
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

    public async Task<WorkflowTransactionOrigin> CaptureTimerCommitOriginAsync(CancellationToken ct = default)
    {
        using var operation = EnterOperation(); RequireOpen();
        try
        {
            if (!_ownerStaged || !_floorStaged || _stagedTimerWake is null || _identity is null || _ownerOperationId is null ||
                _stagedAudit is null || _stagedTimerPublication is null || (_consumption is not null && !_incomingHandled))
                throw new InvalidOperationException("Complete staged timer effects required before capturing commit provenance.");
            if (_timerOrigin is not null) return _timerOrigin;
            var caseId = Guid.ParseExact(_workflow.SchemaName[4..], "N");
            _timerOrigin = _disposableTest is { } profile
                ? await WorkflowTransactionOrigin.CaptureDisposableTestAsync((NpgsqlConnection)_workflow.Database.GetDbConnection(),
                    (NpgsqlTransaction)_workflow.Database.CurrentTransaction!.GetDbTransaction(), caseId, _identity,
                    _ownerOperationId.Value, _stagedTimerWake, _stagedAudit, _stagedTimerPublication, _consumption, profile, ct)
                : await WorkflowTransactionOrigin.CaptureIsolatedAsync((NpgsqlConnection)_workflow.Database.GetDbConnection(),
                    (NpgsqlTransaction)_workflow.Database.CurrentTransaction!.GetDbTransaction(), caseId, _identity,
                    _ownerOperationId.Value, _stagedTimerWake, _stagedAudit, _stagedTimerPublication, _consumption, ct);
            return _timerOrigin;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    internal static async Task ClaimCoordinatorAsync(NpgsqlConnection connection, NpgsqlTransaction root, CancellationToken ct)
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

    public async Task<WorkflowStoredResult> StageCreationAsync(WorkflowSpecification specification, DateTimeOffset effectiveUtc,
        CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen(); ArgumentNullException.ThrowIfNull(specification);
        try
        {
            if (_workflow.IncludesStartReceipts || _ownerStaged || (_consumption is { } incoming && incoming.Binding.Identity.OperationId != specification.RequestId))
                throw new InvalidOperationException("One exact fresh logical owner operation per root required.");
            Bind(new(specification.InstallationId, specification.TenantId, specification.InstanceId));
            var result = await new WorkflowStore(_workflow).StageCreationAsync(specification, effectiveUtc, ct);
            if (result.Duplicate) await RollbackCoreAsync();
            else { _ownerStaged = true; _ownerOperationId = specification.RequestId; }
            return result;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    /// <summary>Isolated admitted start, with key lookup before specification allocation. Does not create IAM delegation.</summary>
    public async Task<WorkflowStoredResult> StageStartAsync(WorkflowStartCommand command,
        Func<WorkflowSpecification> createSpecification, Func<CancellationToken, Task<bool>> stillCurrent,
        DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        return await StageStartCoreAsync(command, createSpecification, stillCurrent, effectiveUtc, ct);
    }

    /// <summary>Retains typed current admission and validates lazy owner allocation plus exact audit revisions.</summary>
    public async Task<WorkflowStoredResult> StageAdmittedStartAsync(WorkflowStartAdmission admitted,
        WorkflowStartAdmissionReader authority, SessionSelection session, DateTimeOffset originalDeadlineUtc,
        string idempotencyKey, Func<WorkflowSpecification> createSpecification, DateTimeOffset effectiveUtc,
        CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        try
        {
            ArgumentNullException.ThrowIfNull(admitted); ArgumentNullException.ThrowIfNull(authority);
            ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(createSpecification);
            if (admitted.InstallationId != _installationId || session.PrincipalId != admitted.Eligibility.WorkflowAuthority.Subject.PrincipalId ||
                session.BootGeneration != admitted.Eligibility.WorkflowAuthority.Subject.BootGeneration)
                throw new InvalidOperationException("Start admission differs from the root installation or current session.");
            var command = admitted.BindCommand(originalDeadlineUtc, idempotencyKey);
            var result = await StageStartCoreAsync(command, () =>
            {
                var specification = createSpecification();
                if (!admitted.MatchesSpecification(specification, command) || specification.CreatedAtUtc != effectiveUtc)
                    throw new InvalidOperationException("Allocated start specification differs from the admitted owners.");
                return specification;
            }, async token => admitted.IsCurrentSuccessor(await authority.ReadUnderAdmissionAsync(session,
                admitted.Selection, admitted.RequestId, admitted.InvocationId, token)), effectiveUtc, ct, admitted);
            if (!result.Duplicate) _startAdmission = admitted;
            return result;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    private async Task<WorkflowStoredResult> StageStartCoreAsync(WorkflowStartCommand command,
        Func<WorkflowSpecification> createSpecification, Func<CancellationToken, Task<bool>> stillCurrent,
        DateTimeOffset effectiveUtc, CancellationToken ct, WorkflowStartAdmission? admitted = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(createSpecification);
            ArgumentNullException.ThrowIfNull(stillCurrent);
            if (_ownerStaged || _consumption is not null || _messages.Envelope is not null ||
                _auditStaged || _floorStaged || _publicationStaged || _admission.IsClosed || !await stillCurrent(ct) || _admission.IsClosed)
                throw new InvalidOperationException("Start requires fresh current admission on a producer root.");
            WorkflowSpecification? generated = null;
            var result = await new WorkflowStartStore(_workflow).StageAsync(command,
                () => generated = createSpecification(), effectiveUtc,
                original => admitted is null || admitted.MatchesOwnerBinding(original, command), ct);
            if (!await stillCurrent(ct) || _admission.IsClosed)
                throw new InvalidOperationException("Workflow start authority changed.");
            if (result.Duplicate)
            {
                await RollbackCoreAsync();
                return result;
            }
            _startSpecification = generated ?? throw new InvalidOperationException("Fresh start has no specification.");
            Bind(new(generated.InstallationId, generated.TenantId, generated.InstanceId));
            _transitionStepId = await _workflow.Set<WorkflowStepRecord>().Where(x =>
                x.InstallationId == generated.InstallationId && x.TenantId == generated.TenantId &&
                x.InstanceId == generated.InstanceId && x.Ordinal == 0).Select(x => x.StepId).SingleAsync(ct);
            _startCommand = command;
            _startAuthority = stillCurrent;
            _ownerOperationId = command.RequestId;
            _ownerStaged = true;
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

    /// <summary>Initial timer CAS plus its exact paired wake on this root. Never an inbox handler or successor replay.</summary>
    public async Task<WorkflowWakeStoredResult> StageInitialTimerDispatchAsync(WorkflowSpecification specification,
        WorkflowWakeOperation wake, CompiledMessageRegistry registry, long expectedWorkflowVersion, long expectedTimerVersion,
        Guid workerId, Guid workerBootId, DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        try
        {
            if (_ownerStaged || _consumption is not null || _messages.Envelope is not null || _auditStaged || _publicationStaged)
                throw new InvalidOperationException("Initial timer scheduler requires one fresh producer-only root.");
            var envelope = WorkflowWakePublication.Prepare(specification, wake, registry, effectiveUtc);
            Bind(new(wake.InstallationId, wake.TenantId, wake.InstanceId));
            var staged = await new WorkflowWakeStore(_workflow).StageInitialDispatchAsync(wake,
                expectedWorkflowVersion, expectedTimerVersion, workerId, workerBootId, effectiveUtc, ct);
            if (staged.Duplicate) { await RollbackCoreAsync(); return staged; }
            _ownerStaged = true; _ownerOperationId = wake.SchedulerOperationId; _transitionStepId = wake.StepId;
            _timerDispatchUtc = effectiveUtc.ToUniversalTime();
            _stagedTimerWake = wake;
            // The delivered wake has a distinct operation ID. Only this typed, persisted binding
            // can pair it with the scheduler mutation; generic publication checks stay unchanged.
            var publication = await _messaging.StagePublicationAsync(_receipts, registry, _messages, envelope, effectiveUtc, ct);
            if (publication.Disposition != ReceiptStageDisposition.Staged)
                throw new InvalidOperationException("Fresh timer dispatch requires a newly staged exact wake publication.");
            _stagedTimerPublication = envelope;
            _publicationStaged = true;
            return staged;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    public async Task<WorkflowWakeStoredResult> StageTimerDueCompletionAsync(Guid workerId, Guid workerBootId,
        DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        using var operation = EnterOperation(); RequireOpen();
        try
        {
            if (_ownerStaged || _consumption is null || _consumptionRegistry is null || _messages.Envelope is null)
                throw new InvalidOperationException("Timer completion requires one freshly claimed native logical input.");
            var delivery = WorkflowWakePublication.ReadDelivery(_consumption);
            Bind(delivery.Identity);
            var result = await new WorkflowWakeStore(_workflow).StageTimerDueCompletionAsync(delivery, workerId, workerBootId, effectiveUtc, ct);
            if (result.Duplicate) { await RollbackCoreAsync(); return result; }
            _ownerStaged = true; _ownerOperationId = delivery.Request.OperationId; _transitionStepId = delivery.Request.StepId;
            _timerDispatchUtc = effectiveUtc.ToUniversalTime();
            _completedTimerDue = result;
            _stagedTimerWake = result.Operation;
            return result;
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    public async Task<WorkflowWakeStoredResult> StageTimerSuccessorAsync(WorkflowWakeSuccessorEvidence evidence,
        WorkflowWakeOperation successor, long expectedTimerVersion, Guid workerId, Guid workerBootId, Guid decisionId,
        DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        using var operation = EnterOperation(); RequireOpen();
        try
        {
            ArgumentNullException.ThrowIfNull(evidence);
            ArgumentNullException.ThrowIfNull(successor);
            if (!_workflow.IncludesCancellationIntents || _ownerStaged || _consumption is not null ||
                _messages.Envelope is not null || _auditStaged || _publicationStaged ||
                evidence.Custody.InstallationId != successor.InstallationId || !evidence.Custody.MatchesTestTarget(_disposableTest) ||
                effectiveUtc < evidence.ObservedAtUtc)
                throw new InvalidOperationException("Successor requires a fresh producer root and current exact fixture evidence.");
            await using var held = await evidence.Custody.EnterAsync(ct);
            var identity = new WorkflowIdentity(successor.InstallationId, successor.TenantId, successor.InstanceId);
            Bind(identity);
            var successors = new WorkflowWakeSuccessorStore(_workflow);
            if (!await successors.LockOwnerAsync(identity, successor.SpecificationHash, ct))
                throw new InvalidOperationException("Successor differs from the locked owner binding.");
            var recorded = await _workflow.Set<WorkflowWakeOperationRecord>().AsNoTracking().AnyAsync(x =>
                x.InstallationId == identity.InstallationId && x.TenantId == identity.TenantId && x.InstanceId == identity.InstanceId &&
                x.StepId == successor.StepId && x.TimerId == successor.TimerId && x.Generation == successor.Generation, ct);
            if (!recorded && (effectiveUtc != successor.OccurredAtUtc ||
                !await evidence.Reader.RevalidateSuccessorAsync(evidence, _messages, effectiveUtc, ct)))
                throw new InvalidOperationException("Successor evidence no longer matches the locked owner and settled delivery.");
            var staged = await successors.StageAsync(evidence.Previous, successor,
                expectedTimerVersion, workerId, workerBootId, decisionId, evidence.EvidenceHash, effectiveUtc, ct);
            if (staged.Duplicate) { await RollbackCoreAsync(); return staged; }
            var envelope = WorkflowWakePublication.Prepare(evidence.Specification, successor, evidence.Registry, effectiveUtc);
            _ownerStaged = true; _ownerOperationId = successor.SchedulerOperationId; _transitionStepId = successor.StepId;
            _timerDispatchUtc = effectiveUtc.ToUniversalTime(); _stagedTimerWake = successor;
            _successorEvidence = evidence;
            var publication = await _messaging.StagePublicationAsync(_receipts, evidence.Registry, _messages, envelope, effectiveUtc, ct);
            if (publication.Disposition != ReceiptStageDisposition.Staged)
                throw new InvalidOperationException("Successor requires a fresh exact publication on its caller root.");
            _stagedTimerPublication = envelope; _publicationStaged = true;
            return staged;
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
            if (_startCommand is { } start && (_startSpecification is not { } specification ||
                intent.Stage != AuditIntentStage.Mutation || intent.SubjectRef != start.ActorId ||
                intent.Permission != "workflow.start" || intent.ScopeKind != "environment" ||
                intent.ScopeId != specification.EnvironmentId || intent.CorrelationId != start.RequestId ||
                intent.SafeReason != "workflow-start-" + start.BusinessHash ||
                intent.PolicyRevision is null or <= 0 || intent.AuthorityRevision is null or <= 0 ||
                intent.OccurredAtUtc != specification.CreatedAtUtc))
                throw new InvalidOperationException("Start audit must bind the exact admitted command.");
            if (_startAdmission is { } admitted && (intent.InvocationId != admitted.InvocationId ||
                intent.PolicyRevision != admitted.Policy.Policy.PolicyRevision ||
                intent.AuthorityRevision != admitted.Eligibility.WorkflowAuthority.Grant.AssignmentRevision))
                throw new InvalidOperationException("Start audit revisions must match the retained admission.");
            var staged = await _audit.StageAsync(intent, ct);
            if (staged.Status != AuditTransactionStageStatus.Staged)
                throw new InvalidOperationException("Fresh workflow root requires a newly staged audit intent; prior AUD alone is insufficient.");
            _stagedAudit = intent;
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
            await StageClockFloorCoreAsync(effectiveUtc, ct);
        }
        catch { await RollbackCoreAsync(); throw; }
    }

    public async Task StageCheckedTimerClockFloorAsync(WorkflowOperationBudget budget, CancellationToken ct = default)
    {
        using var operation = EnterOperation(); RequireOpen();
        try
        {
            ArgumentNullException.ThrowIfNull(budget);
            if (_stagedTimerWake is null || budget.DescriptorHash != _stagedTimerWake.DescriptorHash || budget.AdmittedUtc != _timerDispatchUtc)
                throw new InvalidOperationException("Clock budget differs from the admitted timer operation.");
            budget.ThrowIfExpired();
            await using var command = _workflow.Database.GetDbConnection().CreateCommand();
            command.Transaction = _workflow.Database.CurrentTransaction!.GetDbTransaction(); command.CommandTimeout = 2;
            command.CommandText = "SELECT clock_timestamp()";
            var databaseUtc = (DateTime)(await command.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("Database clock sample unavailable."));
            budget.CheckDatabaseUtc(new DateTimeOffset(DateTime.SpecifyKind(databaseUtc, DateTimeKind.Utc)));
            await StageClockFloorCoreAsync(budget.AdmittedUtc, ct);
            _timerBudget = budget;
        }
        catch (WorkflowClockTrustException) { _admission.Close(); await RollbackCoreAsync(); throw; }
        catch { await RollbackCoreAsync(); throw; }
    }

    private async Task StageClockFloorCoreAsync(DateTimeOffset effectiveUtc, CancellationToken ct)
    {
        if (_identity is null || effectiveUtc == default) throw new InvalidOperationException("Workflow binding and effective clock required.");
        effectiveUtc = effectiveUtc.ToUniversalTime();
        if (_startSpecification is { } start && effectiveUtc != start.CreatedAtUtc)
            throw new InvalidOperationException("Start floor must use the exact admitted creation instant.");
        if (_timerDispatchUtc is { } dispatchedAt && effectiveUtc != dispatchedAt)
            throw new InvalidOperationException("Timer floor must use the exact admitted scheduler instant.");
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

    public async Task<ReceiptStageResult> StagePublicationAsync(CompiledMessageRegistry registry,
        VerifiedMessageEnvelope message, DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        RequireOpen();
        try
        {
            var identity = message.Binding.Identity;
            if (_startSpecification is { } start)
            {
                using var payloadDocument = JsonDocument.Parse(message.CopyPayload());
                var payload = new WorkflowTransitionRequestedCodec().Read(payloadDocument.RootElement);
                if (identity.TypeId != "workflow.transition-requested.v1" || message.Binding.TypeSchemaVersion != 1 ||
                    payload.InstanceId != start.InstanceId || payload.StepId != _transitionStepId ||
                    payload.OperationId != start.RequestId || payload.ExpectedVersion != 1 || payload.CustodyEpoch != 1 ||
                    payload.TransitionKind != WorkflowTransitionKind.Checkpoint ||
                    payload.SpecificationHash != start.SpecificationHash || payload.TemplateHash != WorkflowSpecification.TemplateHash ||
                    message.Delivery.CorrelationId != start.RequestId || identity.StepVersion != 1 ||
                    message.Binding.OccurredAtUtc != start.CreatedAtUtc || message.Binding.DeadlineUtc > start.DeadlineUtc)
                    throw new InvalidOperationException("Start publication differs from the initial compiled checkpoint.");
            }

            if (_completedTimerDue is { } completed)
            {
                WorkflowTransitionResultPublication.RequireTimerDueMatch(message, completed.Operation, completed.WorkflowVersion);
                if (_stagedTimerPublication is { } prior &&
                    (prior.Delivery != message.Delivery || prior.PayloadHash != message.PayloadHash))
                    throw new InvalidOperationException("A staged timer result cannot be rebound to another publication.");
            }
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
            if (_completedTimerDue is not null) _stagedTimerPublication = message;
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
        try { _timerBudget?.ThrowIfExpired(); }
        catch (WorkflowClockTrustException) { _admission.Close(); await RollbackCoreAsync(); return WorkflowCommitDisposition.NoCommitAttempt; }
        catch (TimeoutException) { await RollbackCoreAsync(); return WorkflowCommitDisposition.NoCommitAttempt; }
        if (_successorEvidence is { } successorEvidence)
        {
            try { await using var held = await successorEvidence.Custody.EnterAsync(ct); }
            catch { await RollbackCoreAsync(); return WorkflowCommitDisposition.NoCommitAttempt; }
        }
        if (_startAuthority is { } revalidate)
        {
            try
            {
                if (_admission.IsClosed || !await revalidate(ct) || _admission.IsClosed)
                {
                    await RollbackCoreAsync();
                    return WorkflowCommitDisposition.NoCommitAttempt;
                }
            }
            catch { await RollbackCoreAsync(); return WorkflowCommitDisposition.NoCommitAttempt; }
        }
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
