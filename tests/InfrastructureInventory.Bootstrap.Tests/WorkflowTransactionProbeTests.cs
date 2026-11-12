using Kordra.Api;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowTransactionProbeTests
{
    [Theory]
    [InlineData("committed", false, true, WorkflowTransactionProbeStatus.Committed)]
    [InlineData("aborted", false, true, WorkflowTransactionProbeStatus.Aborted)]
    [InlineData("in progress", false, true, WorkflowTransactionProbeStatus.Unresolved)]
    [InlineData(null, false, true, WorkflowTransactionProbeStatus.Unavailable)]
    [InlineData("missing", false, true, WorkflowTransactionProbeStatus.Unavailable)]
    [InlineData("COMMITTED", false, true, WorkflowTransactionProbeStatus.Unavailable)]
    [InlineData("aborted", true, true, WorkflowTransactionProbeStatus.Unresolved)]
    [InlineData("committed", true, true, WorkflowTransactionProbeStatus.Unresolved)]
    [InlineData("aborted", false, false, WorkflowTransactionProbeStatus.Unavailable)]
    [InlineData("committed", false, false, WorkflowTransactionProbeStatus.Unavailable)]
    public void MissingStatusPreparedOrChangedServerCannotBecomeSafeRollback(string? status, bool prepared, bool sameServer,
        WorkflowTransactionProbeStatus expected)
        => Assert.Equal(expected, WorkflowTransactionProbe.Classify(status, prepared, sameServer));

    [Fact]
    public void CallerCannotConstructOrCopyAnOriginOrServerObservationFromPidOrBoolean()
    {
        Assert.Empty(typeof(WorkflowTransactionOrigin).GetConstructors());
        Assert.Empty(typeof(WorkflowTransactionObservation).GetConstructors());
        Assert.All(typeof(WorkflowTransactionOrigin).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.All(typeof(WorkflowTransactionObservation).GetProperties(), p => Assert.Null(p.SetMethod));
    }
}
