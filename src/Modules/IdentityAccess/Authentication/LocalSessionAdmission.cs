namespace Kordra.Modules.IdentityAccess.Authentication;

/// <summary>Current self-session result whose admission lease may cover the protected response.</summary>
public sealed class LocalSessionAdmission : IAsyncDisposable
{
    private IAsyncDisposable? _lease;

    internal LocalSessionAdmission(LocalIdentityResult result, IAsyncDisposable? lease = null)
    {
        Result = result;
        _lease = lease;
    }

    public LocalIdentityResult Result { get; }

    public async ValueTask DisposeAsync()
    {
        var lease = Interlocked.Exchange(ref _lease, null);
        if (lease is not null)
        {
            await lease.DisposeAsync();
        }
    }
}
