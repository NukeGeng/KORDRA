namespace Kordra.Modules.WorkflowOrchestration.Domain;

/// <summary>Structural rules only. Current authority, CAS, custody and evidence are separate mandatory gates.</summary>
public static class WorkflowStateRules
{
    public static bool IsTerminal(WorkflowState state) => state is
        WorkflowState.Succeeded or WorkflowState.Failed or WorkflowState.Cancelled or WorkflowState.Expired;

    public static bool CanChangeState(WorkflowState from, WorkflowState to)
    {
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to)) return false;
        return from switch
        {
            WorkflowState.Pending => to is WorkflowState.Running or WorkflowState.BlockedAuthority or WorkflowState.Expired or WorkflowState.Cancelled,
            WorkflowState.Running => to is WorkflowState.WaitingTimer or WorkflowState.BlockedAuthority or WorkflowState.Reconciling
                or WorkflowState.Succeeded or WorkflowState.Failed or WorkflowState.CancelRequested,
            WorkflowState.WaitingTimer => to is WorkflowState.Running or WorkflowState.BlockedAuthority or WorkflowState.CancelRequested
                or WorkflowState.Failed or WorkflowState.Expired,
            WorkflowState.BlockedAuthority => to is WorkflowState.Running or WorkflowState.CancelRequested or WorkflowState.Expired,
            WorkflowState.CancelRequested => to is WorkflowState.Cancelled or WorkflowState.Reconciling,
            // Reconciliation is evidence-bound and is deliberately not a normal graph edge.
            _ => false
        };
    }

    public static bool CanChangeStepState(WorkflowStepState from, WorkflowStepState to)
    {
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to)) return false;
        return from switch
        {
            WorkflowStepState.Pending => to == WorkflowStepState.Running,
            WorkflowStepState.Running => to is WorkflowStepState.WaitingTimer or WorkflowStepState.Succeeded
                or WorkflowStepState.Failed or WorkflowStepState.Unknown,
            WorkflowStepState.WaitingTimer => to == WorkflowStepState.Running,
            _ => false
        };
    }

    public static long NextVersion(long currentVersion, long expectedVersion)
    {
        if (currentVersion < 1 || expectedVersion != currentVersion || currentVersion == long.MaxValue)
            throw new InvalidOperationException("Workflow version conflict or exhaustion.");
        return currentVersion + 1;
    }
}
