using System.Security.Cryptography;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ReleaseManagement;

/// <summary>Owner-only idempotency evidence. It is not a permit or proof of audit/commit.</summary>
public sealed record ReleaseReceiptResult(string Status, Guid? RevisionId, Guid? AuditIntentId, byte[]? AuditIntentHash, Guid? BootId = null, Guid? InvocationId = null);

/// <summary>Host independently validates admission and matches the AUD receipt before reporting any result.</summary>
public sealed class ReleaseReceiptStore(ReleaseDbContext db)
{
    public async Task<ReleaseReceiptResult> FindUnderAdmissionAsync(Guid installationId, Guid tenantId, Guid projectId,
        Guid principalId, string action, byte[] scopedKeyHash, byte[] businessHash, CancellationToken ct = default)
    {
        Validate(installationId, tenantId, projectId, principalId, action, scopedKeyHash, businessHash);
        var row = await db.Commands.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
            x.TenantId == tenantId && x.PrincipalId == principalId && x.Action == action && x.ScopedKeyHash == scopedKeyHash, ct);
        if (row is null) return new("absent", null, null, null);
        if (row.ProjectId != projectId || !CryptographicOperations.FixedTimeEquals(row.BusinessHash, businessHash))
            return new("conflict", null, null, null);
        return new("candidate", row.ReleaseRevisionId, row.AuditIntentId, (byte[])row.AuditIntentHash.Clone(), row.BootId, row.InvocationId);
    }

    public void Stage(Guid receiptId, Guid installationId, Guid tenantId, Guid projectId, Guid principalId,
        Guid revisionId, Guid bootId, Guid invocationId, string action, byte[] scopedKeyHash, byte[] businessHash, Guid auditIntentId,
        byte[] auditIntentHash, DateTimeOffset now)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("REL receipt requires the caller's shared transaction.");
        Validate(installationId, tenantId, projectId, principalId, action, scopedKeyHash, businessHash);
        if (receiptId == Guid.Empty || revisionId == Guid.Empty || bootId == Guid.Empty || invocationId == Guid.Empty || auditIntentId == Guid.Empty || auditIntentHash is not { Length: 32 } ||
            now == default || now.Offset != TimeSpan.Zero) throw new ArgumentException("Invalid receipt evidence.");
        db.Commands.Add(new ReleaseCommandReceipt
        {
            Id = receiptId,
            InstallationId = installationId,
            TenantId = tenantId,
            ProjectId = projectId,
            PrincipalId = principalId,
            BootId = bootId,
            InvocationId = invocationId,
            ReleaseRevisionId = revisionId,
            Action = action,
            ScopedKeyHash = (byte[])scopedKeyHash.Clone(),
            BusinessHash = (byte[])businessHash.Clone(),
            AuditIntentId = auditIntentId,
            AuditIntentHash = (byte[])auditIntentHash.Clone(),
            CreatedAtUtc = now
        });
    }

    private static void Validate(Guid installationId, Guid tenantId, Guid projectId, Guid principalId,
        string action, byte[] keyHash, byte[] bodyHash)
    {
        if (installationId == Guid.Empty || tenantId == Guid.Empty || projectId == Guid.Empty || principalId == Guid.Empty ||
            action is not ("create-draft" or "next-draft" or "replace-draft" or "freeze" or "retire") ||
            keyHash is not { Length: 32 } || bodyHash is not { Length: 32 }) throw new ArgumentException("Invalid scoped receipt identity.");
    }
}
