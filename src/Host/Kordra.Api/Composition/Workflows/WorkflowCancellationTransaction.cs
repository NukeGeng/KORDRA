using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kordra.Api;

public sealed record WorkflowCancellationTransactionResult(WorkflowCommitDisposition Disposition, WorkflowStoredResult? Receipt);

/// <summary>Isolated WFL/AUD root for the cancellation intent. The caller holds admission and rechecks current policy.</summary>
public static class WorkflowCancellationTransaction
{
    public static Task<WorkflowCancellationTransactionResult> ExecuteAdmittedIsolatedAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowCancellationIntent intent, Guid projectId, Guid environmentId,
        SessionSelection session, WorkflowCommandAdmissionReader authority,
        FairAsyncReaderWriterAdmissionGate admission, CancellationToken ct = default)
        => ExecuteAdmittedCoreAsync(connection, root, caseId, intent, projectId, environmentId, session, authority, admission, null, ct);

    /// <summary>Explicit ADR-0025 test selection; authority is still resolved from current owner stores.</summary>
    public static Task<WorkflowCancellationTransactionResult> ExecuteAdmittedDisposableTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowCancellationIntent intent, Guid projectId, Guid environmentId,
        SessionSelection session, WorkflowCommandAdmissionReader authority,
        FairAsyncReaderWriterAdmissionGate admission,
        DisposableTestBinding profile, CancellationToken ct = default)
        => ExecuteAdmittedCoreAsync(connection, root, caseId, intent, projectId, environmentId, session, authority, admission, profile, ct);

    private static async Task<WorkflowCancellationTransactionResult> ExecuteAdmittedCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowCancellationIntent intent, Guid projectId, Guid environmentId,
        SessionSelection session, WorkflowCommandAdmissionReader authority,
        FairAsyncReaderWriterAdmissionGate admission,
        DisposableTestBinding? profile, CancellationToken ct)
    {
        if (profile is { } selected) _ = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, selected);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(authority);
        if (intent.ActorId != session.PrincipalId)
            throw new InvalidOperationException("Cancellation actor differs from the current session.");
        await using var held = await admission.AcquireReadAsync(ct);
        var scope = new WorkflowReadScope(intent.TenantId, projectId, environmentId, intent.InstanceId);
        var original = await authority.ReadUnderAdmissionAsync(session, scope, "workflow.cancel", ct);
        if (original is null) return new(WorkflowCommitDisposition.NoCommitAttempt, null);
        var mutation = new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation,
            original.Authority.Subject.PrincipalId, scope.TenantId, scope.InstanceId, "workflow.cancel", "environment",
            scope.EnvironmentId, original.Policy.PolicyRevision, original.Authority.Grant.AssignmentRevision,
            "workflow-cancel-" + intent.CommandHash, original.ObservedAtUtc, intent.RequestId);
        return await ExecuteCoreAsync(connection, root, caseId, intent, projectId, environmentId, mutation, admission,
            async token => original.IsCurrentSuccessor(await authority.ReadUnderAdmissionAsync(
                session, scope, "workflow.cancel", token)), profile, ct);
    }

    public static Task<WorkflowCancellationTransactionResult> ExecuteIsolatedAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowCancellationIntent intent, Guid projectId, Guid environmentId,
        AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission,
        Func<CancellationToken, Task<bool>> stillCurrent, CancellationToken ct = default)
        => ExecuteCoreAsync(connection, root, caseId, intent, projectId, environmentId, mutation, admission, stillCurrent, null, ct);

    /// <summary>Explicit disposable metadata root; never grants authority or replaces evidence/custody checks.</summary>
    public static Task<WorkflowCancellationTransactionResult> ExecuteDisposableTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowCancellationIntent intent, Guid projectId, Guid environmentId,
        AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission,
        Func<CancellationToken, Task<bool>> stillCurrent,
        DisposableTestBinding profile, CancellationToken ct = default)
        => ExecuteCoreAsync(connection, root, caseId, intent, projectId, environmentId, mutation, admission, stillCurrent, profile, ct);

    private static async Task<WorkflowCancellationTransactionResult> ExecuteCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowCancellationIntent intent, Guid projectId, Guid environmentId,
        AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission,
        Func<CancellationToken, Task<bool>> stillCurrent,
        DisposableTestBinding? profile, CancellationToken ct)
    {
        if (profile is { } selected) _ = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, selected);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(stillCurrent);
        if (intent.InstallationId != caseId || projectId == Guid.Empty || environmentId == Guid.Empty ||
            mutation.Stage != AuditIntentStage.Mutation || mutation.SubjectRef != intent.ActorId ||
            mutation.TenantRef != intent.TenantId || mutation.ResourceRef != intent.InstanceId ||
            mutation.Permission != "workflow.cancel" || mutation.ScopeKind != "environment" ||
            mutation.ScopeId != environmentId || mutation.CorrelationId != intent.RequestId ||
            mutation.PolicyRevision is null or <= 0 || mutation.AuthorityRevision is null or <= 0 ||
            mutation.SafeReason != "workflow-cancel-" + intent.CommandHash ||
            mutation.OccurredAtUtc == default || mutation.OccurredAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("Cancellation requires exact trusted scope and mutation audit.");

        await using var workflow = profile is { } workflowProfile
            ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, WorkflowDbContext.CancellationSchemaRevision, workflowProfile)
            : WorkflowDbContext.CreateCancellationFixture(connection, caseId);
        await WorkflowRootTransaction.ClaimCoordinatorAsync(connection, root, ct);
        await workflow.Database.UseTransactionAsync(root, ct);
        await using var audit = profile is { } auditProfile
            ? await AuditTransactionContext.CreateDisposableIsolatedTestAsync(connection, root, caseId, auditProfile, ct)
            : await AuditTransactionContext.CreateIsolatedTestAsync(connection, root, caseId, ct);
        if (admission.IsClosed || !await stillCurrent(ct))
        {
            await audit.RollbackAsync();
            return new(WorkflowCommitDisposition.NoCommitAttempt, null);
        }

        try
        {
            var receipt = await new WorkflowCancellationStore(workflow).StageScopedIntentAsync(intent, projectId,
                environmentId, mutation.OccurredAtUtc, ct);
            if (receipt.Duplicate)
            {
                var allowed = await stillCurrent(ct) && !admission.IsClosed;
                await audit.RollbackAsync();
                return new(WorkflowCommitDisposition.NoCommitAttempt, allowed ? receipt : null);
            }

            if ((await audit.StageAsync(mutation, ct)).Status != AuditTransactionStageStatus.Staged)
                throw new InvalidOperationException("Cancellation requires a fresh mutation audit.");

            var floor = await workflow.Set<WorkflowClockFloorRecord>().SingleOrDefaultAsync(x =>
                x.InstallationId == intent.InstallationId && x.TenantId == intent.TenantId, ct);
            if (floor is null)
                workflow.Add(new WorkflowClockFloorRecord
                    { InstallationId = intent.InstallationId, TenantId = intent.TenantId, FloorUtc = mutation.OccurredAtUtc });
            else if (floor.FloorUtc > mutation.OccurredAtUtc)
                throw new InvalidOperationException("Cancellation clock cannot move backwards.");
            else
                floor.FloorUtc = mutation.OccurredAtUtc;
            await workflow.SaveChangesAsync(ct);

            if (!await stillCurrent(ct) || admission.IsClosed)
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
