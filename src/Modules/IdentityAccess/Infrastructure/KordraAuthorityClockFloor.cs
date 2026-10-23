namespace Kordra.Modules.IdentityAccess.Infrastructure;

/// <summary>Installation-scoped nondecreasing UTC floor, committed with IAM-owned audited state.</summary>
public sealed class KordraAuthorityClockFloor
{
    public Guid InstallationId { get; set; }
    public DateTimeOffset AdmittedUtc { get; set; }
}
