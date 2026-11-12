using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowWakeReceiptInspectionTests
{
    private static WorkflowReceiptExpectation Scheduler(bool creation = false) => new(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
        Guid.NewGuid(), new string('a', 64), creation ? 1 : 5, creation ? null : Guid.NewGuid(), creation ? null : Guid.NewGuid());

    [Fact]
    public void CreationReceiptCannotBeSubstitutedForSchedulerPublication()
        => Assert.Throws<ArgumentException>(() => new WorkflowWakeReceiptExpectation(Scheduler(true), Guid.NewGuid(), 1, new string('b', 64)));

    [Theory]
    [InlineData(0, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData(-1, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData(1, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
    [InlineData(1, "b")][InlineData(1, null)]
    public void GenerationAndCanonicalDescriptorHashMustBeExact(long generation, string? hash)
        => Assert.Throws<ArgumentException>(() => new WorkflowWakeReceiptExpectation(Scheduler(), Guid.NewGuid(), generation, hash!));

    [Fact]
    public async Task ClosedReaderIsUnavailableAndCannotProveRollbackOrOpenConnection()
    {
        await using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        await using var reader = WorkflowWakeReceiptInspector.CreateIsolatedTestInspector(connection, Guid.NewGuid());
        var expected = new WorkflowWakeReceiptExpectation(Scheduler(), Guid.NewGuid(), 1, new string('b', 64));
        Assert.Equal(WorkflowReceiptInspectionStatus.Unavailable, await reader.InspectAsync(expected));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.InspectAsync(expected, cancelled.Token));
        Assert.DoesNotContain(expected.DescriptorHash, expected.ToString());
    }

    [Fact]
    public async Task UncompletedWakeObservationCannotOpenConnectionOrInferAbsenceFromUnavailableState()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var spec = WorkflowSpecification.ReadCanonical(File.ReadAllBytes(Path.Combine(fixture, "checkpoint-v1.json")));
        var wake = WorkflowWakeOperation.ReadCanonical(File.ReadAllBytes(Path.Combine(fixture, "timer-wake-v1.json")), spec);
        await using var connection = new NpgsqlConnection(
            "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        await using var reader = WorkflowWakeReceiptInspector.CreateIsolatedCancellationTestInspector(connection, spec.InstallationId);
        Assert.False(await reader.ObserveUncompletedWakeAsync(spec, wake));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ObserveUncompletedWakeAsync(spec, wake, new(true)));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }
}
