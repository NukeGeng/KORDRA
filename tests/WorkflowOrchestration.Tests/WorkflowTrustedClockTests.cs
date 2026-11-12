using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowTrustedClockTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LongIdleIsNotClockSkewAndPersistedFloorDoesNotExpire()
    {
        var time = new ManualClock(); var clock = new WorkflowTrustedClock(time);
        clock.AdoptPersistedFloor(Now.AddDays(-30)); clock.CheckDatabaseUtc(Now);
        time.Advance(TimeSpan.FromHours(3), TimeSpan.FromHours(3));
        Assert.Equal(time.Utc, clock.GetUtcNow()); clock.CheckDatabaseUtc(time.Utc);
    }

    [Fact]
    public void SmallWallRollbackNeverDecreasesEffectiveUtc()
    {
        var time = new ManualClock(); var clock = new WorkflowTrustedClock(time);
        var original = clock.GetUtcNow();
        time.Advance(TimeSpan.FromMilliseconds(-500), TimeSpan.FromMilliseconds(100));
        Assert.Equal(original, clock.GetUtcNow());
        time.Advance(TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(100));
        Assert.Equal(original.AddMilliseconds(100), clock.GetUtcNow());
    }

    [Theory]
    [InlineData(2, 0)][InlineData(-2, 0)][InlineData(0, 2)]
    public void WallMonotonicDisagreementLatchesUntilExplicitNewClock(double wall, double monotonic)
    {
        var time = new ManualClock(); var clock = new WorkflowTrustedClock(time); clock.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(wall), TimeSpan.FromSeconds(monotonic));
        Assert.Throws<WorkflowClockTrustException>(() => clock.GetUtcNow());
        time.Advance(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        Assert.Throws<WorkflowClockTrustException>(() => clock.GetUtcNow());
    }

    [Fact]
    public void BackwardsMonotonicStampCannotExtendAnyBudget()
    {
        var time = new ManualClock(); var clock = new WorkflowTrustedClock(time); var budget = clock.BeginOperation(Wake(), TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.Zero, TimeSpan.FromTicks(-1));
        Assert.Throws<WorkflowClockTrustException>(() => budget.ThrowIfExpired());
    }

    [Fact]
    public void FloorAheadByOneSecondClampsButLargerOrDecreasingFloorFaults()
    {
        var clock = new WorkflowTrustedClock(new ManualClock());
        clock.AdoptPersistedFloor(Now.AddSeconds(1)); Assert.Equal(Now.AddSeconds(1), clock.GetUtcNow());
        Assert.Throws<WorkflowClockTrustException>(() => clock.AdoptPersistedFloor(Now));
        var future = new WorkflowTrustedClock(new ManualClock());
        Assert.Throws<WorkflowClockTrustException>(() => future.AdoptPersistedFloor(Now.AddSeconds(1).AddTicks(1)));
    }

    [Theory]
    [InlineData(1)][InlineData(-1)]
    public void DatabaseDisagreementBeyondOneSecondFaults(int direction)
    {
        var clock = new WorkflowTrustedClock(new ManualClock());
        Assert.Throws<WorkflowClockTrustException>(() => clock.CheckDatabaseUtc(Now.AddTicks(direction * (TimeSpan.TicksPerSecond + 1))));
        Assert.Throws<WorkflowClockTrustException>(() => clock.GetUtcNow());
    }

    [Fact]
    public void DatabaseRollbackCannotBeHiddenByPermittedLocalSampleSkew()
    {
        var time = new ManualClock(); var clock = new WorkflowTrustedClock(time); clock.CheckDatabaseUtc(Now);
        time.Advance(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        Assert.Throws<WorkflowClockTrustException>(() => clock.CheckDatabaseUtc(Now.AddTicks(-1)));
    }

    [Fact]
    public void DatabasePaceDisagreementIsRejectedEvenWhenEachLocalOffsetIsUnderOneSecond()
    {
        var time = new ManualClock(); var clock = new WorkflowTrustedClock(time); clock.CheckDatabaseUtc(Now.AddMilliseconds(-800));
        time.Advance(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        Assert.Throws<WorkflowClockTrustException>(() => clock.CheckDatabaseUtc(time.Utc.AddMilliseconds(800)));
    }

    [Fact]
    public void MonotonicBudgetExpiresWithoutPoisoningTrustedClockOrRefreshingDeadline()
    {
        var time = new ManualClock(); var clock = new WorkflowTrustedClock(time); var wake = Wake();
        var budget = clock.BeginOperation(wake, TimeSpan.FromMilliseconds(200));
        time.Advance(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(200));
        Assert.Throws<TimeoutException>(() => budget.ThrowIfExpired());
        Assert.Equal(time.Utc, clock.GetUtcNow()); Assert.Equal(wake.DeadlineUtc, budget.DeadlineUtc);
        Assert.Equal(wake.DescriptorHash, budget.DescriptorHash); Assert.DoesNotContain(wake.DescriptorHash, budget.ToString());
    }

    [Fact]
    public void RemainingOriginalLifetimeBoundsALaterBudget()
    {
        var time = new ManualClock(); var clock = new WorkflowTrustedClock(time); var wake = Wake(); clock.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(119), TimeSpan.FromSeconds(119));
        var budget = clock.BeginOperation(wake, TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1));
        Assert.Throws<TimeoutException>(() => budget.ThrowIfExpired());
        Assert.Equal(wake.DeadlineUtc, budget.DeadlineUtc);
    }

    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(121)]
    public void InvalidOperationBudgetIsRejectedWithoutClosingClock(int seconds)
    {
        var clock = new WorkflowTrustedClock(new ManualClock());
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.BeginOperation(Wake(), TimeSpan.FromSeconds(seconds)));
        Assert.Equal(Now, clock.GetUtcNow());
    }

    [Fact]
    public void OriginalDeadlineEqualityRejectsAdmission()
    {
        var time = new ManualClock(); var clock = new WorkflowTrustedClock(time); clock.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120));
        Assert.Throws<InvalidOperationException>(() => clock.BeginOperation(Wake(), TimeSpan.FromSeconds(1)));
    }

    private static WorkflowWakeOperation Wake()
    {
        var spec = new WorkflowSpecification(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test,
            Now.AddMinutes(-1), Now.AddHours(1), Now);
        return new(spec, Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 3, Now, Now.AddSeconds(120));
    }
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Utc { get; private set; } = Now;
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan wall, TimeSpan monotonic) { Utc += wall; _timestamp += monotonic.Ticks; }
    }
}
