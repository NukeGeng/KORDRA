using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;

namespace Kordra.Modules.Messaging.Infrastructure.Recovery;

public enum MessageRecoveryAction { ScheduleRetry, Reject, Expired, AttemptsExhausted, InspectUnknown }
public enum MessageTimerState { NotDue, Ready, Expired }

/// <summary>Immutable planning data only. Not a persisted timer, permit, handler dispatch or commit receipt.</summary>
public sealed class MessageRetrySchedule
{
    public MessageBinding Binding { get; }
    public string PayloadHash { get; }
    public int NextAttempt { get; }
    public DateTimeOffset DueAtUtc { get; }
    internal MessageRetrySchedule(VerifiedMessageEnvelope envelope, int nextAttempt, DateTimeOffset dueAtUtc)
    { Binding = envelope.Binding; PayloadHash = envelope.PayloadHash; NextAttempt = nextAttempt; DueAtUtc = dueAtUtc; }
    public override string ToString() => "MessageRetrySchedule [private metadata]";
}

public sealed record MessageRecoveryDecision(MessageRecoveryAction Action, MessageRetrySchedule? Schedule);

/// <summary>
/// Pure bounded planning under Durable Messaging v1. No exception classification by message text,
/// native retry registration, automatic replay or clock mutation. The owning coordinator supplies
/// current effective time and a classified failure after determining the actual commit boundary.
/// </summary>
public static class MessageRetryPlanner
{
    private static readonly MessageFailurePolicy Policy = new();

    public static MessageRecoveryDecision AfterFailure(VerifiedMessageEnvelope envelope, MessageFailureKind failure,
        int failedAttempt, DateTimeOffset effectiveNow)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!Enum.IsDefined(failure) || failedAttempt < 1 || failedAttempt > Policy.MaximumAttempts)
            throw new ArgumentException("Known failure kind and bounded actual attempt required.");
        RequireTime(envelope.Binding, effectiveNow);
        // Expiry/cancellation cannot turn a possible committed effect into a safe automatic retry.
        if (failure == MessageFailureKind.UnknownCommit) return new(MessageRecoveryAction.InspectUnknown, null);
        if (failure == MessageFailureKind.Reject) return new(MessageRecoveryAction.Reject, null);
        if (effectiveNow >= envelope.Binding.DeadlineUtc) return new(MessageRecoveryAction.Expired, null);
        var delay = Policy.RetryDelay(failure, failedAttempt);
        if (delay is null) return new(MessageRecoveryAction.AttemptsExhausted, null);
        if (delay.Value >= envelope.Binding.DeadlineUtc - effectiveNow)
            return new(MessageRecoveryAction.Expired, null);
        var due = effectiveNow.ToUniversalTime() + delay.Value;
        return new(MessageRecoveryAction.ScheduleRetry, new(envelope, failedAttempt + 1, due));
    }

    public static MessageTimerState InspectTimer(MessageRetrySchedule schedule, DateTimeOffset effectiveNow)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        RequireTime(schedule.Binding, effectiveNow);
        if (effectiveNow >= schedule.Binding.DeadlineUtc) return MessageTimerState.Expired;
        return effectiveNow < schedule.DueAtUtc ? MessageTimerState.NotDue : MessageTimerState.Ready;
    }

    private static void RequireTime(MessageBinding binding, DateTimeOffset effectiveNow)
    {
        if (effectiveNow == default || effectiveNow < binding.OccurredAtUtc)
            throw new ArgumentException("Current effective time cannot precede original message creation.");
    }
}
