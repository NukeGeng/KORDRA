using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowFoundationTests
{
    [Theory]
    [InlineData(WorkflowState.Succeeded)] [InlineData(WorkflowState.Failed)]
    [InlineData(WorkflowState.Cancelled)] [InlineData(WorkflowState.Expired)]
    public void TerminalWorkflowCannotBeResurrected(WorkflowState state)
    {
        Assert.True(WorkflowStateRules.IsTerminal(state));
        foreach (var target in Enum.GetValues<WorkflowState>()) Assert.False(WorkflowStateRules.CanChangeState(state, target));
    }
    [Fact]
    public void UnknownNeedsEvidenceRatherThanTimeoutOrCancelGraphEdge()
    {
        Assert.False(WorkflowStateRules.CanChangeState(WorkflowState.Reconciling, WorkflowState.Failed));
        Assert.False(WorkflowStateRules.CanChangeState(WorkflowState.Reconciling, WorkflowState.Expired));
        Assert.False(WorkflowStateRules.CanChangeState(WorkflowState.Reconciling, WorkflowState.Cancelled));
        Assert.True(WorkflowStateRules.CanChangeState(WorkflowState.CancelRequested, WorkflowState.Reconciling));
        Assert.False(WorkflowStateRules.CanChangeState(WorkflowState.Running, WorkflowState.Cancelled));
    }
    [Fact]
    public void TimerAndAuthorityBoundariesRemainDistinct()
    {
        Assert.True(WorkflowStateRules.CanChangeState(WorkflowState.Running, WorkflowState.WaitingTimer));
        Assert.True(WorkflowStateRules.CanChangeState(WorkflowState.WaitingTimer, WorkflowState.BlockedAuthority));
        Assert.True(WorkflowStateRules.CanChangeState(WorkflowState.BlockedAuthority, WorkflowState.Running));
        Assert.False(WorkflowStateRules.CanChangeState(WorkflowState.BlockedAuthority, WorkflowState.Succeeded));
        Assert.False(WorkflowStateRules.CanChangeState((WorkflowState)0, WorkflowState.Running));
        Assert.False(WorkflowStateRules.CanChangeState(WorkflowState.Running, (WorkflowState)999));
    }
    [Theory]
    [InlineData(0,0)] [InlineData(2,1)] [InlineData(1,2)] [InlineData(long.MaxValue,long.MaxValue)]
    public void StaleFutureInvalidAndOverflowVersionsFailClosed(long current, long expected)
        => Assert.Throws<InvalidOperationException>(() => WorkflowStateRules.NextVersion(current, expected));
    [Fact]
    public void VersionRuleDoesNotClaimDatabaseCas() => Assert.Equal(4, WorkflowStateRules.NextVersion(3,3));
    [Fact]
    public void TenantAndInstallationSubstitutionAreDifferentIdentities()
    {
        var install=Guid.NewGuid(); var tenant=Guid.NewGuid(); var instance=Guid.NewGuid();
        var identity=new WorkflowIdentity(install,tenant,instance);
        Assert.Equal(identity,new WorkflowIdentity(install,tenant,instance));
        Assert.NotEqual(identity,new WorkflowIdentity(install,Guid.NewGuid(),instance));
        Assert.NotEqual(identity,new WorkflowIdentity(Guid.NewGuid(),tenant,instance));
        Assert.NotEqual(identity,new WorkflowIdentity(install,tenant,Guid.NewGuid()));
        Assert.Throws<ArgumentException>(()=>new WorkflowIdentity(install,Guid.Empty,instance));
        Assert.Throws<ArgumentException>(()=>new WorkflowIdentity(Guid.Empty,tenant,instance));
        Assert.Throws<ArgumentException>(()=>new WorkflowIdentity(install,tenant,Guid.Empty));
    }
    [Fact]
    public void DomainAndContractsHaveNoProductInfrastructureDependencies()
    {
        foreach(var assembly in new[]{typeof(WorkflowStateRules).Assembly,typeof(WorkflowIdentity).Assembly})
            Assert.All(assembly.GetReferencedAssemblies(), reference => Assert.StartsWith("System", reference.Name!, StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(WorkflowStepState.Succeeded)] [InlineData(WorkflowStepState.Failed)] [InlineData(WorkflowStepState.Unknown)]
    public void TerminalOrUnknownStepHasNoOrdinaryResumeEdge(WorkflowStepState state)
    {
        foreach(var next in Enum.GetValues<WorkflowStepState>()) Assert.False(WorkflowStateRules.CanChangeStepState(state,next));
        Assert.False(WorkflowStateRules.CanChangeStepState((WorkflowStepState)0,WorkflowStepState.Running));
        Assert.True(WorkflowStateRules.CanChangeStepState(WorkflowStepState.Running,WorkflowStepState.Unknown));
        Assert.True(WorkflowStateRules.CanChangeStepState(WorkflowStepState.WaitingTimer,WorkflowStepState.Running));
    }
}
