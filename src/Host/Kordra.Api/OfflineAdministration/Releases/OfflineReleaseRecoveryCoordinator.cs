using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed record ReleaseRecoveryInspection(string Status, ReleaseRecoveryPlan? BoundPlan = null);
public sealed record ReleaseRecoveryResult(string Status, Guid? CommandId = null, Guid? IntentId = null);

/// <summary>Stopped custody, exact evidence only. No replay, mutation, provider or credential operation.</summary>
public sealed class OfflineReleaseRecoveryCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identity,
    IArtifactProcessQuiescence processes)
{
    public async Task<ReleaseRecoveryInspection> InspectAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> json,
        CancellationToken ct = default)
    {
        var plan = ReleaseRecoveryPlan.Parse(json);
        if (plan is null || plan.ReceiptHash is not null || plan.AuditHash is not null) return new("invalid");
        var evidence = await ReadEvidence(custody, plan, ct);
        return evidence.Status is "confirmed" or "no_commit"
            ? new(evidence.Status, plan with { ReceiptHash = evidence.ReceiptHash, AuditHash = evidence.AuditHash })
            : new(evidence.Status);
    }
    public async Task<ReleaseRecoveryResult> ApplyAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> json,
        string approvedHash, CancellationToken ct = default)
    {
        var plan = ReleaseRecoveryPlan.Parse(json);
        if (plan is null || plan.ReceiptHash is null || plan.AuditHash is null || !ReleaseRecoveryPlan.ValidHash(approvedHash)) return new("invalid");
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(plan.Hash()), Convert.FromHexString(approvedHash))) return new("denied");
        var evidence = await ReadEvidence(custody, plan, ct);
        if (evidence.Status is not ("confirmed" or "no_commit")) return new(evidence.Status);
        if (plan.ReceiptHash != evidence.ReceiptHash || plan.AuditHash != evidence.AuditHash) return new("conflict");
        return new(evidence.Status, evidence.CommandId, evidence.IntentId);
    }
    private async Task<Evidence> ReadEvidence(IOfflineProvisioningLease custody, ReleaseRecoveryPlan plan, CancellationToken ct)
    {
        if (custody is null || installationId == Guid.Empty || plan.InstallationId != installationId) return new("invalid");
        try
        {
            custody.RequireHeld();
            if (!processes.IsQuiescent(plan.OldProcessId)) return new("denied");
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10));
            await using var iam = await identity.CreateDbContextAsync(budget.Token);
            await iam.Database.OpenConnectionAsync(budget.Token);
            var connection = iam.Database.GetDbConnection();
            var lease = new Lease(custody);
            await using var rel = new ReleaseDbContext(ReleaseDbContextFactory.CreateOfflineReceiptReadOptions(connection, lease));
            var auditFactory = new AuditFactory(connection, lease);
            var project = Guid.ParseExact(plan.Resource.Split('/')[4], "D");
            var receipt = await new ReleaseRecoveryReceiptReader(rel, lease).FindUnderCustodyAsync(plan.InstallationId, plan.TenantId,
                plan.PrincipalId, plan.Action, Convert.FromHexString(plan.ScopedKeyHash), budget.Token);
            var intentId = ReleaseCommandHash.AuditIntentId(plan.ScopedKeyHash);
            var lookup = await new AuditAppendStore(auditFactory).LookupAsync(intentId, budget.Token);
            if (lookup.Status is AuditLookupStatus.Unavailable or AuditLookupStatus.Unknown) return new("unknown");
            if (receipt is null)
            {
                if (lookup.Status != AuditLookupStatus.NotFound) return new("unknown");
                custody.RequireHeld(); if (!processes.IsQuiescent(plan.OldProcessId)) return new("denied");
                return new("no_commit", ReceiptHash(plan, null, null), AuditHash(intentId, null));
            }
            if (receipt.ProjectId != project || receipt.BootId != plan.BootId || receipt.InvocationId is null ||
                receipt.InvocationId == Guid.Empty || receipt.BusinessHash != plan.BusinessHash || receipt.AuditIntentId != intentId ||
                lookup.Status != AuditLookupStatus.Found || lookup.Receipt is null || lookup.Receipt.IntentHash != receipt.AuditIntentHash)
                return new("unknown");
            var revision = await new ReleaseQueryStore(rel).ReadRevisionUnderAdmissionAsync(plan.InstallationId, plan.TenantId,
                project, receipt.RevisionId, budget.Token);
            if (revision is null) return new("unknown");
            var parts = plan.Resource.Split('/');
            if (plan.Action == "next-draft" && Guid.ParseExact(parts[6], "D") != revision.ReleaseId ||
                plan.Action is "replace-draft" or "freeze" or "retire" && Guid.ParseExact(parts[6], "D") != revision.RevisionId)
                return new("unknown");
            var permission = plan.Action == "retire" ? "release.retire" : "release.create";
            var matched = await new AuditMutationEvidenceReader(auditFactory).MatchUnderAdmissionAsync(new(intentId,
                receipt.AuditIntentHash, plan.PrincipalId, plan.TenantId, receipt.RevisionId, receipt.InvocationId.Value, permission, "project", project,
                ReleaseCommandHash.AuditReason(plan.InstallationId, plan.ScopedKeyHash, plan.BusinessHash)), budget.Token);
            if (matched != AuditEvidenceStatus.Matched) return new("unknown");
            custody.RequireHeld(); if (!processes.IsQuiescent(plan.OldProcessId)) return new("denied");
            return new("confirmed", ReceiptHash(plan, receipt, revision), AuditHash(intentId, receipt.AuditIntentHash), receipt.Id, intentId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new("unknown"); }
    }
    private sealed record Evidence(string Status, string? ReceiptHash = null, string? AuditHash = null, Guid? CommandId = null, Guid? IntentId = null);
    private static string ReceiptHash(ReleaseRecoveryPlan plan, ReleaseRecoveryReceipt? row, ReleaseRevisionSnapshot? revision) => Hash("release-recovery-receipt-v1\n", w =>
    {
        w.WriteStartObject(); w.WriteString("action", plan.Action); w.WriteString("bootId", plan.BootId.ToString("D"));
        w.WriteString("businessHash", plan.BusinessHash); w.WriteString("installationId", plan.InstallationId.ToString("D"));
        w.WritePropertyName("owner");
        if (revision is null) w.WriteNullValue();
        else
        {
            w.WriteStartObject(); w.WriteString("applicationId", revision.ApplicationId.ToString("D"));
            w.WriteString("artifactId", revision.ArtifactId.ToString("D")); w.WriteString("createdAtUtc", Format(revision.CreatedAtUtc));
            w.WriteString("creatorPrincipalId", revision.CreatorPrincipalId.ToString("D")); w.WriteString("environmentId", revision.EnvironmentId.ToString("D"));
            if (revision.FrozenAtUtc is { } frozen) w.WriteString("frozenAtUtc", Format(frozen)); else w.WriteNull("frozenAtUtc");
            w.WriteString("label", revision.Label); w.WriteString("releaseId", revision.ReleaseId.ToString("D"));
            if (revision.RetiredAtUtc is { } retired) w.WriteString("retiredAtUtc", Format(retired)); else w.WriteNull("retiredAtUtc");
            if (revision.RetiredReasonCode is { } reason) w.WriteString("retiredReasonCode", reason); else w.WriteNull("retiredReasonCode");
            w.WriteString("revisionId", revision.RevisionId.ToString("D")); w.WriteNumber("revisionNumber", revision.RevisionNumber);
            if (revision.SpecHash is { } hash) w.WriteString("specHash", hash); else w.WriteNull("specHash");
            w.WriteString("state", revision.State); w.WriteString("targetId", revision.TargetId.ToString("D"));
            w.WriteString("updatedAtUtc", Format(revision.UpdatedAtUtc)); w.WriteNumber("version", revision.Version); w.WriteEndObject();
        }
        w.WriteString("principalId", plan.PrincipalId.ToString("D")); w.WritePropertyName("receipt");
        if (row is null) w.WriteNullValue();
        else
        {
            w.WriteStartObject(); w.WriteString("auditIntentHash", row.AuditIntentHash); w.WriteString("auditIntentId", row.AuditIntentId.ToString("D"));
            w.WriteString("commandId", row.Id.ToString("D")); w.WriteString("createdAtUtc", Format(row.CreatedAtUtc));
            w.WriteString("invocationId", row.InvocationId!.Value.ToString("D")); w.WriteString("projectId", row.ProjectId.ToString("D"));
            w.WriteString("revisionId", row.RevisionId.ToString("D")); w.WriteEndObject();
        }
        w.WriteString("resource", plan.Resource); w.WriteString("scopedKeyHash", plan.ScopedKeyHash);
        w.WriteString("tenantId", plan.TenantId.ToString("D")); w.WriteEndObject();
    });
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
    private static string AuditHash(Guid intentId, string? hash) => Hash("release-recovery-audit-v1\n", w =>
    { w.WriteStartObject(); if (hash is null) w.WriteNull("intentHash"); else w.WriteString("intentHash", hash); w.WriteString("intentId", intentId.ToString("D")); w.WriteEndObject(); });
    private static string Hash(string prefix, Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream(); stream.Write(Encoding.UTF8.GetBytes(prefix));
        using (var writer = new Utf8JsonWriter(stream)) write(writer);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
    private sealed class Lease(IOfflineProvisioningLease inner) : IReleaseOfflineCustody, IOfflineAuditLease
    { public void RequireHeld() => inner.RequireHeld(); }
    private sealed class AuditFactory(DbConnection connection, IOfflineAuditLease custody) : IDbContextFactory<AuditDbContext>
    {
        public AuditDbContext CreateDbContext() => new(AuditDbContextFactory.CreateOfflineReceiptReadOptions(connection, custody));
    }
}
