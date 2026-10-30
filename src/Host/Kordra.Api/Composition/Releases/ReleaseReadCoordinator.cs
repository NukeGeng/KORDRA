using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Api;

/// <summary>Project-scoped history reads with fresh U/D and one bounded caller handoff.</summary>
public sealed class ReleaseReadCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<ReleaseDbContext> releases, ReleaseProjectAuthorityReader authority, ReleaseSelectionReader selections,
    IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock, IReleaseSchemaReadiness readiness)
{
    private sealed class State { public bool RetainCapacity; }
    private sealed record Audited(string Status, DateTimeOffset Until);
    public async Task<string> ExecuteAsync(SessionSelection subject, Guid projectId, ReleaseReadQuery query,
        IReleaseResponseCommitter response, CancellationToken ct = default)
    {
        if (installationId == Guid.Empty || subject is null || projectId == Guid.Empty || query is null || response is null ||
            query.CorrelationId == Guid.Empty || !Enum.IsDefined(query.Kind) || query.Limit is < 1 or > 50 || query.AfterId == Guid.Empty ||
            query.Kind == ReleaseReadKind.Lineages && query.ResourceId is not null ||
            query.Kind is ReleaseReadKind.Revisions or ReleaseReadKind.Detail && (query.ResourceId is null || query.ResourceId == Guid.Empty) ||
            query.Kind == ReleaseReadKind.Detail && (query.AfterId is not null || query.Limit != 50)) return "invalid";
        var capacity = ProtectedInvocationCapacity.TryReserve(gate); if (capacity is null) return "unavailable";
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10));
        var state = new State(); var work = Run(subject, projectId, query, response, state, budget.Token);
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
    private async Task<string> Run(SessionSelection subject, Guid project, ReleaseReadQuery query,
        IReleaseResponseCommitter response, State state, CancellationToken ct)
    {
        if (!await readiness.CheckAsync(ct)) return "unavailable";
        var invocation = Guid.NewGuid(); var builder = new ReleasePayloadBuilder(releases, selections);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("release-query-v1\n" + JsonSerializer.Serialize(new
        { schemaVersion = 1, kind = query.Kind.ToString(), resourceId = query.ResourceId, query.Limit, query.AfterId }))));
        ReleaseProjectAuthoritySnapshot original; DateTimeOffset until; DateTimeOffset notBefore;
        try
        {
            using (var stage = Stage(ct))
            await using (var held = await gate.AcquireReadAsync(stage.Token))
            {
                var found = await authority.ReadCurrentUnderAdmissionAsync(subject, project, "release.read", invocation, stage.Token);
                if (found is null) return "denied";
                original = found; var now = clock.GetTrustedUtcNow();
                if (now.Offset != TimeSpan.Zero || now == default || now >= original.ValidUntilUtc) return "denied";
                until = Min(original.ValidUntilUtc, now.AddSeconds(10));
                var use = await AuditAsync(subject, original, query, digest, AuditIntentStage.Use, until, null, builder, stage.Token);
                if (use.Status != "durable") { if (use.Status == "unknown") state.RetainCapacity = true; return use.Status; }
                until = Min(until, use.Until);
                notBefore = clock.GetTrustedUtcNow();
                if (notBefore.Offset != TimeSpan.Zero || notBefore < now || notBefore >= until) return "denied";
            }
            var initial = await builder.QueryAsync(installationId, original.Project.TenantId, project, query, clock.GetTrustedUtcNow(), ct);
            var maximum = query.Kind == ReleaseReadKind.Detail ? 96 * 1024 : 64 * 1024;
            if (initial.Bytes.Length > maximum) return "unavailable";
            using (var stage = Stage(ct))
            await using (var held = await gate.AcquireReadAsync(stage.Token))
            {
                var now = clock.GetTrustedUtcNow();
                var current = await authority.ReadCurrentUnderAdmissionAsync(subject, project, "release.read", invocation, stage.Token);
                if (now < notBefore || !Current(original, current, until, now)) return "denied";
                until = Min(until, current!.ValidUntilUtc);
                var fresh = await builder.QueryAsync(installationId, original.Project.TenantId, project, query, now, stage.Token);
                if (!Same(initial, fresh)) return "unavailable";
                if (fresh.SelectionUntilUtc is { } selectionUntil) until = Min(until, selectionUntil);
                var disclose = await AuditAsync(subject, original, query, digest, AuditIntentStage.Disclosure, until, fresh, builder, stage.Token);
                if (disclose.Status != "durable") { if (disclose.Status == "unknown") state.RetainCapacity = true; return disclose.Status; }
                until = Min(until, disclose.Until);
                var handoff = clock.GetTrustedUtcNow(); if (handoff.Offset != TimeSpan.Zero || handoff < now || handoff >= until) return "denied";
                try
                {
                    await response.CommitAsync(new(invocation, project, Convert.ToHexStringLower(SHA256.HashData(fresh.Bytes)),
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
    private async Task<Audited> AuditAsync(SessionSelection subject, ReleaseProjectAuthoritySnapshot original, ReleaseReadQuery query,
        string queryHash, AuditIntentStage kind, DateTimeOffset until, ReleasePayload? payload, ReleasePayloadBuilder builder, CancellationToken ct)
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
            var reason = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("release-read-binding-v1\n" + queryHash + "\n" + original.Digest)));
            var intent = new AuditIntent(1, Guid.NewGuid(), original.InvocationId, kind, original.Subject.PrincipalId, original.Project.TenantId,
                query.ResourceId ?? original.Project.ProjectId, "release.read", "project", original.Project.ProjectId, null,
                original.Grant.AssignmentRevision, "release-query-" + reason, now, query.CorrelationId,
                payload is null ? null : Convert.ToHexStringLower(SHA256.HashData(payload.Bytes)), payload?.Bytes.Length);
            if ((await audit.StageAsync(intent, ct)).Status != AuditTransactionStageStatus.Staged) return new("unavailable", until);
            var current = await authority.ReadCurrentUnderAdmissionAsync(subject, original.Project.ProjectId, "release.read", original.InvocationId, ct);
            var check = clock.GetTrustedUtcNow();
            if (!Current(original, current, until, check) || check < now) return new("denied", until);
            until = Min(until, current!.ValidUntilUtc);
            if (payload is not null)
            {
                var fresh = await builder.QueryAsync(installationId, original.Project.TenantId, original.Project.ProjectId, query, check, ct);
                if (!Same(payload, fresh)) return new("unavailable", until);
                if (fresh.SelectionUntilUtc is { } next) until = Min(until, next);
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
    private static bool Same(ReleasePayload a, ReleasePayload b) => a.Status == b.Status && a.Etag == b.Etag && a.SelectionUntilUtc == b.SelectionUntilUtc && a.Bytes.AsSpan().SequenceEqual(b.Bytes);
    private static bool Current(ReleaseProjectAuthoritySnapshot original, ReleaseProjectAuthoritySnapshot? current, DateTimeOffset until, DateTimeOffset now) =>
        current is not null && current.Digest == original.Digest && now.Offset == TimeSpan.Zero && now < until && now < current.ValidUntilUtc;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static CancellationTokenSource Stage(CancellationToken ct) { var stage = CancellationTokenSource.CreateLinkedTokenSource(ct); stage.CancelAfter(TimeSpan.FromSeconds(2)); return stage; }
    private static void Observe(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
