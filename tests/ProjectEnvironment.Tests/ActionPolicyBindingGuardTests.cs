using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.Catalog.Tests;

public sealed class ActionPolicyBindingGuardTests
{
    [Theory]
    [InlineData("workflow.start")]
    [InlineData("workflow.read")]
    [InlineData("workflow.cancel")]
    [InlineData("workflow.reconcile")]
    [InlineData("workflow.unknown")]
    public async Task MissingEnvironmentNeverFallsBackToProjectOrTargetBinding(string action)
    {
        await using var db = new ProjectDbContextFactory().CreateDbContext([]);
        var reader = new PolicyBindingStore(db);
        Assert.Null(await reader.ReadCurrentForActionUnderAdmissionAsync(Guid.NewGuid(), Guid.NewGuid(), null, action));
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
