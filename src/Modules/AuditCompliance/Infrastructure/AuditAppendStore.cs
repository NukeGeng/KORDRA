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

        var commitStarted = 0;
        // The operation retains sole ownership of its resources until disposal completes. If a provider
        // stalls during cancellation/cleanup, do not block the caller or return that connection early.
        var operation = AppendCoreAsync(intent, () => Volatile.Write(ref commitStarted, 1), cancellationToken);
        try
        {
            return await operation.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Volatile.Read(ref commitStarted) == 0
                ? AuditAppendOutcome.Unavailable()
                : AuditAppendOutcome.Unknown();
        }
    }

    private async Task<AuditAppendOutcome> AppendCoreAsync(
        AuditIntent intent,
        Action commitStarted,
        CancellationToken cancellationToken)
    {
        var commitMayHaveStarted = false;
        try
        {
            await using var anchor = await contextFactory.CreateDbContextAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await anchor.Database.OpenConnectionAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var connection = anchor.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var enlistment = await AuditTransactionContext.CreateAsync(connection, transaction, cancellationToken);
            var staged = await enlistment.StageAsync(intent, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (staged.Status == AuditTransactionStageStatus.Staged)
            {
                commitMayHaveStarted = true;
                commitStarted();
            }

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
            return commitMayHaveStarted ? AuditAppendOutcome.Unknown() : AuditAppendOutcome.Unavailable();
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
