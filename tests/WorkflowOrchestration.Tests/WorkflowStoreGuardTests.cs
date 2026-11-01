using System.Data;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowStoreGuardTests
{
    [Fact]
    public async Task StoreCannotReadOrStageWithoutAnExplicitCallerRoot()
    {
        using var context = WorkflowDbContext.CreateOfflineModel(); var store = new WorkflowStore(context);
        var spec = WorkflowSpecification.ReadCanonical(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "checkpoint-v1.json")));
        var scope = new WorkflowIdentity(spec.InstallationId, spec.TenantId, spec.InstanceId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StageCreationAsync(spec));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StageTransitionAsync(scope, Guid.NewGuid(), Guid.NewGuid(),
            spec.SpecificationHash, 1, WorkflowState.Running, spec.CreatedAtUtc));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StageInitialCustodyAsync(scope, Guid.NewGuid(), Guid.NewGuid(),
            spec.SpecificationHash, Guid.NewGuid(), Guid.NewGuid(), 1, spec.CreatedAtUtc));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void AlteredCanonicalBindingCannotBeInsertedEvenBeforeRootValidation()
    {
        using var context = WorkflowDbContext.CreateOfflineModel();
        var spec = WorkflowSpecification.ReadCanonical(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "checkpoint-v1.json")));
        context.Add(new WorkflowInstanceRecord {
            InstallationId = spec.InstallationId, TenantId = Guid.NewGuid(), InstanceId = spec.InstanceId,
            RequestId = spec.RequestId, Specification = spec.WriteCanonical(), SpecificationHash = spec.SpecificationHash,
            State = 1, Version = 1, CreatedAtUtc = spec.CreatedAtUtc, DeadlineUtc = spec.DeadlineUtc
        });
        Assert.Contains("canonical", Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
