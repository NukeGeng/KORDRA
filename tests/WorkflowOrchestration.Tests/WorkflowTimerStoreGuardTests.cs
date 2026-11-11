using System.Data;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowTimerStoreGuardTests
{
    [Fact]
    public async Task OwnerTimerStoreNeverFallsBackToItsOwnRootOrOpensAConnectionForInvalidScope()
    {
        await using var context = WorkflowDbContext.CreateOfflineModel(); var store = new WorkflowTimerStore(context);
        var request = new WorkflowTimerScheduleRequest(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Guid.NewGuid(),
            Guid.NewGuid(), new string('a', 64), 2, 2, new(Guid.NewGuid(), Guid.NewGuid(), 1, 1), DateTimeOffset.UtcNow);
        Assert.Contains("caller root", (await Assert.ThrowsAsync<InvalidOperationException>(() => store.StageWaitingAsync(request))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => store.DiscoverDueAsync(Guid.Empty, Guid.NewGuid(), DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DiscoverDueAsync(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 101));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DiscoverDueAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.DoesNotContain(request.SpecificationHash, request.ToString());
    }

    [Fact]
    public void TimerDueTimeCannotBeMovedAfterItIsPersisted()
    {
        using var context = WorkflowDbContext.CreateOfflineModel(); var row = Timer(); context.Attach(row);
        row.Version++; row.DueAtUtc = row.DueAtUtc.AddMinutes(1);
        Assert.Contains("immutable", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(1, 0)][InlineData(1, 3)][InlineData(10, 11)][InlineData(0, 10)]
    public void SchedulerAttemptCannotResetSkipOrRenewAfterExhaustion(int before, int after)
    {
        using var context = WorkflowDbContext.CreateOfflineModel(); var row = Timer(); row.SchedulerAttempts = before;
        context.Attach(row); row.Version++; row.SchedulerAttempts = after;
        Assert.Contains("budget", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(long.MaxValue)]
    public void InvalidWorkflowVersionCannotBecomeAnOwnerRequest(long version)
        => Assert.Throws<ArgumentException>(() => new WorkflowTimerScheduleRequest(
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), version,
            2, new(Guid.NewGuid(), Guid.NewGuid(), 1, 1), DateTimeOffset.UtcNow));

    private static WorkflowTimerRecord Timer() => new() {
        InstallationId = Guid.NewGuid(), TenantId = Guid.NewGuid(), InstanceId = Guid.NewGuid(), StepId = Guid.NewGuid(),
        TimerId = Guid.NewGuid(), Generation = 1, Version = 1, Status = 1, DueAtUtc = DateTimeOffset.UtcNow.AddHours(1)
    };
}
