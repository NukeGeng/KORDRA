namespace Kordra.Modules.WorkflowOrchestration.Domain;

/// <summary>Structural schedule limits only; no authority, custody, reconciliation or publication.</summary>
public static class WorkflowTimingRules
{
    public static readonly TimeSpan MaximumWakeLifetime = TimeSpan.FromSeconds(120);
    public const int MaximumSchedulerAttempts = 10;

    /// <summary>Checks a fresh owner creation after the exact receipt lookup.</summary>
    public static void RequireCreationWindow(WorkflowSpecification specification, DateTimeOffset effectiveUtc)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (effectiveUtc < specification.CreatedAtUtc)
            throw new InvalidOperationException("Workflow request creation is in the future.");
        if (effectiveUtc >= specification.DeadlineUtc)
            throw new InvalidOperationException("RecoveryHorizonExceeded");
    }

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

    /// <summary>Validates a new immutable scheduler binding after reconciliation, without granting execution.</summary>
    public static void RequireSuccessorBinding(WorkflowSpecification specification, WorkflowWakeOperation previous,
        WorkflowWakeOperation successor, DateTimeOffset effectiveUtc)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(successor);
        _ = WorkflowWakeOperation.ReadCanonical(previous.WriteCanonical(), specification);
        _ = WorkflowWakeOperation.ReadCanonical(successor.WriteCanonical(), specification);
        if (effectiveUtc < previous.DeadlineUtc || successor.OccurredAtUtc != effectiveUtc.ToUniversalTime() ||
            previous.Generation == long.MaxValue || previous.CustodyEpoch == long.MaxValue ||
            previous.ExpectedWorkflowVersion >= long.MaxValue - 1 || previous.StepVersion > long.MaxValue - 2 ||
            successor.Generation != previous.Generation + 1 ||
            successor.SchedulerAttempt != NextSchedulerAttempt(previous.SchedulerAttempt) ||
            successor.CustodyEpoch != previous.CustodyEpoch + 1 ||
            successor.ExpectedWorkflowVersion != previous.ExpectedWorkflowVersion + 1 ||
            successor.StepVersion != previous.StepVersion || successor.StepId != previous.StepId ||
            successor.TimerId != previous.TimerId)
            throw new InvalidOperationException("Successor must preserve the workflow binding and advance each bounded scheduler counter once.");

        Guid[] priorIdentities = [previous.SchedulerOperationId, previous.WakeOperationId, previous.MessageId];
        Guid[] nextIdentities = [successor.SchedulerOperationId, successor.WakeOperationId, successor.MessageId];
        if (nextIdentities.Distinct().Count() != nextIdentities.Length || nextIdentities.Intersect(priorIdentities).Any())
            throw new InvalidOperationException("A successor requires new distinct scheduler, wake and delivery identities.");
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
