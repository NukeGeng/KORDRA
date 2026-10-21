namespace Kordra.Modules.IdentityAccess.Infrastructure;

/// <summary>Nonsecret idempotency receipt for one self-service authentication command.</summary>
public sealed class KordraAuthenticationCommand
{
    public Guid CommandId { get; set; } = Guid.NewGuid();
    public byte[] IdempotencyKeyHash { get; set; } = [];
    public Guid PrincipalId { get; set; }
    public string CommandKind { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
