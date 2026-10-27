using System.Security.Cryptography;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Commands;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Kordra.Modules.ProjectEnvironment.Contracts;

namespace Kordra.Api;

/// <summary>API-stopped SEC/IAM-clock/AUD composition only. Never reads Keychain or registers an HTTP route.</summary>
public sealed class OfflineRegistryCredentialCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> contexts,
    IPolicyTenantAuthority tenants, IProjectIdentityReader projects, IAuditAppendPort auditLookup,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock, IOfflinePolicyAuditTransactionFactory? auditTransactions = null)
{
    public async Task<CredentialImpact?> InspectAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> json, CancellationToken ct = default)
    {
        custody.RequireHeld(); var m = RegistryCredentialManifest.Parse(json); if (m is null || m.InstallationId != installationId) return null;
        await using var held = await gate.AcquireReadAsync(ct);
        if (!await tenants.IsActiveTenantUnderAdmissionAsync(m.TenantId, ct) || (await projects.ReadCurrentProjectUnderAdmissionAsync(m.ProjectId, ct))?.TenantId != m.TenantId) return null;
        await using var db = await contexts.CreateDbContextAsync(ct); if (!Allowed(db)) return null;
        await db.Database.OpenConnectionAsync(ct);
        await using var sec = new CredentialDbContext(CredentialDbContextFactory.CreateOfflineEnlistedOptions(db.Database.GetDbConnection()));
        var previous = await new RegistryCredentialBindingStore(sec).ReadCurrentUnderAdmissionAsync(m.Selection, ct); custody.RequireHeld();
        return new(m.Hash(), previous?.BindingId, previous?.Revision ?? 0);
    }
    public async Task<CredentialOperationResult> ExecuteAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> json, string approvedHash, CancellationToken ct = default)
    {
        custody.RequireHeld(); var m = RegistryCredentialManifest.Parse(json);
        if (m is null || installationId == Guid.Empty || m.InstallationId != installationId) return new("invalid");
        var hash = m.Hash(); if (hash != approvedHash) return new("denied");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10)); ct = budget.Token;
        await using var held = await gate.AcquireWriteAsync(ct);
        var commitAttempted = false; CredentialOperationResult? resolved = null;
        try
        {
            custody.RequireHeld(); await using var iam = await contexts.CreateDbContextAsync(ct); if (!Allowed(iam)) return new("denied");
            await iam.Database.OpenConnectionAsync(ct);
            await using var sec = new CredentialDbContext(CredentialDbContextFactory.CreateOfflineEnlistedOptions(iam.Database.GetDbConnection()));
            var previous = await sec.RegistryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId && x.ManifestId == m.ManifestId, ct);
            if (previous is not null)
            {
                if (previous.ContentHash != hash) return new("conflict");
                var receipt = await auditLookup.LookupAsync(previous.AuditIntentId, ct);
                if (receipt.Status != AuditLookupStatus.Found || receipt.Receipt?.IntentHash != previous.AuditHash) { gate.Close(); return new("unknown"); }
                var replay = JsonSerializer.Deserialize<CredentialOperationResult>(previous.ResultJson);
                var committed = await sec.RegistryRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId && x.ManifestId == m.ManifestId &&
                    x.TenantId == m.TenantId && x.ProjectId == m.ProjectId && x.RegistryId == m.RegistryId && x.Repository == m.Repository && x.ConsumerId == m.ConsumerId && x.Operation == m.Operation, ct);
                if (replay is null || replay.Status != "success" || replay.BindingId != m.BindingId || replay.Revision != m.ExpectedRevision + 1 || replay.ReferenceId == Guid.Empty ||
                    previous.AuditIntentId != m.ManifestId || committed is null || committed.BindingId != replay.BindingId ||
                    committed.Revision != replay.Revision || committed.ReferenceId != replay.ReferenceId || committed.State != (m.Action == "revoke" ? "revoked" : "active") ||
                    AuditIntentCanonicalizer.ComputeHash(Intent(m, replay, previous.AuditOccurredAtUtc)) != previous.AuditHash)
                { gate.Close(); return new("unknown"); }
                custody.RequireHeld(); return replay;
            }
            if (!await tenants.IsActiveTenantUnderAdmissionAsync(m.TenantId, ct)) return new("denied");
            var project = await projects.ReadCurrentProjectUnderAdmissionAsync(m.ProjectId, ct); if (project is null || project.TenantId != m.TenantId) return new("denied");
            await using var tx = await iam.Database.BeginTransactionAsync(ct);
            await sec.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            var now = Now(); var row = await new RegistryCredentialBindingStore(sec).StageAsync(custody, m, now, ct);
            if (row is null) return new("conflict");
            var result = new CredentialOperationResult("success", row.BindingId, row.Revision, row.ReferenceId);
            var resultJson = JsonSerializer.Serialize(result);
            var occurred = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
            var intent = Intent(m, result, occurred);
            sec.RegistryCommands.Add(new()
            {
                InstallationId = installationId,
                ManifestId = m.ManifestId,
                ContentHash = hash,
                ResultJson = resultJson,
                AuditOccurredAtUtc = occurred,
                AuditIntentId = intent.IntentId,
                AuditHash = AuditIntentCanonicalizer.ComputeHash(intent)
            });
            await sec.SaveChangesAsync(ct); await AuthorityClockFloorStore.StageAsync(iam, installationId, Now(), ct);
            await using var audit = await (auditTransactions ?? new OfflinePolicyAuditTransactionFactory()).EnlistAsync(iam.Database.GetDbConnection(), tx.GetDbTransaction(), new Lease(custody), ct);
            if ((await audit.StageAsync(intent, ct)).Status != AuditTransactionStageStatus.Staged) return new("unavailable");
            custody.RequireHeld();
            if (!m.IsActiveAt(Now()) || !await tenants.IsActiveTenantUnderAdmissionAsync(m.TenantId, ct) || await projects.ReadCurrentProjectUnderAdmissionAsync(m.ProjectId, ct) != project) return new("denied");
            commitAttempted = true;
            var outcome = await audit.CommitAsync(ct);
            if (outcome.Status == AuditAppendStatus.Unknown) gate.Close();
            resolved = outcome.Status == AuditAppendStatus.Durable ? result : new(outcome.Status == AuditAppendStatus.Unknown ? "unknown" : "unavailable");
            return resolved;
        }
        catch when (resolved is not null) { return resolved; }
        catch when (!commitAttempted) { return new("unavailable"); }
        catch { gate.Close(); return new("unknown"); }
    }
    private static AuditIntent Intent(RegistryCredentialManifest manifest, CredentialOperationResult result, DateTimeOffset occurred)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifest.Hash() + "\n" + JsonSerializer.Serialize(result))));
        return new(1, manifest.ManifestId, manifest.ManifestId, AuditIntentStage.Mutation, null, manifest.TenantId, manifest.ProjectId,
            "sec.registry-credential-binding", "project", manifest.ProjectId, null, result.Revision, "registry-credential-binding-" + digest, occurred, manifest.ManifestId);
    }
    private DateTimeOffset Now() { try { return clock.GetTrustedUtcNow(); } catch { gate.Close(); throw; } }
    private static bool Allowed(KordraIdentityDbContext db)
    {
        var c = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        return c.Host == "127.0.0.1" && c.Port == 5432 && (c.Database == "kordra_test" && c.Username == "kordra_test" || c.Database == "kordra" && c.Username == "kordra_admin");
    }
    private sealed class Lease(IOfflineProvisioningLease held) : IOfflineAuditLease { public void RequireHeld() => held.RequireHeld(); }
}
