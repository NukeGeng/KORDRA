using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Data.Common;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.ReleaseManagement;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kordra.Api;

/// <summary>Exact facts IAM needs to reissue a workflow delegation (12.3b). Data only; the request confers no authority.</summary>
public sealed record WorkflowDelegationReissueRequest(Guid InstallationId, Guid TenantId, Guid ProjectId, Guid EnvironmentId,
    Guid InstanceId, Guid ActorId, Guid ReleaseId, long ReleaseRevision, string ReleaseHash, string SpecificationHash,
    DateTimeOffset NotAfterUtc, Guid CorrelationId);

/// <summary>
/// IAM-owned port, implemented in 12.3b. It must stage on the same caller root under the authority fence, verify the previous
/// delegation belongs to this instance and actor, create the new delegation, invalidate the old one and advance the epoch
/// with its AUD. 12.2f ships no production implementation; returning null denies without effects.
/// </summary>
public interface IWorkflowDelegationReissuer
{
    Task<WorkflowDelegationReference?> StageReissueAsync(DbConnection connection, DbTransaction root,
        WorkflowDelegationReissueRequest request, CancellationToken ct = default);
}

/// <summary>Resolves the exact frozen REL revision of an immutable workflow specification. Read-only; no authority.</summary>
public interface IWorkflowReleaseRevisionResolver
{
    Task<Guid?> ResolveUnderAdmissionAsync(WorkflowSpecification specification, CancellationToken ct = default);
}

/// <summary>Bounded paging over the existing REL owner projection; fails closed beyond the bound or on any ambiguity.</summary>
public sealed class WorkflowReleaseRevisionResolver(IDbContextFactory<ReleaseDbContext> releases) : IWorkflowReleaseRevisionResolver
{
    private const int PageSize = 50;
    private const int MaximumPages = 20;

    public async Task<Guid?> ResolveUnderAdmissionAsync(WorkflowSpecification specification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        await using var db = await releases.CreateDbContextAsync(ct);
        var store = new ReleaseQueryStore(db);
        Guid? after = null; Guid? found = null;
        for (var page = 0; page < MaximumPages; page++)
        {
            var rows = await store.ReadRevisionsUnderAdmissionAsync(specification.InstallationId, specification.TenantId,
                specification.ProjectId, specification.ReleaseId, PageSize, after, ct);
            if (rows is null) return null;
            foreach (var row in rows.Items.Where(x => x.RevisionNumber == specification.ReleaseRevision))
            {
                if (found is not null || row.SpecHash != specification.ReleaseHash || row.State == "Draft") return null;
                found = row.RevisionId;
            }
            if (rows.NextAfterId is not { } next) return found;
            after = next;
        }
        return null;
    }
}

/// <summary>Pure binding of current start admission to an existing immutable workflow. Not authority by itself.</summary>
public static class WorkflowReauthorizationAdmission
{
    public static WorkflowStartSelection SelectionFor(WorkflowSpecification specification, Guid releaseRevisionId)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return new(specification.TenantId, specification.ProjectId, specification.EnvironmentId, specification.ReleaseId,
            releaseRevisionId, specification.ReleaseRevision, specification.ReleaseHash, WorkflowSpecification.TemplateId,
            WorkflowSpecification.TemplateVersion);
    }

    public static bool Binds(WorkflowStartAdmission? admission, WorkflowSpecification specification,
        WorkflowReauthorizationCommand command, SessionSelection session) =>
        admission is not null && specification is not null && command is not null && session is not null &&
        admission.InstallationId == specification.InstallationId && command.InstallationId == specification.InstallationId &&
        command.TenantId == specification.TenantId && command.InstanceId == specification.InstanceId &&
        command.SpecificationHash == specification.SpecificationHash && admission.RequestId == command.RequestId &&
        admission.Selection.TenantId == specification.TenantId && admission.Selection.ProjectId == specification.ProjectId &&
        admission.Selection.EnvironmentId == specification.EnvironmentId && admission.Selection.ReleaseId == specification.ReleaseId &&
        admission.Selection.ReleaseRevision == specification.ReleaseRevision && admission.Selection.ReleaseHash == specification.ReleaseHash &&
        admission.Eligibility.ApplicationId == specification.ApplicationId && admission.Eligibility.TargetId == specification.TargetId &&
        admission.Eligibility.Classification == specification.Environment &&
        admission.Eligibility.WorkflowAuthority.Subject.PrincipalId == command.ActorId && session.PrincipalId == command.ActorId;

    public static AuditIntent CreateMutationAudit(WorkflowStartAdmission admission, WorkflowSpecification specification,
        WorkflowReauthorizationCommand command, SessionSelection session, Guid intentId)
    {
        if (intentId == Guid.Empty || !Binds(admission, specification, command, session))
            throw new ArgumentException("Reauthorization audit requires the exact admitted workflow and command.");
        return new(1, intentId, admission.InvocationId, AuditIntentStage.Mutation, command.ActorId, specification.TenantId,
            specification.InstanceId, "workflow.start", "environment", specification.EnvironmentId,
            admission.Policy.Policy.PolicyRevision, admission.Eligibility.WorkflowAuthority.Grant.AssignmentRevision,
            "workflow-reauthorize-" + command.CommandHash, admission.ObservedAtUtc, command.RequestId);
    }
}

public sealed record WorkflowReauthorizationTransactionResult(WorkflowCommitDisposition Disposition, WorkflowAuthorityBindingResult? Receipt);

/// <summary>
/// Inactive 12.2f composition: WFL binding + IAM delegation port + AUD + floor on one caller root. Isolated/disposable
/// test targets only; no route, consumer or DI registration. Never moves a workflow out of BlockedAuthority.
/// </summary>
public static class WorkflowReauthorizationTransaction
{
    public static Task<WorkflowReauthorizationTransactionResult> ExecuteAdmittedIsolatedAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowReauthorizationCommand command, Guid projectId, Guid environmentId,
        SessionSelection session, WorkflowStartAdmissionReader authority, IWorkflowReleaseRevisionResolver revisions,
        IWorkflowDelegationReissuer reissuer, FairAsyncReaderWriterAdmissionGate admission, CancellationToken ct = default)
        => ExecuteAdmittedCoreAsync(connection, root, caseId, command, projectId, environmentId, session, authority, revisions,
            reissuer, admission, null, ct);

    /// <summary>Explicit ADR-0025 test selection; authority is still resolved from current owner stores.</summary>
    public static Task<WorkflowReauthorizationTransactionResult> ExecuteAdmittedDisposableTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowReauthorizationCommand command, Guid projectId, Guid environmentId,
        SessionSelection session, WorkflowStartAdmissionReader authority, IWorkflowReleaseRevisionResolver revisions,
        IWorkflowDelegationReissuer reissuer, FairAsyncReaderWriterAdmissionGate admission, DisposableTestBinding profile,
        CancellationToken ct = default)
        => ExecuteAdmittedCoreAsync(connection, root, caseId, command, projectId, environmentId, session, authority, revisions,
            reissuer, admission, profile, ct);

    private static async Task<WorkflowReauthorizationTransactionResult> ExecuteAdmittedCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowReauthorizationCommand command, Guid projectId, Guid environmentId,
        SessionSelection session, WorkflowStartAdmissionReader authority, IWorkflowReleaseRevisionResolver revisions,
        IWorkflowDelegationReissuer reissuer, FairAsyncReaderWriterAdmissionGate admission, DisposableTestBinding? profile,
        CancellationToken ct)
    {
        if (profile is { } selected) _ = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, selected);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(revisions);
        if (command.ActorId != session.PrincipalId)
            throw new InvalidOperationException("Reauthorization actor differs from the current session.");
        await using var held = await admission.AcquireReadAsync(ct);
        var specification = await ReadScopedSpecificationAsync(connection, root, caseId, command, projectId, environmentId, profile, ct);
        if (specification is null) return new(WorkflowCommitDisposition.NoCommitAttempt, null);
        if (await revisions.ResolveUnderAdmissionAsync(specification, ct) is not { } revisionId)
            return new(WorkflowCommitDisposition.NoCommitAttempt, null);
        var selection = WorkflowReauthorizationAdmission.SelectionFor(specification, revisionId);
        var invocation = Guid.NewGuid();
        var original = await authority.ReadUnderAdmissionAsync(session, selection, command.RequestId, invocation, ct);
        if (!WorkflowReauthorizationAdmission.Binds(original, specification, command, session))
            return new(WorkflowCommitDisposition.NoCommitAttempt, null);
        var mutation = WorkflowReauthorizationAdmission.CreateMutationAudit(original!, specification, command, session, Guid.NewGuid());
        return await ExecuteCoreAsync(connection, root, caseId, command, projectId, environmentId, mutation, admission,
            async token => original!.IsCurrentSuccessor(await authority.ReadUnderAdmissionAsync(
                session, selection, command.RequestId, invocation, token)), reissuer, profile, ct);
    }

    public static Task<WorkflowReauthorizationTransactionResult> ExecuteIsolatedAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowReauthorizationCommand command, Guid projectId, Guid environmentId,
        AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission, Func<CancellationToken, Task<bool>> stillCurrent,
        IWorkflowDelegationReissuer reissuer, CancellationToken ct = default)
        => ExecuteCoreAsync(connection, root, caseId, command, projectId, environmentId, mutation, admission, stillCurrent,
            reissuer, null, ct);

    /// <summary>Explicit disposable root; never grants authority or replaces current admission.</summary>
    public static Task<WorkflowReauthorizationTransactionResult> ExecuteDisposableTestAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowReauthorizationCommand command, Guid projectId, Guid environmentId,
        AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission, Func<CancellationToken, Task<bool>> stillCurrent,
        IWorkflowDelegationReissuer reissuer, DisposableTestBinding profile, CancellationToken ct = default)
        => ExecuteCoreAsync(connection, root, caseId, command, projectId, environmentId, mutation, admission, stillCurrent,
            reissuer, profile, ct);

    private static async Task<WorkflowReauthorizationTransactionResult> ExecuteCoreAsync(NpgsqlConnection connection,
        NpgsqlTransaction root, Guid caseId, WorkflowReauthorizationCommand command, Guid projectId, Guid environmentId,
        AuditIntent mutation, FairAsyncReaderWriterAdmissionGate admission, Func<CancellationToken, Task<bool>> stillCurrent,
        IWorkflowDelegationReissuer reissuer, DisposableTestBinding? profile, CancellationToken ct)
    {
        if (profile is { } selected) _ = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, selected);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(stillCurrent);
        ArgumentNullException.ThrowIfNull(reissuer);
        if (command.InstallationId != caseId || projectId == Guid.Empty || environmentId == Guid.Empty ||
            mutation.Stage != AuditIntentStage.Mutation || mutation.SubjectRef != command.ActorId ||
            mutation.TenantRef != command.TenantId || mutation.ResourceRef != command.InstanceId ||
            mutation.Permission != "workflow.start" || mutation.ScopeKind != "environment" ||
            mutation.ScopeId != environmentId || mutation.CorrelationId != command.RequestId ||
            mutation.PolicyRevision is null or <= 0 || mutation.AuthorityRevision is null or <= 0 ||
            mutation.SafeReason != "workflow-reauthorize-" + command.CommandHash ||
            mutation.OccurredAtUtc == default || mutation.OccurredAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("Reauthorization requires exact trusted scope and mutation audit.");

        await using var workflow = Workflow(connection, caseId, profile);
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
            var store = new WorkflowAuthorityBindingStore(workflow);
            // Serialize on the owner instance before the receipt check and any delegation reissue (PR32 review):
            // a concurrent execution of the same request waits here and then observes the recorded receipt.
            if (!await store.LockOwnerAsync(command, ct))
            {
                await audit.RollbackAsync();
                return new(WorkflowCommitDisposition.NoCommitAttempt, null);
            }
            // A retry returns the original receipt and must never reissue a delegation.
            if (await store.ReadRecordedAsync(command, ct) is { } recorded)
                return await RetainOriginalAsync(recorded);
            var specification = await ScopedAsync(workflow, command, projectId, environmentId, ct)
                ?? throw new InvalidOperationException("Workflow unavailable in trusted reauthorization scope.");
            var delegation = await reissuer.StageReissueAsync(connection, root, new(command.InstallationId, command.TenantId,
                projectId, environmentId, command.InstanceId, command.ActorId, specification.ReleaseId, specification.ReleaseRevision,
                specification.ReleaseHash, command.SpecificationHash, specification.DeadlineUtc, command.RequestId), ct);
            if (delegation is null || delegation.DelegationId == Guid.Empty ||
                delegation.ValidUntilUtc <= mutation.OccurredAtUtc || delegation.ValidUntilUtc > specification.DeadlineUtc)
            {
                await audit.RollbackAsync();
                return new(WorkflowCommitDisposition.NoCommitAttempt, null);
            }
            var receipt = await store.StageReauthorizationAsync(command, delegation, mutation.OccurredAtUtc, ct);
            // Defense in depth: a duplicate here means delegation effects were staged for an already recorded
            // request. Never commit them; roll the whole root back and return only the original receipt.
            if (receipt.Duplicate) return await RetainOriginalAsync(receipt);

            if ((await audit.StageAsync(mutation, ct)).Status != AuditTransactionStageStatus.Staged)
                throw new InvalidOperationException("Reauthorization requires a fresh mutation audit.");
            var floor = await workflow.Set<WorkflowClockFloorRecord>().SingleOrDefaultAsync(x =>
                x.InstallationId == command.InstallationId && x.TenantId == command.TenantId, ct);
            if (floor is null)
                workflow.Add(new WorkflowClockFloorRecord
                    { InstallationId = command.InstallationId, TenantId = command.TenantId, FloorUtc = mutation.OccurredAtUtc });
            else if (floor.FloorUtc > mutation.OccurredAtUtc)
                throw new InvalidOperationException("Reauthorization clock cannot move backwards.");
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

        async Task<WorkflowReauthorizationTransactionResult> RetainOriginalAsync(WorkflowAuthorityBindingResult original)
        {
            var allowed = await stillCurrent(ct) && !admission.IsClosed;
            await audit.RollbackAsync();
            return new(WorkflowCommitDisposition.NoCommitAttempt, allowed ? original : null);
        }
    }

    private static WorkflowDbContext Workflow(NpgsqlConnection connection, Guid caseId, DisposableTestBinding? profile) =>
        profile is { } selected
            ? WorkflowDbContext.CreateDisposableTestFixture(connection, caseId, WorkflowDbContext.AuthorizationSchemaRevision, selected)
            : WorkflowDbContext.CreateAuthorizationFixture(connection, caseId);

    private static async Task<WorkflowSpecification?> ReadScopedSpecificationAsync(NpgsqlConnection connection, NpgsqlTransaction root,
        Guid caseId, WorkflowReauthorizationCommand command, Guid projectId, Guid environmentId, DisposableTestBinding? profile,
        CancellationToken ct)
    {
        if (command.InstallationId != caseId || projectId == Guid.Empty || environmentId == Guid.Empty) return null;
        await using var workflow = Workflow(connection, caseId, profile);
        await workflow.Database.UseTransactionAsync(root, ct);
        return await ScopedAsync(workflow, command, projectId, environmentId, ct);
    }

    private static async Task<WorkflowSpecification?> ScopedAsync(WorkflowDbContext workflow, WorkflowReauthorizationCommand command,
        Guid projectId, Guid environmentId, CancellationToken ct)
    {
        var bytes = await workflow.Set<WorkflowInstanceRecord>().AsNoTracking().Where(x => x.InstallationId == command.InstallationId &&
            x.TenantId == command.TenantId && x.InstanceId == command.InstanceId).Select(x => x.Specification).SingleOrDefaultAsync(ct);
        if (bytes is null) return null;
        var specification = WorkflowSpecification.ReadCanonical(bytes);
        return specification.ProjectId == projectId && specification.EnvironmentId == environmentId &&
            specification.SpecificationHash == command.SpecificationHash ? specification : null;
    }
}
