using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.IdentityAccess.Authentication;

public enum WorkflowDelegationSessionStatus { Current, Revoked, Unavailable }

/// <summary>
/// 12.3b (D2): a delegation outlives idle/absolute browser-session expiry but not an explicit revocation (logout, security
/// invalidation) of the session it was created from, nor a different boot generation. Never loads the session reference.
/// </summary>
public sealed class WorkflowDelegationSessionReader(IDbContextFactory<KordraIdentityDbContext> contexts)
{
    public async Task<WorkflowDelegationSessionStatus> ReadUnderAdmissionAsync(Guid sessionId, Guid principalId, Guid bootGeneration,
        CancellationToken ct = default)
    {
        if (sessionId == Guid.Empty || principalId == Guid.Empty || bootGeneration == Guid.Empty)
            return WorkflowDelegationSessionStatus.Unavailable;
        await using var db = await contexts.CreateDbContextAsync(ct);
        var row = await db.Sessions.AsNoTracking().Where(x => x.SessionId == sessionId)
            .Select(x => new { x.PrincipalId, x.BootGeneration, x.IsRevoked }).SingleOrDefaultAsync(ct);
        if (row is null || row.PrincipalId != principalId || row.BootGeneration != bootGeneration || row.IsRevoked)
            return WorkflowDelegationSessionStatus.Revoked;
        return WorkflowDelegationSessionStatus.Current;
    }
}
