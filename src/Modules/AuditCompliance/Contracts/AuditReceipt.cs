namespace Kordra.Modules.AuditCompliance.Contracts;

/// <summary>A durable reference to one persisted intent; it is not proof that protected work completed.</summary>
public sealed record AuditReceipt(
    Guid RecordId,
    Guid IntentId,
    string IntentHash,
    DateTimeOffset RecordedAtUtc);
