using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.ApprovalGovernance;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ReleaseManagement;
using Kordra.Modules.ReleaseManagement.Contracts;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.ReleaseManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public sealed record ApprovalRecoveryInspection(string Status, ApprovalRecoveryPlan? BoundPlan = null);
public sealed record ApprovalRecoveryResult(string Status, Guid? CommandId = null, Guid? IntentId = null);

/// <summary>Stopped, quiescent exact-evidence reconciliation only. Never creates or replays an approval decision.</summary>
public sealed class OfflineApprovalRecoveryCoordinator(Guid installation, IDbContextFactory<KordraIdentityDbContext> identity,
    IArtifactProcessQuiescence processes)
{
    public async Task<ApprovalRecoveryInspection> InspectAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> json, CancellationToken ct = default)
    {
        var plan = ApprovalRecoveryPlan.Parse(json);
        if (plan is null || plan.ReceiptHash is not null || plan.AuditHash is not null) return new("invalid");
        var evidence = await ReadEvidence(custody, plan, ct);
        return evidence.Status is "confirmed" or "no_commit" ? new(evidence.Status, plan with { ReceiptHash = evidence.ReceiptHash, AuditHash = evidence.AuditHash }) : new(evidence.Status);
    }
    public async Task<ApprovalRecoveryResult> ApplyAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> json, string approvedHash, CancellationToken ct = default)
    {
        var plan = ApprovalRecoveryPlan.Parse(json);
        if (plan is null || plan.ReceiptHash is null || plan.AuditHash is null || !ApprovalRecoveryPlan.ValidHash(approvedHash)) return new("invalid");
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(plan.Hash()), Convert.FromHexString(approvedHash))) return new("denied");
        var evidence = await ReadEvidence(custody, plan, ct);
        if (evidence.Status is not ("confirmed" or "no_commit")) return new(evidence.Status);
        if (evidence.ReceiptHash != plan.ReceiptHash || evidence.AuditHash != plan.AuditHash) return new("conflict");
        return new(evidence.Status, evidence.CommandId, evidence.IntentId);
    }
    private async Task<Evidence> ReadEvidence(IOfflineProvisioningLease custody, ApprovalRecoveryPlan plan, CancellationToken ct)
    {
        if (custody is null || installation == Guid.Empty || installation != plan.InstallationId) return new("invalid");
        try
        {
            custody.RequireHeld(); if (!processes.IsQuiescent(plan.OldProcessId)) return new("denied");
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10)); var token = budget.Token;
            await using var iam = await identity.CreateDbContextAsync(token); await iam.Database.OpenConnectionAsync(token);
            var lease = new Lease(custody); var connection = iam.Database.GetDbConnection();
            IDbContextFactory<ApprovalDbContext> approvals = new ApprovalFactory(connection, lease);
            IDbContextFactory<ReleaseDbContext> releases = new ReleaseFactory(connection, lease); var audits = new AuditFactory(connection, lease);
            await using var apr = await approvals.CreateDbContextAsync(token);
            var receipt = await new ApprovalRecoveryReceiptReader(apr, lease).FindUnderCustodyAsync(installation, plan.TenantId, plan.PrincipalId,
                plan.Action, Convert.FromHexString(plan.ScopedKeyHash), token);
            var intentId = ReleaseCommandHash.AuditIntentId(plan.ScopedKeyHash);
            var audit = await new AuditAppendStore(audits).LookupAsync(intentId, token);
            if (audit.Status is AuditLookupStatus.Unavailable or AuditLookupStatus.Unknown) return new("unknown");
            // Missing receipts cannot prove no-commit if the installation contains incomplete owner/audit history.
            if (!await new ApprovalSchemaReadiness(installation, approvals, new ReleaseSchemaReadiness(installation, releases)).CheckAsync(token)) return new("unknown");
            if (receipt is null)
            {
                if (audit.Status != AuditLookupStatus.NotFound) return new("unknown");
                custody.RequireHeld(); if (!processes.IsQuiescent(plan.OldProcessId)) return new("denied");
                return new("no_commit", ReceiptHash(plan, null, null, null), AuditHash(intentId, null));
            }
            var route = plan.Resource.Split('/'); var environment = Guid.ParseExact(route[4], "D");
            if (receipt.EnvironmentId != environment || receipt.BootId != plan.BootId || receipt.InvocationId == Guid.Empty ||
                receipt.BusinessHash != plan.BusinessHash || receipt.AuditIntentId != intentId ||
                plan.Action != "request" && receipt.RequestId != Guid.ParseExact(route[6], "D") ||
                audit.Status != AuditLookupStatus.Found || audit.Receipt?.IntentHash != receipt.AuditIntentHash) return new("unknown");
            var owner = await new ApprovalRequestQueryStore(apr).ReadUnderAdmissionAsync(installation, plan.TenantId, receipt.ProjectId,
                environment, receipt.RequestId, token);
            if (owner is null) return new("unknown");
            await using var rel = await releases.CreateDbContextAsync(token);
            var revision = await new ReleaseQueryStore(rel).ReadRevisionUnderAdmissionAsync(installation, plan.TenantId, receipt.ProjectId, owner.Request.ReleaseRevisionId, token);
            if (revision is null || revision.EnvironmentId != environment || revision.SpecHash != Convert.ToHexStringLower(owner.Request.SpecHash)) return new("unknown");
            var permission = plan.Action switch { "request" => "release.request-approval", "revoke" => "release.approval.revoke", _ => "release.approve" };
            var reason = "approval-m-" + ReleaseCommandHash.AuditReason(installation, plan.ScopedKeyHash, plan.BusinessHash)["release-m-".Length..];
            if (await new AuditMutationEvidenceReader(audits).MatchUnderAdmissionAsync(new(intentId, receipt.AuditIntentHash, plan.PrincipalId,
                plan.TenantId, receipt.RequestId, receipt.InvocationId, permission, "environment", environment, reason), token) != AuditEvidenceStatus.Matched) return new("unknown");
            custody.RequireHeld(); if (!processes.IsQuiescent(plan.OldProcessId)) return new("denied");
            return new("confirmed", ReceiptHash(plan, receipt, owner, revision), AuditHash(intentId, receipt.AuditIntentHash), receipt.Id, intentId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new("unknown"); }
    }
    private sealed record Evidence(string Status, string? ReceiptHash = null, string? AuditHash = null, Guid? CommandId = null, Guid? IntentId = null);
    private static string ReceiptHash(ApprovalRecoveryPlan plan, ApprovalRecoveryReceipt? receipt, ApprovalRequestOwnerSnapshot? owner, ReleaseRevisionSnapshot? revision) =>
        Hash("approval-recovery-receipt-v1\n", new
        {
            schemaVersion = 1, plan.InstallationId, plan.TenantId, plan.PrincipalId, plan.BootId, plan.Action, plan.Resource, plan.ScopedKeyHash, plan.BusinessHash,
            receipt, owner, release = revision is null ? null : new { revision.ReleaseId, revision.RevisionId, revision.Version, revision.State, revision.SpecHash }
        });
    private static string AuditHash(Guid intentId, string? hash) => Hash("approval-recovery-audit-v1\n", new { intentId, intentHash = hash });
    private static string Hash<T>(string prefix, T value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(Encoding.UTF8.GetBytes(prefix));
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(value)); return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private sealed class Lease(IOfflineProvisioningLease custody) : IOfflineApprovalCustody, IReleaseOfflineCustody, IOfflineAuditLease
    { public void RequireHeld() => custody.RequireHeld(); }
    private sealed class ApprovalFactory(DbConnection connection, IOfflineApprovalCustody custody) : IDbContextFactory<ApprovalDbContext>
    { public ApprovalDbContext CreateDbContext() => new(ApprovalDbContextFactory.CreateOfflineReceiptReadOptions(connection, custody)); }
    private sealed class ReleaseFactory(DbConnection connection, IReleaseOfflineCustody custody) : IDbContextFactory<ReleaseDbContext>
    { public ReleaseDbContext CreateDbContext() => new(ReleaseDbContextFactory.CreateOfflineReceiptReadOptions(connection, custody)); }
    private sealed class AuditFactory(DbConnection connection, IOfflineAuditLease custody) : IDbContextFactory<AuditDbContext>
    { public AuditDbContext CreateDbContext() => new(AuditDbContextFactory.CreateOfflineReceiptReadOptions(connection, custody)); }
}
