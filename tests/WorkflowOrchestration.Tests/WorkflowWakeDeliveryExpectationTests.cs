using Kordra.Modules.WorkflowOrchestration.Contracts;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowWakeDeliveryExpectationTests
{
    [Theory]
    [InlineData("instance")][InlineData("kind")][InlineData("message")][InlineData("correlation")]
    [InlineData("causation")][InlineData("version")][InlineData("empty-time")][InlineData("no-lifetime")][InlineData("long-lifetime")]
    public void InvalidOrNonTimerDataCannotBecomeClosedDeliveryExpectation(string invalid)
    {
        var identity = new WorkflowIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var kind = invalid == "kind" ? WorkflowTransitionKind.Checkpoint : WorkflowTransitionKind.TimerDue;
        var request = new WorkflowTransitionRequestedV1(invalid == "instance" ? Guid.NewGuid() : identity.InstanceId,
            Guid.NewGuid(), 7, 1, Guid.NewGuid(), new string('a', 64), new string('b', 64), kind,
            kind == WorkflowTransitionKind.TimerDue ? Guid.NewGuid() : null, kind == WorkflowTransitionKind.TimerDue ? 1 : null);
        var start = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        var end = invalid == "no-lifetime" ? start : start.AddSeconds(120).AddTicks(invalid == "long-lifetime" ? 1 : 0);
        Assert.Throws<ArgumentException>(() => new WorkflowWakeDeliveryExpectation(identity, request,
            invalid == "message" ? Guid.Empty : Guid.NewGuid(), invalid == "correlation" ? Guid.Empty : Guid.NewGuid(),
            invalid == "causation" ? Guid.Empty : Guid.NewGuid(), invalid == "version" ? 0 : 4,
            invalid == "empty-time" ? default : start, end));
    }
}
