using System.Security.Cryptography;
using System.Text;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.ReleaseManagement.Domain;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Api;

/// <summary>One invocation's durable U evidence; cannot be constructed by a caller or reused for another mutation.</summary>
public sealed class ApprovalUseTicket
{
    private int _consumed;
    private int _disclosed;
    internal ApprovalUseTicket(Guid installationId, FairAsyncReaderWriterAdmissionGate gate, SessionSelection subject,
        ParsedApprovalMutation command, Guid correlationId, Guid invocationId, string keyHash, ApprovalAuthorityCaptureResult original,
        DateTimeOffset notBeforeUtc, DateTimeOffset validUntilUtc) =>
        (InstallationId, Gate, Subject, Command, CorrelationId, InvocationId, KeyHash, Original, NotBeforeUtc, ValidUntilUtc) =
        (installationId, gate, subject, command, correlationId, invocationId, keyHash, original, notBeforeUtc, validUntilUtc);
    internal Guid InstallationId { get; }
    internal FairAsyncReaderWriterAdmissionGate Gate { get; }
    internal SessionSelection Subject { get; }
    internal ParsedApprovalMutation Command { get; }
    internal Guid CorrelationId { get; }
    internal Guid InvocationId { get; }
    internal string KeyHash { get; }
    internal ApprovalAuthorityCaptureResult Original { get; }
    internal DateTimeOffset NotBeforeUtc { get; }
    internal DateTimeOffset ValidUntilUtc { get; }
    internal bool TryConsume() => Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;
    internal bool TryDisclose() => Interlocked.CompareExchange(ref _disclosed, 1, 0) == 0;
    public override string ToString() => "ApprovalUseTicket [private]";
}
public sealed record ApprovalUseResult(string Status, ApprovalUseTicket? Ticket = null);

/// <summary>Fresh project U and audit, before any release owner mutation. Not an HTTP response.</summary>
public sealed class ApprovalUseAdmission(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    ApprovalAuthorityCapture authority, IArtifactAuditTransactionFactory audits,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock)
{
    private Task<ApprovalAuthorityCaptureResult?> CaptureAsync(SessionSelection subject, Guid projectId, ParsedApprovalMutation command, CancellationToken ct) =>
        command.Action switch
        {
            "request" => authority.CaptureRequesterUnderAdmissionAsync(subject, projectId, command.EnvironmentId, ct),
            "grant" or "reject" => authority.CaptureApproverUnderAdmissionAsync(subject, projectId, command.EnvironmentId, ct),
            "revoke" => authority.CaptureRevokerUnderAdmissionAsync(subject, projectId, command.EnvironmentId, ct),
            _ => Task.FromResult<ApprovalAuthorityCaptureResult?>(null)
        };

    public async Task<ApprovalUseResult> BeginAsync(SessionSelection subject, ParsedApprovalMutation command,
        Guid projectId, string idempotencyKey, Guid correlationId, CancellationToken ct = default)
    {
        if (installationId == Guid.Empty || subject is null || command is null || correlationId == Guid.Empty || projectId == Guid.Empty || correlationId != command.CorrelationId) return new("invalid");
        IArtifactAuditTransaction? audit = null; ApprovalUseResult? confirmed = null; var useDurable = false;
        try
        {
            await using var admission = await gate.AcquireReadAsync(ct);
            using var stage = CancellationTokenSource.CreateLinkedTokenSource(ct); stage.CancelAfter(TimeSpan.FromSeconds(2));
            var token = stage.Token; var invocation = Guid.NewGuid();
            var original = await CaptureAsync(subject, projectId, command, token);
            if (original is null) return new("denied");
            var key = ReleaseCommandHash.ScopedKey(installationId, original.Project.TenantId, original.Subject.PrincipalId, command.Action, command.Resource, idempotencyKey);
            var now = clock.GetTrustedUtcNow();
            if (now.Offset != TimeSpan.Zero || now == default || now >= original.SessionValidUntilUtc) return new("denied");
            var invocationLimit = now.AddSeconds(10);
            var until = original.SessionValidUntilUtc < invocationLimit ? original.SessionValidUntilUtc : invocationLimit;
            await using var iam = await identity.CreateDbContextAsync(token);
            IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(iam.Database.GetDbConnection().ConnectionString);
            await using var tx = await iam.Database.BeginTransactionAsync(token);
            audit = await audits.EnlistAsync(iam.Database.GetDbConnection(), tx.GetDbTransaction(), token);
            await using var ownedAudit = audit;
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                "approval-use-v1\n" + key + "\n" + command.BusinessHash + "\n" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { original.Evidence, original.Subject, original.Project, original.Environment, original.SessionValidUntilUtc }))))));
            var intent = new AuditIntent(1, Guid.NewGuid(), invocation, AuditIntentStage.Use, original.Subject.PrincipalId,
                original.Project.TenantId, command.RequestId ?? command.ReleaseRevisionId ?? command.EnvironmentId, command.Permission,
                "environment", command.EnvironmentId, null, original.Evidence.EnvironmentGrant.AssignmentRevision, "approval-u-" + digest, now, correlationId);
            if ((await audit.StageAsync(intent, token)).Status != AuditTransactionStageStatus.Staged) return new("unavailable");
            var check = await CaptureAsync(subject, projectId, command, token);
            var current = clock.GetTrustedUtcNow();
            if (check is null || !original.SameAuthorityAs(check) || current.Offset != TimeSpan.Zero || current < now ||
                current >= until || current >= check.SessionValidUntilUtc) return new("denied");
            if (check.SessionValidUntilUtc < until) until = check.SessionValidUntilUtc;
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
                ? new("admitted", new ApprovalUseTicket(installationId, gate, subject, command, correlationId, invocation, key, original, after, until))
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
