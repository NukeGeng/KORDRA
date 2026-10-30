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

public sealed record ReleaseResponseCommitment(Guid InvocationId, Guid ProjectId, string PayloadSha256,
    int StatusCode, string? Etag, DateTimeOffset ValidUntilUtc, int MaximumBytes = 96 * 1024);
public interface IReleaseResponseCommitter
{
    Task CommitAsync(ReleaseResponseCommitment selection, ReadOnlyMemory<byte> bytes, CancellationToken ct);
    void Abort();
}

/// <summary>Fresh D authority, bounded current response and AUD/floor before one caller-bound handoff.</summary>
public sealed class ReleaseMutationDisclosure(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ReleaseDbContext> releases, ReleaseProjectAuthorityReader authority, ReleaseSelectionReader selections,
    IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock)
{

    public async Task<string> DiscloseAsync(ReleaseMutationResult mutation, IReleaseResponseCommitter response, CancellationToken ct = default)
    {
        if (mutation is null || response is null || mutation.Status is not ("durable" or "replay") || mutation.RevisionId is null ||
            mutation.Ticket is not { } ticket || ticket.InstallationId != installationId || !ReferenceEquals(ticket.Gate, gate) ||
            !ticket.TryDisclose()) return "invalid";
        IArtifactAuditTransaction? audit = null; var auditDurable = false; var handoffStarted = false;
        try
        {
            await using var held = await gate.AcquireReadAsync(ct);
            using var stage = CancellationTokenSource.CreateLinkedTokenSource(ct); stage.CancelAfter(TimeSpan.FromSeconds(2)); var token = stage.Token;
            var original = ticket.Original; var command = ticket.Command; var invocation = original.InvocationId;
            var current = await authority.ReadCurrentUnderAdmissionAsync(ticket.Subject, command.ProjectId, command.Permission, invocation, token);
            var now = clock.GetTrustedUtcNow();
            if (now < ticket.NotBeforeUtc || !Current(original, current, ticket.ValidUntilUtc, now)) return "denied";
            var until = Min(ticket.ValidUntilUtc, current!.ValidUntilUtc);
            var builder = new ReleasePayloadBuilder(releases, selections);
            var reply = await builder.DetailAsync(installationId, original.Project.TenantId, command.ProjectId, mutation.RevisionId.Value,
                ticket.CorrelationId, now, command.Action == "freeze", token);
            if (reply.Status != 200) throw new InvalidDataException("Committed release is missing.");
            if (reply.Bytes.Length > 96 * 1024) return "unavailable";
            if (reply.SelectionUntilUtc is { } selectionUntil) until = Min(until, selectionUntil);
            if (now >= until) return "denied";
            await using var iam = await identity.CreateDbContextAsync(token);
            IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(iam.Database.GetDbConnection().ConnectionString);
            await using var tx = await iam.Database.BeginTransactionAsync(token);
            audit = await audits.EnlistAsync(iam.Database.GetDbConnection(), tx.GetDbTransaction(), token);
            await using var ownedAudit = audit;
            var payloadHash = Convert.ToHexStringLower(SHA256.HashData(reply.Bytes));
            var reasonHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                "release-disclosure-v1\n" + ticket.KeyHash + "\n" + command.BusinessHash + "\n" + original.Digest)));
            var intent = new AuditIntent(1, Guid.NewGuid(), invocation, AuditIntentStage.Disclosure, original.Subject.PrincipalId,
                original.Project.TenantId, mutation.RevisionId.Value, command.Permission, "project", command.ProjectId,
                null, original.Grant.AssignmentRevision, "release-d-" + reasonHash, now, ticket.CorrelationId, payloadHash, reply.Bytes.Length);
            if ((await audit.StageAsync(intent, token)).Status != AuditTransactionStageStatus.Staged) return "unavailable";
            var rechecked = await authority.ReadCurrentUnderAdmissionAsync(ticket.Subject, command.ProjectId, command.Permission, invocation, token);
            var checkTime = clock.GetTrustedUtcNow();
            if (!Current(original, rechecked, until, checkTime) || checkTime < now) return "denied";
            until = Min(until, rechecked!.ValidUntilUtc);
            var fresh = await builder.DetailAsync(installationId, original.Project.TenantId, command.ProjectId, mutation.RevisionId.Value,
                ticket.CorrelationId, checkTime, command.Action == "freeze", token);
            if (fresh.Status != reply.Status || !fresh.Bytes.AsSpan().SequenceEqual(reply.Bytes) || fresh.Etag != reply.Etag ||
                fresh.SelectionUntilUtc != reply.SelectionUntilUtc) return "unavailable";
            if (fresh.SelectionUntilUtc is { } next) until = Min(until, next);
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
            await response.CommitAsync(new(invocation, command.ProjectId, payloadHash,
                mutation.Status == "durable" && command.Action is ("create-draft" or "next-draft") ? 201 : 200, reply.Etag, until), reply.Bytes, token);
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
    private static bool Current(ReleaseProjectAuthoritySnapshot original, ReleaseProjectAuthoritySnapshot? current, DateTimeOffset until, DateTimeOffset now) =>
        current is not null && current.Digest == original.Digest && now.Offset == TimeSpan.Zero && now < until && now < current.ValidUntilUtc;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
