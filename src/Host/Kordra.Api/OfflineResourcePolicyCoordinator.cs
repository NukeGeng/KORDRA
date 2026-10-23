using System.Security.Cryptography;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Kordra.Api;

public sealed record ResourcePolicyOperationResult(string Status, Guid PolicyId = default, long Revision = 0, IReadOnlyList<PolicyBinding>? Bindings = null);
public sealed record ResourcePolicyImpact(string ManifestHash, int ExistingBindings, IReadOnlyList<Guid> BindingIds, bool Truncated);

/// <summary>Offline technical composition. No HTTP registration, automatic approval or target/provider operation.</summary>
public sealed class OfflineResourcePolicyCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> contexts,
    IPolicyTenantAuthority tenants, IAuditAppendPort auditLookup, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock,
    IOfflinePolicyAuditTransactionFactory? auditTransactions = null)
{
    public async Task<ResourcePolicyImpact?> InspectAsync(IOfflineProvisioningLease custody, string json, CancellationToken ct = default)
    {
        custody.RequireHeld(); var manifest = ResourcePolicyManifest.Parse(json);
        if (manifest is null || manifest.InstallationId != installationId) return null;
        await using var admission = await gate.AcquireReadAsync(ct);
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (!MatchesDatabase(db, manifest)) return null;
        await db.Database.OpenConnectionAsync(ct);
        await using var prj = new ProjectDbContext(ProjectDbContextFactory.CreateOfflineEnlistedOptions(db.Database.GetDbConnection()));
        var store = new PolicyBindingStore(prj);
        if (!await tenants.IsActiveTenantUnderAdmissionAsync(manifest.TenantId, ct) ||
            await store.ReadOwnershipUnderAdmissionAsync(manifest.TenantId, manifest.ProjectId, null, ct) is null) return null;
        if (manifest.PolicyId is not { } id) return new(manifest.Hash(), 0, Array.Empty<Guid>(), false);
        var impact = await store.ReadPolicyImpactUnderAdmissionAsync(manifest.TenantId, manifest.ProjectId, id, ct);
        custody.RequireHeld();
        return new(manifest.Hash(), impact.Count, impact.BindingIds, impact.Count > impact.BindingIds.Count);
    }

    public async Task<ResourcePolicyOperationResult> ExecuteAsync(IOfflineProvisioningLease custody, string json,
        string approvedManifestHash, CancellationToken ct = default)
    {
        custody.RequireHeld(); var manifest = ResourcePolicyManifest.Parse(json);
        if (installationId == Guid.Empty || manifest is null || manifest.InstallationId != installationId) return new("invalid");
        var hash = manifest.Hash();
        if (hash != approvedManifestHash) return new("denied");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10)); ct = budget.Token;
        await using var admission = await gate.AcquireWriteAsync(ct);
        var commitAttempted = false; ResourcePolicyOperationResult? resolved = null;
        try
        {
            custody.RequireHeld();
            await using var db = await contexts.CreateDbContextAsync(ct);
            if (!MatchesDatabase(db, manifest)) return new("denied");
            var iam = new ResourcePolicyStore(db);
            // Exact trusted receipt reconciliation precedes mutable policy/parent/expiry validation.
            var previous = await iam.ReadCommandUnderAdmissionAsync(installationId, manifest.ManifestId, ct);
            if (previous is not null)
            {
                if (previous.ContentHash != hash) return new("conflict");
                var audit = await auditLookup.LookupAsync(previous.AuditIntentId, ct);
                if (audit.Status != AuditLookupStatus.Found || audit.Receipt?.IntentHash != previous.AuditIntentHash)
                { gate.Close(); return new("unknown"); }
                var replay = JsonSerializer.Deserialize<ResourcePolicyOperationResult>(previous.ResultJson);
                if (replay is null || replay.Status != "success" || replay.PolicyId == Guid.Empty || replay.Revision <= 0)
                { gate.Close(); return new("unknown"); }
                custody.RequireHeld(); return replay;
            }
            if (!await tenants.IsActiveTenantUnderAdmissionAsync(manifest.TenantId, ct)) return new("denied");
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await using var projectDb = new ProjectDbContext(ProjectDbContextFactory.CreateOfflineEnlistedOptions(db.Database.GetDbConnection()));
            await projectDb.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
            var projects = new PolicyBindingStore(projectDb);
            if (await projects.ReadOwnershipUnderAdmissionAsync(manifest.TenantId, manifest.ProjectId, null, ct) is null) return new("denied");
            var now = TrustedUtcNow();
            ResourcePolicySnapshot? policy;
            if (manifest.Operation == "bind")
                policy = await iam.ReadCurrentUnderAdmissionAsync(manifest.TenantId, manifest.ProjectId, manifest.PolicyId!.Value, manifest.ExpectedCurrentRevision, ct);
            else policy = await iam.StagePublicationAsync(custody, manifest.TenantId, manifest.ProjectId, manifest.PolicyId,
                manifest.ExpectedCurrentRevision, manifest.Payload!, now, ct);
            if (policy is null) return new("conflict");
            if (!PolicyReadyForCommit(policy.Payload, manifest.Operation, now)) return new("denied");
            var bindings = new List<PolicyBinding>();
            foreach (var b in manifest.Bindings)
            {
                if (b.ScopeKind != policy.Payload.ScopeKind) return new("denied");
                var binding = await projects.StageAsync(manifest.TenantId, manifest.ProjectId, b,
                    new(policy.PolicyId, policy.Revision), manifest.ManifestId, allowReplacement: true, ct);
                if (binding is null) return new("conflict");
                bindings.Add(binding);
            }
            var result = new ResourcePolicyOperationResult("success", policy.PolicyId, policy.Revision, bindings.AsReadOnly());
            var resultJson = JsonSerializer.Serialize(result);
            var digest = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hash + "\n" + resultJson)));
            var intent = new AuditIntent(1, manifest.ManifestId, manifest.ManifestId, AuditIntentStage.Mutation, null, manifest.TenantId, policy.PolicyId,
                "iam.resource-policy", "project", manifest.ProjectId, policy.Revision, policy.Revision, "resource-policy-" + digest, now, manifest.ManifestId);
            iam.StageCommand(installationId, manifest.ManifestId, new(hash, resultJson, intent.IntentId, AuditIntentCanonicalizer.ComputeHash(intent)));
            // All preparatory owner work is reversible; failure here has not attempted a commit.
            await iam.PersistAsync(ct); await projects.PersistAsync(ct);
            await iam.StageClockFloorAsync(installationId, TrustedUtcNow(), ct);
            await using var auditTransaction = await (auditTransactions ?? new OfflinePolicyAuditTransactionFactory())
                .EnlistAsync(db.Database.GetDbConnection(), transaction.GetDbTransaction(), new AuditLease(custody), ct);
            var staged = await auditTransaction.StageAsync(intent, ct);
            if (staged.Status != AuditTransactionStageStatus.Staged) return new("unavailable");
            custody.RequireHeld();
            now = TrustedUtcNow();
            if (!PolicyReadyForCommit(policy.Payload, manifest.Operation, now)) return new("denied");
            commitAttempted = true;
            var outcome = await auditTransaction.CommitAsync(ct);
            if (outcome.Status == AuditAppendStatus.Unknown) gate.Close();
            resolved = outcome.Status == AuditAppendStatus.Durable ? result : new(outcome.Status == AuditAppendStatus.Unknown ? "unknown" : "unavailable");
            return resolved;
        }
        catch when (resolved is not null) { return resolved; }
        catch when (!commitAttempted) { return new("unavailable"); }
        catch { gate.Close(); return new("unknown"); }
    }
    private static bool PolicyReadyForCommit(ResourcePolicyPayload payload, string operation, DateTimeOffset now) =>
        payload.EffectiveFromUtc <= now &&
        (operation != "bind" || payload.Status == "active") &&
        (payload.Status != "active" || payload.ExpiresAtUtc is null || payload.ExpiresAtUtc > now);

    private DateTimeOffset TrustedUtcNow()
    { try { return clock.GetTrustedUtcNow(); } catch { gate.Close(); throw; } }
    private static bool MatchesDatabase(KordraIdentityDbContext db, ResourcePolicyManifest manifest)
    {
        var parsed = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        return parsed.Host == "127.0.0.1" && parsed.Port == 5432 && parsed.Database == manifest.DatabaseIdentity.DatabaseName && parsed.Username == manifest.DatabaseIdentity.ProvisioningRole;
    }
    private sealed class AuditLease(IOfflineProvisioningLease lease) : IOfflineAuditLease
    { public void RequireHeld() => lease.RequireHeld(); }
}
