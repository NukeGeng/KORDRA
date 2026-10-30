using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ReleaseManagement;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.ReleaseManagement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Kordra.Api;

/// <summary>Internal transaction outcome, not an HTTP response or disclosure authorization.</summary>
public sealed class ReleaseMutationResult
{
    internal ReleaseMutationResult(string status, Guid? revisionId = null, ReleaseUseTicket? ticket = null) =>
        (Status, RevisionId, Ticket) = (status, revisionId, ticket);
    public string Status { get; }
    public Guid? RevisionId { get; }
    internal ReleaseUseTicket? Ticket { get; }
}

/// <summary>
/// Bounded REL/AUD/floor mutation under write admission; not registered as a public endpoint. The outer
/// Requires a one-use ticket from durable U audit; the outer coordinator still owns fresh D authorization and caller-bound disclosure.
/// An exact owner/AUD match yields only a replay candidate; disclosure still requires a fresh D admission.
/// </summary>
public sealed class ReleaseMutationTransaction(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    ReleaseProjectAuthorityReader authority, ReleaseSelectionReader selections, IArtifactAuditTransactionFactory audits,
    IAuditMutationEvidenceReader auditEvidence,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock)
{
    public async Task<ReleaseMutationResult> ExecuteAsync(ReleaseUseTicket ticket, CancellationToken ct = default)
    {
        if (ticket is null || installationId == Guid.Empty || ticket.InstallationId != installationId ||
            !ReferenceEquals(ticket.Gate, gate) || !ticket.TryConsume()) return new("invalid");
        var subject = ticket.Subject; var command = ticket.Command; var correlationId = ticket.CorrelationId;
        IArtifactAuditTransaction? audit = null;
        ReleaseMutationResult? confirmed = null;
        try
        {
            await using var admission = await gate.AcquireWriteAsync(ct);
            using var stage = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stage.CancelAfter(TimeSpan.FromSeconds(2)); var token = stage.Token;
            var original = ticket.Original;
            var invocation = original.InvocationId;
            var currentAdmission = await authority.ReadCurrentUnderAdmissionAsync(subject, command.ProjectId, command.Permission, invocation, token);
            var admissionTime = clock.GetTrustedUtcNow();
            if (currentAdmission is null || currentAdmission.Digest != original.Digest || admissionTime.Offset != TimeSpan.Zero ||
                admissionTime < ticket.NotBeforeUtc || admissionTime >= ticket.ValidUntilUtc || admissionTime >= currentAdmission.ValidUntilUtc) return new("denied");
            var tenant = original.Project.TenantId;
            var key = Convert.FromHexString(ticket.KeyHash);
            var body = Convert.FromHexString(command.BusinessHash);
            var auditReason = ReleaseCommandHash.AuditReason(installationId, Convert.ToHexStringLower(key), command.BusinessHash);
            var expectedIntentId = ReleaseCommandHash.AuditIntentId(Convert.ToHexStringLower(key));
            await using var iam = await identity.CreateDbContextAsync(token);
            IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(iam.Database.GetDbConnection().ConnectionString);
            await using var tx = await iam.Database.BeginTransactionAsync(token);
            await using var rel = new ReleaseDbContext(ReleaseDbContextFactory.CreateEnlistedOptions(iam.Database.GetDbConnection()));
            await rel.Database.UseTransactionAsync(tx.GetDbTransaction(), token);
            var receipts = new ReleaseReceiptStore(rel);
            var replay = await receipts.FindUnderAdmissionAsync(installationId, tenant, command.ProjectId,
                original.Subject.PrincipalId, command.Action, key, body, token);
            if (replay.Status == "conflict") return new("conflict");
            if (replay.Status == "candidate")
            {
                var revisionExists = await rel.Revisions.AsNoTracking().AnyAsync(x => x.InstallationId == installationId &&
                    x.TenantId == tenant && x.ProjectId == command.ProjectId && x.Id == replay.RevisionId, token);
                if (!revisionExists || replay.RevisionId is null || replay.AuditIntentId != expectedIntentId || replay.AuditIntentHash is not { Length: 32 } ||
                    replay.BootId is null || replay.BootId == Guid.Empty || replay.InvocationId is null || replay.InvocationId == Guid.Empty)
                { gate.Close(); return new("unknown"); }
                var proof = await auditEvidence.MatchUnderAdmissionAsync(new(replay.AuditIntentId.Value,
                    Convert.ToHexStringLower(replay.AuditIntentHash), original.Subject.PrincipalId, tenant, replay.RevisionId.Value, replay.InvocationId.Value,
                    command.Permission, "project", command.ProjectId, auditReason), token);
                if (proof is AuditEvidenceStatus.Missing or AuditEvidenceStatus.Mismatch) { gate.Close(); return new("unknown"); }
                if (proof != AuditEvidenceStatus.Matched) return new("unavailable");
                var current = await authority.ReadCurrentUnderAdmissionAsync(subject, command.ProjectId, command.Permission, invocation, token);
                var replayTime = clock.GetTrustedUtcNow();
                if (current is null || current.Digest != original.Digest || replayTime.Offset != TimeSpan.Zero ||
                    replayTime >= ticket.ValidUntilUtc || replayTime >= current.ValidUntilUtc) return new("denied");
                return new("replay", replay.RevisionId, ticket);
            }
            if (replay.Status != "absent") return new("unavailable");
            if (command.ExpectedVersion is { } expected)
            {
                var versionRow = await rel.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
                    x.TenantId == tenant && x.ProjectId == command.ProjectId && x.Id == command.RevisionId, token);
                if (versionRow is null) return new("conflict");
                if (versionRow.Version != expected) return new("precondition");
            }
            var now = clock.GetTrustedUtcNow();
            if (now.Offset != TimeSpan.Zero || now >= ticket.ValidUntilUtc) return new("denied");
            var store = new ReleaseMutationStore(rel);
            Guid revisionId;
            ReleaseSelection? selected = command.Selection;
            ReleaseIntent? frozenIntent = null;
            DateTimeOffset? policyExpiry = null;
            if (command.Action == "freeze")
            {
                var row = await rel.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
                    x.TenantId == tenant && x.ProjectId == command.ProjectId && x.Id == command.RevisionId, token);
                if (row is null || row.State != "Draft") return new("conflict");
                selected = ReleaseSelection.Create(row.ApplicationId, row.EnvironmentId, row.TargetId, row.ArtifactId);
            }
            if (selected is not null)
            {
                var current = await selections.ReadUnderAdmissionAsync(installationId, tenant, command.ProjectId, selected, now, token);
                if (current.Status is "owner_missing" or "owner_mismatch") return new("denied");
                if (command.Action == "freeze")
                {
                    if (current.Status != "eligible") return new(current.Status switch
                    {
                        "qualification_required" => "ineligible",
                        "policy_missing" => "conflict",
                        "policy_denied" => "denied",
                        _ => "unavailable"
                    });
                    if (current.Intent is null) throw new InvalidDataException("Eligible release selection has no intent.");
                    frozenIntent = current.Intent;
                    policyExpiry = current.EligibilityExpiresAtUtc;
                }
            }
            switch (command.Action)
            {
                case "create-draft":
                    revisionId = Guid.NewGuid();
                    store.StageFirstDraft(installationId, ReleaseDraft.Create(tenant, command.ProjectId, Guid.NewGuid(), revisionId,
                        1, original.Subject.PrincipalId, command.Label!, command.Selection!), now);
                    break;
                case "next-draft":
                    revisionId = Guid.NewGuid();
                    var latest = await rel.Revisions.Where(x => x.InstallationId == installationId && x.TenantId == tenant &&
                        x.ProjectId == command.ProjectId && x.ReleaseId == command.ReleaseId).Select(x => (long?)x.RevisionNumber).MaxAsync(token);
                    if (latest is null or long.MaxValue) return new("conflict");
                    await store.StageNextDraftAsync(installationId, tenant, command.ProjectId, command.BaseRevisionId!.Value,
                        ReleaseDraft.Create(tenant, command.ProjectId, command.ReleaseId!.Value, revisionId, latest.Value + 1,
                            original.Subject.PrincipalId, command.Label!, command.Selection!), now, token);
                    break;
                case "replace-draft":
                    revisionId = command.RevisionId!.Value;
                    await store.StageReplaceDraftAsync(installationId, tenant, command.ProjectId, revisionId,
                        original.Subject.PrincipalId, command.ExpectedVersion!.Value, command.Label!, command.Selection!, now, token);
                    break;
                case "freeze":
                    revisionId = command.RevisionId!.Value;
                    await store.StageFreezeAsync(installationId, tenant, command.ProjectId, revisionId,
                        original.Subject.PrincipalId, command.ExpectedVersion!.Value, command.ExpectedSelectionHash!, frozenIntent!, now, token);
                    break;
                case "retire":
                    revisionId = command.RevisionId!.Value;
                    await store.StageRetireAsync(installationId, tenant, command.ProjectId, revisionId,
                        command.ExpectedVersion!.Value, command.ReasonCode!, now, token);
                    break;
                default: return new("invalid");
            }
            audit = await audits.EnlistAsync(iam.Database.GetDbConnection(), tx.GetDbTransaction(), token);
            await using var ownedAudit = audit;
            var intentId = expectedIntentId;
            var intent = new AuditIntent(1, intentId, invocation, AuditIntentStage.Mutation, original.Subject.PrincipalId,
                tenant, revisionId, command.Permission, "project", command.ProjectId, null,
                original.Grant.AssignmentRevision, auditReason, now, correlationId);
            var auditHash = Convert.FromHexString(AuditIntentCanonicalizer.ComputeHash(intent));
            var auditStage = await audit.StageAsync(intent, token);
            // An existing deterministic mutation intent without its exact owner receipt is inconsistent.
            if (auditStage.Status == AuditTransactionStageStatus.AlreadyDurable ||
                auditStage.Status == AuditTransactionStageStatus.Rejected && auditStage.ReasonCode == "intent_conflict")
            { gate.Close(); return new("unknown"); }
            if (auditStage.Status != AuditTransactionStageStatus.Staged) return new("unavailable");
            receipts.Stage(Guid.NewGuid(), installationId, tenant, command.ProjectId, original.Subject.PrincipalId, revisionId, original.Subject.BootGeneration, invocation,
                command.Action, key, body, intentId, auditHash, now);
            await rel.SaveChangesAsync(token);
            await AuthorityClockFloorStore.StageAsync(iam, installationId, clock.GetTrustedUtcNow(), token);
            var rechecked = await authority.ReadCurrentUnderAdmissionAsync(subject, command.ProjectId, command.Permission, invocation, token);
            var finalTime = clock.GetTrustedUtcNow();
            if (rechecked is null || rechecked.Digest != original.Digest || finalTime.Offset != TimeSpan.Zero ||
                finalTime < now || finalTime >= ticket.ValidUntilUtc || finalTime >= rechecked.ValidUntilUtc) return new("denied");
            if (frozenIntent is not null)
            {
                var current = await selections.ReadUnderAdmissionAsync(installationId, tenant, command.ProjectId, selected!, finalTime, token);
                if (current.Status != "eligible" || current.Intent?.SpecHash != frozenIntent.SpecHash ||
                    current.EligibilityExpiresAtUtc != policyExpiry) return new("conflict");
            }
            var commitTime = clock.GetTrustedUtcNow();
            if (commitTime.Offset != TimeSpan.Zero || commitTime < finalTime || commitTime >= ticket.ValidUntilUtc ||
                commitTime >= rechecked.ValidUntilUtc) return new("denied");
            await AuthorityClockFloorStore.StageAsync(iam, installationId, commitTime, token);
            var immediatelyBeforeCommit = clock.GetTrustedUtcNow();
            if (immediatelyBeforeCommit.Offset != TimeSpan.Zero || immediatelyBeforeCommit < commitTime ||
                immediatelyBeforeCommit >= ticket.ValidUntilUtc || immediatelyBeforeCommit >= rechecked.ValidUntilUtc ||
                policyExpiry is { } expiry && immediatelyBeforeCommit >= expiry) return new("denied");
            var outcome = await audit.CommitAsync(token);
            if (outcome.Status == AuditAppendStatus.Unknown && audit.CommitAttempted) gate.Close();
            confirmed = outcome.Status == AuditAppendStatus.Durable ? new("durable", revisionId, ticket) :
                outcome.Status == AuditAppendStatus.Unknown && audit.CommitAttempted ? new("unknown") : new("unavailable");
            return confirmed;
        }
        catch (InvalidDataException) when (audit?.CommitAttempted != true) { gate.Close(); return new("unavailable"); }
        catch (ArgumentException) when (audit is null) { return new("invalid"); }
        catch (ReleaseMutationConflictException) when (audit?.CommitAttempted != true) { return new("conflict"); }
        catch (DbUpdateConcurrencyException) when (audit?.CommitAttempted != true) { return new("conflict"); }
        catch (DbUpdateException error) when (audit?.CommitAttempted != true && error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_rel_revisions_release_id" or
                "IX_rel_revisions_release_id_revision_number" or "IX_rel_commands_installation_id_tenant_id_principal_id_action_~" })
        { return new("conflict"); }
        catch (InvalidOperationException) when (audit is null) { return new("unavailable"); }
        catch
        {
            if (confirmed is not null) return confirmed;
            if (audit?.CommitAttempted == true) { gate.Close(); return new("unknown"); }
            return new("unavailable");
        }
    }
}
