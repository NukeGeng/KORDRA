using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ApprovalGovernance;

/// <summary>Private stopped-custody receipt facts. Never a replay or grant capability.</summary>
public sealed record ApprovalRecoveryReceipt(Guid Id, Guid InstallationId, Guid TenantId, Guid ProjectId, Guid EnvironmentId,
    Guid PrincipalId, Guid BootId, Guid InvocationId, Guid RequestId, string Action, string ScopedKeyHash,
    string BusinessHash, Guid AuditIntentId, string AuditIntentHash, DateTimeOffset CreatedAtUtc);

public sealed class ApprovalRecoveryReceiptReader(ApprovalDbContext db, IOfflineApprovalCustody custody)
{
    public async Task<ApprovalRecoveryReceipt?> FindUnderCustodyAsync(Guid installation, Guid tenant, Guid principal,
        string action, byte[] key, CancellationToken ct = default)
    {
        custody.RequireHeld();
        if (installation == Guid.Empty || tenant == Guid.Empty || principal == Guid.Empty ||
            action is not ("request" or "grant" or "reject" or "revoke") || key is not { Length: 32 })
            throw new ArgumentException("Invalid approval recovery selection.");
        var row = await db.RequestCommands.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installation &&
            x.TenantId == tenant && x.PrincipalId == principal && x.Action == action && x.ScopedKeyHash == key, ct);
        if (row is null) { custody.RequireHeld(); return null; }
        var owner = await db.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.RequestId && x.InstallationId == installation && x.TenantId == tenant, ct);
        if (owner is null) throw new InvalidDataException("Approval receipt has no matching owner.");
        custody.RequireHeld();
        return new(row.Id, row.InstallationId, row.TenantId, owner.ProjectId, owner.EnvironmentId, row.PrincipalId,
            row.BootId, row.InvocationId, row.RequestId, row.Action, Convert.ToHexStringLower(row.ScopedKeyHash),
            Convert.ToHexStringLower(row.BusinessHash), row.AuditIntentId, Convert.ToHexStringLower(row.AuditIntentHash), row.CreatedAtUtc);
    }
}
