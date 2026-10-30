using System.Security.Cryptography;
using System.Text;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.ReleaseManagement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Api;

/// <summary>One invocation's durable U evidence; cannot be constructed by a caller or reused for another mutation.</summary>
public sealed class ReleaseUseTicket
{
    private int _consumed;
    private int _disclosed;
    internal ReleaseUseTicket(Guid installationId, FairAsyncReaderWriterAdmissionGate gate, SessionSelection subject,
        ReleaseMutationCommand command, Guid correlationId, string keyHash, ReleaseProjectAuthoritySnapshot original,
        DateTimeOffset notBeforeUtc, DateTimeOffset validUntilUtc) =>
        (InstallationId, Gate, Subject, Command, CorrelationId, KeyHash, Original, NotBeforeUtc, ValidUntilUtc) =
        (installationId, gate, subject, command, correlationId, keyHash, original, notBeforeUtc, validUntilUtc);
    internal Guid InstallationId { get; }
    internal FairAsyncReaderWriterAdmissionGate Gate { get; }
    internal SessionSelection Subject { get; }
    internal ReleaseMutationCommand Command { get; }
    internal Guid CorrelationId { get; }
    internal string KeyHash { get; }
    internal ReleaseProjectAuthoritySnapshot Original { get; }
    internal DateTimeOffset NotBeforeUtc { get; }
    internal DateTimeOffset ValidUntilUtc { get; }
    internal bool TryConsume() => Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;
    internal bool TryDisclose() => Interlocked.CompareExchange(ref _disclosed, 1, 0) == 0;
    public override string ToString() => "ReleaseUseTicket [private]";
}
public sealed record ReleaseUseResult(string Status, ReleaseUseTicket? Ticket = null);

/// <summary>Fresh project U and audit, before any release owner mutation. Not an HTTP response.</summary>
public sealed class ReleaseUseAdmission(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    ReleaseProjectAuthorityReader authority, IArtifactAuditTransactionFactory audits,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock)
{
    public async Task<ReleaseUseResult> BeginAsync(SessionSelection subject, ReleaseMutationCommand command,
        string idempotencyKey, Guid correlationId, CancellationToken ct = default)
    {
        if (installationId == Guid.Empty || subject is null || command is null || correlationId == Guid.Empty) return new("invalid");
        IArtifactAuditTransaction? audit = null; ReleaseUseResult? confirmed = null; var useDurable = false;
        try
        {
            await using var admission = await gate.AcquireReadAsync(ct);
            using var stage = CancellationTokenSource.CreateLinkedTokenSource(ct); stage.CancelAfter(TimeSpan.FromSeconds(2));
            var token = stage.Token; var invocation = Guid.NewGuid();
            var original = await authority.ReadCurrentUnderAdmissionAsync(subject, command.ProjectId, command.Permission, invocation, token);
            if (original is null) return new("denied");
            var key = command.ScopedKeyHash(installationId, original.Project.TenantId, original.Subject.PrincipalId, idempotencyKey);
            var now = clock.GetTrustedUtcNow();
            if (now.Offset != TimeSpan.Zero || now == default || now >= original.ValidUntilUtc) return new("denied");
            var invocationLimit = now.AddSeconds(10);
            var until = original.ValidUntilUtc < invocationLimit ? original.ValidUntilUtc : invocationLimit;
            await using var iam = await identity.CreateDbContextAsync(token);
            IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(iam.Database.GetDbConnection().ConnectionString);
            await using var tx = await iam.Database.BeginTransactionAsync(token);
            audit = await audits.EnlistAsync(iam.Database.GetDbConnection(), tx.GetDbTransaction(), token);
            await using var ownedAudit = audit;
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                "release-use-v1\n" + key + "\n" + command.BusinessHash + "\n" + original.Digest)));
            var intent = new AuditIntent(1, Guid.NewGuid(), invocation, AuditIntentStage.Use, original.Subject.PrincipalId,
                original.Project.TenantId, command.RevisionId ?? command.ReleaseId ?? command.ProjectId, command.Permission,
                "project", command.ProjectId, null, original.Grant.AssignmentRevision, "release-u-" + digest, now, correlationId);
            if ((await audit.StageAsync(intent, token)).Status != AuditTransactionStageStatus.Staged) return new("unavailable");
            var check = await authority.ReadCurrentUnderAdmissionAsync(subject, command.ProjectId, command.Permission, invocation, token);
            var current = clock.GetTrustedUtcNow();
            if (check is null || check.Digest != original.Digest || current.Offset != TimeSpan.Zero || current < now ||
                current >= until || current >= check.ValidUntilUtc) return new("denied");
            if (check.ValidUntilUtc < until) until = check.ValidUntilUtc;
            await AuthorityClockFloorStore.StageAsync(iam, installationId, current, token);
            var beforeCommit = clock.GetTrustedUtcNow();
            if (beforeCommit.Offset != TimeSpan.Zero || beforeCommit < current || beforeCommit >= until) return new("denied");
            var outcome = await audit.CommitAsync(token);
            if (outcome.Status == AuditAppendStatus.Unknown && audit.CommitAttempted) gate.Close();
            if (outcome.Status != AuditAppendStatus.Durable)
                return outcome.Status == AuditAppendStatus.Unknown && audit.CommitAttempted ? new("unknown") : new("unavailable");
            useDurable = true;
            var after = clock.GetTrustedUtcNow();
            confirmed = after.Offset == TimeSpan.Zero && after >= beforeCommit && after < until
                ? new("admitted", new ReleaseUseTicket(installationId, gate, subject, command, correlationId, key, original, after, until))
                : new("denied");
            return confirmed;
        }
        catch (ArgumentException) when (audit is null) { return new("invalid"); }
        catch
        {
            if (confirmed is not null) return confirmed;
            if (useDurable) return new("unavailable");
            if (audit?.CommitAttempted == true) { gate.Close(); return new("unknown"); }
            return new("unavailable");
        }
    }
}
