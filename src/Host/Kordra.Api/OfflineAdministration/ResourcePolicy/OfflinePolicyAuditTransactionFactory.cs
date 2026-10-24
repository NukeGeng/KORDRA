using System.Data.Common;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;

namespace Kordra.Api;

public interface IOfflinePolicyAuditTransactionFactory
{
    ValueTask<IOfflinePolicyAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, IOfflineAuditLease lease, CancellationToken ct = default);
}
public interface IOfflinePolicyAuditTransaction : IAsyncDisposable
{
    Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct = default);
    Task<AuditAppendOutcome> CommitAsync(CancellationToken ct = default);
}
public sealed class OfflinePolicyAuditTransactionFactory : IOfflinePolicyAuditTransactionFactory
{
    public async ValueTask<IOfflinePolicyAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, IOfflineAuditLease lease, CancellationToken ct = default) =>
        new Enlisted(await AuditTransactionContext.CreateOfflineAsync(connection, transaction, lease, ct));
    private sealed class Enlisted(AuditTransactionContext context) : IOfflinePolicyAuditTransaction
    {
        public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct = default) => context.StageAsync(intent, ct);
        public Task<AuditAppendOutcome> CommitAsync(CancellationToken ct = default) => context.CommitAsync(ct);
        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
