using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Api;

public sealed record ArtifactRecoveryResult(string Status, Guid? RegistrationId = null);

/// <summary>Stopped-installation recovery only. No registry, credential, replay, artifact or declaration writes.</summary>
public sealed class OfflineArtifactRecoveryCoordinator(Guid installation, IDbContextFactory<KordraIdentityDbContext> identity,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock, IArtifactProcessQuiescence processes,
    IArtifactAuditTransactionFactory? auditTransactions = null)
{
    public async Task<ArtifactRecoveryPlan?> InspectAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> json, CancellationToken ct = default)
    {
        custody.RequireHeld(); var plan = ArtifactRecoveryPlan.Parse(json);
        if (plan is null || plan.InstallationId != installation || !processes.IsQuiescent(plan.OldProcessId)) return null;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10)); ct = budget.Token;
        await using var held = await gate.AcquireReadAsync(ct);
        await using var iam = await identity.CreateDbContextAsync(ct);
        ArtifactDbContextFactory.ValidateEnlistedTarget(iam.Database.GetDbConnection(), offline: true);
        await iam.Database.OpenConnectionAsync(ct);
        await using var art = new ArtifactDbContext(ArtifactDbContextFactory.CreateEnlistedOptions(iam.Database.GetDbConnection(), offline: true));
        await using var aud = new AuditDbContext(AuditDbContextFactory.CreateOfflineReceiptReadOptions(iam.Database.GetDbConnection(), new AuditLease(custody)));
        var row = await Selected(art, plan, ct); if (row is null) return null;
        var history = await History(aud, plan, ct);
        if (history.Any(e => e.IntentId == plan.PlanId)) return null;
        var use = history.Where(e => e.Stage == "U").ToArray();
        var auditHash = HistoryHash(use);
        if (row.State == "Pending" && !ValidPendingHistory(row, history) ||
            row.State == "Registered" && (row.ArtifactId is not { } id ||
                !await art.Artifacts.AsNoTracking().AnyAsync(a => a.Id == id && a.TenantId == row.TenantId && a.ProjectId == row.ProjectId, ct) ||
                !ValidRegisteredHistory(row, history, auditHash)) ||
            row.State == "Failed" && !ValidFailedHistory(row, history, auditHash) ||
            row.State is not ("Pending" or "Registered" or "Failed")) return null;
        custody.RequireHeld(); if (!processes.IsQuiescent(plan.OldProcessId)) return null;
        return plan with { ReceiptHash = ReceiptHash(row), AuditHash = auditHash };
    }

    public async Task<ArtifactRecoveryResult> ExecuteAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> json, string approvedHash, CancellationToken ct = default)
    {
        custody.RequireHeld(); var plan = ArtifactRecoveryPlan.Parse(json);
        if (plan is null || plan.InstallationId != installation || !ArtifactRecoveryPlan.ValidHash(plan.ReceiptHash) || !ArtifactRecoveryPlan.ValidHash(plan.AuditHash)) return new("invalid");
        if (approvedHash != plan.Hash() || !processes.IsQuiescent(plan.OldProcessId)) return new("denied");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10)); ct = budget.Token;
        await using var held = await gate.AcquireWriteAsync(ct);
        IArtifactAuditTransaction? audit = null; ArtifactRecoveryResult? confirmed = null;
        try
        {
            await using var iam = await identity.CreateDbContextAsync(ct);
            ArtifactDbContextFactory.ValidateEnlistedTarget(iam.Database.GetDbConnection(), offline: true);
            await using var tx = await iam.Database.BeginTransactionAsync(ct);
            await using var art = new ArtifactDbContext(ArtifactDbContextFactory.CreateEnlistedOptions(iam.Database.GetDbConnection(), offline: true));
            await art.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            await using var lookup = new AuditDbContext(AuditDbContextFactory.CreateOfflineReceiptReadOptions(iam.Database.GetDbConnection(), new AuditLease(custody)));
            await lookup.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            var row = await Selected(art, plan, ct); if (row is null) return Fence();
            var history = await History(lookup, plan, ct);
            if (ReceiptHash(row) != plan.ReceiptHash) return new("conflict");
            var existing = history.Where(x => x.IntentId == plan.PlanId).ToArray();
            var original = history.Where(x => x.IntentId != plan.PlanId).ToArray();
            if (row.State == "Registered")
            {
                if (existing.Length != 0 || row.ArtifactId is not { } artifactId ||
                    !await art.Artifacts.AsNoTracking().AnyAsync(a => a.Id == artifactId && a.TenantId == row.TenantId && a.ProjectId == row.ProjectId, ct) ||
                    !ValidRegisteredHistory(row, original, plan.AuditHash!)) return Fence();
                custody.RequireHeld(); if (!processes.IsQuiescent(plan.OldProcessId)) return new("denied");
                return new("unchanged", row.Id);
            }
            if (row.State == "Abandoned")
            {
                if (existing.Length != 1 || row.CompletedAtUtc is not { } completed || row.SafeReason != "interrupted_attempt" ||
                    HistoryHash(original) != plan.AuditHash || !ValidEntry(existing[0]) ||
                    Convert.ToHexStringLower(existing[0].CanonicalIntentHash) != AuditIntentCanonicalizer.ComputeHash(Intent(plan, row, completed)))
                    return Fence();
                custody.RequireHeld(); if (!processes.IsQuiescent(plan.OldProcessId)) return new("denied");
                return new("success", row.Id);
            }
            if (row.State == "Failed")
            {
                if (existing.Length != 0 || !ValidFailedHistory(row, original, plan.AuditHash!)) return Fence();
                custody.RequireHeld(); if (!processes.IsQuiescent(plan.OldProcessId)) return new("denied");
                return new("unchanged", row.Id);
            }
            if (row.State != "Pending") return Fence();
            if (existing.Length != 0 || !ValidPendingHistory(row, original) || HistoryHash(original) != plan.AuditHash) return Fence();
            var now = clock.GetTrustedUtcNow();
            if (now.Offset != TimeSpan.Zero || now == default || now == DateTimeOffset.MaxValue) return new("unavailable");
            now = new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
            if (now < row.CreatedAtUtc) return new("denied");
            var prepared = await new ArtifactRegistrationStore(art).PrepareAbandonedAsync(installation, plan.RegistrationId, plan.BootId, plan.InvocationId, now, ct);
            if (prepared.Status != "staged_abandoned") return new("conflict");
            audit = await (auditTransactions ?? new OfflineArtifactAuditTransactionFactory(new AuditLease(custody)))
                .EnlistAsync(iam.Database.GetDbConnection(), tx.GetDbTransaction(), ct);
            await using var ownedAudit = audit;
            if ((await audit.StageAsync(Intent(plan, row, now), ct)).Status != AuditTransactionStageStatus.Staged) return new("unavailable");
            await art.SaveChangesAsync(ct); await AuthorityClockFloorStore.StageAsync(iam, installation, now, ct);
            custody.RequireHeld(); if (!processes.IsQuiescent(plan.OldProcessId)) return new("denied");
            var committed = await audit.CommitAsync(ct);
            confirmed = committed.Status == AuditAppendStatus.Durable ? new("success", row.Id) :
                committed.Status == AuditAppendStatus.Unknown && audit.CommitAttempted ? Fence() : new("unavailable");
            return confirmed;
        }
        catch
        {
            if (confirmed is not null) return confirmed;
            return audit?.CommitAttempted == true ? Fence() : new("unavailable");
        }
    }

    private ArtifactRecoveryResult Fence() { gate.Close(); return new("unknown"); }
    private static Task<ArtifactRegistrationRecord?> Selected(ArtifactDbContext db, ArtifactRecoveryPlan plan, CancellationToken ct) =>
        db.Registrations.SingleOrDefaultAsync(r => r.Id == plan.RegistrationId && r.InstallationId == plan.InstallationId &&
            r.BootId == plan.BootId && r.InvocationId == plan.InvocationId, ct);
    private static async Task<AuditEntry[]> History(AuditDbContext db, ArtifactRecoveryPlan plan, CancellationToken ct) =>
        await db.Entries.AsNoTracking().Where(e => e.InvocationId == plan.InvocationId || e.IntentId == plan.PlanId).OrderBy(e => e.IntentId).Take(9).ToArrayAsync(ct);
    private static bool ValidPendingHistory(ArtifactRegistrationRecord row, AuditEntry[] entries) =>
        entries.Length is >= 1 and <= 2 && row.ArtifactId is null && row.CompletedAtUtc is null && row.AttemptVerifiedAtUtc is null &&
        entries.All(e => ValidEntry(e) && e.InvocationId == row.InvocationId && e.SubjectRef == row.PrincipalId && e.TenantRef == row.TenantId &&
            e.ResourceRef == row.Id && e.Permission == "artifact.register" && e.ScopeKind == "project" && e.ScopeId == row.ProjectId &&
            e.Stage == "U" && (e.SafeReason.StartsWith("artifact-u-", StringComparison.Ordinal) || e.SafeReason.StartsWith("artifact-c-", StringComparison.Ordinal))) &&
        entries.Count(e => e.SafeReason.StartsWith("artifact-u-", StringComparison.Ordinal)) == 1 &&
        entries.Count(e => e.SafeReason.StartsWith("artifact-c-", StringComparison.Ordinal)) <= 1;
    private static bool ValidEntry(AuditEntry entry) => entry.CanonicalIntentHash.AsSpan().SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(entry.CanonicalIntent)));
    private static bool ValidRegisteredHistory(ArtifactRegistrationRecord row, AuditEntry[] entries, string expectedPriorHash)
    {
        if (entries.Length is < 3 or > 4 || row.ArtifactId is null || row.CompletedAtUtc is null || row.AttemptVerifiedAtUtc is null) return false;
        var use = entries.Where(e => e.Stage == "U").ToArray();
        var mutation = entries.Where(e => e.Stage == "mutation").ToArray();
        var disclosure = entries.Where(e => e.Stage == "D").ToArray();
        return use.Length == 2 && mutation.Length == 1 && disclosure.Length <= 1 && use.Length + mutation.Length + disclosure.Length == entries.Length &&
            use.Count(e => e.SafeReason.StartsWith("artifact-u-", StringComparison.Ordinal)) == 1 &&
            use.Count(e => e.SafeReason.StartsWith("artifact-c-", StringComparison.Ordinal)) == 1 &&
            use.All(e => Matches(e, row, row.Id)) && HistoryHash(use) == expectedPriorHash &&
            Matches(mutation[0], row, row.ArtifactId.Value) && mutation[0].SafeReason.StartsWith("artifact-m-", StringComparison.Ordinal) &&
            disclosure.All(e => Matches(e, row, row.ArtifactId.Value) && e.SafeReason.StartsWith("artifact-d-", StringComparison.Ordinal));
    }
    private static bool ValidFailedHistory(ArtifactRegistrationRecord row, AuditEntry[] entries, string expectedPriorHash)
    {
        if (entries.Length is < 2 or > 4 || row.ArtifactId is not null || row.CompletedAtUtc is null) return false;
        var use = entries.Where(e => e.Stage == "U").ToArray();
        var outcome = entries.Where(e => e.Stage == "outcome").ToArray();
        var disclosure = entries.Where(e => e.Stage == "D").ToArray();
        return use.Length is >= 1 and <= 2 && outcome.Length == 1 && disclosure.Length <= 1 && use.Length + outcome.Length + disclosure.Length == entries.Length &&
            use.Count(e => e.SafeReason.StartsWith("artifact-u-", StringComparison.Ordinal)) == 1 &&
            use.Count(e => e.SafeReason.StartsWith("artifact-c-", StringComparison.Ordinal)) == use.Length - 1 &&
            use.All(e => Matches(e, row, row.Id)) && HistoryHash(use) == expectedPriorHash &&
            Matches(outcome[0], row, row.Id) && outcome[0].SafeReason.StartsWith("artifact-failed-", StringComparison.Ordinal) &&
            disclosure.All(e => Matches(e, row, row.Id) && e.SafeReason.StartsWith("artifact-failure-d-", StringComparison.Ordinal));
    }
    private static bool Matches(AuditEntry entry, ArtifactRegistrationRecord row, Guid resource) =>
        ValidEntry(entry) && entry.InvocationId == row.InvocationId && entry.SubjectRef == row.PrincipalId && entry.TenantRef == row.TenantId &&
        entry.ResourceRef == resource && entry.Permission == "artifact.register" && entry.ScopeKind == "project" && entry.ScopeId == row.ProjectId;
    private static string ReceiptHash(ArtifactRegistrationRecord row) => Hash("artifact-recovery-receipt-v1\n", JsonSerializer.SerializeToUtf8Bytes(new
    { row.Id, row.InstallationId, row.TenantId, row.ProjectId, row.PrincipalId, row.BootId, row.InvocationId, row.KeyHash, row.BusinessHash, row.IdentityHash, row.RequestJson, row.CreatedAtUtc }));
    private static string HistoryHash(AuditEntry[] entries) => Hash("artifact-recovery-audit-v1\n", JsonSerializer.SerializeToUtf8Bytes(
        entries.OrderBy(e => e.IntentId).Select(e => new { e.IntentId, hash = Convert.ToHexStringLower(e.CanonicalIntentHash) })));
    private static string Hash(string domain, byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(domain).Concat(bytes).ToArray()));
    private static AuditIntent Intent(ArtifactRecoveryPlan plan, ArtifactRegistrationRecord row, DateTimeOffset occurred) => new(1, plan.PlanId,
        row.InvocationId, AuditIntentStage.Outcome, null, row.TenantId, row.Id, "artifact.recover", "project", row.ProjectId, null, null,
        "artifact-abandoned-" + plan.Hash(), occurred, plan.PlanId, ActionOutcome: AuditActionOutcome.Cancelled);
    private sealed class AuditLease(IOfflineProvisioningLease custody) : IOfflineAuditLease { public void RequireHeld() => custody.RequireHeld(); }
}

public sealed class OfflineArtifactAuditTransactionFactory(IOfflineAuditLease custody) : IArtifactAuditTransactionFactory
{
    public async ValueTask<IArtifactAuditTransaction> EnlistAsync(System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction, CancellationToken ct) =>
        new Enlisted(await AuditTransactionContext.CreateOfflineAsync(connection, transaction, custody, ct));
    private sealed class Enlisted(AuditTransactionContext inner) : IArtifactAuditTransaction
    {
        public bool CommitAttempted => inner.CommitAttempted;
        public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct) => inner.StageAsync(intent, ct);
        public Task<AuditAppendOutcome> CommitAsync(CancellationToken ct) => inner.CommitAsync(ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
