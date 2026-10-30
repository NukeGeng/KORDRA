using System.Text.Json;
using Kordra.ApprovalGovernance.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ApprovalGovernance;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
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

/// <summary>Internal commit outcome only. Fresh audited D is required before any response.</summary>
public sealed class ApprovalMutationResult
{
    internal ApprovalMutationResult(string status, Guid? requestId = null, ApprovalUseTicket? ticket = null) =>
        (Status, RequestId, Ticket) = (status, requestId, ticket);
    public string Status { get; }
    public Guid? RequestId { get; }
    internal ApprovalUseTicket? Ticket { get; }
}

public sealed class ApprovalMutationTransaction(Guid installation, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ReleaseDbContext> releases, ApprovalAuthorityCapture authority, StoredApprovalAuthorityReader durableAuthority,
    ApprovalSubjectReader subjects, IArtifactAuditTransactionFactory audits, IAuditMutationEvidenceReader auditEvidence,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock)
{
    private Task<ApprovalAuthorityCaptureResult?> Capture(ApprovalUseTicket ticket, CancellationToken ct) => ticket.Command.Action switch
    {
        "request" => authority.CaptureRequesterUnderAdmissionAsync(ticket.Subject, ticket.Original.Project.ProjectId, ticket.Command.EnvironmentId, ct),
        "grant" or "reject" => authority.CaptureApproverUnderAdmissionAsync(ticket.Subject, ticket.Original.Project.ProjectId, ticket.Command.EnvironmentId, ct),
        "revoke" => authority.CaptureRevokerUnderAdmissionAsync(ticket.Subject, ticket.Original.Project.ProjectId, ticket.Command.EnvironmentId, ct),
        _ => Task.FromResult<ApprovalAuthorityCaptureResult?>(null)
    };
    public async Task<ApprovalMutationResult> ExecuteAsync(ApprovalUseTicket ticket, CancellationToken ct = default)
    {
        if (ticket is null || installation == Guid.Empty || ticket.InstallationId != installation || !ReferenceEquals(ticket.Gate, gate) || !ticket.TryConsume()) return new("invalid");
        IArtifactAuditTransaction? audit = null; ApprovalMutationResult? confirmed = null;
        try
        {
            await using var admission = await gate.AcquireWriteAsync(ct);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(2)); var token = budget.Token;
            var original = ticket.Original; var command = ticket.Command;
            var tenant = original.Project.TenantId; var project = original.Project.ProjectId; var environment = command.EnvironmentId;
            var current = await Capture(ticket, token); var now = clock.GetTrustedUtcNow();
            if (!original.SameAuthorityAs(current) || !Time(now, ticket, original)) return new("denied");
            var key = Convert.FromHexString(ticket.KeyHash); var body = Convert.FromHexString(command.BusinessHash);
            var intentId = ReleaseCommandHash.AuditIntentId(ticket.KeyHash);
            // Startup integrity scans use owner-specific prefixes; APR receipts must never look like orphan REL mutations.
            var reason = "approval-m-" + ReleaseCommandHash.AuditReason(installation, ticket.KeyHash, command.BusinessHash)["release-m-".Length..];
            await using var iam = await identity.CreateDbContextAsync(token);
            IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(iam.Database.GetDbConnection().ConnectionString);
            await using var tx = await iam.Database.BeginTransactionAsync(token);
            await using var apr = new ApprovalDbContext(ApprovalDbContextFactory.CreateEnlistedOptions(iam.Database.GetDbConnection()));
            await apr.Database.UseTransactionAsync(tx.GetDbTransaction(), token);
            var receipts = new ApprovalRequestReceiptStore(apr);
            var receipt = await receipts.FindUnderAdmissionAsync(installation, tenant, project, environment, original.Subject.PrincipalId,
                command.Action, key, body, token);
            if (receipt.Status == "conflict") return new("conflict");
            if (receipt.Status == "inconsistent") { gate.Close(); return new("unknown"); }
            if (receipt.Status == "candidate")
            {
                if (receipt.RequestId is null || receipt.AuditIntentId != intentId || receipt.AuditIntentHash is not { Length: 32 } || receipt.InvocationId is null)
                { gate.Close(); return new("unknown"); }
                var match = await auditEvidence.MatchUnderAdmissionAsync(new(intentId, Convert.ToHexStringLower(receipt.AuditIntentHash),
                    original.Subject.PrincipalId, tenant, receipt.RequestId.Value, receipt.InvocationId.Value,
                    command.Permission, "environment", environment, reason), token);
                if (match is AuditEvidenceStatus.Missing or AuditEvidenceStatus.Mismatch) { gate.Close(); return new("unknown"); }
                if (match != AuditEvidenceStatus.Matched) return new("unavailable");
                var replayAuthority = await Capture(ticket, token); var replayTime = clock.GetTrustedUtcNow();
                return original.SameAuthorityAs(replayAuthority) && Time(replayTime, ticket, original) && replayTime >= now
                    ? new("replay", receipt.RequestId, ticket) : new("denied");
            }
            if (receipt.Status != "absent") return new("unavailable");
            var store = new ApprovalRequestStore(apr); ApprovalRequestRecord row;
            ApprovalSubjectResult? basis = null; DateTimeOffset? expiry = null;
            if (command.Action == "request")
            {
                basis = await subjects.ReadEligibleUnderAdmissionAsync(installation, tenant, project, environment, command.ReleaseRevisionId!.Value, now, token);
                if (basis.Status != "eligible") return new(SubjectFailure(basis.Status));
                if (basis.Revision is null || basis.Policy is null) throw new InvalidDataException("Approval subject evidence incomplete.");
                var classification = Classification(basis.Revision);
                expiry = ApprovalDeadlines.Pending(now, basis.Policy, classification, original.Evidence.EnvironmentGrant.ExpiresAtUtc, original.Evidence.CompanionGrant.ExpiresAtUtc);
                row = new() { Id = Guid.NewGuid(), InstallationId = installation, TenantId = tenant, ProjectId = project,
                    EnvironmentId = environment, ReleaseRevisionId = basis.Revision.RevisionId, RequesterPrincipalId = original.Subject.PrincipalId,
                    SpecHash = Convert.FromHexString(basis.Revision.SpecHash!), RequesterEvidence = ApprovalAuthorityEvidenceCodec.Encode(original.Evidence),
                    CreatedAtUtc = now, PendingExpiresAtUtc = expiry.Value };
                store.StageRequest(row);
            }
            else
            {
                var existing = await apr.Requests.SingleOrDefaultAsync(x => x.InstallationId == installation && x.TenantId == tenant &&
                    x.ProjectId == project && x.EnvironmentId == environment && x.Id == command.RequestId, token);
                if (existing is null) return new("not_found");
                row = existing;
                if (row.Version != command.ExpectedVersion) return new("precondition");
                await using var rel = await releases.CreateDbContextAsync(token);
                var frozen = await new ReleaseQueryStore(rel).ReadRevisionUnderAdmissionAsync(installation, tenant, project, row.ReleaseRevisionId, token);
                if (frozen is null || frozen.EnvironmentId != environment || frozen.SpecHash != Convert.ToHexStringLower(row.SpecHash))
                    throw new InvalidDataException("Approval subject integrity mismatch.");
                byte[]? evidence = null;
                if (command.Action == "grant")
                {
                    if (now >= row.PendingExpiresAtUtc) return new("conflict");
                    basis = await subjects.ReadEligibleUnderAdmissionAsync(installation, tenant, project, environment, row.ReleaseRevisionId, now, token);
                    if (basis.Status != "eligible") return new(SubjectFailure(basis.Status));
                    if (basis.Revision?.SpecHash != frozen.SpecHash || basis.Policy is null) throw new InvalidDataException("Approval subject evidence mismatch.");
                    var requesterStatus = await durableAuthority.CheckUnderAdmissionAsync(row.RequesterEvidence, "requester", tenant, project, environment, row.RequesterPrincipalId, token);
                    if (requesterStatus != DurableApprovalAuthorityStatus.Current)
                        return new(requesterStatus == DurableApprovalAuthorityStatus.Unavailable ? "unavailable" : "denied");
                    var requester = ApprovalAuthorityEvidenceCodec.Decode(row.RequesterEvidence, "requester", tenant, project, environment);
                    try
                    {
                        expiry = ApprovalDeadlines.Grant(now, basis.Policy, Classification(frozen), row.RequesterPrincipalId, original.Subject.PrincipalId,
                            row.PendingExpiresAtUtc, requester.EnvironmentGrant.ExpiresAtUtc, requester.CompanionGrant.ExpiresAtUtc,
                            original.Evidence.EnvironmentGrant.ExpiresAtUtc, original.Evidence.CompanionGrant.ExpiresAtUtc);
                    }
                    catch (InvalidOperationException) { return new("denied"); }
                    evidence = ApprovalAuthorityEvidenceCodec.Encode(original.Evidence);
                }
                var action = command.Action switch { "grant" => ApprovalDecisionAction.Grant, "reject" => ApprovalDecisionAction.Reject,
                    "revoke" => ApprovalDecisionAction.Revoke, _ => throw new ArgumentException("Invalid approval action.") };
                await store.StageDecisionAsync(installation, tenant, project, environment, row.Id, command.ExpectedVersion!.Value, Guid.NewGuid(),
                    original.Subject.PrincipalId, action, command.ReasonCode!, now, expiry, evidence, token);
            }
            audit = await audits.EnlistAsync(iam.Database.GetDbConnection(), tx.GetDbTransaction(), token);
            await using var ownedAudit = audit;
            var intent = new AuditIntent(1, intentId, ticket.InvocationId, AuditIntentStage.Mutation, original.Subject.PrincipalId, tenant, row.Id,
                command.Permission, "environment", environment, null, original.Evidence.EnvironmentGrant.AssignmentRevision, reason, now, command.CorrelationId);
            var staged = await audit.StageAsync(intent, token);
            if (staged.Status == AuditTransactionStageStatus.AlreadyDurable || staged.Status == AuditTransactionStageStatus.Rejected && staged.ReasonCode == "intent_conflict")
            { gate.Close(); return new("unknown"); }
            if (staged.Status != AuditTransactionStageStatus.Staged) return new("unavailable");
            await receipts.StageAsync(Guid.NewGuid(), installation, tenant, project, environment, original.Subject.PrincipalId, row.Id,
                original.Subject.BootGeneration, ticket.InvocationId, command.Action, key, body, intentId,
                Convert.FromHexString(AuditIntentCanonicalizer.ComputeHash(intent)), now, token);
            await apr.SaveChangesAsync(token);
            var rechecked = await Capture(ticket, token); var final = clock.GetTrustedUtcNow();
            if (!original.SameAuthorityAs(rechecked) || !Time(final, ticket, original) || final < now || expiry is { } deadline && final >= deadline) return new("denied");
            if (basis is not null)
            {
                var fresh = await subjects.ReadEligibleUnderAdmissionAsync(installation, tenant, project, environment, row.ReleaseRevisionId, final, token);
                if (fresh.Status != "eligible" || fresh.Revision?.SpecHash != basis.Revision?.SpecHash || fresh.Policy?.PayloadHash != basis.Policy?.PayloadHash) return new("conflict");
                if (command.Action == "grant")
                {
                    var requesterStatus = await durableAuthority.CheckUnderAdmissionAsync(row.RequesterEvidence, "requester", tenant, project, environment,
                        row.RequesterPrincipalId, token);
                    if (requesterStatus != DurableApprovalAuthorityStatus.Current)
                        return new(requesterStatus == DurableApprovalAuthorityStatus.Unavailable ? "unavailable" : "denied");
                }
            }
            await AuthorityClockFloorStore.StageAsync(iam, installation, final, token);
            var beforeCommit = clock.GetTrustedUtcNow();
            if (!Time(beforeCommit, ticket, original) || beforeCommit < final || expiry is { } expires && beforeCommit >= expires) return new("denied");
            var outcome = await audit.CommitAsync(token);
            if (outcome.Status == AuditAppendStatus.Unknown && audit.CommitAttempted) gate.Close();
            confirmed = outcome.Status == AuditAppendStatus.Durable ? new("durable", row.Id, ticket) :
                outcome.Status == AuditAppendStatus.Unknown && audit.CommitAttempted ? new("unknown") : new("unavailable");
            return confirmed;
        }
        catch (ApprovalRequestConflictException) when (audit?.CommitAttempted != true) { return new("conflict"); }
        catch (DbUpdateConcurrencyException) when (audit?.CommitAttempted != true) { return new("conflict"); }
        catch (DbUpdateException error) when (audit?.CommitAttempted != true && error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return new("conflict"); }
        catch (InvalidDataException) when (audit?.CommitAttempted != true) { gate.Close(); return new("unavailable"); }
        catch
        {
            if (confirmed is not null) return confirmed;
            if (audit?.CommitAttempted == true) { gate.Close(); return new("unknown"); }
            return new("unavailable");
        }
    }
    private static string SubjectFailure(string status) => status switch
    {
        "absent" => "not_found",
        "qualification_required" => "ineligible",
        "policy_denied" => "denied",
        "not_frozen" or "subject_changed" or "owner_missing" or "owner_mismatch" or "policy_missing" => "conflict",
        _ => "unavailable"
    };
    private static bool Time(DateTimeOffset now, ApprovalUseTicket ticket, ApprovalAuthorityCaptureResult original) =>
        now.Offset == TimeSpan.Zero && now >= ticket.NotBeforeUtc && now < ticket.ValidUntilUtc && now < original.SessionValidUntilUtc;
    private static string Classification(ReleaseRevisionSnapshot revision)
    {
        using var doc = JsonDocument.Parse(revision.GetCanonicalSubject() ?? throw new InvalidDataException("Missing frozen approval subject."));
        return doc.RootElement.GetProperty("environment").GetProperty("classification").GetString() ?? throw new InvalidDataException("Missing classification.");
    }
}
