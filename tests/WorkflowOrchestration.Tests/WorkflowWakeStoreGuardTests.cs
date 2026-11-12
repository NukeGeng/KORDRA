using System.Data;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowWakeStoreGuardTests
{
    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(3)]
    public async Task SuccessorCannotOpenAConnectionOrFallBackToItsOwnRoot(int revision)
    {
        await using var context = revision == 1 ? WorkflowDbContext.CreateOfflineModel() :
            revision == 2 ? WorkflowDbContext.CreateOfflineWakeModel() : WorkflowDbContext.CreateOfflineCancellationModel();
        var spec = WorkflowSpecification.ReadCanonical(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "checkpoint-v1.json")));
        var at = spec.DueAtUtc;
        var prior = new WorkflowWakeOperation(spec, Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 3, at, at.AddSeconds(120));
        var next = new WorkflowWakeOperation(spec, prior.StepId, prior.TimerId, 2, 2, 2,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 6, 3, prior.DeadlineUtc, prior.DeadlineUtc.AddSeconds(120));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowWakeSuccessorStore(context).StageAsync(
            prior, next, 2, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), next.OccurredAtUtc));
        Assert.Contains("caller root", error.Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.False(context.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task WakeStagingNeverFallsBackToAutocommitOrActivatesOldModel(bool expanded)
    {
        await using var context = expanded ? WorkflowDbContext.CreateOfflineWakeModel() : WorkflowDbContext.CreateOfflineModel();
        var spec = WorkflowSpecification.ReadCanonical(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "checkpoint-v1.json")));
        var now = spec.DueAtUtc;
        var wake = new WorkflowWakeOperation(spec, Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 5, 3, now, now.AddSeconds(120));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowWakeStore(context)
            .StageInitialDispatchAsync(wake, 4, 1, Guid.NewGuid(), Guid.NewGuid(), now));
        Assert.Contains("caller root", error.Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        var request = new WorkflowTransitionRequestedV1(spec.InstanceId, wake.StepId, wake.ExpectedWorkflowVersion, wake.CustodyEpoch,
            wake.WakeOperationId, spec.SpecificationHash, wake.TemplateHash, WorkflowTransitionKind.TimerDue, wake.TimerId, wake.Generation);
        var delivery = new WorkflowWakeDeliveryExpectation(new(spec.InstallationId, spec.TenantId, spec.InstanceId), request,
            wake.MessageId, spec.RequestId, wake.SchedulerOperationId, wake.StepVersion, wake.OccurredAtUtc, wake.DeadlineUtc);
        Assert.Contains("caller root", (await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkflowWakeStore(context)
            .StageTimerDueCompletionAsync(delivery, Guid.NewGuid(), Guid.NewGuid(), now))).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
