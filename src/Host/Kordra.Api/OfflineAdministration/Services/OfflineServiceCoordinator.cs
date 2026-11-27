using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Kordra.Api;

public sealed record ServiceManifestImpact(string ManifestHash, string Kind, Guid ServiceId, string? SpkiSha256, int BindingCount);
public sealed record ServiceManifestResult(string Status, long Generation = 0, long AuthorityEpoch = 0, bool Replayed = false);

/// <summary>
/// 12.3d (D6/D7) offline service manifests: API-stopped IAM/AUD/clock-floor composition only, never an HTTP route. Order:
/// writer gate → IAM root → <c>iam_workflow.apply_service_manifest</c>, which locks the installation authority fence (creating
/// it for a first bootstrap) before it reads or changes the registry and advances the epoch once → trusted time → AUD intent
/// and clock floor on the same root → commit. An exact replay is answered from the manifest receipt before the fence and
/// commits nothing. The admin login needs the NOLOGIN role that holds EXECUTE on the function (D7); runtime logins never do.
/// </summary>
public sealed class OfflineServiceCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> contexts,
    IAuditAppendPort auditLookup, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock,
    IOfflinePolicyAuditTransactionFactory? auditTransactions = null)
{
    private readonly DisposableTestBinding? disposableTest;
    private OfflineServiceCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> contexts,
        IAuditAppendPort auditLookup, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock,
        IOfflinePolicyAuditTransactionFactory? auditTransactions, DisposableTestBinding profile)
        : this(installationId, contexts, auditLookup, gate, clock, auditTransactions) => disposableTest = profile;
    public static OfflineServiceCoordinator CreateDisposableTestCoordinator(Guid installationId, IDbContextFactory<KordraIdentityDbContext> contexts,
        IAuditAppendPort auditLookup, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock,
        IOfflinePolicyAuditTransactionFactory? auditTransactions, DisposableTestBinding profile)
        => new(installationId, contexts, auditLookup, gate, clock, auditTransactions, profile);

    /// <summary>Read-only: validates the manifest and its target database without connecting; reports the hash to approve.</summary>
    public async Task<ServiceManifestImpact?> InspectAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> json, CancellationToken ct = default)
    {
        custody.RequireHeld();
        var m = ServiceManifestDocument.Parse(json);
        if (m is null || installationId == Guid.Empty || m.InstallationId != installationId) return null;
        await using var iam = await contexts.CreateDbContextAsync(ct);
        return MatchesDatabase(iam, m) ? new(m.Hash(), m.Kind, m.ServiceId, m.SpkiSha256, m.Bindings.Count) : null;
    }

    public async Task<ServiceManifestResult> ApplyAsync(IOfflineProvisioningLease custody, ReadOnlyMemory<byte> json, string approvedHash,
        CancellationToken ct = default)
    {
        custody.RequireHeld();
        var m = ServiceManifestDocument.Parse(json);
        if (m is null || installationId == Guid.Empty || m.InstallationId != installationId) return new("invalid");
        if (m.Hash() != approvedHash) return new("denied");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10)); ct = budget.Token;
        await using var held = await gate.AcquireWriteAsync(ct);
        var commitAttempted = false; ServiceManifestResult? resolved = null;
        try
        {
            custody.RequireHeld();
            await using var iam = await contexts.CreateDbContextAsync(ct);
            if (!MatchesDatabase(iam, m)) return new("denied");
            await iam.Database.OpenConnectionAsync(ct);
            await using var tx = await iam.Database.BeginTransactionAsync(ct);
            var connection = (NpgsqlConnection)iam.Database.GetDbConnection(); var root = (NpgsqlTransaction)tx.GetDbTransaction();
            var applied = await IamServiceManifestWriter.ApplyAsync(connection, root, m.ToPortManifest(), ct);
            if (applied.Duplicate)
            {
                // Exact replay (same hash, kind, service and AUD intent): the original apply must have its durable AUD intent.
                var receipt = await auditLookup.LookupAsync(m.ManifestId, ct);
                if (receipt.Status != AuditLookupStatus.Found) { gate.Close(); return new("unknown"); }
                custody.RequireHeld();
                return new("success", applied.Generation, applied.AuthorityEpoch, true);
            }
            // Trusted time only now: the function already holds the fence on this root.
            var now = Now();
            var intent = Intent(m, applied, new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero));
            await AuthorityClockFloorStore.StageAsync(iam, installationId, Now(), ct);
            await using var audit = await (auditTransactions ?? (disposableTest is { } auditProfile
                ? OfflinePolicyAuditTransactionFactory.CreateDisposableTestFactory(auditProfile) : new OfflinePolicyAuditTransactionFactory()))
                .EnlistAsync(connection, root, new Lease(custody), ct);
            var staged = await audit.StageAsync(intent, ct);
            // A manifest id reused for a different manifest: nothing commits.
            if (staged.Status == AuditTransactionStageStatus.AlreadyDurable ||
                staged.Status == AuditTransactionStageStatus.Rejected && staged.ReasonCode == "intent_conflict") return new("conflict");
            if (staged.Status != AuditTransactionStageStatus.Staged) return new("unavailable");
            custody.RequireHeld();
            commitAttempted = true;
            var outcome = await audit.CommitAsync(ct);
            if (outcome.Status == AuditAppendStatus.Unknown) gate.Close();
            resolved = outcome.Status == AuditAppendStatus.Durable ? new("success", applied.Generation, applied.AuthorityEpoch)
                : new(outcome.Status == AuditAppendStatus.Unknown ? "unknown" : "unavailable");
            return resolved;
        }
        catch when (resolved is not null) { return resolved; }
        catch (PostgresException error) when (!commitAttempted) { return new(Refusal(error.SqlState)); }
        catch when (!commitAttempted) { return new("unavailable"); }
        catch { gate.Close(); return new("unknown"); }
    }

    /// <summary>Fixed IAM reason codes only; never echoes values.</summary>
    private static string Refusal(string state) => state switch
    {
        "KD021" => "invalid",
        "KD017" or "KD022" or "KD023" => "conflict",
        "KD002" or PostgresErrorCodes.InsufficientPrivilege => "denied",
        _ => "unavailable"
    };

    private static AuditIntent Intent(ServiceManifestDocument m, WorkflowServiceManifestApplied applied, DateTimeOffset occurred)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(m.Hash() + "\n" + m.Kind + "\n" +
            applied.Generation.ToString(CultureInfo.InvariantCulture) + "\n" + applied.AuthorityEpoch.ToString(CultureInfo.InvariantCulture))));
        return new(1, m.ManifestId, m.ManifestId, AuditIntentStage.Mutation, null, null, m.ServiceId, "iam.service-manifest", "installation",
            m.InstallationId, null, applied.AuthorityEpoch, "service-manifest-" + digest, occurred, m.ManifestId);
    }

    private DateTimeOffset Now() { try { return clock.GetTrustedUtcNow(); } catch { gate.Close(); throw; } }

    private bool MatchesDatabase(KordraIdentityDbContext db, ServiceManifestDocument manifest)
    {
        if (disposableTest is { } profile)
        {
            IdentityAccessDbContextFactory.ValidateDisposableTestTarget(db.Database.GetConnectionString()!, profile);
            return manifest.ExpectedDatabase == "kordra_test_" + profile.RunId.ToString("N") && manifest.ExpectedRole == "kordra_test";
        }
        var c = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        return c.Host == "127.0.0.1" && c.Port == 5432 && c.Database == manifest.ExpectedDatabase && c.Username == manifest.ExpectedRole &&
            (c.Username == "kordra_admin" || c.Database == "kordra_test" && c.Username == "kordra_test");
    }

    private sealed class Lease(IOfflineProvisioningLease held) : IOfflineAuditLease { public void RequireHeld() => held.RequireHeld(); }
}
