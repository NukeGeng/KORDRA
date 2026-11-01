namespace Kordra.Modules.WorkflowOrchestration.Domain;

public enum WorkflowState
{
    Pending = 1, Running, WaitingTimer, BlockedAuthority, CancelRequested,
    Reconciling, Succeeded, Failed, Cancelled, Expired
}
public enum WorkflowStepState { Pending = 1, Running, WaitingTimer, Succeeded, Failed, Unknown }
