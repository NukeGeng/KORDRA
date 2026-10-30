using System.Security.Cryptography;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ApprovalGovernance;

/// <summary>A candidate is owner evidence only; the caller must independently verify AUD and current authorization.</summary>
public sealed record ApprovalRequestReceiptResult(string Status, Guid? RequestId = null, Guid? AuditIntentId = null,
    byte[]? AuditIntentHash = null, Guid? BootId = null, Guid? InvocationId = null);

public sealed class ApprovalRequestReceiptStore(ApprovalDbContext db)
{
    public async Task<ApprovalRequestReceiptResult> FindUnderAdmissionAsync(Guid installationId, Guid tenantId,
        Guid projectId, Guid environmentId, Guid principalId, string action, byte[] scopedKeyHash, byte[] businessHash,
        CancellationToken ct = default)
    {
        Validate(installationId, tenantId, projectId, environmentId, principalId, action, scopedKeyHash, businessHash);
        var row = await db.RequestCommands.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
            x.TenantId == tenantId && x.PrincipalId == principalId && x.Action == action && x.ScopedKeyHash == scopedKeyHash, ct);
        if (row is null) return new("absent");
        if (!CryptographicOperations.FixedTimeEquals(row.BusinessHash, businessHash)) return new("conflict");
        var owner = await db.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.RequestId &&
            x.InstallationId == installationId && x.TenantId == tenantId, ct);
        if (owner is null) return new("inconsistent");
        if (owner.ProjectId != projectId || owner.EnvironmentId != environmentId) return new("conflict");
        if (row.AuditIntentHash is not { Length: 32 } || row.AuditIntentId == Guid.Empty || row.BootId == Guid.Empty || row.InvocationId == Guid.Empty)
            return new("inconsistent");
        return new("candidate", row.RequestId, row.AuditIntentId, (byte[])row.AuditIntentHash.Clone(), row.BootId, row.InvocationId);
    }

    public async Task StageAsync(Guid receiptId, Guid installationId, Guid tenantId, Guid projectId, Guid environmentId,
        Guid principalId, Guid requestId, Guid bootId, Guid invocationId, string action, byte[] scopedKeyHash,
        byte[] businessHash, Guid auditIntentId, byte[] auditIntentHash, DateTimeOffset now, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("APR receipt requires the caller's shared transaction.");
        Validate(installationId, tenantId, projectId, environmentId, principalId, action, scopedKeyHash, businessHash);
        if (receiptId == Guid.Empty || requestId == Guid.Empty || bootId == Guid.Empty || invocationId == Guid.Empty ||
            auditIntentId == Guid.Empty || auditIntentHash is not { Length: 32 } || now == default || now == DateTimeOffset.MaxValue || now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid APR receipt evidence.");
        var stagedOwner = db.Requests.Local.SingleOrDefault(x => x.Id == requestId && x.InstallationId == installationId && x.TenantId == tenantId);
        var owner = stagedOwner ?? await db.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId &&
            x.InstallationId == installationId && x.TenantId == tenantId, ct);
        if (owner is null || owner.ProjectId != projectId || owner.EnvironmentId != environmentId)
            throw new ApprovalRequestConflictException("APR receipt owner scope mismatch.");
        db.RequestCommands.Add(new ApprovalRequestCommandReceipt { Id = receiptId, InstallationId = installationId,
            TenantId = tenantId, PrincipalId = principalId, RequestId = requestId, BootId = bootId, InvocationId = invocationId,
            Action = action, ScopedKeyHash = (byte[])scopedKeyHash.Clone(), BusinessHash = (byte[])businessHash.Clone(),
            AuditIntentId = auditIntentId, AuditIntentHash = (byte[])auditIntentHash.Clone(), CreatedAtUtc = now });
    }

    private static void Validate(Guid installationId, Guid tenantId, Guid projectId, Guid environmentId, Guid principalId,
        string action, byte[] key, byte[] body)
    {
        if (installationId == Guid.Empty || tenantId == Guid.Empty || projectId == Guid.Empty || environmentId == Guid.Empty ||
            principalId == Guid.Empty || action is not ("request" or "grant" or "reject" or "revoke") ||
            key is not { Length: 32 } || body is not { Length: 32 }) throw new ArgumentException("Invalid APR receipt identity.");
    }
}
