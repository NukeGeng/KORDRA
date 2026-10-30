using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.ReleaseManagement.Domain;

namespace Kordra.Api;

/// <summary>Owns bounded U, mutation and D lifetime. An unresolved task keeps capacity and fences admission.</summary>
public sealed class ReleaseMutationExecutionCoordinator(ReleaseUseAdmission uses, ReleaseMutationTransaction mutations,
    ReleaseMutationDisclosure disclosures, FairAsyncReaderWriterAdmissionGate gate, IReleaseSchemaReadiness readiness)
{
    private sealed class State { public bool RetainCapacity; }
    public async Task<string> ExecuteAsync(SessionSelection subject, ReleaseMutationCommand command, string key,
        Guid correlationId, IReleaseResponseCommitter response, CancellationToken ct = default)
    {
        if (subject is null || command is null || response is null || correlationId == Guid.Empty) return "invalid";
        var capacity = ProtectedInvocationCapacity.TryReserve(gate);
        if (capacity is null) return "unavailable";
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10));
        var state = new State(); var task = Run(subject, command, key, correlationId, response, state, budget.Token);
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
    private async Task<string> Run(SessionSelection subject, ReleaseMutationCommand command, string key, Guid correlationId,
        IReleaseResponseCommitter response, State state, CancellationToken ct)
    {
        if (!await readiness.CheckAsync(ct)) return "unavailable";
        var use = await uses.BeginAsync(subject, command, key, correlationId, ct);
        if (use.Status != "admitted" || use.Ticket is null) { if (use.Status == "unknown") state.RetainCapacity = true; return use.Status; }
        var mutation = await mutations.ExecuteAsync(use.Ticket, ct);
        if (mutation.Status is not ("durable" or "replay")) { if (mutation.Status == "unknown") state.RetainCapacity = true; return mutation.Status; }
        var disclosure = await disclosures.DiscloseAsync(mutation, response, ct);
        if (disclosure == "unknown") state.RetainCapacity = true;
        return disclosure;
    }
    private static void Observe(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
