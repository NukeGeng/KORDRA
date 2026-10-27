using System.Security.Cryptography;
using System.Text;
using Kordra.ApprovalGovernance.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ApprovalGovernance;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Kordra.Api;

public sealed record ApprovalPolicyBindingImpact(Guid EnvironmentId, Guid? BindingId, long BindingRevision);
public sealed record ApprovalPolicyInspectResult(int SchemaVersion, string ManifestHash, long CurrentPolicyRevision,
    IReadOnlyList<ApprovalPolicyBindingImpact> Bindings);
public sealed record ApprovalPolicyApplyResult(string Status, int SchemaVersion = 1, Guid ManifestId = default,
    Guid PolicyId = default, long PolicyRevision = 0, string? PayloadHash = null, Guid CommandId = default, bool Replayed = false);

/// <summary>Exact stopped policy publication; Host owns one enlisted owner/audit/floor transaction.</summary>
public sealed class OfflineApprovalPolicyCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> identities,
    IAuditAppendPort auditLookup, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock,
    IOfflinePolicyAuditTransactionFactory? auditTransactions = null)
{
    public async Task<ApprovalPolicyInspectResult?> InspectAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> input,
        CancellationToken cancellationToken = default)
    {
        custody.RequireHeld();
        ApprovalPolicyManifest manifest;
        try { manifest = ApprovalPolicyManifest.Parse(input); } catch (ArgumentException) { return null; }
        if (manifest.InstallationId != installationId) return null;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); budget.CancelAfter(TimeSpan.FromSeconds(10));
        await using var admission = await gate.AcquireReadAsync(budget.Token);
        await using var identity = await identities.CreateDbContextAsync(budget.Token);
        if (!MatchesDatabase(identity, manifest)) return null;
        await identity.Database.OpenConnectionAsync(budget.Token);
        await using var apr = new ApprovalDbContext(ApprovalDbContextFactory.CreateOfflineEnlistedOptions(identity.Database.GetDbConnection()));
        await using var prj = new ProjectDbContext(ProjectDbContextFactory.CreateOfflineEnlistedOptions(identity.Database.GetDbConnection()));
        await using var ten = new TenantDbContext(TenantDbContextFactory.CreateOfflineEnlistedOptions(identity.Database.GetDbConnection()));
        if (!await ValidOwnerAsync(manifest, prj, ten, budget.Token)) return null;
        var head = await apr.Heads.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId && x.TenantId == manifest.TenantId &&
            x.ProjectId == manifest.ProjectId && x.PolicyId == manifest.PolicyId, budget.Token);
        var bindings = new List<ApprovalPolicyBindingImpact>();
        foreach (var change in manifest.BindingChanges)
        {
            var environment = await new ReleasePolicyEnvironmentReader(prj).ReadCurrentUnderAdmissionAsync(
                manifest.TenantId, manifest.ProjectId, change.EnvironmentId, budget.Token);
            if (environment is null) return null;
            var row = await apr.Bindings.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId && x.TenantId == manifest.TenantId &&
                x.ProjectId == manifest.ProjectId && x.EnvironmentId == change.EnvironmentId, budget.Token);
            bindings.Add(new(change.EnvironmentId, row?.BindingId, row?.BindingRevision ?? 0));
        }
        custody.RequireHeld();
        return new(1, manifest.CommandHash, head?.CurrentRevision ?? 0, bindings.AsReadOnly());
    }

    public async Task<ApprovalPolicyApplyResult> ApplyAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> input,
        string approvedHash, CancellationToken cancellationToken = default)
    {
        custody.RequireHeld();
        ApprovalPolicyManifest manifest;
        try { manifest = ApprovalPolicyManifest.Parse(input); } catch (ArgumentException) { return new("invalid"); }
        if (installationId == Guid.Empty || manifest.InstallationId != installationId) return new("invalid");
        if (manifest.CommandHash != approvedHash) return new("denied");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); budget.CancelAfter(TimeSpan.FromSeconds(10));
        await using var admission = await gate.AcquireWriteAsync(budget.Token);
        var commitAttempted = false; ApprovalPolicyApplyResult? result = null;
        try
        {
            custody.RequireHeld();
            await using var identity = await identities.CreateDbContextAsync(budget.Token);
            if (!MatchesDatabase(identity, manifest)) return new("denied");
            await using var transaction = await identity.Database.BeginTransactionAsync(budget.Token);
            var connection = identity.Database.GetDbConnection(); var dbTransaction = transaction.GetDbTransaction();
            await using var apr = new ApprovalDbContext(ApprovalDbContextFactory.CreateOfflineEnlistedOptions(connection));
            await apr.Database.UseTransactionAsync(dbTransaction, budget.Token);
            var store = new ApprovalPolicyStore(apr);
            var previous = await store.ReadCommandUnderAdmissionAsync(installationId, manifest.ManifestId, budget.Token);
            if (previous is not null)
            {
                if (previous.CommandHash.Length != 32) { gate.Close(); return new("unknown"); }
                if (!CryptographicOperations.FixedTimeEquals(previous.CommandHash, Convert.FromHexString(manifest.CommandHash))) return new("conflict");
                if (!await HasExactRevisionAsync(apr, manifest, budget.Token)) { gate.Close(); return new("unknown"); }
                if (!await HasExactBindingHistoryAsync(apr, manifest, budget.Token)) { gate.Close(); return new("unknown"); }
                var audit = await auditLookup.LookupAsync(previous.AuditIntentId, budget.Token);
                string expectedAuditHash;
                try { expectedAuditHash = AuditIntentCanonicalizer.ComputeHash(CreateIntent(manifest, previous.CommandId, previous.CreatedAtUtc)); }
                catch { gate.Close(); return new("unknown"); }
                if (previous.CommandId == Guid.Empty || previous.AuditIntentId != manifest.ManifestId ||
                    previous.AuditIntentHash.Length != 32 || expectedAuditHash != Convert.ToHexStringLower(previous.AuditIntentHash) ||
                    previous.TenantId != manifest.TenantId || previous.ProjectId != manifest.ProjectId || previous.PolicyId != manifest.PolicyId ||
                    previous.PolicyRevision != manifest.ExpectedPolicyRevision + 1 || previous.PayloadHash.Length != 32 || previous.AuditIntentHash.Length != 32 ||
                    !CryptographicOperations.FixedTimeEquals(previous.PayloadHash, Convert.FromHexString(manifest.Policy.PayloadHash)) ||
                    audit.Status != AuditLookupStatus.Found || audit.Receipt?.IntentHash != Convert.ToHexStringLower(previous.AuditIntentHash))
                { gate.Close(); return new("unknown"); }
                custody.RequireHeld();
                return new("success", 1, previous.ManifestId, previous.PolicyId, previous.PolicyRevision,
                    Convert.ToHexStringLower(previous.PayloadHash), previous.CommandId, true);
            }
            await using var prj = new ProjectDbContext(ProjectDbContextFactory.CreateOfflineEnlistedOptions(connection));
            await prj.Database.UseTransactionAsync(dbTransaction, budget.Token);
            await using var ten = new TenantDbContext(TenantDbContextFactory.CreateOfflineEnlistedOptions(connection));
            await ten.Database.UseTransactionAsync(dbTransaction, budget.Token);
            if (!await ValidOwnerAsync(manifest, prj, ten, budget.Token)) return new("denied");
            var owner = new ReleasePolicyEnvironmentReader(prj);
            var evidence = new List<ApprovalEnvironmentEvidence>();
            foreach (var change in manifest.BindingChanges)
            {
                var fact = await owner.ReadCurrentUnderAdmissionAsync(manifest.TenantId, manifest.ProjectId, change.EnvironmentId, budget.Token);
                if (fact is null) return new("denied");
                evidence.Add(new(fact.TenantId, fact.ProjectId, fact.EnvironmentId, fact.ProjectRevision, fact.EnvironmentRevision, fact.Classification));
            }
            var now = Now();
            if (!await store.StagePublicationAsync(new ApprovalLease(custody), manifest, evidence, now, budget.Token)) return new("conflict");
            var commandId = Guid.NewGuid();
            var intent = CreateIntent(manifest, commandId, now);
            store.StageCommand(new ApprovalLease(custody), manifest, commandId, intent.IntentId, AuditIntentCanonicalizer.ComputeHash(intent), now);
            await apr.SaveChangesAsync(budget.Token);
            await AuthorityClockFloorStore.StageAsync(identity, installationId, Now(), budget.Token);
            await using var auditTransaction = await (auditTransactions ?? new OfflinePolicyAuditTransactionFactory())
                .EnlistAsync(connection, dbTransaction, new AuditLease(custody), budget.Token);
            var staged = await auditTransaction.StageAsync(intent, budget.Token);
            if (staged.Status == AuditTransactionStageStatus.AlreadyDurable ||
                staged.Status == AuditTransactionStageStatus.Rejected && staged.ReasonCode == "intent_conflict")
            { gate.Close(); return new("unknown"); }
            if (staged.Status != AuditTransactionStageStatus.Staged) return new("unavailable");
            custody.RequireHeld();
            _ = Now();
            commitAttempted = true;
            var outcome = await auditTransaction.CommitAsync(budget.Token);
            if (outcome.Status == AuditAppendStatus.Durable)
            {
                // AUD can resolve an independently durable duplicate after rolling back the owner transaction.
                // A durable audit receipt alone therefore cannot establish this APR mutation committed.
                if (outcome.Receipt is null || outcome.Receipt.RecordId == Guid.Empty ||
                    outcome.Receipt.IntentId != intent.IntentId || outcome.Receipt.IntentHash != AuditIntentCanonicalizer.ComputeHash(intent))
                { gate.Close(); return new("unknown"); }
                await apr.Database.UseTransactionAsync((System.Data.Common.DbTransaction?)null, budget.Token);
                var confirmed = await store.ReadCommandUnderAdmissionAsync(installationId, manifest.ManifestId, budget.Token);
                if (confirmed is null || confirmed.CommandId != commandId || confirmed.PolicyId != manifest.PolicyId ||
                    confirmed.TenantId != manifest.TenantId || confirmed.ProjectId != manifest.ProjectId ||
                    confirmed.PolicyRevision != manifest.ExpectedPolicyRevision + 1 || confirmed.AuditIntentId != intent.IntentId ||
                    confirmed.CommandHash.Length != 32 || confirmed.PayloadHash.Length != 32 || confirmed.AuditIntentHash.Length != 32 ||
                    !CryptographicOperations.FixedTimeEquals(confirmed.CommandHash, Convert.FromHexString(manifest.CommandHash)) ||
                    !CryptographicOperations.FixedTimeEquals(confirmed.PayloadHash, Convert.FromHexString(manifest.Policy.PayloadHash)) ||
                    Convert.ToHexStringLower(confirmed.AuditIntentHash) != outcome.Receipt.IntentHash ||
                    !await HasExactRevisionAsync(apr, manifest, budget.Token) ||
                    !await HasExactBindingHistoryAsync(apr, manifest, budget.Token))
                { gate.Close(); return new("unknown"); }
                result = new("success", 1, manifest.ManifestId, manifest.PolicyId, manifest.ExpectedPolicyRevision + 1,
                    manifest.Policy.PayloadHash, commandId);
            }
            else
            {
                var contradictory = outcome.Status == AuditAppendStatus.Rejected && outcome.ReasonCode == "intent_conflict";
                if (outcome.Status == AuditAppendStatus.Unknown || contradictory) gate.Close();
                result = new(outcome.Status == AuditAppendStatus.Unknown || contradictory ? "unknown" : "unavailable");
            }
            return result;
        }
        catch when (result is not null) { return result; }
        catch when (!commitAttempted) { return new("unavailable"); }
        catch { gate.Close(); return new("unknown"); }
    }

    private static async Task<bool> HasExactRevisionAsync(ApprovalDbContext apr, ApprovalPolicyManifest manifest, CancellationToken ct)
    {
        var revision = await apr.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == manifest.InstallationId &&
            x.TenantId == manifest.TenantId && x.ProjectId == manifest.ProjectId && x.PolicyId == manifest.PolicyId &&
            x.Revision == manifest.ExpectedPolicyRevision + 1, ct);
        return revision is not null && revision.PayloadHash.Length == 32 &&
            CryptographicOperations.FixedTimeEquals(revision.PayloadHash, Convert.FromHexString(manifest.Policy.PayloadHash)) &&
            CryptographicOperations.FixedTimeEquals(revision.CanonicalPayload, manifest.Policy.GetCanonicalBytes());
    }

    private static async Task<bool> HasExactBindingHistoryAsync(ApprovalDbContext apr, ApprovalPolicyManifest manifest, CancellationToken ct)
    {
        var history = await apr.BindingHistory.AsNoTracking().Where(x => x.InstallationId == manifest.InstallationId &&
            x.TenantId == manifest.TenantId && x.ProjectId == manifest.ProjectId && x.ManifestId == manifest.ManifestId)
            .Take(33).ToArrayAsync(ct);
        if (history.Length != manifest.BindingChanges.Count) return false;
        foreach (var change in manifest.BindingChanges)
        {
            var selected = history.Where(x => x.EnvironmentId == change.EnvironmentId).ToArray();
            if (selected.Length != 1 || selected[0].BindingId == Guid.Empty || selected[0].PolicyId != manifest.PolicyId ||
                selected[0].PolicyRevision != manifest.ExpectedPolicyRevision + 1 ||
                selected[0].BindingRevision != change.ExpectedBindingRevision + 1 ||
                change.ExpectedBindingId is { } id && selected[0].BindingId != id) return false;
        }
        return true;
    }

    private static AuditIntent CreateIntent(ApprovalPolicyManifest manifest, Guid commandId, DateTimeOffset now)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.CommandHash + "\n" + commandId.ToString("D"))));
        return new AuditIntent(1, manifest.ManifestId, manifest.ManifestId, AuditIntentStage.Mutation, null, manifest.TenantId,
            manifest.PolicyId, "apr.policy", "project", manifest.ProjectId, manifest.ExpectedPolicyRevision + 1,
            manifest.ExpectedPolicyRevision + 1, "approval-policy-" + digest, now, manifest.ManifestId);
    }

    private DateTimeOffset Now()
    {
        try
        {
            var now = clock.GetTrustedUtcNow();
            if (now.Offset != TimeSpan.Zero || now == default || now == DateTimeOffset.MaxValue) throw new InvalidOperationException("Untrusted clock.");
            return new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        }
        catch { gate.Close(); throw; }
    }
    private static bool MatchesDatabase(KordraIdentityDbContext db, ApprovalPolicyManifest manifest)
    {
        var parsed = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        return parsed.Host == "127.0.0.1" && parsed.Port == 5432 && parsed.Database == manifest.ExpectedDatabase &&
            (parsed.Username == "kordra_admin" || parsed.Database == "kordra_test" && parsed.Username == "kordra_test");
    }
    private static async Task<bool> ValidOwnerAsync(ApprovalPolicyManifest manifest, ProjectDbContext prj, TenantDbContext ten, CancellationToken ct)
    {
        if (!await new EnlistedTenantOrganizationAuthority(ten).IsActiveTenantUnderAdmissionAsync(manifest.TenantId, ct)) return false;
        var project = await new ProjectIdentityReader(prj).ReadCurrentProjectUnderAdmissionAsync(manifest.ProjectId, ct);
        return project is not null && project.TenantId == manifest.TenantId;
    }
    private sealed class ApprovalLease(IOfflineProvisioningLease lease) : IOfflineApprovalCustody { public void RequireHeld() => lease.RequireHeld(); }
    private sealed class AuditLease(IOfflineProvisioningLease lease) : IOfflineAuditLease { public void RequireHeld() => lease.RequireHeld(); }
}
