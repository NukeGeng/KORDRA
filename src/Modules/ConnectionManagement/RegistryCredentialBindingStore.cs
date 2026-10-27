using Kordra.Modules.ConnectionManagement.Commands;
using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ConnectionManagement;

/// <summary>Typed registry metadata only; no permission, credential lookup, audit or independent commit.</summary>
public sealed class RegistryCredentialBindingStore(CredentialDbContext db) : IRegistryCredentialBindingReader
{
    /// <summary>Metadata-only minimum registry/allocation schema check; never read Keychain or private command receipts.</summary>
    public Task<bool> HasRegistrySchemaV1Async(CancellationToken ct = default) => db.Database.SqlQueryRaw<bool>("""
        SELECT (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='app' AND c.relkind='r' AND c.relname IN ('sec_native_reference_allocations',
                  'sec_registry_profiles','sec_registry_references','sec_registry_binding_revisions',
                  'sec_registry_binding_heads','sec_registry_command_receipts')) = 6
          AND (SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace
                WHERE n.nspname='app' AND c.convalidated AND c.contype='f'
                  AND c.conname IN ('fk_sec_inventory_allocation_owner','fk_sec_registry_allocation_owner')) = 2
          AND EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace
                WHERE n.nspname='app' AND c.conname='PK_sec_native_reference_allocations' AND c.contype='p') AS "Value"
        """).SingleAsync(ct);

    public async Task<RegistryCredentialBindingSnapshotV1?> ReadCurrentUnderAdmissionAsync(RegistryCredentialSelectionV1 s, CancellationToken ct = default)
    {
        if (s is null || !s.IsValid) return null;
        var head = await db.RegistryHeads.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == s.InstallationId &&
            x.TenantId == s.TenantId && x.ProjectId == s.ProjectId && x.RegistryId == s.RegistryId &&
            x.Repository == s.Repository && x.ConsumerId == s.ConsumerId && x.Operation == s.Operation, ct);
        if (head is null) return null;
        var row = await db.RegistryRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == s.InstallationId &&
            x.TenantId == s.TenantId && x.BindingId == head.BindingId && x.Revision == head.Revision, ct);
        if (row is null || row.ProjectId != s.ProjectId || row.RegistryId != s.RegistryId || row.Repository != s.Repository ||
            row.ConsumerId != s.ConsumerId || row.Operation != s.Operation) return null;
        var profile = await db.RegistryProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == s.InstallationId &&
            x.TenantId == s.TenantId && x.ProfileId == row.ProfileId, ct);
        if (profile is null || profile.ProjectId != s.ProjectId || profile.RegistryId != s.RegistryId || profile.Repository != s.Repository ||
            profile.ConsumerId != s.ConsumerId || profile.Operation != s.Operation) return null;
        return new(1, s, row.BindingId, row.Revision, row.ProfileId, row.ReferenceId, profile.ProviderKind,
            profile.CredentialFormat, row.State, row.NotBeforeUtc, row.ExpiresAtUtc);
    }

    public async Task<Guid?> ReadLocatorUnderAdmissionAsync(RegistryCredentialBindingSnapshotV1 selected, CancellationToken ct)
    {
        var current = await ReadCurrentUnderAdmissionAsync(selected.Selection, ct);
        if (current is null || current.Digest() != selected.Digest()) return null;
        var s = selected.Selection;
        if (!await db.ReferenceAllocations.AsNoTracking().AnyAsync(a => a.InstallationId == s.InstallationId &&
            a.ReferenceId == selected.ReferenceId && a.TenantId == s.TenantId && a.OwnerKind == "registry" &&
            a.RegistryProfileId == selected.ProfileId && a.InventoryProfileId == null, ct)) return null;
        return await db.RegistryReferences.AsNoTracking().Where(r => r.InstallationId == s.InstallationId &&
            r.TenantId == s.TenantId && r.ProfileId == selected.ProfileId && r.ReferenceId == selected.ReferenceId)
            .Select(r => (Guid?)r.LocatorId).SingleOrDefaultAsync(ct);
    }

    public async Task<RegistryCredentialBindingSnapshotV1?> StageAsync(IOfflineProvisioningLease custody,
        RegistryCredentialManifest manifest, DateTimeOffset now, CancellationToken ct = default)
    {
        custody.RequireHeld();
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Registry binding mutation requires the caller's shared transaction.");
        if (!manifest.IsValid() || !manifest.IsActiveAt(now)) return null;
        var s = manifest.Selection;
        if (manifest.Action != "revoke" && await db.ReferenceAllocations.AnyAsync(a => a.InstallationId == s.InstallationId && a.ReferenceId == manifest.ReferenceId, ct)) return null;
        var head = await db.RegistryHeads.SingleOrDefaultAsync(x => x.InstallationId == s.InstallationId &&
            x.TenantId == s.TenantId && x.ProjectId == s.ProjectId && x.RegistryId == s.RegistryId &&
            x.Repository == s.Repository && x.ConsumerId == s.ConsumerId && x.Operation == s.Operation, ct);
        var previous = head is null ? null : await ReadCurrentUnderAdmissionAsync(s, ct);
        if (head is null ? manifest.ExpectedRevision != 0 || manifest.ExpectedBindingId is not null || manifest.Action != "create"
            : previous is null || head.BindingId != manifest.ExpectedBindingId || head.Revision != manifest.ExpectedRevision) return null;
        var revision = checked((head?.Revision ?? 0) + 1);
        Guid profileId, referenceId;
        DateTimeOffset from, until;
        string state;
        if (manifest.Action == "create")
        {
            if (previous is not null && previous.State != "revoked") return null;
            if (await db.RegistryRevisions.AnyAsync(r => r.InstallationId == s.InstallationId && r.BindingId == manifest.BindingId, ct) ||
                await db.RegistryProfiles.AnyAsync(p => p.InstallationId == s.InstallationId && p.ProfileId == manifest.ProfileId, ct)) return null;
            profileId = manifest.ProfileId!.Value;
            db.RegistryProfiles.Add(new()
            {
                InstallationId = s.InstallationId,
                TenantId = s.TenantId,
                ProjectId = s.ProjectId,
                RegistryId = s.RegistryId,
                Repository = s.Repository,
                ProfileId = profileId,
                ConsumerId = s.ConsumerId,
                Operation = s.Operation
            });
        }
        else
        {
            if (previous is null || previous.State == "revoked" || previous.BindingId != manifest.BindingId ||
                manifest.Action == "rotate" && previous.ProfileId != manifest.ProfileId) return null;
            profileId = previous.ProfileId;
        }
        if (manifest.Action == "revoke")
        {
            referenceId = previous!.ReferenceId; from = previous.NotBeforeUtc; until = previous.ExpiresAtUtc; state = "revoked";
        }
        else
        {
            referenceId = manifest.ReferenceId!.Value;
            db.ReferenceAllocations.Add(new()
            {
                InstallationId = s.InstallationId,
                ReferenceId = referenceId,
                TenantId = s.TenantId,
                OwnerKind = "registry",
                RegistryProfileId = profileId
            });
            db.RegistryReferences.Add(new()
            {
                InstallationId = s.InstallationId,
                TenantId = s.TenantId,
                ProfileId = profileId,
                ReferenceId = referenceId,
                LocatorId = manifest.LocatorId!.Value
            });
            from = manifest.NotBeforeUtc!.Value; until = manifest.ExpiresAtUtc!.Value; state = "active";
        }
        db.RegistryRevisions.Add(new()
        {
            InstallationId = s.InstallationId,
            TenantId = s.TenantId,
            ProjectId = s.ProjectId,
            RegistryId = s.RegistryId,
            Repository = s.Repository,
            ConsumerId = s.ConsumerId,
            Operation = s.Operation,
            BindingId = manifest.BindingId,
            Revision = revision,
            ProfileId = profileId,
            ReferenceId = referenceId,
            State = state,
            NotBeforeUtc = from,
            ExpiresAtUtc = until,
            ManifestId = manifest.ManifestId
        });
        if (head is null)
        {
            head = new()
            {
                InstallationId = s.InstallationId,
                TenantId = s.TenantId,
                ProjectId = s.ProjectId,
                RegistryId = s.RegistryId,
                Repository = s.Repository,
                ConsumerId = s.ConsumerId,
                Operation = s.Operation
            };
            db.RegistryHeads.Add(head);
        }
        head.BindingId = manifest.BindingId; head.Revision = revision;
        return new(1, s, manifest.BindingId, revision, profileId, referenceId, "macos-keychain-file-v1", "opaque-bytes-v1", state, from, until);
    }
}
