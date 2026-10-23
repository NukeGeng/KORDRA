using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using Kordra.Modules.AuditCompliance.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kordra.Modules.AuditCompliance.Infrastructure;

public enum AuditTransactionStageStatus
{
    Staged,
    AlreadyDurable,
    Rejected,
    Unavailable
}

/// <summary>Transaction-local staging result; Staged is never reported as a durable append outcome.</summary>
public sealed record AuditTransactionStageOutcome(
    AuditTransactionStageStatus Status,
    AuditReceipt? Receipt,
    string? ReasonCode);

/// <summary>
/// Enlists AUD-owned writes in a caller-owned PostgreSQL transaction without exposing EF or Npgsql types
/// through the module Contracts assembly. The caller commits owner state through CommitAsync's shared transaction.
/// </summary>
public sealed class AuditTransactionContext : IAsyncDisposable
{
    private readonly DbTransaction _transaction;
    private readonly AuditDbContext _auditContext;
    private readonly long _deadlineTimestamp;
    private readonly CancellationToken _lifetimeCancellationToken;
    private readonly TimeProvider _timeProvider;
    private AuditEntry? _entry;
    private bool _completed;
    private bool _commitAttempted;

    private AuditTransactionContext(
        DbTransaction transaction, AuditDbContext auditContext, CancellationToken cancellationToken, TimeProvider timeProvider)
    {
        _transaction = transaction;
        _auditContext = auditContext;
        _lifetimeCancellationToken = cancellationToken;
        _timeProvider = timeProvider;
        _deadlineTimestamp = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
    }

    private IOfflineAuditLease? _offlineLease;

    public static async Task<AuditTransactionContext> CreateOfflineAsync(DbConnection connection, DbTransaction transaction,
        IOfflineAuditLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease); lease.RequireHeld();
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("Offline AUD requires the open enlisted transaction.");
        var context = new AuditDbContext(AuditDbContextFactory.CreateOfflineTransactionOptions(connection));
        try
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            return new AuditTransactionContext(transaction, context, cancellationToken, TimeProvider.System) { _offlineLease = lease };
        }
        catch { await context.DisposeAsync(); throw; }
    }

    public static async Task<AuditTransactionContext> CreateAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException("AUD enlistment requires an open connection and its active transaction.");
        }

        var options = AuditDbContextFactory.CreateTransactionOptions(connection);
        var context = new AuditDbContext(options);
        try
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            return new AuditTransactionContext(transaction, context, cancellationToken, timeProvider ?? TimeProvider.System);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async Task<AuditTransactionStageOutcome> StageAsync(
        AuditIntent intent,
        CancellationToken cancellationToken = default)
    {
        _offlineLease?.RequireHeld();
        using var budget = CreateBudget(cancellationToken);
        cancellationToken = budget.Token;
        if (_completed || _entry is not null)
        {
            return new AuditTransactionStageOutcome(AuditTransactionStageStatus.Rejected, null, "transaction_not_open");
        }

        if (intent is null)
        {
            return new AuditTransactionStageOutcome(AuditTransactionStageStatus.Rejected, null, "invalid_intent");
        }

        CanonicalizedAuditIntent canonical;
        try
        {
            canonical = AuditIntentCanonicalizer.Canonicalize(intent);
        }
        catch (AuditIntentValidationException)
        {
            return new AuditTransactionStageOutcome(AuditTransactionStageStatus.Rejected, null, "invalid_intent");
        }

        try
        {
            var existing = await _auditContext.Entries.AsNoTracking()
                .SingleOrDefaultAsync(entry => entry.IntentId == canonical.Intent.IntentId, cancellationToken);
            if (existing is not null)
            {
                var sameHash = CryptographicOperations.FixedTimeEquals(existing.CanonicalIntentHash, canonical.Hash);
                if (!await TryRollbackAsync(cancellationToken))
                {
                    return new AuditTransactionStageOutcome(AuditTransactionStageStatus.Unavailable, null, null);
                }

                return sameHash
                    ? new AuditTransactionStageOutcome(AuditTransactionStageStatus.AlreadyDurable, existing.ToReceipt(), null)
                    : new AuditTransactionStageOutcome(AuditTransactionStageStatus.Rejected, null, "intent_conflict");
            }

            _entry = AuditEntry.Create(canonical, _timeProvider.GetUtcNow());
            _auditContext.Entries.Add(_entry);
            return new AuditTransactionStageOutcome(AuditTransactionStageStatus.Staged, null, null);
        }
        catch
        {
            await TryRollbackAsync(cancellationToken);
            return new AuditTransactionStageOutcome(AuditTransactionStageStatus.Unavailable, null, null);
        }
    }

    /// <summary>Commits the enlisted owner state and AUD row; only a confirmed commit returns Durable.</summary>
    public async Task<AuditAppendOutcome> CommitAsync(CancellationToken cancellationToken = default)
    {
        _offlineLease?.RequireHeld();
        using var budget = CreateBudget(cancellationToken);
        cancellationToken = budget.Token;
        if (_completed || _entry is null)
        {
            return AuditAppendOutcome.Rejected("transaction_not_staged");
        }

        try
        {
            await _auditContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            var rolledBack = await TryRollbackAsync(cancellationToken);
            if (!rolledBack)
            {
                return AuditAppendOutcome.Unknown();
            }

            if (exception.InnerException is PostgresException postgresException &&
                postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                postgresException.ConstraintName == "uq_aud_intents_intent_id")
            {
                return await ResolveUniqueConflictAsync(cancellationToken);
            }

            return AuditAppendOutcome.Unavailable();
        }
        catch
        {
            var rolledBack = await TryRollbackAsync(cancellationToken);
            return rolledBack ? AuditAppendOutcome.Unavailable() : AuditAppendOutcome.Unknown();
        }

        _commitAttempted = true;
        try
        {
            await _transaction.CommitAsync(cancellationToken);
            _completed = true;
            return AuditAppendOutcome.Durable(_entry.ToReceipt());
        }
        catch
        {
            // The server may have committed even when its acknowledgement was lost. Never replay here.
            _completed = true;
            return AuditAppendOutcome.Unknown();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_completed || _commitAttempted)
        {
            return;
        }

        _ = await TryRollbackAsync(cancellationToken);
    }

    private async Task<AuditAppendOutcome> ResolveUniqueConflictAsync(CancellationToken cancellationToken)
    {
        if (_entry is null)
        {
            return AuditAppendOutcome.Unavailable();
        }

        using var lookupDeadline = CreateBudget(cancellationToken);
        lookupDeadline.CancelAfter(TimeSpan.FromMilliseconds(Math.Min(100,
            Math.Max(0, (_deadlineTimestamp - Stopwatch.GetTimestamp()) * 1000d / Stopwatch.Frequency))));
        try
        {
            await _auditContext.Database.UseTransactionAsync((DbTransaction?)null, lookupDeadline.Token);
            var existing = await _auditContext.Entries.AsNoTracking()
                .SingleOrDefaultAsync(entry => entry.IntentId == _entry.IntentId, lookupDeadline.Token);
            if (existing is null)
            {
                return AuditAppendOutcome.Unavailable();
            }

            return CryptographicOperations.FixedTimeEquals(existing.CanonicalIntentHash, _entry.CanonicalIntentHash)
                ? AuditAppendOutcome.Durable(existing.ToReceipt())
                : AuditAppendOutcome.Rejected("intent_conflict");
        }
        catch
        {
            return AuditAppendOutcome.Unknown();
        }
    }

    private async Task<bool> TryRollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_completed || _commitAttempted)
        {
            return _completed;
        }

        try
        {
            using var budget = CreateBudget(cancellationToken);
            await _transaction.RollbackAsync(budget.Token);
            _completed = true;
            await _auditContext.Database.UseTransactionAsync((DbTransaction?)null, budget.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private CancellationTokenSource CreateBudget(CancellationToken cancellationToken)
    {
        var ticksLeft = _deadlineTimestamp - Stopwatch.GetTimestamp();
        var remaining = ticksLeft <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)ticksLeft / Stopwatch.Frequency);
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellationToken);
        budget.CancelAfter(remaining);
        return budget;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed && !_commitAttempted)
        {
            _ = await TryRollbackAsync();
        }

        await _auditContext.DisposeAsync();
    }
}
