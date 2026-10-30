using System.Text.Json;
using Kordra.ApprovalGovernance.Domain;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Api;

/// <summary>Fresh display eligibility only; caller still authorizes and audits disclosure under admission.</summary>
public sealed record ApprovalEligibilityResult(ApprovalEffectiveResult Effective, DateTimeOffset ObservedAtUtc, DateTimeOffset? ValidUntilUtc);

public sealed class ApprovalEligibilityReader(ApprovalSubjectReader subjects, StoredApprovalAuthorityReader authority, IAuthorityClock clock)
{
    private static ApprovalEligibilityResult Unavailable(DateTimeOffset now) => new(new(ApprovalEffectiveStatus.Unavailable, null), now, null);
    public async Task<ApprovalEligibilityResult> ReadUnderAdmissionAsync(ApprovalRequestRecord row, Guid installation,
        Guid tenant, Guid project, Guid environment, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var before = clock.GetTrustedUtcNow();
        if (row is null || !ValidTime(before) || installation == Guid.Empty || tenant == Guid.Empty || project == Guid.Empty || environment == Guid.Empty ||
            row.InstallationId != installation || row.TenantId != tenant || row.ProjectId != project || row.EnvironmentId != environment ||
            row.Id == Guid.Empty || row.ReleaseRevisionId == Guid.Empty || row.RequesterPrincipalId == Guid.Empty || row.Version <= 0 ||
            !ValidTime(row.CreatedAtUtc) || !ValidTime(row.PendingExpiresAtUtc) || row.PendingExpiresAtUtc <= row.CreatedAtUtc || before < row.CreatedAtUtc ||
            !Enum.TryParse<ApprovalRecordedState>(row.State, out var state) || !Enum.IsDefined(state) || row.State != state.ToString()) return Unavailable(before);
        if (state is ApprovalRecordedState.Rejected or ApprovalRecordedState.Revoked)
            return new(ApprovalEffectiveState.Evaluate(state, before, row.PendingExpiresAtUtc, row.GrantExpiresAtUtc, ApprovalEvidenceStatus.Unavailable, null, null), before, null);
        if (state == ApprovalRecordedState.Granted && (row.ApproverPrincipalId is null || row.ApproverPrincipalId == Guid.Empty ||
            row.ApproverEvidence is not { Length: > 0 } || row.GrantExpiresAtUtc is null || !ValidTime(row.GrantExpiresAtUtc.Value) ||
            row.GrantExpiresAtUtc > row.PendingExpiresAtUtc || row.GrantedAtUtc is null || !ValidTime(row.GrantedAtUtc.Value) ||
            row.GrantedAtUtc < row.CreatedAtUtc || row.GrantedAtUtc > before || row.GrantExpiresAtUtc <= row.GrantedAtUtc)) return Unavailable(before);
        if (state == ApprovalRecordedState.Pending && (row.GrantExpiresAtUtc is not null || row.GrantedAtUtc is not null || row.ApproverPrincipalId is not null || row.ApproverEvidence is not null)) return Unavailable(before);
        var deadline = state == ApprovalRecordedState.Pending ? row.PendingExpiresAtUtc : row.GrantExpiresAtUtc!.Value;
        if (before >= deadline) return new(new(ApprovalEffectiveStatus.Expired, "expired"), before, null);
        try
        {
            var evidence = ApprovalEvidenceStatus.Current; string? reason = null; bool? blocked = null;
            var subject = await subjects.ReadEligibleUnderAdmissionAsync(installation, tenant, project, environment, row.ReleaseRevisionId, before, ct);
            var until = deadline;
            if (subject.Status != "eligible")
            {
                reason = subject.Status switch
                {
                    "absent" or "not_frozen" or "subject_changed" => "subject_changed",
                    "owner_missing" or "owner_mismatch" or "qualification_required" => "selection_changed",
                    "policy_missing" or "policy_denied" => "policy_changed",
                    _ => null
                };
                evidence = reason is null ? ApprovalEvidenceStatus.Unavailable : ApprovalEvidenceStatus.Invalidated;
            }
            else if (subject.Revision is null || subject.Policy is null || row.SpecHash is not { Length: 32 } ||
                subject.Revision.SpecHash != Convert.ToHexStringLower(row.SpecHash)) evidence = ApprovalEvidenceStatus.Unavailable;
            else
            {
                var requester = await authority.CheckUnderAdmissionAsync(row.RequesterEvidence, "requester", tenant, project, environment, row.RequesterPrincipalId, ct);
                var approver = state == ApprovalRecordedState.Granted
                    ? await authority.CheckUnderAdmissionAsync(row.ApproverEvidence!, "approver", tenant, project, environment, row.ApproverPrincipalId!.Value, ct)
                    : DurableApprovalAuthorityStatus.Current;
                if (requester is DurableApprovalAuthorityStatus.Invalidated or DurableApprovalAuthorityStatus.Expired ||
                    approver is DurableApprovalAuthorityStatus.Invalidated or DurableApprovalAuthorityStatus.Expired)
                { evidence = ApprovalEvidenceStatus.Invalidated; reason = "assignment_changed"; }
                else if (requester != DurableApprovalAuthorityStatus.Current || approver != DurableApprovalAuthorityStatus.Current)
                    evidence = ApprovalEvidenceStatus.Unavailable;
                else
                {
                    using var doc = JsonDocument.Parse(subject.Revision.GetCanonicalSubject()!);
                    var classification = doc.RootElement.GetProperty("environment").GetProperty("classification").GetString()!;
                    var rule = subject.Policy.FindApplicableAllowRule(classification, before);
                    if (rule is null) { evidence = ApprovalEvidenceStatus.Invalidated; reason = "policy_changed"; }
                    else
                    {
                        blocked = rule.BlockedWindowsUtc.Any(x => x.StartsAtUtc <= before && before < x.EndsAtUtc);
                        foreach (var window in rule.BlockedWindowsUtc)
                        {
                            if (window.StartsAtUtc > before && window.StartsAtUtc < until) until = window.StartsAtUtc;
                            if (window.EndsAtUtc > before && window.EndsAtUtc < until) until = window.EndsAtUtc;
                        }
                    }
                }
            }
            var after = clock.GetTrustedUtcNow();
            if (!ValidTime(after) || after < before) return Unavailable(after);
            // A crossed policy-window boundary requires a fresh owner evaluation, never a stale display.
            if (after < deadline && after >= until) return Unavailable(after);
            var result = ApprovalEffectiveState.Evaluate(state, after, row.PendingExpiresAtUtc, row.GrantExpiresAtUtc, evidence, reason, blocked);
            return new(result, after, result.Status is ApprovalEffectiveStatus.Pending or ApprovalEffectiveStatus.Granted ? until : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return Unavailable(clock.GetTrustedUtcNow()); }
    }
    private static bool ValidTime(DateTimeOffset value) => value.Offset == TimeSpan.Zero && value != default && value != DateTimeOffset.MaxValue;
}
