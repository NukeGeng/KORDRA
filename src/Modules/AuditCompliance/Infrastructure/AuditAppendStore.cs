using Kordra.Modules.AuditCompliance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.AuditCompliance.Infrastructure;

/// <summary>Owner adapter for immutable append and exact intent-id lookup.</summary>
public sealed class AuditAppendStore(IDbContextFactory<AuditDbContext> contextFactory) : IAuditAppendPort
{
    public async ValueTask<AuditAppendOutcome> AppendAsync(
        AuditIntent intent,
        CancellationToken cancellationToken = default)
    {
        if (intent is null)
        {
            return AuditAppendOutcome.Rejected("invalid_intent");
        }

        var isDenial = intent.Stage == AuditIntentStage.Outcome &&
            intent.ActionOutcome == AuditActionOutcome.Denied;
        using var appendBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        appendBudget.CancelAfter(isDenial ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(1));
        cancellationToken = appendBudget.Token;

        try
        {
            await using var anchor = await contextFactory.CreateDbContextAsync(cancellationToken);
            await anchor.Database.OpenConnectionAsync(cancellationToken);
            var connection = anchor.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var enlistment = await AuditTransactionContext.CreateAsync(connection, transaction, cancellationToken);
            var staged = await enlistment.StageAsync(intent, cancellationToken);
            return staged.Status switch
            {
                AuditTransactionStageStatus.Staged => await enlistment.CommitAsync(cancellationToken),
                AuditTransactionStageStatus.AlreadyDurable when staged.Receipt is not null => AuditAppendOutcome.Durable(staged.Receipt),
                AuditTransactionStageStatus.Rejected => AuditAppendOutcome.Rejected(staged.ReasonCode ?? "rejected"),
                _ => AuditAppendOutcome.Unavailable()
            };
        }
        catch
        {
            return AuditAppendOutcome.Unavailable();
        }
    }

    public async ValueTask<AuditLookupOutcome> LookupAsync(
        Guid intentId,
        CancellationToken cancellationToken = default)
    {
        if (intentId == Guid.Empty)
        {
            return AuditLookupOutcome.NotFound();
        }

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            var entry = await dbContext.Entries.AsNoTracking()
                .SingleOrDefaultAsync(value => value.IntentId == intentId, cancellationToken);
            return entry is null
                ? AuditLookupOutcome.NotFound()
                : AuditLookupOutcome.Found(entry.ToReceipt());
        }
        catch
        {
            return AuditLookupOutcome.Unavailable();
        }
    }
}
