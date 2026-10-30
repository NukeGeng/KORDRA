using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ApprovalMutationExecutionCoordinatorTests
{
    private static ParsedApprovalMutation Command() => ApprovalRequestParser.Parse("request", Guid.NewGuid().ToString("D"), null,
        JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = Guid.NewGuid(), releaseRevisionId = Guid.NewGuid() }), StringValues.Empty);
    private static SessionSelection Subject => new(Guid.NewGuid(), new string('a', 43), Guid.NewGuid());
    [Fact]
    public async Task CapacityAndFailedReadinessPreventUseAndReleaseCompletedReservations()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate(); var readiness = new NotReady(); var sink = new Sink();
        var coordinator = new ApprovalMutationExecutionCoordinator(null!, null!, null!, gate, readiness);
        var command = Command();
        var held = Enumerable.Range(0, 16).Select(_ => ProtectedInvocationCapacity.TryReserve(gate)).ToArray();
        Assert.All(held, x => Assert.NotNull(x));
        try
        {
            Assert.Equal("unavailable", await coordinator.ExecuteAsync(Subject, Guid.NewGuid(), command, "synthetic-key-1234", command.CorrelationId, sink));
            Assert.Equal(0, readiness.Calls);
        }
        finally { foreach (var reservation in held) reservation!.Dispose(); }
        Assert.Equal("unavailable", await coordinator.ExecuteAsync(Subject, Guid.NewGuid(), command, "synthetic-key-1234", command.CorrelationId, sink));
        Assert.Equal(1, readiness.Calls); Assert.False(sink.Aborted);
        held = Enumerable.Range(0, 16).Select(_ => ProtectedInvocationCapacity.TryReserve(gate)).ToArray();
        try { Assert.All(held, x => Assert.NotNull(x)); }
        finally { foreach (var reservation in held) reservation?.Dispose(); }
    }
    [Fact]
    public async Task CancellationWhileReadinessStillRunsFencesAndAbortsWithoutUse()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate(); var readiness = new BlockingReadiness(); var sink = new Sink();
        var command = Command(); using var cancel = new CancellationTokenSource();
        var work = new ApprovalMutationExecutionCoordinator(null!, null!, null!, gate, readiness)
            .ExecuteAsync(Subject, Guid.NewGuid(), command, "synthetic-key-1234", command.CorrelationId, sink, cancel.Token);
        await readiness.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        try
        {
            Assert.Equal("unknown", await work.WaitAsync(TimeSpan.FromSeconds(5))); Assert.True(sink.Aborted);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await gate.AcquireReadAsync(); });
        }
        finally { readiness.Complete.TrySetResult(false); }
    }
    private sealed class NotReady : IApprovalSchemaReadiness
    {
        public int Calls;
        public Task<bool> CheckAsync(CancellationToken ct) { Calls++; return Task.FromResult(false); }
    }
    private sealed class BlockingReadiness : IApprovalSchemaReadiness
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> CheckAsync(CancellationToken ct) { Started.TrySetResult(); return Complete.Task; }
    }
    private sealed class Sink : IApprovalResponseCommitter
    {
        public bool Aborted;
        public Task CommitAsync(ApprovalResponseCommitment selection, ReadOnlyMemory<byte> bytes, CancellationToken ct) => throw new InvalidOperationException("Unexpected disclosure.");
        public void Abort() => Aborted = true;
    }
}
