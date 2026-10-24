using Kordra.Modules.ConnectionManagement.Commands;
using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ConnectionManagement;

/// <summary>SEC owner metadata only. No Keychain calls and no authority inferred from a reference.</summary>
public sealed class CredentialBindingStore(CredentialDbContext db) : ICredentialBindingReader
{
    public async Task<CredentialBindingSnapshotV1?> ReadCurrentUnderAdmissionAsync(CredentialSelectionV1 s, CancellationToken ct = default)
    {
        if (s is null || !s.IsValid) return null;
        var head = await db.Heads.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == s.InstallationId && x.TenantId == s.TenantId &&
            x.TargetId == s.TargetId && x.ConsumerId == s.ConsumerId && x.Operation == s.Operation, ct);
        if (head is null) return null;
        var row = await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == s.InstallationId && x.TenantId == s.TenantId && x.BindingId == head.BindingId && x.Revision == head.Revision, ct);
        if (row is null) return null;
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == s.InstallationId && x.TenantId == s.TenantId && x.ProfileId == row.ProfileId, ct);
        if (profile is null || profile.TargetId != s.TargetId || profile.ConsumerId != s.ConsumerId || profile.Operation != s.Operation) return null;
        return new(1, s, row.BindingId, row.Revision, row.ProfileId, row.ReferenceId, profile.ProviderKind, profile.CredentialFormat, row.State, row.NotBeforeUtc, row.ExpiresAtUtc);
    }
    public async Task<Guid?> ReadLocatorUnderAdmissionAsync(CredentialBindingSnapshotV1 selected, CancellationToken ct)
    {
        var now = await ReadCurrentUnderAdmissionAsync(selected.Selection, ct);
        if (now is null || now.Digest() != selected.Digest()) return null;
        return await db.References.AsNoTracking().Where(x => x.InstallationId == selected.Selection.InstallationId && x.TenantId == selected.Selection.TenantId &&
            x.ProfileId == selected.ProfileId && x.ReferenceId == selected.ReferenceId).Select(x => (Guid?)x.LocatorId).SingleOrDefaultAsync(ct);
    }
    public async Task<CredentialBindingSnapshotV1?> StageAsync(IOfflineProvisioningLease custody, CredentialManifest m, DateTimeOffset now, CancellationToken ct)
    {
        custody.RequireHeld(); if (!m.IsValid() || !m.IsActiveAt(now)) return null;
        var s = m.Selection;
        var head = await db.Heads.SingleOrDefaultAsync(x => x.InstallationId == s.InstallationId && x.TenantId == s.TenantId && x.TargetId == s.TargetId && x.ConsumerId == s.ConsumerId && x.Operation == s.Operation, ct);
        var previous = head is null ? null : await ReadCurrentUnderAdmissionAsync(s, ct);
        if (head is null ? m.ExpectedRevision != 0 || m.ExpectedBindingId is not null || m.Action != "create" : previous is null || head.BindingId != m.ExpectedBindingId || head.Revision != m.ExpectedRevision) return null;
        var revision = checked((head?.Revision ?? 0) + 1);
        Guid profile, reference; DateTimeOffset from, until; string state;
        if (m.Action == "create")
        {
            if (previous is not null && previous.State != "revoked") return null;
            if (await db.Revisions.AnyAsync(x => x.InstallationId == s.InstallationId && x.BindingId == m.BindingId, ct) ||
                await db.Profiles.AnyAsync(x => x.InstallationId == s.InstallationId && x.ProfileId == m.ProfileId, ct)) return null;
            profile = m.ProfileId!.Value;
            db.Profiles.Add(new() { InstallationId = s.InstallationId, TenantId = s.TenantId, ProfileId = profile, TargetId = s.TargetId, ConsumerId = s.ConsumerId, Operation = s.Operation });
        }
        else
        {
            if (previous is null || previous.State == "revoked" || previous.BindingId != m.BindingId || m.Action == "rotate" && previous.ProfileId != m.ProfileId) return null;
            profile = previous.ProfileId;
        }
        if (m.Action == "revoke") { reference = previous!.ReferenceId; from = previous.NotBeforeUtc; until = previous.ExpiresAtUtc; state = "revoked"; }
        else
        {
            reference = m.ReferenceId!.Value;
            if (await db.References.AnyAsync(x => x.InstallationId == s.InstallationId && x.ReferenceId == reference, ct)) return null;
            db.References.Add(new() { InstallationId = s.InstallationId, TenantId = s.TenantId, ProfileId = profile, ReferenceId = reference, LocatorId = m.LocatorId!.Value });
            from = m.NotBeforeUtc!.Value; until = m.ExpiresAtUtc!.Value; state = "active";
        }
        db.Revisions.Add(new() { InstallationId = s.InstallationId, TenantId = s.TenantId, TargetId = s.TargetId, ConsumerId = s.ConsumerId, Operation = s.Operation,
            BindingId = m.BindingId, Revision = revision, ProfileId = profile, ReferenceId = reference, State = state, NotBeforeUtc = from, ExpiresAtUtc = until, ManifestId = m.ManifestId });
        if (head is null) { head = new() { InstallationId = s.InstallationId, TenantId = s.TenantId, TargetId = s.TargetId, ConsumerId = s.ConsumerId, Operation = s.Operation }; db.Heads.Add(head); }
        head.BindingId = m.BindingId; head.Revision = revision;
        return new(1, s, m.BindingId, revision, profile, reference, "macos-keychain-file-v1", "opaque-bytes-v1", state, from, until);
    }
}
