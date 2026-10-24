using System.Data.Common;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.TenantManagement.Infrastructure;

namespace Kordra.Api;

/// <summary>Host-only AUD enlistment bridge; does not register TEN endpoints or grant authority.</summary>
public sealed class TenantAuditTransactionAdapter(ICatalogClockFloorWriter? clockFloor = null) : ITenantAuditTransactionFactory
{
    public async ValueTask<ITenantAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken = default) =>
        new Enlisted(await AuditTransactionContext.CreateAsync(connection, transaction, cancellationToken), connection, transaction, clockFloor);

    private sealed class Enlisted(AuditTransactionContext context, DbConnection connection, DbTransaction transaction, ICatalogClockFloorWriter? clockFloor) : ITenantAuditTransaction
    {
        public async Task<AuditAppendOutcome> CommitAsync(AuditIntent intent, Func<CancellationToken, Task<bool>> saveOwnerAsync, CancellationToken cancellationToken = default)
        {
            var staged = await context.StageAsync(intent, cancellationToken);
            if (staged.Status != AuditTransactionStageStatus.Staged)
            {
                await context.RollbackAsync(cancellationToken);
                return AuditAppendOutcome.Unavailable();
            }
            try
            {
                if (!await saveOwnerAsync(cancellationToken))
                {
                    await context.RollbackAsync(cancellationToken);
                    return AuditAppendOutcome.Rejected("authority_invalid_at_commit");
                }
                if (clockFloor is not null) await clockFloor.StageAsync(connection, transaction, cancellationToken);
            }
            catch
            {
                // No commit is attempted when owner validation/save fails. Context disposal
                // and the owning transaction unwind before admission is released.
                await context.RollbackAsync(CancellationToken.None);
                return AuditAppendOutcome.Unavailable();
            }
            return await context.CommitAsync(cancellationToken);
        }
        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
