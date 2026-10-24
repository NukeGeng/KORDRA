using System.Data.Common;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;

namespace Kordra.Api;

/// <summary>Host composition adapter that exposes AUD enlistment to IAM without cross-owner table access.</summary>
public sealed class IdentityAccessAuditTransactionAdapter : IIdentityAuditTransactionFactory
{
    public async ValueTask<IIdentityAuditTransaction> EnlistAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var context = await AuditTransactionContext.CreateAsync(connection, transaction, cancellationToken);
        return new EnlistedAuditTransaction(context);
    }

    private sealed class EnlistedAuditTransaction(AuditTransactionContext context) : IIdentityAuditTransaction
    {
        public async Task<AuditAppendOutcome> CommitWithOwnerMutationAsync(
            AuditIntent intent,
            Func<CancellationToken, Task> saveOwnerMutationAsync,
            CancellationToken cancellationToken = default)
        {
            var staged = await context.StageAsync(intent, cancellationToken);
            if (staged.Status != AuditTransactionStageStatus.Staged)
            {
                await context.RollbackAsync(cancellationToken);
                return staged.Status switch
                {
                    AuditTransactionStageStatus.AlreadyDurable =>
                        AuditAppendOutcome.Rejected("duplicate_intent_requires_reconciliation"),
                    AuditTransactionStageStatus.Rejected =>
                        AuditAppendOutcome.Rejected(staged.ReasonCode ?? "rejected"),
                    _ => AuditAppendOutcome.Unavailable()
                };
            }

            try
            {
                await saveOwnerMutationAsync(cancellationToken);
                return await context.CommitAsync(cancellationToken);
            }
            catch
            {
                // A failed/unknown rollback must never be reported as a durable owner mutation.
                await context.RollbackAsync(CancellationToken.None);
                return AuditAppendOutcome.Unknown();
            }
        }

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
