namespace Kordra.Modules.IdentityAccess.Infrastructure;

/// <summary>Persisted server-side session metadata; the browser reference itself is never stored.</summary>
public sealed class KordraSession
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public byte[] SessionReferenceHash { get; set; } = [];
    public Guid PrincipalId { get; set; }
    public long PrincipalRevision { get; set; }
    public Guid BootGeneration { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastActivityAtUtc { get; set; }
    public DateTimeOffset AbsoluteExpiresAtUtc { get; set; }
    public bool IsRevoked { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? RevocationReason { get; set; }

    public KordraIdentityUser? Principal { get; set; }
}
