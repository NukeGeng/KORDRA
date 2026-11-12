using Kordra.Modules.WorkflowOrchestration.Domain;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure;

public sealed class WorkflowClockTrustException() : InvalidOperationException("Workflow clock is untrusted; admission must remain closed.");

/// <summary>Process clock guard only. Floor is owner-loaded; samples/budgets never confer business authority.</summary>
public sealed class WorkflowTrustedClock(TimeProvider timeProvider)
{
    private static readonly TimeSpan MaximumDisagreement = TimeSpan.FromSeconds(1);
    private readonly TimeProvider _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly object _sync = new();
    private DateTimeOffset? _lastWall, _effective, _persistedFloor, _lastDatabase;
    private long _lastStamp, _lastDatabaseStamp;
    private bool _faulted;

    public DateTimeOffset GetUtcNow() { lock (_sync) return Sample().Utc; }

    public void AdoptPersistedFloor(DateTimeOffset floor)
    {
        lock (_sync)
        {
            var sample = Sample(); floor = floor.ToUniversalTime();
            if (floor == default || (_persistedFloor is { } prior && floor < prior) || floor - sample.Utc > MaximumDisagreement)
                throw Fault();
            _persistedFloor = floor;
            if (floor > _effective) _effective = floor;
        }
    }

    /// <summary>Call immediately after the bounded DB sample; a stale sample is never an idle-floor exception.</summary>
    public void CheckDatabaseUtc(DateTimeOffset databaseUtc)
    {
        lock (_sync)
        {
            var sample = Sample(); databaseUtc = databaseUtc.ToUniversalTime();
            if (databaseUtc == default || Disagrees(databaseUtc - sample.Utc)) throw Fault();
            if (_lastDatabase is { } previous)
            {
                var elapsed = Elapsed(_lastDatabaseStamp, sample.Stamp);
                if (databaseUtc < previous || PaceDisagrees(databaseUtc - previous, elapsed)) throw Fault();
            }
            _lastDatabase = databaseUtc; _lastDatabaseStamp = sample.Stamp;
        }
    }

    public WorkflowOperationBudget BeginOperation(WorkflowWakeOperation wake, TimeSpan maximumDuration)
    {
        ArgumentNullException.ThrowIfNull(wake);
        if (maximumDuration <= TimeSpan.Zero || maximumDuration > WorkflowTimingRules.MaximumWakeLifetime)
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        lock (_sync)
        {
            var sample = Sample();
            if (sample.Utc < wake.OccurredAtUtc || sample.Utc >= wake.DeadlineUtc)
                throw new InvalidOperationException("Operation is outside the original wake window.");
            var remaining = wake.DeadlineUtc - sample.Utc;
            return new(this, wake.DescriptorHash, sample.Utc, wake.DeadlineUtc, sample.Stamp,
                remaining < maximumDuration ? remaining : maximumDuration);
        }
    }

    internal void CheckBudget(WorkflowOperationBudget budget)
    {
        lock (_sync)
        {
            var sample = Sample();
            if (sample.Utc >= budget.DeadlineUtc || Elapsed(budget.StartedStamp, sample.Stamp) >= budget.Duration)
                throw new TimeoutException("Workflow operation exhausted its original wall/monotonic budget.");
        }
    }

    private (DateTimeOffset Utc, long Stamp) Sample()
    {
        if (_faulted) throw new WorkflowClockTrustException();
        if (_time.TimestampFrequency <= 0) throw Fault();
        var wall = _time.GetUtcNow().ToUniversalTime(); var stamp = _time.GetTimestamp();
        if (wall == default) throw Fault();
        if (_lastWall is { } previous && PaceDisagrees(wall - previous, Elapsed(_lastStamp, stamp))) throw Fault();
        _lastWall = wall; _lastStamp = stamp;
        if (_effective is null || wall > _effective) _effective = wall;
        return (_effective.Value, stamp);
    }
    private TimeSpan Elapsed(long from, long to)
    {
        try
        {
            if (to < from || _time.TimestampFrequency <= 0) throw Fault();
            var elapsed = _time.GetElapsedTime(from, to);
            if (elapsed < TimeSpan.Zero) throw Fault();
            return elapsed;
        }
        catch (OverflowException) { throw Fault(); }
    }
    private bool PaceDisagrees(TimeSpan wallElapsed, TimeSpan monotonicElapsed)
    {
        try { return Disagrees(wallElapsed - monotonicElapsed); }
        catch (OverflowException) { throw Fault(); }
    }
    private static bool Disagrees(TimeSpan difference) => difference > MaximumDisagreement || difference < -MaximumDisagreement;
    private WorkflowClockTrustException Fault() { _faulted = true; return new(); }
}

/// <summary>Original descriptor-bound operation budget. It cannot renew a wake or be moved to another clock.</summary>
public sealed class WorkflowOperationBudget
{
    private readonly WorkflowTrustedClock _clock;
    public string DescriptorHash { get; }
    public DateTimeOffset AdmittedUtc { get; }
    public DateTimeOffset DeadlineUtc { get; }
    internal long StartedStamp { get; }
    internal TimeSpan Duration { get; }
    internal WorkflowOperationBudget(WorkflowTrustedClock clock, string hash, DateTimeOffset admittedUtc,
        DateTimeOffset deadlineUtc, long startedStamp, TimeSpan duration)
    { _clock = clock; DescriptorHash = hash; AdmittedUtc = admittedUtc; DeadlineUtc = deadlineUtc; StartedStamp = startedStamp; Duration = duration; }
    public void ThrowIfExpired() => _clock.CheckBudget(this);
    public void CheckDatabaseUtc(DateTimeOffset databaseUtc) { _clock.CheckDatabaseUtc(databaseUtc); ThrowIfExpired(); }
    public override string ToString() => "WorkflowOperationBudget [private, not authority]";
}
