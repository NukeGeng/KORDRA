using Kordra.Modules.ReleaseManagement.Contracts;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ReleaseManagement;

/// <summary>Immutable nonsecret receipt facts for stopped-custody reconciliation only.</summary>
public sealed record ReleaseRecoveryReceipt(Guid Id, Guid InstallationId, Guid TenantId, Guid ProjectId,
    Guid PrincipalId, Guid? BootId, Guid? InvocationId, Guid RevisionId, string Action, string ScopedKeyHash,
    string BusinessHash, Guid AuditIntentId, string AuditIntentHash, DateTimeOffset CreatedAtUtc);

public sealed class ReleaseRecoveryReceiptReader(ReleaseDbContext db, IReleaseOfflineCustody custody)
{
    public async Task<ReleaseRecoveryReceipt?> FindUnderCustodyAsync(Guid installationId, Guid tenantId, Guid principalId,
        string action, byte[] scopedKeyHash, CancellationToken ct = default)
    {
        custody.RequireHeld();
        if (installationId == Guid.Empty || tenantId == Guid.Empty || principalId == Guid.Empty ||
            string.IsNullOrEmpty(action) || scopedKeyHash is not { Length: 32 }) throw new ArgumentException("Invalid recovery selection.");
        var row = await db.Commands.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
            x.TenantId == tenantId && x.PrincipalId == principalId && x.Action == action && x.ScopedKeyHash == scopedKeyHash, ct);
        custody.RequireHeld();
        return row is null ? null : new(row.Id, row.InstallationId, row.TenantId, row.ProjectId, row.PrincipalId,
            row.BootId, row.InvocationId, row.ReleaseRevisionId, row.Action, Convert.ToHexStringLower(row.ScopedKeyHash),
            Convert.ToHexStringLower(row.BusinessHash), row.AuditIntentId, Convert.ToHexStringLower(row.AuditIntentHash), row.CreatedAtUtc);
    }
}
