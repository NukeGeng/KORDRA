using System.Data.Common;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Api;

public interface IArtifactAuditTransactionFactory
{
    ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct);
}
public interface IArtifactAuditTransaction : IAsyncDisposable
{
    bool CommitAttempted { get; }
    Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct);
    Task<AuditAppendOutcome> CommitAsync(CancellationToken ct);
}
public sealed class ArtifactAuditTransactionFactory : IArtifactAuditTransactionFactory
{
    public async ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection c, DbTransaction t, CancellationToken ct) =>
        new Enlisted(await AuditTransactionContext.CreateAsync(c, t, ct));
    private sealed class Enlisted(AuditTransactionContext audit) : IArtifactAuditTransaction
    {
        public bool CommitAttempted => audit.CommitAttempted;
        public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct) => audit.StageAsync(intent, ct);
        public Task<AuditAppendOutcome> CommitAsync(CancellationToken ct) => audit.CommitAsync(ct);
        public ValueTask DisposeAsync() => audit.DisposeAsync();
    }
}
public sealed record ArtifactOwnerCommit(string Status, PreparedArtifactRegistration? Prepared = null);

/// <summary>One ART/AUD/IAM-floor commit on the same approved connection. No provider or independent owner commit.</summary>
public sealed class ArtifactOwnerTransaction(Guid installation, IDbContextFactory<KordraIdentityDbContext> identity,
    IArtifactAuditTransactionFactory audits, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock)
{
    public async Task<ArtifactOwnerCommit> RunAsync(Func<PreparedArtifactRegistration?, AuditIntent> intent,
        Func<ArtifactRegistrationStore, CancellationToken, Task<PreparedArtifactRegistration?>> prepare,
        Func<CancellationToken, Task<bool>> stillCurrent, CancellationToken ct)
    {
        IArtifactAuditTransaction? audit = null;
        ArtifactOwnerCommit? confirmed = null;
        try
        {
            await using var iam = await identity.CreateDbContextAsync(ct);
            ArtifactDbContextFactory.ValidateEnlistedTarget(iam.Database.GetDbConnection());
            await using var tx = await iam.Database.BeginTransactionAsync(ct);
            await using var art = new ArtifactDbContext(ArtifactDbContextFactory.CreateEnlistedOptions(iam.Database.GetDbConnection()));
            await art.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            audit = await audits.EnlistAsync(iam.Database.GetDbConnection(), tx.GetDbTransaction(), ct);
            await using var ownedAudit = audit;
            var prepared = await prepare(new(art), ct);
            if (prepared is { Status: not ("staged_pending" or "staged_registered" or "staged_failed") }) return new("rejected", prepared);
            if ((await audit.StageAsync(intent(prepared), ct)).Status != AuditTransactionStageStatus.Staged) return new("unavailable");
            await art.SaveChangesAsync(ct);
            await AuthorityClockFloorStore.StageAsync(iam, installation, clock.GetTrustedUtcNow(), ct);
            if (!await stillCurrent(ct)) return new("denied");
            var outcome = await audit.CommitAsync(ct);
            if (outcome.Status == AuditAppendStatus.Unknown && audit.CommitAttempted) gate.Close();
            confirmed = new(outcome.Status == AuditAppendStatus.Durable ? "durable" :
                outcome.Status == AuditAppendStatus.Unknown && audit.CommitAttempted ? "unknown" : "unavailable",
                outcome.Status == AuditAppendStatus.Durable ? prepared : null);
            return confirmed;
        }
        catch
        {
            // A successful commit remains successful even if disposal fails afterward.
            if (confirmed is not null) return confirmed;
            if (audit?.CommitAttempted == true) { gate.Close(); return new("unknown"); }
            return new("unavailable");
        }
    }
}
