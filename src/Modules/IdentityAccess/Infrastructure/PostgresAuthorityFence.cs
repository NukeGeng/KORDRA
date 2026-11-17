using System.Data.Common;
using Kordra.Modules.IdentityAccess.Contracts;
using Npgsql;

namespace Kordra.Modules.IdentityAccess.Infrastructure;

/// <summary>
/// 12.3c (D1) enforced fence: <c>iam_workflow.acquire_fence</c> / <c>advance_epoch</c> on the writer's own root through the
/// IAM function port. The session login must be an active <c>api</c>-kind service of this installation (D7 for offline
/// writers). Not registered in DI until activation; owner errors surface as <see cref="PostgresException"/>.
/// </summary>
public sealed class PostgresAuthorityFence(Guid installationId) : IAuthorityFence
{
    public AuthorityFenceMode Mode => AuthorityFenceMode.Enforced;

    public async Task<IAuthorityFenceLease> AcquireAsync(DbConnection connection, DbTransaction root, CancellationToken ct = default)
    {
        if (installationId == Guid.Empty) throw new InvalidOperationException("Enforced authority fence needs an installation.");
        if (connection is not NpgsqlConnection npgsql || root is not NpgsqlTransaction transaction)
            throw new InvalidOperationException("Enforced authority fence needs the writer's PostgreSQL root.");
        var port = new WorkflowAuthorityFunctionPort(npgsql, transaction);
        var epoch = await port.AcquireFenceAsync(installationId, ct);
        return new Lease(port, installationId, epoch);
    }

    private sealed class Lease(WorkflowAuthorityFunctionPort port, Guid installationId, long epoch) : IAuthorityFenceLease
    {
        public long? Epoch { get; private set; } = epoch;
        public bool Advanced { get; private set; }

        public async Task AdvanceAsync(string reasonCode, CancellationToken ct = default)
        {
            if (!AuthorityFence.IsReasonCode(reasonCode)) throw new ArgumentException("Invalid authority reason code.", nameof(reasonCode));
            if (Advanced) return;
            Epoch = await port.AdvanceEpochAsync(installationId, reasonCode, ct);
            Advanced = true;
        }
    }
}
