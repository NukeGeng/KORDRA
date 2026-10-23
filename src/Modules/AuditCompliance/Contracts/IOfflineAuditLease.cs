namespace Kordra.Modules.AuditCompliance.Contracts;

/// <summary>Trusted offline composition retains the canonical installation lock with API stopped.</summary>
public interface IOfflineAuditLease
{
    void RequireHeld();
}
