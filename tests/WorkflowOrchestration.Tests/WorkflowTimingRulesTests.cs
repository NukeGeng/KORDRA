using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowTimingRulesTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static WorkflowSpecification Spec(DateTimeOffset? created = null, DateTimeOffset? deadline = null)
    {
        var start = created ?? Created; var end = deadline ?? start.AddHours(24);
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test,
            start, end, start.AddHours(1));
    }

    [Fact]
    public void HourLongTimerGetsOnly120SecondsWhenDueWithoutChangingBusinessHashOrDeadline()
    {
        var spec = Spec(); var hash = spec.SpecificationHash;
        var window = WorkflowTimingRules.DueWakeWindow(spec, Created.AddHours(1));
        Assert.Equal(Created.AddHours(1), window.OccurredAtUtc);
        Assert.Equal(Created.AddHours(1).AddSeconds(120), window.DeadlineUtc);
        Assert.Equal(Created.AddHours(24), spec.DeadlineUtc); Assert.Equal(hash, spec.SpecificationHash);
        Assert.Equal(120, (window.DeadlineUtc - window.OccurredAtUtc).TotalSeconds);
    }

    [Fact]
    public void WakeDeadlineStaysStrictlyBeforeOriginalWorkflowDeadlineAndNeverRenewsIt()
    {
        var spec = Spec(); var now = spec.DeadlineUtc.AddSeconds(-5);
        var first = WorkflowTimingRules.DueWakeWindow(spec, now);
        var successor = WorkflowTimingRules.DueWakeWindow(spec, now.AddSeconds(2));
        Assert.Equal(spec.DeadlineUtc.AddTicks(-1), first.DeadlineUtc);
        Assert.Equal(first.DeadlineUtc, successor.DeadlineUtc);
        var last = WorkflowTimingRules.DueWakeWindow(spec, spec.DeadlineUtc.AddTicks(-2));
        Assert.Equal(1, (last.DeadlineUtc - last.OccurredAtUtc).Ticks);
    }

    [Theory]
    [InlineData(-1)][InlineData(0)][InlineData(3599)]
    public void ClockBeforeCreationAndNotYetDueCannotCreateWake(int seconds)
        => Assert.Throws<InvalidOperationException>(() => WorkflowTimingRules.DueWakeWindow(Spec(), Created.AddSeconds(seconds)));

    [Theory]
    [InlineData(-1)][InlineData(0)][InlineData(1)]
    public void NoPositiveWindowAtOrAfterLastPermittedTick(long ticks)
    {
        var spec = Spec();
        Assert.Throws<InvalidOperationException>(() => WorkflowTimingRules.DueWakeWindow(spec, spec.DeadlineUtc.AddTicks(ticks)));
    }

    [Fact]
    public void UtcNormalizationAndMaximumDateArithmeticRemainBounded()
    {
        var spec = Spec(); var offsetNow = Created.AddHours(1).ToOffset(TimeSpan.FromHours(7));
        Assert.Equal(TimeSpan.Zero, WorkflowTimingRules.DueWakeWindow(spec, offsetNow).OccurredAtUtc.Offset);
        var start = DateTimeOffset.MaxValue.AddHours(-24); var far = Spec(start, DateTimeOffset.MaxValue);
        var last = WorkflowTimingRules.DueWakeWindow(far, far.DeadlineUtc.AddTicks(-2));
        Assert.Equal(DateTimeOffset.MaxValue.AddTicks(-1), last.DeadlineUtc);
    }

    [Theory]
    [InlineData(0, 1)][InlineData(8, 9)][InlineData(9, 10)]
    public void SchedulerAttemptHasFiniteNonRenewingBudget(int before, int after)
        => Assert.Equal(after, WorkflowTimingRules.NextSchedulerAttempt(before));

    [Theory]
    [InlineData(-1)][InlineData(10)][InlineData(11)][InlineData(int.MaxValue)]
    public void InvalidOrExhaustedCounterCannotBecomeAnotherAttempt(int attempts)
        => Assert.Throws<InvalidOperationException>(() => WorkflowTimingRules.NextSchedulerAttempt(attempts));
}
