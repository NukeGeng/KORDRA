using Kordra.BuildingBlocks.Security;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class FairAsyncReaderWriterAdmissionGateTests
{
    [Fact]
    public async Task Waiting_writer_is_admitted_before_later_readers()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();
        await using var firstReader = await gate.AcquireReadAsync();
        var writerTask = gate.AcquireWriteAsync().AsTask();
        var laterReaderTask = gate.AcquireReadAsync().AsTask();

        Assert.False(writerTask.IsCompleted);
        Assert.False(laterReaderTask.IsCompleted);

        await firstReader.DisposeAsync();
        await using var writer = await writerTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(laterReaderTask.IsCompleted);

        await writer.DisposeAsync();
        await using var laterReader = await laterReaderTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Concurrent_readers_share_admission_until_a_writer_arrives()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();
        await using var firstReader = await gate.AcquireReadAsync();
        await using var secondReader = await gate.AcquireReadAsync();
        var writerTask = gate.AcquireWriteAsync().AsTask();

        Assert.False(writerTask.IsCompleted);
        await secondReader.DisposeAsync();
        Assert.False(writerTask.IsCompleted);

        await firstReader.DisposeAsync();
        await using var writer = await writerTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Timed_out_writer_is_removed_and_does_not_block_waiting_readers()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();
        await using var firstReader = await gate.AcquireReadAsync();
        var writerTask = gate.AcquireWriteAsync(TimeSpan.FromMilliseconds(80)).AsTask();
        var readerTask = gate.AcquireReadAsync().AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writerTask);
        await using var secondReader = await readerTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Cancellation_removes_waiter_without_leaking_admission()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();
        await using var reader = await gate.AcquireReadAsync();
        using var cancellation = new CancellationTokenSource();
        var writerTask = gate.AcquireWriteAsync(cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writerTask);
        await using var anotherReader = await gate.AcquireReadAsync();
    }

    [Fact]
    public async Task Acquisition_timeout_cannot_exceed_approved_one_second_bound()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await gate.AcquireWriteAsync(TimeSpan.FromMilliseconds(1001)));
    }
}
