using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class MessageRetryPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FiveAttemptsKeepOriginalHashDeadlineAndDoNotInvokeConsumer()
    {
        var envelope = Envelope(); var time = Now;
        foreach (var attempt in new[] { 1, 2, 3, 4 })
        {
            var decision = MessageRetryPlanner.AfterFailure(envelope, MessageFailureKind.Transient, attempt, time);
            Assert.Equal(MessageRecoveryAction.ScheduleRetry, decision.Action);
            var schedule = Assert.IsType<MessageRetrySchedule>(decision.Schedule);
            Assert.Same(envelope.Binding, schedule.Binding); Assert.Equal(envelope.PayloadHash, schedule.PayloadHash);
            Assert.Equal(attempt + 1, schedule.NextAttempt);
            Assert.Equal(MessageTimerState.NotDue, MessageRetryPlanner.InspectTimer(schedule, schedule.DueAtUtc.AddTicks(-1)));
            Assert.Equal(MessageTimerState.Ready, MessageRetryPlanner.InspectTimer(schedule, schedule.DueAtUtc));
            time = schedule.DueAtUtc;
        }
        Assert.Equal(Now.AddSeconds(81), time);
        var exhausted = MessageRetryPlanner.AfterFailure(envelope, MessageFailureKind.Transient, 5, time);
        Assert.Equal(MessageRecoveryAction.AttemptsExhausted, exhausted.Action); Assert.Null(exhausted.Schedule);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(119)]
    [InlineData(120)]
    [InlineData(100000)]
    public void UnknownCommitNeverBecomesRetryOrOrdinaryExpiry(int elapsedSeconds)
    {
        var decision = MessageRetryPlanner.AfterFailure(Envelope(), MessageFailureKind.UnknownCommit, 1, Now.AddSeconds(elapsedSeconds));
        Assert.Equal(MessageRecoveryAction.InspectUnknown, decision.Action); Assert.Null(decision.Schedule);
    }

    [Fact]
    public void DeadlineDoesNotExtendAndExpiredTimerCannotRun()
    {
        var envelope = Envelope();
        var noRoom = MessageRetryPlanner.AfterFailure(envelope, MessageFailureKind.Transient, 1, Now.AddSeconds(119));
        Assert.Equal(MessageRecoveryAction.Expired, noRoom.Action); Assert.Null(noRoom.Schedule);
        var planned = MessageRetryPlanner.AfterFailure(envelope, MessageFailureKind.Transient, 1, Now);
        Assert.Equal(MessageTimerState.Expired, MessageRetryPlanner.InspectTimer(planned.Schedule!, Now.AddSeconds(120)));
        var expired = MessageRetryPlanner.AfterFailure(envelope, MessageFailureKind.Transient, 1, Now.AddSeconds(121));
        Assert.Equal(MessageRecoveryAction.Expired, expired.Action); Assert.Null(expired.Schedule);
    }

    [Fact]
    public void RejectionAndInvalidAttemptOrTimeNeverSchedule()
    {
        var envelope = Envelope();
        var rejected = MessageRetryPlanner.AfterFailure(envelope, MessageFailureKind.Reject, 1, Now);
        Assert.Equal(MessageRecoveryAction.Reject, rejected.Action); Assert.Null(rejected.Schedule);
        foreach (var attempt in new[] { -1, 0, 6, int.MaxValue })
            Assert.Throws<ArgumentException>(() => MessageRetryPlanner.AfterFailure(envelope, MessageFailureKind.Transient, attempt, Now));
        Assert.Throws<ArgumentException>(() => MessageRetryPlanner.AfterFailure(envelope, (MessageFailureKind)99, 1, Now));
        Assert.Throws<ArgumentException>(() => MessageRetryPlanner.AfterFailure(envelope, MessageFailureKind.Transient, 1, Now.AddTicks(-1)));
    }

    [Fact]
    public void RemainingLifetimeCheckCannotOverflowAtCalendarBoundary()
    {
        var created = DateTimeOffset.MaxValue.AddSeconds(-120);
        var decision = MessageRetryPlanner.AfterFailure(Envelope(created), MessageFailureKind.Transient, 4,
            DateTimeOffset.MaxValue.AddSeconds(-10));
        Assert.Equal(MessageRecoveryAction.Expired, decision.Action); Assert.Null(decision.Schedule);
    }

    private static VerifiedMessageEnvelope Envelope(DateTimeOffset? createdAt = null)
    {
        var created = createdAt ?? Now;
        var registry = new CompiledMessageRegistry([new CompiledMessageRegistration<bool>(new("fixture.observe", 1, MessageScopeKind.Tenant, false),
            new BooleanCodec(), new Consumer(), "fixture.observe", new())]);
        var binding = new MessageBinding(new(Guid.NewGuid(), MessageScope.Tenant(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), 1, "fixture.observe"), 1, null, created, created.AddSeconds(120));
        var codec = new MessageEnvelopeCodec(registry);
        var wire = codec.Write(binding, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), "{\"enabled\":true}"u8);
        return codec.Read(wire, MessageEnvelopeCodec.ContentType, binding.Identity.InstallationId, binding.Identity.Scope, created);
    }
    private sealed class BooleanCodec : IMessagePayloadCodec<bool>
    {
        public bool Read(JsonElement payload) => payload.GetProperty("enabled").GetBoolean();
        public void Write(Utf8JsonWriter writer, bool payload) { writer.WriteStartObject(); writer.WriteBoolean("enabled", payload); writer.WriteEndObject(); }
    }
    private sealed class Consumer : IMessageConsumer<bool>
    { public ValueTask HandleAsync(bool message, CancellationToken cancellationToken) => throw new InvalidOperationException("Planner must not invoke a handler."); }
}
