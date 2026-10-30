using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

namespace Kordra.Api;

public sealed record ApprovalResponseCommitment(Guid InvocationId, Guid EnvironmentId, string PayloadSha256,
    int StatusCode, string? Etag, DateTimeOffset ValidUntilUtc, int MaximumBytes = 96 * 1024);
public interface IApprovalResponseCommitter
{
    Task CommitAsync(ApprovalResponseCommitment selection, ReadOnlyMemory<byte> bytes, CancellationToken ct);
    void Abort();
}

/// <summary>Fresh D authority, bounded current response and AUD/floor before one caller-bound handoff.</summary>
public sealed class ApprovalMutationDisclosure(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ReleaseDbContext> releases, IDbContextFactory<Kordra.Modules.ApprovalGovernance.Infrastructure.ApprovalDbContext> approvals,
    ApprovalAuthorityCapture authority, ApprovalEligibilityReader eligibility,
    IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock)
{

    public async Task<string> DiscloseAsync(ApprovalUseTicket ticket, ApprovalMutationResult mutation, IApprovalResponseCommitter response, CancellationToken ct = default)
    {
        if (mutation is null || response is null || mutation.Status is not ("durable" or "replay") || mutation.RequestId is null ||
            !ReferenceEquals(mutation.Ticket, ticket) || ticket is null || ticket.InstallationId != installationId || !ReferenceEquals(ticket.Gate, gate) ||
            !ticket.TryDisclose()) return "invalid";
        IArtifactAuditTransaction? audit = null; var auditDurable = false; var handoffStarted = false;
        try
        {
            await using var held = await gate.AcquireReadAsync(ct);
            using var stage = CancellationTokenSource.CreateLinkedTokenSource(ct); stage.CancelAfter(TimeSpan.FromSeconds(2)); var token = stage.Token;
            var original = ticket.Original; var command = ticket.Command; var invocation = ticket.InvocationId;
            var current = await Capture(ticket, token);
            var now = clock.GetTrustedUtcNow();
            if (now < ticket.NotBeforeUtc || !Current(original, current, ticket.ValidUntilUtc, now)) return "denied";
            var until = Min(ticket.ValidUntilUtc, current!.SessionValidUntilUtc);
            var builder = new ApprovalPayloadBuilder(approvals, releases, eligibility);
            var reply = await builder.DetailAsync(installationId, original.Project.TenantId, original.Project.ProjectId, command.EnvironmentId, mutation.RequestId.Value,
                ticket.CorrelationId, token);
            if (reply.Status == 503) return "unavailable";
            if (reply.Status != 200) throw new InvalidDataException("Committed approval is missing.");
            if (reply.Bytes.Length > 96 * 1024) return "unavailable";
            if (reply.ValidUntilUtc is { } selectionUntil) until = Min(until, selectionUntil);
            if (now >= until) return "denied";
            await using var iam = await identity.CreateDbContextAsync(token);
            IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(iam.Database.GetDbConnection().ConnectionString);
            await using var tx = await iam.Database.BeginTransactionAsync(token);
            audit = await audits.EnlistAsync(iam.Database.GetDbConnection(), tx.GetDbTransaction(), token);
            await using var ownedAudit = audit;
            var payloadHash = Convert.ToHexStringLower(SHA256.HashData(reply.Bytes));
            var reasonHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                "approval-disclosure-v1\n" + ticket.KeyHash + "\n" + command.BusinessHash + "\n" + original.Subject.SessionId.ToString("D"))));
            var intent = new AuditIntent(1, Guid.NewGuid(), invocation, AuditIntentStage.Disclosure, original.Subject.PrincipalId,
                original.Project.TenantId, mutation.RequestId.Value, command.Permission, "environment", command.EnvironmentId,
                null, original.Evidence.EnvironmentGrant.AssignmentRevision, "approval-d-" + reasonHash, now, ticket.CorrelationId, payloadHash, reply.Bytes.Length);
            if ((await audit.StageAsync(intent, token)).Status != AuditTransactionStageStatus.Staged) return "unavailable";
            var rechecked = await Capture(ticket, token);
            var checkTime = clock.GetTrustedUtcNow();
            if (!Current(original, rechecked, until, checkTime) || checkTime < now) return "denied";
            until = Min(until, rechecked!.SessionValidUntilUtc);
            var fresh = await builder.DetailAsync(installationId, original.Project.TenantId, original.Project.ProjectId, command.EnvironmentId, mutation.RequestId.Value,
                ticket.CorrelationId, token);
            if (fresh.Status != reply.Status || !fresh.Bytes.AsSpan().SequenceEqual(reply.Bytes) || fresh.Etag != reply.Etag ||
                fresh.ValidUntilUtc != reply.ValidUntilUtc) return "unavailable";
            if (fresh.ValidUntilUtc is { } next) until = Min(until, next);
            if (clock.GetTrustedUtcNow() >= until) return "denied";
            await AuthorityClockFloorStore.StageAsync(iam, installationId, clock.GetTrustedUtcNow(), token);
            var beforeCommit = clock.GetTrustedUtcNow();
            if (beforeCommit.Offset != TimeSpan.Zero || beforeCommit < checkTime || beforeCommit >= until) return "denied";
            var outcome = await audit.CommitAsync(token);
            if (outcome.Status == AuditAppendStatus.Unknown && audit.CommitAttempted) gate.Close();
            if (outcome.Status != AuditAppendStatus.Durable) return outcome.Status == AuditAppendStatus.Unknown && audit.CommitAttempted ? "unknown" : "unavailable";
            auditDurable = true;
            var beforeHandoff = clock.GetTrustedUtcNow();
            if (beforeHandoff.Offset != TimeSpan.Zero || beforeHandoff < beforeCommit || beforeHandoff >= until) return "denied";
            token.ThrowIfCancellationRequested();
            handoffStarted = true;
            await response.CommitAsync(new(invocation, command.EnvironmentId, payloadHash,
                mutation.Status == "durable" && command.Action == "request" ? 201 : 200, reply.Etag, until), reply.Bytes, token);
            return "completed";
        }
        catch (ReleaseDisclosureDeniedException) { return "denied"; }
        catch (InvalidDataException) { gate.Close(); return "unavailable"; }
        catch
        {
            if (handoffStarted) { gate.Close(); response.Abort(); return "unknown"; }
            if (!auditDurable && audit?.CommitAttempted == true) { gate.Close(); response.Abort(); return "unknown"; }
            return "unavailable";
        }
    }
    private Task<ApprovalAuthorityCaptureResult?> Capture(ApprovalUseTicket ticket, CancellationToken ct) => ticket.Command.Action switch
    {
        "request" => authority.CaptureRequesterUnderAdmissionAsync(ticket.Subject, ticket.Original.Project.ProjectId, ticket.Command.EnvironmentId, ct),
        "grant" or "reject" => authority.CaptureApproverUnderAdmissionAsync(ticket.Subject, ticket.Original.Project.ProjectId, ticket.Command.EnvironmentId, ct),
        "revoke" => authority.CaptureRevokerUnderAdmissionAsync(ticket.Subject, ticket.Original.Project.ProjectId, ticket.Command.EnvironmentId, ct),
        _ => Task.FromResult<ApprovalAuthorityCaptureResult?>(null)
    };
    private static bool Current(ApprovalAuthorityCaptureResult original, ApprovalAuthorityCaptureResult? current, DateTimeOffset until, DateTimeOffset now) =>
        current is not null && original.SameAuthorityAs(current) && now.Offset == TimeSpan.Zero && now < until && now < current.SessionValidUntilUtc;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
