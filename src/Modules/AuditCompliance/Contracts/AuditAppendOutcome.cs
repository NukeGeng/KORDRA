namespace Kordra.Modules.AuditCompliance.Contracts;

public enum AuditAppendStatus
{
    Durable,
    Rejected,
    Unavailable,
    Unknown
}

/// <summary>Append result. A durable result is returned only after the database confirms commit.</summary>
public sealed record AuditAppendOutcome(
    AuditAppendStatus Status,
    AuditReceipt? Receipt,
    string? ReasonCode)
{
    public static AuditAppendOutcome Durable(AuditReceipt receipt) =>
        new(AuditAppendStatus.Durable, receipt, null);

    public static AuditAppendOutcome Rejected(string reasonCode) =>
        new(AuditAppendStatus.Rejected, null, reasonCode);

    public static AuditAppendOutcome Unavailable() =>
        new(AuditAppendStatus.Unavailable, null, null);

    public static AuditAppendOutcome Unknown() =>
        new(AuditAppendStatus.Unknown, null, null);
}

public enum AuditLookupStatus
{
    Found,
    NotFound,
    Unavailable,
    Unknown
}

/// <summary>Exact intent-id lookup. Absence does not establish whether protected work occurred.</summary>
public sealed record AuditLookupOutcome(
    AuditLookupStatus Status,
    AuditReceipt? Receipt)
{
    public static AuditLookupOutcome Found(AuditReceipt receipt) => new(AuditLookupStatus.Found, receipt);
    public static AuditLookupOutcome NotFound() => new(AuditLookupStatus.NotFound, null);
    public static AuditLookupOutcome Unavailable() => new(AuditLookupStatus.Unavailable, null);
    public static AuditLookupOutcome Unknown() => new(AuditLookupStatus.Unknown, null);
}
