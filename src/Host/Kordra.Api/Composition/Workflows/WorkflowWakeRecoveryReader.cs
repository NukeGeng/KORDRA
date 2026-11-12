using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine.Runtime;

namespace Kordra.Api;

/// <summary>
/// Read-only composition of owner observations, not a commit receipt, authority or backend-terminal proof.
/// Even matching observations cannot authorize replay, a new generation or custody takeover.
/// </summary>
public sealed class WorkflowWakeRecoveryReader : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly Guid _caseId;
    private readonly DisposableTestBinding? _disposableTest;
    private readonly WorkflowWakeReceiptInspector _scheduler;
    private readonly WorkflowReceiptInspector _workflow;
    private readonly MessageReceiptInspector _messages;
    private readonly AuditMutationEvidenceReader _audit;

    private WorkflowWakeRecoveryReader(NpgsqlConnection independent, Guid caseId, bool includesCancellation, DisposableTestBinding? disposableTest = null)
    {
        _connection = independent; _caseId = caseId; _disposableTest = disposableTest;
        _scheduler = disposableTest is { } schedulerProfile
            ? WorkflowWakeReceiptInspector.CreateDisposableTestInspector(independent, caseId, includesCancellation, schedulerProfile)
            : includesCancellation ? WorkflowWakeReceiptInspector.CreateIsolatedCancellationTestInspector(independent, caseId)
                : WorkflowWakeReceiptInspector.CreateIsolatedTestInspector(independent, caseId);
        _workflow = disposableTest is { } workflowProfile
            ? WorkflowReceiptInspector.CreateDisposableTestInspector(independent, caseId, workflowProfile)
            : WorkflowReceiptInspector.CreateIsolatedTestInspector(independent, caseId);
        _messages = disposableTest is { } messageProfile
            ? MessageReceiptInspector.CreateDisposableTestInspector(independent, caseId, messageProfile)
            : MessageReceiptInspector.CreateIsolatedTestInspector(independent, caseId);
        _audit = new(new AuditReaderFactory(independent, caseId, disposableTest));
    }

    public static WorkflowWakeRecoveryReader CreateIsolatedTestReader(NpgsqlConnection independent, Guid caseId)
    {
        _ = WorkflowFixtureTarget.Validate(independent, caseId);
        return new(independent, caseId, includesCancellation: false);
    }

    public static WorkflowWakeRecoveryReader CreateIsolatedCancellationTestReader(NpgsqlConnection independent, Guid caseId)
        => new(independent, caseId, includesCancellation: true);

    public static WorkflowWakeRecoveryReader CreateDisposableTestReader(NpgsqlConnection independent, Guid caseId,
        bool includesCancellation, DisposableTestBinding profile)
    {
        _ = WorkflowFixtureTarget.ValidateDisposableTest(independent, caseId, profile);
        return new(independent, caseId, includesCancellation, profile);
    }

    public async Task<WorkflowReceiptInspectionStatus> InspectSchedulerAsync(WorkflowSpecification specification,
        WorkflowWakeOperation wake, VerifiedMessageEnvelope request, AuditIntent schedulerAudit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            ValidateRequest(specification, wake, request);
            var audit = AuditExpectation(specification, wake, schedulerAudit);
            if (_connection.State != ConnectionState.Open) return WorkflowReceiptInspectionStatus.Unavailable;
            var owner = await _scheduler.InspectAsync(new(new(new(wake.InstallationId, wake.TenantId, wake.InstanceId),
                wake.RequestId, wake.SpecificationHash, wake.ExpectedWorkflowVersion, wake.SchedulerOperationId, wake.StepId),
                wake.TimerId, wake.Generation, wake.DescriptorHash), ct);
            var publication = await _messages.InspectPublicationAsync(request.Binding.Identity, request.PayloadHash, ct);
            var publicationStatus = MessageStatus(publication.Status);
            if (publication.Status == ReceiptInspectionStatus.MatchingObserved && publication.OriginalMessageId != wake.MessageId)
                publicationStatus = WorkflowReceiptInspectionStatus.Conflict;
            return Combine(owner, publicationStatus, AuditStatus(await _audit.MatchUnderAdmissionAsync(audit, ct)));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { return WorkflowReceiptInspectionStatus.Conflict; }
    }

    public async Task<WorkflowReceiptInspectionStatus> InspectCompletionAsync(WorkflowSpecification specification,
        WorkflowWakeOperation wake, VerifiedMessageEnvelope request, AuditIntent schedulerAudit,
        VerifiedMessageEnvelope result, AuditIntent completionAudit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            ValidateRequest(specification, wake, request);
            if (wake.ExpectedWorkflowVersion == long.MaxValue) return WorkflowReceiptInspectionStatus.Conflict;
            var resultingVersion = wake.ExpectedWorkflowVersion + 1;
            WorkflowTransitionResultPublication.RequireTimerDueMatch(result, wake, resultingVersion);
            var audit = AuditExpectation(specification, wake, completionAudit);
            _ = AuditExpectation(specification, wake, schedulerAudit);
            if (schedulerAudit.IntentId == completionAudit.IntentId) return WorkflowReceiptInspectionStatus.Conflict;
            if (_connection.State != ConnectionState.Open) return WorkflowReceiptInspectionStatus.Unavailable;
            var scheduler = await InspectSchedulerAsync(specification, wake, request, schedulerAudit, ct);
            var owner = await _workflow.InspectAsync(new(new(wake.InstallationId, wake.TenantId, wake.InstanceId),
                wake.RequestId, wake.SpecificationHash, resultingVersion, wake.WakeOperationId, wake.StepId), ct);
            var logical = await _messages.InspectLogicalAsync(request.Binding.Identity, request.PayloadHash, ct);
            var logicalStatus = MessageStatus(logical.Status);
            if (logical.Status == ReceiptInspectionStatus.MatchingObserved && logical.OriginalOutcome != LogicalReceiptOutcome.Completed)
                logicalStatus = WorkflowReceiptInspectionStatus.Conflict;
            var publication = await _messages.InspectPublicationAsync(result.Binding.Identity, result.PayloadHash, ct);
            var publicationStatus = MessageStatus(publication.Status);
            if (publication.Status == ReceiptInspectionStatus.MatchingObserved && publication.OriginalMessageId != result.Delivery.MessageId)
                publicationStatus = WorkflowReceiptInspectionStatus.Conflict;
            return Combine(scheduler, owner, logicalStatus, publicationStatus, AuditStatus(await _audit.MatchUnderAdmissionAsync(audit, ct)));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { return WorkflowReceiptInspectionStatus.Conflict; }
    }

    /// <summary>Revalidates a closed operator selection under fixture custody. Does not write or disclose a decision.</summary>
    public async Task<bool> ObserveReconciliationSelectionAsync(WorkflowReconciliationSelection selection,
        WorkflowReadScope scope, WorkflowFixtureCustodyLease custody, WorkflowTransactionOrigin origin,
        WorkflowSpecification specification, WorkflowWakeOperation wake, VerifiedMessageEnvelope request,
        AuditIntent schedulerAudit, VerifiedMessageEnvelope result, AuditIntent completionAudit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (selection is not { IsValid: true } || scope is null || custody is null || origin is null ||
            specification is null || wake is null || request is null || result is null || schedulerAudit is null ||
            completionAudit is null || custody.InstallationId != _caseId || !custody.MatchesTestTarget(_disposableTest) || specification.InstallationId != _caseId ||
            scope.TenantId != specification.TenantId || scope.ProjectId != specification.ProjectId ||
            scope.EnvironmentId != specification.EnvironmentId || scope.InstanceId != specification.InstanceId ||
            selection.OperationId != wake.WakeOperationId || !MatchesOrigin(origin, wake, selection.OperationId) ||
            wake.ExpectedWorkflowVersion == long.MaxValue)
            return false;

        var committed = selection.Kind == WorkflowReconciliationKind.BindMatchingCommittedReceipt;
        if (selection.ExpectedVersion != wake.ExpectedWorkflowVersion + (committed ? 1 : 0))
            return false;
        try
        {
            if (selection.EvidenceHash != WorkflowReconciliationEvidence.ComputeHash(origin, wake, request, schedulerAudit, result, completionAudit))
                return false;
            await using var held = await custody.EnterAsync(ct);
            var matched = committed
                ? await ReconcileCompletionAsync(origin, specification, wake, request, schedulerAudit, result, completionAudit, ct)
                    == WorkflowReceiptInspectionStatus.MatchingObserved
                : await ObserveAbortedCompletionAsync(origin, specification, wake, request, schedulerAudit, result, completionAudit, ct);
            return matched && !custody.IsClosed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NpgsqlException)
        { return false; }
    }

    /// <summary>Correlates scheduler receipts with the terminal original root. Does not authorize replay.</summary>
    public Task<WorkflowReceiptInspectionStatus> ReconcileSchedulerAsync(WorkflowTransactionOrigin origin,
        WorkflowSpecification specification, WorkflowWakeOperation wake, VerifiedMessageEnvelope request,
        AuditIntent schedulerAudit, CancellationToken ct = default)
        => ReconcileAsync(origin, wake, wake.SchedulerOperationId, schedulerAudit, request, null,
            () => InspectSchedulerAsync(specification, wake, request, schedulerAudit, ct), ct);

    /// <summary>Correlates completion receipts with the terminal original receiver root.</summary>
    public Task<WorkflowReceiptInspectionStatus> ReconcileCompletionAsync(WorkflowTransactionOrigin origin,
        WorkflowSpecification specification, WorkflowWakeOperation wake, VerifiedMessageEnvelope request,
        AuditIntent schedulerAudit, VerifiedMessageEnvelope result, AuditIntent completionAudit, CancellationToken ct = default)
        => ReconcileAsync(origin, wake, wake.WakeOperationId, completionAudit, result, request,
            () => InspectCompletionAsync(specification, wake, request, schedulerAudit, result, completionAudit, ct), ct);

    /// <summary>
    /// Observes an aborted, disconnected receiver root and the corresponding owner facts. This
    /// does not prove native delivery settlement or custody, and never authorizes a successor.
    /// </summary>
    public async Task<bool> ObserveAbortedCompletionAsync(WorkflowTransactionOrigin origin,
        WorkflowSpecification specification, WorkflowWakeOperation wake, VerifiedMessageEnvelope request,
        AuditIntent schedulerAudit, VerifiedMessageEnvelope result, AuditIntent completionAudit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ct.ThrowIfCancellationRequested();
        if (!MatchesOrigin(origin, wake, wake.WakeOperationId) || wake.ExpectedWorkflowVersion == long.MaxValue)
            return false;
        try
        {
            ValidateRequest(specification, wake, request);
            WorkflowTransitionResultPublication.RequireTimerDueMatch(result, wake, wake.ExpectedWorkflowVersion + 1);
            var audit = AuditExpectation(specification, wake, completionAudit);
            if (schedulerAudit.IntentId == completionAudit.IntentId || !origin.MatchesStagedEffects(completionAudit, result, request))
                return false;

            var root = _disposableTest is { } profile
                ? await WorkflowTransactionProbe.InspectDisposableTestAsync(origin, _connection, profile, ct)
                : await WorkflowTransactionProbe.InspectIsolatedAsync(origin, _connection, ct);
            if (root.Status != WorkflowTransactionProbeStatus.Aborted || root.OriginalBackendPresent != false ||
                !await _scheduler.ObserveUncompletedWakeAsync(specification, wake, ct) ||
                await InspectSchedulerAsync(specification, wake, request, schedulerAudit, ct) != WorkflowReceiptInspectionStatus.MatchingObserved)
                return false;

            var logical = await _messages.InspectLogicalAsync(request.Binding.Identity, request.PayloadHash, ct);
            var publication = await _messages.InspectPublicationAsync(result.Binding.Identity, result.PayloadHash, ct);
            return logical.ExactKeyAbsent && publication.ExactKeyAbsent &&
                await _audit.MatchUnderAdmissionAsync(audit, ct) == AuditEvidenceStatus.Missing;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { return false; }
    }

    public async Task<WorkflowWakeSuccessorEvidence?> ObserveSuccessorEvidenceAsync(WorkflowFixtureCustodyLease custody,
        WorkflowTransactionOrigin origin, WorkflowSpecification specification, WorkflowWakeOperation wake,
        VerifiedMessageEnvelope request, AuditIntent schedulerAudit, VerifiedMessageEnvelope result, AuditIntent completionAudit,
        CompiledMessageRegistry registry, MessageContext native, DateTimeOffset effectiveUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(custody);
        if (custody.InstallationId != _caseId || !custody.MatchesTestTarget(_disposableTest) || effectiveUtc == default) return null;
        await using var held = await custody.EnterAsync(ct);
        if (!await ObserveUncompletedPredecessorAsync(origin, specification, wake, request, schedulerAudit, result, completionAudit, ct) ||
            !await ObserveSettlementAsync(origin, wake, request, registry, native, effectiveUtc, ct))
            return null;
        return new(this, custody, origin, specification, wake, request, schedulerAudit, result, completionAudit,
            registry, effectiveUtc.ToUniversalTime());
    }

    internal async Task<bool> RevalidateSuccessorAsync(WorkflowWakeSuccessorEvidence evidence,
        MessageContext native, DateTimeOffset effectiveUtc, CancellationToken ct)
        => ReferenceEquals(evidence.Reader, this) &&
            await ObserveUncompletedPredecessorAsync(evidence.Origin, evidence.Specification, evidence.Previous, evidence.Request,
                evidence.SchedulerAudit, evidence.Result, evidence.CompletionAudit, ct) &&
            await ObserveSettlementAsync(evidence.Origin, evidence.Previous, evidence.Request, evidence.Registry, native, effectiveUtc, ct);

    private async Task<bool> ObserveSettlementAsync(WorkflowTransactionOrigin origin, WorkflowWakeOperation wake,
        VerifiedMessageEnvelope request, CompiledMessageRegistry registry, MessageContext native,
        DateTimeOffset effectiveUtc, CancellationToken ct)
        => await _messages.ObserveExpiredQuarantineAsync(request, registry, native, effectiveUtc, ct) ||
            (origin.OperationId == wake.WakeOperationId &&
                await _messages.ObserveExpiredHandledAsync(request, registry, native, effectiveUtc, ct));

    private async Task<bool> ObserveUncompletedPredecessorAsync(WorkflowTransactionOrigin origin,
        WorkflowSpecification specification, WorkflowWakeOperation wake, VerifiedMessageEnvelope request,
        AuditIntent schedulerAudit, VerifiedMessageEnvelope result, AuditIntent completionAudit, CancellationToken ct)
    {
        if (origin.OperationId == wake.WakeOperationId)
            return await ObserveAbortedCompletionAsync(origin, specification, wake, request, schedulerAudit, result, completionAudit, ct);
        if (origin.OperationId != wake.SchedulerOperationId || wake.ExpectedWorkflowVersion == long.MaxValue) return false;
        try
        {
            WorkflowTransitionResultPublication.RequireTimerDueMatch(result, wake, wake.ExpectedWorkflowVersion + 1);
            if (await ReconcileSchedulerAsync(origin, specification, wake, request, schedulerAudit, ct) != WorkflowReceiptInspectionStatus.MatchingObserved ||
                !await _scheduler.ObserveUncompletedWakeAsync(specification, wake, ct))
                return false;
            var logical = await _messages.InspectLogicalAsync(request.Binding.Identity, request.PayloadHash, ct);
            var publication = await _messages.InspectPublicationAsync(result.Binding.Identity, result.PayloadHash, ct);
            return logical.ExactKeyAbsent && publication.ExactKeyAbsent;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }

    private bool MatchesOrigin(WorkflowTransactionOrigin origin, WorkflowWakeOperation wake, Guid operationId)
        => origin.CaseId == _caseId && origin.OperationId == operationId &&
            origin.Identity.InstallationId == wake.InstallationId && origin.Identity.TenantId == wake.TenantId &&
            origin.Identity.InstanceId == wake.InstanceId && origin.DescriptorHash == wake.DescriptorHash;

    private async Task<WorkflowReceiptInspectionStatus> ReconcileAsync(WorkflowTransactionOrigin origin,
        WorkflowWakeOperation wake, Guid expectedOperationId, AuditIntent audit, VerifiedMessageEnvelope publication,
        VerifiedMessageEnvelope? consumption, Func<Task<WorkflowReceiptInspectionStatus>> inspectReceipts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ct.ThrowIfCancellationRequested();
        if (!MatchesOrigin(origin, wake, expectedOperationId))
            return WorkflowReceiptInspectionStatus.Conflict;
        try
        {
            if (!origin.MatchesStagedEffects(audit, publication, consumption))
                return WorkflowReceiptInspectionStatus.Conflict;
        }
        catch (ArgumentException)
        { return WorkflowReceiptInspectionStatus.Conflict; }

        var root = _disposableTest is { } profile
                ? await WorkflowTransactionProbe.InspectDisposableTestAsync(origin, _connection, profile, ct)
                : await WorkflowTransactionProbe.InspectIsolatedAsync(origin, _connection, ct);
        // Probe first so receipt reads cannot predate the original root's confirmed termination.
        if (root.Status == WorkflowTransactionProbeStatus.Unavailable)
            return WorkflowReceiptInspectionStatus.Unavailable;
        if (root.Status == WorkflowTransactionProbeStatus.Unresolved || root.OriginalBackendPresent != false)
            return WorkflowReceiptInspectionStatus.StillUnknown;

        return ClassifyTerminalRoot(root.Status, await inspectReceipts());
    }

    // These inputs express observation only. A caller cannot turn this classification into a permit.
    public static WorkflowReceiptInspectionStatus ClassifyTerminalRoot(WorkflowTransactionProbeStatus root,
        WorkflowReceiptInspectionStatus receipts) => (root, receipts) switch
    {
        (_, WorkflowReceiptInspectionStatus.Conflict) => WorkflowReceiptInspectionStatus.Conflict,
        (WorkflowTransactionProbeStatus.Unavailable, _) => WorkflowReceiptInspectionStatus.Unavailable,
        (WorkflowTransactionProbeStatus.Unresolved, _) => WorkflowReceiptInspectionStatus.StillUnknown,
        (_, WorkflowReceiptInspectionStatus.Unavailable) => WorkflowReceiptInspectionStatus.Unavailable,
        (WorkflowTransactionProbeStatus.Committed, WorkflowReceiptInspectionStatus.MatchingObserved) =>
            WorkflowReceiptInspectionStatus.MatchingObserved,
        (WorkflowTransactionProbeStatus.Aborted, WorkflowReceiptInspectionStatus.MatchingObserved) =>
            WorkflowReceiptInspectionStatus.Conflict,
        // The receipt reader cannot distinguish complete absence from partial evidence.
        // An aborted root plus StillUnknown therefore cannot establish a safe successor.
        _ => WorkflowReceiptInspectionStatus.StillUnknown
    };

    private void ValidateRequest(WorkflowSpecification specification, WorkflowWakeOperation wake, VerifiedMessageEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(specification); ArgumentNullException.ThrowIfNull(wake); ArgumentNullException.ThrowIfNull(request);
        if (specification.InstallationId != _caseId || specification.TenantId != _caseId)
            throw new InvalidOperationException("Expected fixture scope mismatch.");
        _ = WorkflowWakeOperation.ReadCanonical(wake.WriteCanonical(), specification);
        var delivery = WorkflowWakePublication.ReadDelivery(request); var payload = delivery.Request;
        if (delivery.Identity.InstallationId != wake.InstallationId || delivery.Identity.TenantId != wake.TenantId ||
            delivery.Identity.InstanceId != wake.InstanceId || payload.StepId != wake.StepId || payload.OperationId != wake.WakeOperationId ||
            payload.ExpectedVersion != wake.ExpectedWorkflowVersion || payload.SpecificationHash != wake.SpecificationHash ||
            payload.TemplateHash != wake.TemplateHash || payload.CustodyEpoch != wake.CustodyEpoch || payload.TimerId != wake.TimerId ||
            payload.Generation != wake.Generation || delivery.StepVersion != wake.StepVersion || delivery.MessageId != wake.MessageId ||
            delivery.CorrelationId != wake.RequestId || delivery.CausationId != wake.SchedulerOperationId ||
            delivery.OccurredAtUtc != wake.OccurredAtUtc || delivery.DeadlineUtc != wake.DeadlineUtc)
            throw new InvalidOperationException("Observed request differs from the retained wake binding.");
    }

    private static AuditMutationExpectation AuditExpectation(WorkflowSpecification spec, WorkflowWakeOperation wake, AuditIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Stage != AuditIntentStage.Mutation || intent.SubjectRef is not { } subject || intent.TenantRef != wake.TenantId ||
            intent.ResourceRef != wake.InstanceId || intent.ScopeKind != "environment" || intent.ScopeId != spec.EnvironmentId ||
            intent.CorrelationId != wake.RequestId)
            throw new InvalidOperationException("Audit evidence differs from the expected owner scope.");
        return new(intent.IntentId, AuditIntentCanonicalizer.ComputeHash(intent), subject, wake.TenantId, wake.InstanceId,
            intent.InvocationId, intent.Permission, intent.ScopeKind, spec.EnvironmentId, intent.SafeReason);
    }

    private static WorkflowReceiptInspectionStatus MessageStatus(ReceiptInspectionStatus status) => status switch {
        ReceiptInspectionStatus.MatchingObserved => WorkflowReceiptInspectionStatus.MatchingObserved,
        ReceiptInspectionStatus.Conflict => WorkflowReceiptInspectionStatus.Conflict,
        ReceiptInspectionStatus.StillUnknown => WorkflowReceiptInspectionStatus.StillUnknown,
        _ => WorkflowReceiptInspectionStatus.Unavailable
    };
    private static WorkflowReceiptInspectionStatus AuditStatus(AuditEvidenceStatus status) => status switch {
        AuditEvidenceStatus.Matched => WorkflowReceiptInspectionStatus.MatchingObserved,
        AuditEvidenceStatus.Mismatch => WorkflowReceiptInspectionStatus.Conflict,
        AuditEvidenceStatus.Missing => WorkflowReceiptInspectionStatus.StillUnknown,
        _ => WorkflowReceiptInspectionStatus.Unavailable
    };
    private static WorkflowReceiptInspectionStatus Combine(params WorkflowReceiptInspectionStatus[] statuses)
        => statuses.Contains(WorkflowReceiptInspectionStatus.Conflict) ? WorkflowReceiptInspectionStatus.Conflict
        : statuses.Contains(WorkflowReceiptInspectionStatus.Unavailable) ? WorkflowReceiptInspectionStatus.Unavailable
        : statuses.Contains(WorkflowReceiptInspectionStatus.StillUnknown) ? WorkflowReceiptInspectionStatus.StillUnknown
        : statuses.All(x => x == WorkflowReceiptInspectionStatus.MatchingObserved)
            ? WorkflowReceiptInspectionStatus.MatchingObserved : WorkflowReceiptInspectionStatus.Unavailable;

    public async ValueTask DisposeAsync() { await _scheduler.DisposeAsync(); await _workflow.DisposeAsync(); }
    private sealed class AuditReaderFactory(NpgsqlConnection connection, Guid caseId, DisposableTestBinding? disposableTest = null) : IDbContextFactory<AuditDbContext>
    {
        public AuditDbContext CreateDbContext() => disposableTest is { } profile
            ? AuditDbContextFactory.CreateDisposableIsolatedTestContext(connection, caseId, profile)
            : AuditDbContextFactory.CreateIsolatedTestContext(connection, caseId);
    }
}

/// <summary>Opaque fixture observation. Current custody, owner CAS and root guards still apply.</summary>
public sealed class WorkflowWakeSuccessorEvidence
{
    internal WorkflowWakeRecoveryReader Reader { get; }
    internal WorkflowFixtureCustodyLease Custody { get; }
    internal WorkflowTransactionOrigin Origin { get; }
    internal WorkflowSpecification Specification { get; }
    internal WorkflowWakeOperation Previous { get; }
    internal VerifiedMessageEnvelope Request { get; }
    internal AuditIntent SchedulerAudit { get; }
    internal VerifiedMessageEnvelope Result { get; }
    internal AuditIntent CompletionAudit { get; }
    internal CompiledMessageRegistry Registry { get; }
    internal DateTimeOffset ObservedAtUtc { get; }
    public string EvidenceHash { get; }

    internal WorkflowWakeSuccessorEvidence(WorkflowWakeRecoveryReader reader, WorkflowFixtureCustodyLease custody,
        WorkflowTransactionOrigin origin, WorkflowSpecification specification, WorkflowWakeOperation previous,
        VerifiedMessageEnvelope request, AuditIntent schedulerAudit, VerifiedMessageEnvelope result,
        AuditIntent completionAudit, CompiledMessageRegistry registry, DateTimeOffset observedAtUtc)
    {
        Reader = reader; Custody = custody; Origin = origin; Specification = specification; Previous = previous;
        Request = request; SchedulerAudit = schedulerAudit; Result = result; CompletionAudit = completionAudit;
        Registry = registry; ObservedAtUtc = observedAtUtc;
        EvidenceHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            schemaVersion = 1, origin.CaseId, origin.FullTransactionId, origin.BackendPid,
            origin.BackendStartedUtc, origin.ServerStartedUtc, previous.DescriptorHash,
            auditHash = AuditIntentCanonicalizer.ComputeHash(completionAudit),
            requestHash = request.PayloadHash, requestDelivery = request.Delivery,
            resultHash = result.PayloadHash, resultDelivery = result.Delivery,
            observedAtUtc = observedAtUtc.UtcTicks
        })));
    }
    public override string ToString() => "WorkflowWakeSuccessorEvidence [private observation]";
}
