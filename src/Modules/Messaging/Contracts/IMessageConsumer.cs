namespace Kordra.Modules.Messaging.Contracts;

/// <summary>Typed consumer contract only; resolving it is never an authorization decision.</summary>
public interface IMessageConsumer<T>
{
    ValueTask HandleAsync(T message, CancellationToken cancellationToken);
}

public enum MessageFailureKind { Reject, Transient, UnknownCommit }

/// <summary>Closed failure categories. UnknownCommit is reconciliation-only, never automatic retry.</summary>
public sealed record MessageFailurePolicy
{
    public int MaximumAttempts => 5;
    public TimeSpan MaximumLifetime => TimeSpan.FromSeconds(120);
    public TimeSpan? RetryDelay(MessageFailureKind kind, int failedAttempt) => kind == MessageFailureKind.Transient
        ? failedAttempt switch
        {
            1 => TimeSpan.FromSeconds(1), 2 => TimeSpan.FromSeconds(5),
            3 => TimeSpan.FromSeconds(15), 4 => TimeSpan.FromSeconds(60), _ => null
        } : null;
}
