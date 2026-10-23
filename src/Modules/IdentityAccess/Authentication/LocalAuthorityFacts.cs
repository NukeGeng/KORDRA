using System.Security.Cryptography;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.IdentityAccess.Authentication;

public sealed partial class LocalAuthenticationService
{
    public async Task InitializeOfflineClockAsync(IOfflineProvisioningLease custody, CancellationToken ct = default)
    {
        custody.RequireHeld();
        await using var admission = await _admission.AcquireWriteAsync(ct);
        await using var db = await _contexts.CreateDbContextAsync(ct);
        var floor = await db.AuthorityClockFloors.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == _configuration.InstallationId, ct);
        custody.RequireHeld();
        if (floor is not null)
        {
            try { _clock.AdoptPersistedFloor(floor.AdmittedUtc); }
            catch { _admission.Close(); throw; }
        }
    }
    /// <summary>Trusted composition already holds common admission. Does not nest a lock or refresh authority.</summary>
    public async Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default)
    {
        if (selection is null || selection.PrincipalId == Guid.Empty || selection.BootGeneration != _configuration.BootGeneration ||
            !TryDecodeSessionReference(selection.Reference, out var reference)) return null;
        byte[]? hash = null;
        try
        {
            var now = GetTrustedUtcNow(); hash = SHA256.HashData(reference);
            await using var db = await _contexts.CreateDbContextAsync(ct);
            var row = await db.Sessions.AsNoTracking().Where(s => s.PrincipalId == selection.PrincipalId && s.BootGeneration == selection.BootGeneration && s.SessionReferenceHash == hash)
                .Select(s => new { s.SessionId, s.PrincipalId, s.BootGeneration, s.PrincipalRevision, s.AbsoluteExpiresAtUtc, s.LastActivityAtUtc,
                    s.IsRevoked, CurrentRevision = s.Principal!.Revision, Status = s.Principal.AccountStatus }).SingleOrDefaultAsync(ct);
            if (row is null || row.IsRevoked || row.Status != "active" || row.PrincipalRevision != row.CurrentRevision ||
                row.AbsoluteExpiresAtUtc <= now || row.LastActivityAtUtc + _configuration.IdleLifetime <= now) return null;
            return new(row.PrincipalId, row.SessionId, row.BootGeneration, row.PrincipalRevision, row.AbsoluteExpiresAtUtc, row.LastActivityAtUtc + _configuration.IdleLifetime);
        }
        finally { CryptographicOperations.ZeroMemory(reference); if (hash is not null) CryptographicOperations.ZeroMemory(hash); }
    }

    public async Task<AuthorityCommitStatus> CommitUnderAdmissionAsync(AuthorityCommitment c, CancellationToken ct = default)
    {
        if (c is null || c.IntentId == Guid.Empty || c.InvocationId == Guid.Empty || c.SubjectId == Guid.Empty || c.TenantId == Guid.Empty || c.TargetId == Guid.Empty ||
            c.ScopeId == Guid.Empty || c.Stage is not (AuthorityStage.Use or AuthorityStage.Disclosure) || c.PolicyRevision <= 0 || c.AuthorityRevision <= 0 ||
            c.BindingDigest.Length != 64 || c.BindingDigest.Any(x => !char.IsAsciiHexDigitLower(x)) ||
            !FoundationPermissions.IsValid(new("target.read", c.ScopeKind))) return AuthorityCommitStatus.Rejected;
        var protocolEntered = false;
        AuditAppendOutcome? resolved = null;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(1));
            await using var db = await _contexts.CreateDbContextAsync(budget.Token);
            await using var tx = await db.Database.BeginTransactionAsync(budget.Token);
            var intent = new AuditIntent(1, c.IntentId, c.InvocationId,
                c.Stage == AuthorityStage.Use ? AuditIntentStage.Use : AuditIntentStage.Disclosure,
                c.SubjectId, c.TenantId, c.TargetId, "target.read", c.ScopeKind, c.ScopeId, c.PolicyRevision, c.AuthorityRevision,
                "authority-" + c.BindingDigest, c.OccurredAtUtc, c.InvocationId, c.ResultDigest, c.ResultSize);
            resolved = await CommitWithAuditAsync(db, tx, intent, _ => Task.CompletedTask, budget.Token, () => protocolEntered = true);
        }
        catch when (resolved is not null) { /* Resolved commit remains resolved if later context disposal fails. */ }
        catch when (!protocolEntered) { return AuthorityCommitStatus.Unavailable; }
        catch { _admission.Close(); return AuthorityCommitStatus.Unknown; }
        return resolved!.Status switch
        {
            AuditAppendStatus.Durable => AuthorityCommitStatus.Durable,
            AuditAppendStatus.Unknown => AuthorityCommitStatus.Unknown,
            AuditAppendStatus.Rejected => AuthorityCommitStatus.Rejected,
            _ => AuthorityCommitStatus.Unavailable
        };
    }

    private Task PersistClockFloorAsync(KordraIdentityDbContext db, DateTimeOffset now, CancellationToken ct) =>
        AuthorityClockFloorStore.StageAsync(db, _configuration.InstallationId, now, ct);
}
