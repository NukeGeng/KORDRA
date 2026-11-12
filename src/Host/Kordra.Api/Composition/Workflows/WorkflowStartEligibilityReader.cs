using System.Text.Json;
using Kordra.ApprovalGovernance.Domain;
using Kordra.Modules.ApprovalGovernance;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

/// <summary>Owner-resolved scope and exact release revision selection. This is not an HTTP request.</summary>
public sealed record WorkflowStartSelection(Guid TenantId, Guid ProjectId, Guid EnvironmentId, Guid ReleaseId,
    Guid ReleaseRevisionId, long ReleaseRevision, string ReleaseHash,
    string TemplateId, int TemplateVersion)
{
    internal bool IsValid => TenantId != Guid.Empty && ProjectId != Guid.Empty && EnvironmentId != Guid.Empty &&
        ReleaseId != Guid.Empty && ReleaseRevisionId != Guid.Empty && ReleaseRevision > 0 &&
        ReleaseHash is { Length: 64 } && ReleaseHash.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9') &&
        TemplateId == WorkflowSpecification.TemplateId && TemplateVersion == WorkflowSpecification.TemplateVersion;
}

/// <summary>Current start facts only. Policy, delegation, mutation and audited handoff are separate.</summary>
public sealed record WorkflowStartEligibility(WorkflowEnvironmentAuthoritySnapshot WorkflowAuthority,
    ReleaseProjectAuthoritySnapshot ReleaseAuthority, Guid ApprovalId, long ApprovalVersion,
    Guid ApplicationId, Guid TargetId, long ReleaseOwnerVersion, WorkflowEnvironment Classification,
    DateTimeOffset ValidUntilUtc, Guid InstallationId = default, WorkflowStartSelection? Selection = null);

/// <summary>Trusted Host owner adapter; never resolve an implementation or facts from request data.</summary>
public interface IWorkflowStartEligibilityReader
{
    Task<WorkflowStartEligibility?> ReadUnderAdmissionAsync(SessionSelection session, WorkflowStartSelection selection,
        Guid invocationId, CancellationToken ct = default);
}

public sealed class WorkflowStartEligibilityReader(Guid installationId, WorkflowEnvironmentAuthorityReader workflows,
    ReleaseProjectAuthorityReader releases, IDbContextFactory<ApprovalDbContext> approvalContexts,
    ApprovalSubjectReader subjects, ApprovalEligibilityReader approvalEligibility, IAuthorityClock clock) : IWorkflowStartEligibilityReader
{
    public async Task<WorkflowStartEligibility?> ReadUnderAdmissionAsync(SessionSelection session,
        WorkflowStartSelection selection, Guid invocationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (installationId == Guid.Empty || invocationId == Guid.Empty || session is null || selection is not { IsValid: true })
            return null;

        try
        {
            var before = clock.GetTrustedUtcNow();
            if (!FiniteUtc(before)) return null;

            var workflow = await workflows.ReadCurrentUnderAdmissionAsync(session, selection.TenantId, selection.ProjectId,
                selection.EnvironmentId, "workflow.start", ct);
            if (workflow is null) return null;
            var release = await releases.ReadCurrentUnderAdmissionAsync(session, selection.ProjectId, "release.read", invocationId, ct);
            if (release is null || release.Project != workflow.Project ||
                release.Subject.SessionId != workflow.Subject.SessionId ||
                release.Subject.PrincipalId != workflow.Subject.PrincipalId ||
                release.Subject.PrincipalRevision != workflow.Subject.PrincipalRevision ||
                release.Subject.BootGeneration != workflow.Subject.BootGeneration)
                return null;

            await using var db = await approvalContexts.CreateDbContextAsync(ct);
            var row = await new ApprovalRequestQueryStore(db).ReadForRevisionUnderAdmissionAsync(installationId,
                selection.TenantId, selection.ProjectId, selection.EnvironmentId, selection.ReleaseRevisionId, ct);
            if (row is null || row.ReleaseRevisionId != selection.ReleaseRevisionId || row.SpecHash is not { Length: 32 } ||
                Convert.ToHexStringLower(row.SpecHash) != selection.ReleaseHash)
                return null;

            var subject = await subjects.ReadEligibleUnderAdmissionAsync(installationId, selection.TenantId,
                selection.ProjectId, selection.EnvironmentId, selection.ReleaseRevisionId, before, ct);
            var revision = subject.Revision;
            if (subject.Status != "eligible" || revision is null || revision.ReleaseId != selection.ReleaseId ||
                revision.RevisionId != selection.ReleaseRevisionId || revision.RevisionNumber != selection.ReleaseRevision ||
                revision.SpecHash != selection.ReleaseHash || revision.GetCanonicalSubject() is not { } canonical)
                return null;

            using var document = JsonDocument.Parse(canonical);
            var classification = document.RootElement.GetProperty("environment").GetProperty("classification").GetString() switch
            {
                "Development" => WorkflowEnvironment.Development,
                "Test" => WorkflowEnvironment.Test,
                _ => (WorkflowEnvironment?)null
            };
            if (classification is null) return null;

            var effective = await approvalEligibility.ReadUnderAdmissionAsync(row, installationId, selection.TenantId,
                selection.ProjectId, selection.EnvironmentId, ct);
            if (effective.Effective.Status != ApprovalEffectiveStatus.Granted ||
                effective.ValidUntilUtc is not { } approvalUntil || !FiniteUtc(approvalUntil))
                return null;

            var until = new[] { workflow.ValidUntilUtc, release.ValidUntilUtc, approvalUntil }.Min();
            var after = clock.GetTrustedUtcNow();
            ct.ThrowIfCancellationRequested();
            if (!FiniteUtc(after) || effective.ObservedAtUtc < before || after < effective.ObservedAtUtc || after >= until)
                return null;

            return new(workflow, release, row.Id, row.Version, revision.ApplicationId, revision.TargetId,
                revision.Version, classification.Value, until, installationId, selection);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static bool FiniteUtc(DateTimeOffset time) => time.Offset == TimeSpan.Zero &&
        time != default && time != DateTimeOffset.MaxValue;
}
