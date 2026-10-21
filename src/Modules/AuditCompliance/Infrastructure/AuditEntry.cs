using Kordra.Modules.AuditCompliance.Contracts;

namespace Kordra.Modules.AuditCompliance.Infrastructure;

/// <summary>Immutable persisted AUD metadata. No mutable operation result or payload is modeled.</summary>
public sealed class AuditEntry
{
    private AuditEntry() { }

    public Guid RecordId { get; private set; }
    public int SchemaVersion { get; private set; }
    public Guid IntentId { get; private set; }
    public Guid InvocationId { get; private set; }
    public string Stage { get; private set; } = string.Empty;
    public Guid? SubjectRef { get; private set; }
    public Guid? TenantRef { get; private set; }
    public Guid? ResourceRef { get; private set; }
    public string Permission { get; private set; } = string.Empty;
    public string ScopeKind { get; private set; } = string.Empty;
    public Guid? ScopeId { get; private set; }
    public long? PolicyRevision { get; private set; }
    public long? AuthorityRevision { get; private set; }
    public string SafeReason { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string? ResultDigest { get; private set; }
    public long? ResultSizeBytes { get; private set; }
    public string CanonicalIntent { get; private set; } = string.Empty;
    public byte[] CanonicalIntentHash { get; private set; } = [];
    public DateTimeOffset RecordedAtUtc { get; private set; }

    internal static AuditEntry Create(CanonicalizedAuditIntent canonical, DateTimeOffset recordedAtUtc) => new()
    {
        RecordId = Guid.NewGuid(),
        SchemaVersion = canonical.Intent.SchemaVersion,
        IntentId = canonical.Intent.IntentId,
        InvocationId = canonical.Intent.InvocationId,
        Stage = canonical.StageCode,
        SubjectRef = canonical.Intent.SubjectRef,
        TenantRef = canonical.Intent.TenantRef,
        ResourceRef = canonical.Intent.ResourceRef,
        Permission = canonical.Intent.Permission,
        ScopeKind = canonical.Intent.ScopeKind,
        ScopeId = canonical.Intent.ScopeId,
        PolicyRevision = canonical.Intent.PolicyRevision,
        AuthorityRevision = canonical.Intent.AuthorityRevision,
        SafeReason = canonical.Intent.SafeReason,
        OccurredAtUtc = canonical.Intent.OccurredAtUtc,
        CorrelationId = canonical.Intent.CorrelationId,
        ResultDigest = canonical.Intent.ResultDigest,
        ResultSizeBytes = canonical.Intent.ResultSizeBytes,
        CanonicalIntent = canonical.CanonicalJson,
        CanonicalIntentHash = [.. canonical.Hash],
        // PostgreSQL timestamps preserve microseconds, while .NET can supply 100 ns ticks.
        // Return exactly the value that will survive a database round trip.
        RecordedAtUtc = new DateTimeOffset(
            recordedAtUtc.UtcTicks - recordedAtUtc.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero)
    };

    internal AuditReceipt ToReceipt() => new(
        RecordId,
        IntentId,
        Convert.ToHexString(CanonicalIntentHash).ToLowerInvariant(),
        RecordedAtUtc);
}
