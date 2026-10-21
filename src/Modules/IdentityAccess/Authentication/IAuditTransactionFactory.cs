using System.Data.Common;
using Kordra.Modules.AuditCompliance.Contracts;

namespace Kordra.Modules.IdentityAccess.Authentication;

/// <summary>Host-composed bridge to AUD's transaction-enlisted append; IAM never addresses AUD storage.</summary>
public interface IIdentityAuditTransactionFactory
{
    ValueTask<IIdentityAuditTransaction> EnlistAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken = default);
}

/// <summary>Stages one AUD-owned record with the caller's owner mutation and shared commit.</summary>
public interface IIdentityAuditTransaction : IAsyncDisposable
{
    Task<AuditAppendOutcome> CommitWithOwnerMutationAsync(
        AuditIntent intent,
        Func<CancellationToken, Task> saveOwnerMutationAsync,
        CancellationToken cancellationToken = default);
}
