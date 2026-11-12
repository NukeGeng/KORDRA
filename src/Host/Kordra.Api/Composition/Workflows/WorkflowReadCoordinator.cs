using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Security.Cryptography;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Api;

public sealed record WorkflowReadScope(Guid TenantId, Guid ProjectId, Guid EnvironmentId, Guid InstanceId);
public sealed record WorkflowReadPolicy(Guid BindingId, long BindingRevision, Guid PolicyId, long PolicyRevision,
    DateTimeOffset ValidUntilUtc, string AuthorityDigest = "");

/// <summary>Returns current allow evidence for this exact owner scope and workflow.read, or null.</summary>
public interface IWorkflowReadPolicyReader
{
    Task<WorkflowReadPolicy?> ReadAllowedUnderAdmissionAsync(WorkflowReadScope scope, CancellationToken ct);
}

public sealed record WorkflowReadCommitment(Guid InvocationId, WorkflowReadScope Scope, long? BeforeVersion,
    string PayloadSha256, int PayloadSize, int StatusCode, DateTimeOffset ValidUntilUtc);

public interface IWorkflowReadResponseCommitter
{
    Task CommitAsync(WorkflowReadCommitment commitment, ReadOnlyMemory<byte> payload, CancellationToken ct);
    void Abort();
}

public enum WorkflowReadOutcome { Completed, Invalid, Denied, NotFound, Unavailable, Unknown }

/// <summary>Inactive composition. Scope is owner-resolved and every page has its own U/D pair.</summary>
public sealed class WorkflowReadCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    IDbContextFactory<WorkflowDbContext> workflows, WorkflowEnvironmentAuthorityReader authority,
    IWorkflowReadPolicyReader policies, IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate,
    IAuthorityClock clock)
{
    private DisposableTestBinding? disposableTest;

    public static WorkflowReadCoordinator CreateDisposableTestCoordinator(Guid installationId,
        IDbContextFactory<KordraIdentityDbContext> identity, IDbContextFactory<WorkflowDbContext> workflows,
        WorkflowEnvironmentAuthorityReader authority, IWorkflowReadPolicyReader policies,
        IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock,
        DisposableTestBinding profile) =>
        new(installationId, identity, workflows, authority, policies, audits, gate, clock) { disposableTest = profile };

    private bool isolatedTest;

    /// <summary>
    /// Legacy isolated kordra_test fixture only. The IAM clock floor stays in the case-owned schema and is
    /// removed with that namespace; it never touches the runtime app schema. Authority, audit and gate rules are unchanged.
    /// </summary>
    public static WorkflowReadCoordinator CreateIsolatedTestCoordinator(Guid installationId,
        IDbContextFactory<KordraIdentityDbContext> identity, IDbContextFactory<WorkflowDbContext> workflows,
        WorkflowEnvironmentAuthorityReader authority, IWorkflowReadPolicyReader policies,
        IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock) =>
        new(installationId, identity, workflows, authority, policies, audits, gate, clock) { isolatedTest = true };

    private const string Permission = "workflow.read";
    private const int MaximumBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WorkflowReadOutcome> ReadAsync(SessionSelection subject, WorkflowReadScope scope,
        long? beforeVersion, Guid correlationId, IWorkflowReadResponseCommitter response, CancellationToken ct = default)
    {
        if (installationId == Guid.Empty || subject is null || scope is null || response is null || correlationId == Guid.Empty ||
            scope.TenantId == Guid.Empty || scope.ProjectId == Guid.Empty || scope.EnvironmentId == Guid.Empty ||
            scope.InstanceId == Guid.Empty || beforeVersion is < 1)
            return WorkflowReadOutcome.Invalid;

        using var capacity = ProtectedInvocationCapacity.TryReserve(gate);
        if (capacity is null) return WorkflowReadOutcome.Unavailable;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        var token = budget.Token;
        var invocation = Guid.NewGuid();
        try
        {
            await using var held = await gate.AcquireReadAsync(token);
            var initial = await ReadEligibilityAsync(subject, scope, token);
            if (initial is null) return WorkflowReadOutcome.Denied;
            var until = Earlier(initial.Until, initial.Now.AddSeconds(10));
            var now = initial.Now;
            var use = await RecordAsync(AuditIntentStage.Use, null);
            if (use != WorkflowReadOutcome.Completed) return use;

            await using var workflow = await workflows.CreateDbContextAsync(token);
            await workflow.Database.OpenConnectionAsync(token);
            var reader = new WorkflowMetadataReader(workflow);
            var workflowId = new WorkflowIdentity(installationId, scope.TenantId, scope.InstanceId);
            var page = await ReadPageAsync();
            // An authorized negative lookup is also a disclosure, with a fixed safe payload.
            var payload = Encode(page);
            if (payload.Length > MaximumBytes) return WorkflowReadOutcome.Unavailable;
            var payloadHash = Digest(payload);
            var disclose = await RecordAsync(AuditIntentStage.Disclosure, payloadHash, payload.Length, async () =>
            {
                var freshPayload = Encode(await ReadPageAsync());
                return payload.AsSpan().SequenceEqual(freshPayload);
            });
            if (disclose != WorkflowReadOutcome.Completed) return disclose;

            var handoff = await ReadEligibilityAsync(subject, scope, token);
            if (!Revalidate(handoff) || gate.IsClosed) return WorkflowReadOutcome.Denied;
            token.ThrowIfCancellationRequested();
            try
            {
                await response.CommitAsync(new(invocation, scope, beforeVersion, payloadHash, payload.Length,
                    page is null ? 404 : 200, until), payload, token);
                return page is null ? WorkflowReadOutcome.NotFound : WorkflowReadOutcome.Completed;
            }
            catch
            {
                gate.Close();
                response.Abort();
                return WorkflowReadOutcome.Unknown;
            }

            Task<WorkflowMetadataPage?> ReadPageAsync() =>
                reader.ReadAsync(workflowId, scope.ProjectId, scope.EnvironmentId, beforeVersion, token);

            bool Revalidate(Eligibility? fresh)
            {
                if (fresh is null || fresh.Now < now || fresh.Now >= until ||
                    fresh.Authority.Subject.PrincipalId != initial.Authority.Subject.PrincipalId ||
                    fresh.Authority.Subject.SessionId != initial.Authority.Subject.SessionId ||
                    fresh.Authority.Subject.BootGeneration != initial.Authority.Subject.BootGeneration ||
                    fresh.Authority.Subject.PrincipalRevision != initial.Authority.Subject.PrincipalRevision ||
                    fresh.Authority.Grant != initial.Authority.Grant || fresh.Authority.Project != initial.Authority.Project ||
                    fresh.Authority.Environment != initial.Authority.Environment || (fresh.Policy with { ValidUntilUtc = initial.Policy.ValidUntilUtc }) != initial.Policy)
                    return false;
                now = fresh.Now;
                until = Earlier(until, fresh.Until);
                return true;
            }

            async Task<WorkflowReadOutcome> RecordAsync(AuditIntentStage stage, string? digest, int? size = null, Func<Task<bool>>? payloadStillCurrent = null)
            {
                IArtifactAuditTransaction? audit = null;
                var durable = false;
                try
                {
                    if (!Revalidate(await ReadEligibilityAsync(subject, scope, token))) return WorkflowReadOutcome.Denied;
                    await using var iam = await identity.CreateDbContextAsync(token);
                    if (disposableTest is { } profile)
                        IdentityAccessDbContextFactory.ValidateDisposableTestTarget(iam.Database.GetDbConnection().ConnectionString, profile);
                    else if (isolatedTest)
                        IdentityAccessDbContextFactory.ValidateIsolatedTestTarget(iam.Database.GetDbConnection().ConnectionString);
                    else
                        IdentityAccessDbContextFactory.ValidateRuntimeOrTestTransactionTarget(iam.Database.GetDbConnection().ConnectionString);
                    await using var root = await iam.Database.BeginTransactionAsync(token);
                    audit = await audits.EnlistAsync(iam.Database.GetDbConnection(), root.GetDbTransaction(), token);
                    await using var ownedAudit = audit;
                    var queryDigest = Digest(JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        installationId, scope, beforeVersion, initial.Policy.BindingId, initial.Policy.BindingRevision,
                        initial.Policy.PolicyId, initial.Policy.AuthorityDigest
                    }, Json));
                    var intent = new AuditIntent(1, Guid.NewGuid(), invocation, stage, initial.Authority.Subject.PrincipalId,
                        scope.TenantId, scope.InstanceId, Permission, "environment", scope.EnvironmentId,
                        initial.Policy.PolicyRevision, initial.Authority.Grant.AssignmentRevision,
                        "workflow-query-" + queryDigest, now, correlationId, digest, size);
                    if ((await audit.StageAsync(intent, token)).Status != AuditTransactionStageStatus.Staged)
                        return WorkflowReadOutcome.Unavailable;
                    if (payloadStillCurrent is not null && !await payloadStillCurrent()) return WorkflowReadOutcome.Unavailable;
                    if (!Revalidate(await ReadEligibilityAsync(subject, scope, token))) return WorkflowReadOutcome.Denied;
                    if (disposableTest is { } floorProfile)
                        await AuthorityClockFloorStore.StageDisposableIsolatedTestAsync(iam, installationId, now, floorProfile, token);
                    else if (isolatedTest)
                        await AuthorityClockFloorStore.StageIsolatedTestAsync(iam, installationId, now, token);
                    else
                        await AuthorityClockFloorStore.StageAsync(iam, installationId, now, token);
                    var commitTime = clock.GetTrustedUtcNow();
                    if (!ValidUtc(commitTime) || commitTime < now || commitTime >= until || gate.IsClosed)
                        return WorkflowReadOutcome.Denied;
                    var result = await audit.CommitAsync(token);
                    durable = result.Status == AuditAppendStatus.Durable;
                    if (durable)
                    {
                        now = commitTime;
                        return WorkflowReadOutcome.Completed;
                    }
                    if (result.Status != AuditAppendStatus.Unknown || !audit.CommitAttempted)
                        return WorkflowReadOutcome.Unavailable;
                }
                catch
                {
                    if (durable) return WorkflowReadOutcome.Completed;
                    if (audit?.CommitAttempted != true) return WorkflowReadOutcome.Unavailable;
                }
                gate.Close();
                return WorkflowReadOutcome.Unknown;
            }
        }
        catch { return WorkflowReadOutcome.Unavailable; }
    }

    private async Task<Eligibility?> ReadEligibilityAsync(SessionSelection subject, WorkflowReadScope scope, CancellationToken ct)
    {
        var current = await authority.ReadCurrentUnderAdmissionAsync(subject, scope.TenantId, scope.ProjectId,
            scope.EnvironmentId, Permission, ct);
        if (current is null) return null;
        var policy = await policies.ReadAllowedUnderAdmissionAsync(scope, ct);
        if (policy is null || policy.BindingId == Guid.Empty || policy.BindingRevision <= 0 || policy.PolicyId == Guid.Empty ||
            policy.PolicyRevision <= 0 || policy.AuthorityDigest is not { Length: 64 } ||
            policy.AuthorityDigest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            !ValidUtc(policy.ValidUntilUtc))
            return null;
        var now = clock.GetTrustedUtcNow();
        var until = Earlier(current.ValidUntilUtc, policy.ValidUntilUtc);
        return ValidUtc(now) && now < until ? new(current, policy, now, until) : null;
    }

    private static byte[] Encode(WorkflowMetadataPage? page) =>
        JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, found = page is not null, page }, Json);

    private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static bool ValidUtc(DateTimeOffset utc) => utc.Offset == TimeSpan.Zero && utc != default && utc != DateTimeOffset.MaxValue;
    private static string Digest(ReadOnlySpan<byte> payload) => Convert.ToHexStringLower(SHA256.HashData(payload));
    private sealed record Eligibility(WorkflowEnvironmentAuthoritySnapshot Authority, WorkflowReadPolicy Policy,
        DateTimeOffset Now, DateTimeOffset Until);
}
