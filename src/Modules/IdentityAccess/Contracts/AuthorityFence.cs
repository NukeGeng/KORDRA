using System.Data.Common;

namespace Kordra.Modules.IdentityAccess.Contracts;

public enum AuthorityFenceMode { Inactive, Enforced }

/// <summary>
/// 12.3c (D1): every invalidating writer acquires the installation authority fence on its OWN root transaction, after the
/// API writer gate and before reading any authorization fact, and advances the epoch at most once before committing an
/// authority-relevant change. <see cref="AuthorityFence.Inactive"/> is the explicit pre-activation mode (D3).
/// </summary>
public interface IAuthorityFence
{
    AuthorityFenceMode Mode { get; }
    Task<IAuthorityFenceLease> AcquireAsync(DbConnection connection, DbTransaction root, CancellationToken ct = default);
}

public interface IAuthorityFenceLease
{
    /// <summary>The epoch observed under the lock; null in <see cref="AuthorityFenceMode.Inactive"/>.</summary>
    long? Epoch { get; }
    bool Advanced { get; }
    /// <summary>Advances the epoch in the same root (D2). A second call on the same lease is a no-op.</summary>
    Task AdvanceAsync(string reasonCode, CancellationToken ct = default);
}

public static class AuthorityFence
{
    /// <summary>Pre-activation mode: no database access and no ordering claim. Activation must refuse it (D3).</summary>
    public static IAuthorityFence Inactive { get; } = new InactiveFence();

    public static bool IsReasonCode(string? reasonCode) => reasonCode is { Length: > 0 and <= 64 } &&
        reasonCode[0] is >= 'a' and <= 'z' && reasonCode.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-');

    private sealed class InactiveFence : IAuthorityFence
    {
        public AuthorityFenceMode Mode => AuthorityFenceMode.Inactive;
        public Task<IAuthorityFenceLease> AcquireAsync(DbConnection connection, DbTransaction root, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(root);
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IAuthorityFenceLease>(new InactiveLease());
        }
    }

    private sealed class InactiveLease : IAuthorityFenceLease
    {
        public long? Epoch => null;
        public bool Advanced { get; private set; }
        public Task AdvanceAsync(string reasonCode, CancellationToken ct = default)
        {
            if (!IsReasonCode(reasonCode)) throw new ArgumentException("Invalid authority reason code.", nameof(reasonCode));
            ct.ThrowIfCancellationRequested();
            Advanced = true;
            return Task.CompletedTask;
        }
    }
}
