using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.ReleaseManagement.Domain;

namespace Kordra.Api;

/// <summary>Owns bounded U, mutation and D lifetime. An unresolved task keeps capacity and fences admission.</summary>
public sealed class ApprovalMutationExecutionCoordinator(ApprovalUseAdmission uses, ApprovalMutationTransaction mutations,
    ApprovalMutationDisclosure disclosures, FairAsyncReaderWriterAdmissionGate gate, IApprovalSchemaReadiness readiness, IApprovalEnvironmentIdentityReader? environments = null)
{
    private sealed class State { public bool RetainCapacity; }
    public async Task<string> ExecuteAsync(SessionSelection subject, Guid projectId, ParsedApprovalMutation command, string key,
        Guid correlationId, IApprovalResponseCommitter response, CancellationToken ct = default)
    {
        if (subject is null || (projectId == Guid.Empty && environments is null) || command is null || response is null || correlationId == Guid.Empty || command.CorrelationId != correlationId) return "invalid";
        var capacity = ProtectedInvocationCapacity.TryReserve(gate);
        if (capacity is null) return "unavailable";
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10));
        var state = new State(); var task = Run(subject, projectId, command, key, correlationId, response, state, budget.Token);
        try
        {
            var status = await task.WaitAsync(budget.Token);
            if (status == "unknown") { state.RetainCapacity = true; response.Abort(); }
            return status;
        }
        catch
        {
            if (!task.IsCompleted)
            { state.RetainCapacity = true; gate.Close(); Observe(task); response.Abort(); return "unknown"; }
            return "unavailable";
        }
        finally { if (task.IsCompleted && !state.RetainCapacity) capacity.Dispose(); }
    }
    private async Task<string> Run(SessionSelection subject, Guid projectId, ParsedApprovalMutation command, string key, Guid correlationId,
        IApprovalResponseCommitter response, State state, CancellationToken ct)
    {
        if (!await readiness.CheckAsync(ct)) return "unavailable";
        if (projectId == Guid.Empty)
        {
            using var scopeBudget = CancellationTokenSource.CreateLinkedTokenSource(ct); scopeBudget.CancelAfter(TimeSpan.FromSeconds(2));
            await using var held = await gate.AcquireReadAsync(scopeBudget.Token);
            var owner = await environments!.ReadCurrentUnderAdmissionAsync(command.EnvironmentId, scopeBudget.Token);
            if (owner is null || owner.EnvironmentId != command.EnvironmentId || owner.TenantId == Guid.Empty || owner.ProjectId == Guid.Empty) return "denied";
            projectId = owner.ProjectId;
        }
        var use = await uses.BeginAsync(subject, command, projectId, key, correlationId, ct);
        if (use.Status != "admitted" || use.Ticket is null) { if (use.Status == "unknown") state.RetainCapacity = true; return use.Status; }
        var mutation = await mutations.ExecuteAsync(use.Ticket, ct);
        if (mutation.Status is not ("durable" or "replay")) { if (mutation.Status == "unknown") state.RetainCapacity = true; return mutation.Status; }
        var disclosure = await disclosures.DiscloseAsync(use.Ticket, mutation, response, ct);
        if (disclosure == "unknown") state.RetainCapacity = true;
        return disclosure;
    }
    private static void Observe(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
