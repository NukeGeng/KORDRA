using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kordra.Api;

/// <summary>Server-resolved timer evidence. Never deserialize this from an operator request.</summary>
public sealed record WorkflowReconciliationSource(WorkflowSpecification Specification, WorkflowWakeOperation Wake,
    WorkflowTransactionOrigin Origin, VerifiedMessageEnvelope Request, AuditIntent SchedulerAudit,
    VerifiedMessageEnvelope Result, AuditIntent CompletionAudit);

public sealed record WorkflowReconciliationTransactionResult(WorkflowCommitDisposition Disposition, WorkflowStoredResult? Receipt);

/// <summary>Inactive isolated WFL/AUD composition; records a decision, never replays a workflow effect.</summary>
public static class WorkflowReconciliationTransaction
{
    public static Task<WorkflowReconciliationTransactionResult> ExecuteAdmittedIsolatedAsync(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId, WorkflowReconciliationSelection selection,
        WorkflowReadScope scope, SessionSelection session, WorkflowCommandAdmissionReader authority,
        WorkflowFixtureCustodyLease custody, WorkflowWakeRecoveryReader evidenceReader,
        WorkflowReconciliationSource source, FairAsyncReaderWriterAdmissionGate admission, CancellationToken ct = default)
        => ExecuteAdmittedCoreAsync(connection, root, caseId, selection, scope, session, authority, custody, evidenceReader, source, admission, null, ct);

    /// <summary>Explicit ADR-0025 test selection; authority is still resolved from current owner stores.</summary>
    public static Task<WorkflowReconciliationTransactionResult> ExecuteAdmittedDisposableTestAsync(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId, WorkflowReconciliationSelection selection,
        WorkflowReadScope scope, SessionSelection session, WorkflowCommandAdmissionReader authority,
        WorkflowFixtureCustodyLease custody, WorkflowWakeRecoveryReader evidenceReader,
        WorkflowReconciliationSource source, FairAsyncReaderWriterAdmissionGate admission,
        DisposableTestBinding profile, CancellationToken ct = default)
        => ExecuteAdmittedCoreAsync(connection, root, caseId, selection, scope, session, authority, custody, evidenceReader, source, admission, profile, ct);

    private static async Task<WorkflowReconciliationTransactionResult> ExecuteAdmittedCoreAsync(NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId, WorkflowReconciliationSelection selection,
        WorkflowReadScope scope, SessionSelection session, WorkflowCommandAdmissionReader authority,
        WorkflowFixtureCustodyLease custody, WorkflowWakeRecoveryReader evidenceReader,
        WorkflowReconciliationSource source, FairAsyncReaderWriterAdmissionGate admission,
        DisposableTestBinding? profile, CancellationToken ct)
    {
        if (profile is { } selected) _ = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, selected);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(evidenceReader);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(admission);
        if (!selection.IsValid || source.Specification.InstallationId != caseId ||
            source.Specification.TenantId != scope.TenantId || source.Specification.ProjectId != scope.ProjectId ||
            source.Specification.EnvironmentId != scope.EnvironmentId || source.Specification.InstanceId != scope.InstanceId)
            throw new InvalidOperationException("Reconciliation requires exact server-resolved owner scope.");
        var command = selection.BindToOwner(source.Specification, session.PrincipalId);
        await using var held = await admission.AcquireReadAsync(ct);
        var original = await authority.ReadUnderAdmissionAsync(session, scope, "workflow.reconcile", ct);
        if (original is null) return new(WorkflowCommitDisposition.NoCommitAttempt, null);
        var mutation = new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation,
            original.Authority.Subject.PrincipalId, scope.TenantId, scope.InstanceId, "workflow.reconcile", "environment",
            scope.EnvironmentId, original.Policy.PolicyRevision, original.Authority.Grant.AssignmentRevision,
            "workflow-reconcile-" + command.CommandHash, original.ObservedAtUtc, command.RequestId);
        return await ExecuteCoreAsync(connection, root, caseId, command, scope.ProjectId, scope.EnvironmentId,
            mutation, admission, custody,
            token => evidenceReader.ObserveReconciliationSelectionAsync(selection, scope, custody, source.Origin,
                source.Specification, source.Wake, source.Request, source.SchedulerAudit, source.Result, source.CompletionAudit, token),
            async token => original.IsCurrentSuccessor(await authority.ReadUnderAdmissionAsync(
                session, scope, "workflow.reconcile", token)), profile, ct);
    }

    /// <summary>Isolated test seam. Caller retains admission; typed evidence and current authority remain separate checks.</summary>
    public static Task<WorkflowReconciliationTransactionResult> ExecuteIsolatedAsync(
        NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId, WorkflowReconciliationCommand command,
        Guid projectId, Guid environmentId, AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission,
        WorkflowFixtureCustodyLease custody, Func<CancellationToken, Task<bool>> verifyEvidence,
        Func<CancellationToken, Task<bool>> stillCurrent, CancellationToken ct = default)
        => ExecuteCoreAsync(connection, root, caseId, command, projectId, environmentId, mutation, admission, custody, verifyEvidence, stillCurrent, null, ct);

    /// <summary>Explicit disposable metadata root; never grants authority or replaces evidence/custody checks.</summary>
    public static Task<WorkflowReconciliationTransactionResult> ExecuteDisposableTestAsync(
        NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId, WorkflowReconciliationCommand command,
        Guid projectId, Guid environmentId, AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission,
        WorkflowFixtureCustodyLease custody, Func<CancellationToken, Task<bool>> verifyEvidence,
        Func<CancellationToken, Task<bool>> stillCurrent,
        DisposableTestBinding profile, CancellationToken ct = default)
        => ExecuteCoreAsync(connection, root, caseId, command, projectId, environmentId, mutation, admission, custody, verifyEvidence, stillCurrent, profile, ct);

    private static async Task<WorkflowReconciliationTransactionResult> ExecuteCoreAsync(
        NpgsqlConnection connection, NpgsqlTransaction root, Guid caseId, WorkflowReconciliationCommand command,
        Guid projectId, Guid environmentId, AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission,
        WorkflowFixtureCustodyLease custody, Func<CancellationToken, Task<bool>> verifyEvidence,
        Func<CancellationToken, Task<bool>> stillCurrent,
        DisposableTestBinding? profile, CancellationToken ct)
    {
        if (profile is { } selected) _ = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, selected);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(verifyEvidence);
        ArgumentNullException.ThrowIfNull(stillCurrent);
        ct.ThrowIfCancellationRequested();
        if (command.InstallationId != caseId || projectId == Guid.Empty || environmentId == Guid.Empty ||
            mutation.Stage != AuditIntentStage.Mutation || mutation.SubjectRef != command.ActorId ||
            mutation.TenantRef != command.TenantId || mutation.ResourceRef != command.InstanceId ||
            mutation.Permission != "workflow.reconcile" || mutation.ScopeKind != "environment" ||
            mutation.ScopeId != environmentId || mutation.CorrelationId != command.RequestId ||
            mutation.PolicyRevision is null or <= 0 || mutation.AuthorityRevision is null or <= 0 ||
            mutation.SafeReason != "workflow-reconcile-" + command.CommandHash || mutation.OccurredAtUtc == default ||
            mutation.OccurredAtUtc == DateTimeOffset.MaxValue || mutation.OccurredAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("Reconciliation requires exact trusted scope and mutation audit.");
        if (custody is null || custody.InstallationId != caseId || !custody.MatchesTestTarget(profile))
            throw new InvalidOperationException("Reconciliation requires exact installation custody.");

        // Retain custody across owner locking, all independent evidence reads, AUD/floor and COMMIT.
        await using var custodyHeld = await custody.EnterAsync(ct);
        await using var workflow = profile is { } workflowProfile
            ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, WorkflowDbContext.ReconciliationSchemaRevision, workflowProfile)
            : WorkflowDbContext.CreateReconciliationFixture(connection, caseId);
        await WorkflowRootTransaction.ClaimCoordinatorAsync(connection, root, ct);
        await workflow.Database.UseTransactionAsync(root, ct);
        await using var audit = profile is { } auditProfile
            ? await AuditTransactionContext.CreateDisposableIsolatedTestAsync(connection, root, caseId, auditProfile, ct)
            : await AuditTransactionContext.CreateIsolatedTestAsync(connection, root, caseId, ct);
        if (admission.IsClosed || custody.IsClosed || !await stillCurrent(ct))
        {
            await audit.RollbackAsync();
            return new(WorkflowCommitDisposition.NoCommitAttempt, null);
        }
        try
        {
            var receipt = await new WorkflowOperatorDecisionStore(workflow).StageAsync(command, projectId,
                environmentId, verifyEvidence, mutation.OccurredAtUtc, ct);
            if (receipt.Duplicate)
            {
                var allowed = await stillCurrent(ct) && !admission.IsClosed && !custody.IsClosed;
                await audit.RollbackAsync();
                return new(WorkflowCommitDisposition.NoCommitAttempt, allowed ? receipt : null);
            }
            if ((await audit.StageAsync(mutation, ct)).Status != AuditTransactionStageStatus.Staged)
                throw new InvalidOperationException("Reconciliation requires a fresh mutation audit.");
            var floor = await workflow.Set<WorkflowClockFloorRecord>().SingleOrDefaultAsync(x =>
                x.InstallationId == caseId && x.TenantId == command.TenantId, ct);
            if (floor is null)
                workflow.Add(new WorkflowClockFloorRecord
                    { InstallationId = caseId, TenantId = command.TenantId, FloorUtc = mutation.OccurredAtUtc });
            else if (floor.FloorUtc > mutation.OccurredAtUtc)
                throw new InvalidOperationException("Reconciliation clock cannot move backwards.");
            else floor.FloorUtc = mutation.OccurredAtUtc;
            await workflow.SaveChangesAsync(ct);
            if (!await stillCurrent(ct) || !await verifyEvidence(ct) || admission.IsClosed || custody.IsClosed)
            {
                await audit.RollbackAsync();
                return new(WorkflowCommitDisposition.NoCommitAttempt, null);
            }
            var outcome = await audit.CommitAsync(ct);
            var disposition = WorkflowCommitClassification.Classify(outcome.Status, audit.CommitAttempted);
            if (disposition == WorkflowCommitDisposition.Unknown) admission.Close();
            return new(disposition, disposition == WorkflowCommitDisposition.Committed ? receipt : null);
        }
        catch
        {
            if (audit.CommitAttempted)
            {
                admission.Close();
                return new(WorkflowCommitDisposition.Unknown, null);
            }
            await audit.RollbackAsync();
            throw;
        }
    }
}
