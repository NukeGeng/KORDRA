using System.Collections.Concurrent;
namespace Kordra.Messaging.Tests;
public sealed record SyntheticQueuedLoggingProbe(Guid CaseId, string SyntheticToken);
public sealed class SyntheticLoggingTransientFailure(string token) : Exception("Synthetic transient failure", new ArgumentException(token));
public sealed class SyntheticQueuedLoggingState
{
    public ConcurrentDictionary<Guid, int> Attempts { get; } = new();
    public TaskCompletionSource<Guid> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
public sealed class SyntheticQueuedLoggingHandler
{
    public static void Handle(SyntheticQueuedLoggingProbe message, SyntheticQueuedLoggingState state)
    {
        var attempt = state.Attempts.AddOrUpdate(message.CaseId, 1, static (_, old) => old + 1);
        if (attempt == 1) throw new SyntheticLoggingTransientFailure(message.SyntheticToken);
        state.Completed.TrySetResult(message.CaseId);
    }
}
