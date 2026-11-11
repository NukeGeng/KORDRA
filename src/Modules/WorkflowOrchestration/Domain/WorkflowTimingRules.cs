namespace Kordra.Modules.WorkflowOrchestration.Domain;

/// <summary>Structural schedule limits only; no authority, custody, reconciliation or publication.</summary>
public static class WorkflowTimingRules
{
    public static readonly TimeSpan MaximumWakeLifetime = TimeSpan.FromSeconds(120);
    public const int MaximumSchedulerAttempts = 10;

    public static WorkflowWakeWindow DueWakeWindow(WorkflowSpecification specification, DateTimeOffset effectiveNow)
    {
        ArgumentNullException.ThrowIfNull(specification);
        effectiveNow = effectiveNow.ToUniversalTime();
        if (effectiveNow < specification.CreatedAtUtc || effectiveNow < specification.DueAtUtc ||
            effectiveNow >= specification.DeadlineUtc.AddTicks(-1))
            throw new InvalidOperationException("No due workflow wake window remains.");

        var lastPermitted = specification.DeadlineUtc.AddTicks(-1);
        var remaining = lastPermitted - effectiveNow;
        var deadline = remaining > MaximumWakeLifetime ? effectiveNow.Add(MaximumWakeLifetime) : lastPermitted;
        return new(effectiveNow, deadline);
    }

    /// <summary>A bounded counter calculation, not permission to retry an unresolved operation.</summary>
    public static int NextSchedulerAttempt(int previousAttempts)
    {
        if (previousAttempts is < 0 or >= MaximumSchedulerAttempts)
            throw new InvalidOperationException("Scheduler operation budget unavailable or exhausted.");
        return previousAttempts + 1;
    }
}

/// <summary>Deadline data for a new admitted scheduler operation; never a replay/permit.</summary>
public sealed record WorkflowWakeWindow
{
    public DateTimeOffset OccurredAtUtc { get; }
    public DateTimeOffset DeadlineUtc { get; }

    internal WorkflowWakeWindow(DateTimeOffset occurredAtUtc, DateTimeOffset deadlineUtc)
    { OccurredAtUtc = occurredAtUtc; DeadlineUtc = deadlineUtc; }
}
