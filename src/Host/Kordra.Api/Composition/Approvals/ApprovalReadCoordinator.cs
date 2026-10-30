using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Api;

public interface IApprovalSchemaReadiness { Task<bool> CheckAsync(CancellationToken ct); }

/// <summary>Environment-scoped history reads with fresh U/D and one bounded caller handoff.</summary>
public sealed class ApprovalReadCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ReleaseDbContext> releases, IDbContextFactory<Kordra.Modules.ApprovalGovernance.Infrastructure.ApprovalDbContext> approvals,
    ApprovalAuthorityCapture authority, ApprovalEligibilityReader eligibility,
    IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock, IApprovalSchemaReadiness readiness, IApprovalEnvironmentIdentityReader? environments = null)
{
    private sealed class State { public bool RetainCapacity; }
    private sealed record Audited(string Status, DateTimeOffset Until);
    public async Task<string> ExecuteAsync(SessionSelection subject, Guid projectId, Guid environmentId, ApprovalReadQuery query,
        IApprovalResponseCommitter response, CancellationToken ct = default)
    {
        if (installationId == Guid.Empty || subject is null || (projectId == Guid.Empty && environments is null) || environmentId == Guid.Empty || query is null || response is null ||
            query.CorrelationId == Guid.Empty || query.Limit is < 1 or > 50 || query.AfterId == Guid.Empty || query.RequestId == Guid.Empty ||
            query.RequestId is not null && (query.AfterId is not null || query.Limit != 50)) return "invalid";
        var capacity = ProtectedInvocationCapacity.TryReserve(gate); if (capacity is null) return "unavailable";
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10));
        var state = new State(); var work = Run(subject, projectId, environmentId, query, response, state, budget.Token);
        try
        {
            var status = await work.WaitAsync(budget.Token);
            if (status == "unknown") { state.RetainCapacity = true; response.Abort(); }
            return status;
        }
        catch
        {
            if (!work.IsCompleted) { state.RetainCapacity = true; gate.Close(); Observe(work); response.Abort(); return "unknown"; }
            return "unavailable";
        }
        finally { if (work.IsCompleted && !state.RetainCapacity) capacity.Dispose(); }
    }
    private async Task<string> Run(SessionSelection subject, Guid project, Guid environment, ApprovalReadQuery query,
        IApprovalResponseCommitter response, State state, CancellationToken ct)
    {
        if (!await readiness.CheckAsync(ct)) return "unavailable";
        var invocation = Guid.NewGuid(); var builder = new ApprovalPayloadBuilder(approvals, releases, eligibility);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("approval-query-v1\n" + JsonSerializer.Serialize(new
        { schemaVersion = 1, environmentId = environment, requestId = query.RequestId, query.Limit, query.AfterId }))));
        ApprovalAuthorityCaptureResult original; DateTimeOffset until; DateTimeOffset notBefore;
        try
        {
            using (var stage = Stage(ct))
            await using (var held = await gate.AcquireReadAsync(stage.Token))
            {
                if (project == Guid.Empty)
                {
                    var owner = await environments!.ReadCurrentUnderAdmissionAsync(environment, stage.Token);
                    if (owner is null || owner.EnvironmentId != environment || owner.TenantId == Guid.Empty || owner.ProjectId == Guid.Empty) return "denied";
                    project = owner.ProjectId;
                }
                var found = await authority.CaptureReadUnderAdmissionAsync(subject, project, environment, stage.Token);
                if (found is null) return "denied";
                original = found; var now = clock.GetTrustedUtcNow();
                if (now.Offset != TimeSpan.Zero || now == default || now >= original.SessionValidUntilUtc) return "denied";
                until = Min(original.SessionValidUntilUtc, now.AddSeconds(10));
                var use = await AuditAsync(subject, original, invocation, query, digest, AuditIntentStage.Use, until, null, builder, stage.Token);
                if (use.Status != "durable") { if (use.Status == "unknown") state.RetainCapacity = true; return use.Status; }
                until = Min(until, use.Until);
                notBefore = clock.GetTrustedUtcNow();
                if (notBefore.Offset != TimeSpan.Zero || notBefore < now || notBefore >= until) return "denied";
            }
            ApprovalPayload initial;
            using (var stage = Stage(ct))
            await using (var held = await gate.AcquireReadAsync(stage.Token))
                initial = await builder.QueryAsync(installationId, original.Project.TenantId, project, environment, query, stage.Token);
            if (initial.Status == 503) return "unavailable";
            var maximum = query.RequestId is not null ? 96 * 1024 : 64 * 1024;
            if (initial.Bytes.Length > maximum) return "unavailable";
            using (var stage = Stage(ct))
            await using (var held = await gate.AcquireReadAsync(stage.Token))
            {
                var now = clock.GetTrustedUtcNow();
                var current = await authority.CaptureReadUnderAdmissionAsync(subject, project, environment, stage.Token);
                if (now < notBefore || !Current(original, current, until, now)) return "denied";
                until = Min(until, current!.SessionValidUntilUtc);
                var fresh = await builder.QueryAsync(installationId, original.Project.TenantId, project, environment, query, stage.Token);
                if (!Same(initial, fresh)) return "unavailable";
                if (fresh.ValidUntilUtc is { } selectionUntil) until = Min(until, selectionUntil);
                var disclose = await AuditAsync(subject, original, invocation, query, digest, AuditIntentStage.Disclosure, until, fresh, builder, stage.Token);
                if (disclose.Status != "durable") { if (disclose.Status == "unknown") state.RetainCapacity = true; return disclose.Status; }
                until = Min(until, disclose.Until);
                var handoff = clock.GetTrustedUtcNow(); if (handoff.Offset != TimeSpan.Zero || handoff < now || handoff >= until) return "denied";
                try
                {
                    await response.CommitAsync(new(invocation, environment, Convert.ToHexStringLower(SHA256.HashData(fresh.Bytes)),
                        fresh.Status, fresh.Etag, until, maximum), fresh.Bytes, stage.Token);
                    return "completed";
                }
                catch { gate.Close(); state.RetainCapacity = true; response.Abort(); return "unknown"; }
            }
        }
        catch (InvalidDataException) { gate.Close(); return "unavailable"; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return "unavailable"; }
    }
    private async Task<Audited> AuditAsync(SessionSelection subject, ApprovalAuthorityCaptureResult original, Guid invocation, ApprovalReadQuery query,
        string queryHash, AuditIntentStage kind, DateTimeOffset until, ApprovalPayload? payload, ApprovalPayloadBuilder builder, CancellationToken ct)
    {
        IArtifactAuditTransaction? audit = null; Audited? confirmed = null;
        try
        {
            var now = clock.GetTrustedUtcNow();
            if (now.Offset != TimeSpan.Zero || now >= until) return new("denied", until);
            await using var iam = await identity.CreateDbContextAsync(ct);
            IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(iam.Database.GetDbConnection().ConnectionString);
            await using var tx = await iam.Database.BeginTransactionAsync(ct);
            audit = await audits.EnlistAsync(iam.Database.GetDbConnection(), tx.GetDbTransaction(), ct);
            await using var ownedAudit = audit;
            var reason = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("approval-read-binding-v1\n" + queryHash + "\n" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { original.Evidence, original.Subject, original.Project, original.Environment }))))));
            var intent = new AuditIntent(1, Guid.NewGuid(), invocation, kind, original.Subject.PrincipalId, original.Project.TenantId,
                query.RequestId ?? original.Environment.EnvironmentId, "release.approval.read", "environment", original.Environment.EnvironmentId, null,
                original.Evidence.EnvironmentGrant.AssignmentRevision, "approval-query-" + reason, now, query.CorrelationId,
                payload is null ? null : Convert.ToHexStringLower(SHA256.HashData(payload.Bytes)), payload?.Bytes.Length);
            if ((await audit.StageAsync(intent, ct)).Status != AuditTransactionStageStatus.Staged) return new("unavailable", until);
            var current = await authority.CaptureReadUnderAdmissionAsync(subject, original.Project.ProjectId, original.Environment.EnvironmentId, ct);
            var check = clock.GetTrustedUtcNow();
            if (!Current(original, current, until, check) || check < now) return new("denied", until);
            until = Min(until, current!.SessionValidUntilUtc);
            if (payload is not null)
            {
                var fresh = await builder.QueryAsync(installationId, original.Project.TenantId, original.Project.ProjectId, original.Environment.EnvironmentId, query, ct);
                if (!Same(payload, fresh)) return new("unavailable", until);
                if (fresh.ValidUntilUtc is { } next) until = Min(until, next);
            }
            await AuthorityClockFloorStore.StageAsync(iam, installationId, clock.GetTrustedUtcNow(), ct);
            var before = clock.GetTrustedUtcNow();
            if (before.Offset != TimeSpan.Zero || before < check || before >= until) return new("denied", until);
            var result = await audit.CommitAsync(ct);
            if (result.Status == AuditAppendStatus.Unknown && audit.CommitAttempted) gate.Close();
            confirmed = result.Status == AuditAppendStatus.Durable ? new("durable", until) :
                result.Status == AuditAppendStatus.Unknown && audit.CommitAttempted ? new("unknown", until) : new("unavailable", until);
            return confirmed;
        }
        catch (InvalidDataException) { gate.Close(); return new("unavailable", until); }
        catch
        {
            if (confirmed is not null) return confirmed;
            if (audit?.CommitAttempted == true) { gate.Close(); return new("unknown", until); }
            return new("unavailable", until);
        }
    }
    private static bool Same(ApprovalPayload a, ApprovalPayload b) => a.Status == b.Status && a.Etag == b.Etag && a.ValidUntilUtc == b.ValidUntilUtc && a.Bytes.AsSpan().SequenceEqual(b.Bytes);
    private static bool Current(ApprovalAuthorityCaptureResult original, ApprovalAuthorityCaptureResult? current, DateTimeOffset until, DateTimeOffset now) =>
        current is not null && original.SameAuthorityAs(current) && now.Offset == TimeSpan.Zero && now < until && now < current.SessionValidUntilUtc;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static CancellationTokenSource Stage(CancellationToken ct) { var stage = CancellationTokenSource.CreateLinkedTokenSource(ct); stage.CancelAfter(TimeSpan.FromSeconds(2)); return stage; }
    private static void Observe(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
