using Kordra.BuildingBlocks.Security;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class FairAsyncReaderWriterAdmissionGateTests
{
    [Fact]
    public async Task Closing_the_gate_fails_queued_and_future_admissions_even_after_existing_leases_release()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();
        using var reader = await gate.AcquireReadAsync();
        var writer = gate.AcquireWriteAsync().AsTask();
        var queuedReader = gate.AcquireReadAsync().AsTask();
        gate.Close();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await queuedReader);
        reader.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await gate.AcquireReadAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await gate.AcquireWriteAsync());
    }

    [Fact]
    public async Task Draining_a_writer_releases_only_sixteen_readers_and_preserves_fifo()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();
        using var writer = await gate.AcquireWriteAsync();
        var readers = Enumerable.Range(0, 18).Select(_ => gate.AcquireReadAsync().AsTask()).ToArray();
        writer.Dispose();
        var leases = new List<FairAsyncReaderWriterAdmissionGate.AdmissionLease>();
        try
        {
            foreach (var task in readers.Take(16)) leases.Add(await task);
            Assert.False(readers[16].IsCompleted);
            Assert.False(readers[17].IsCompleted);
            leases[0].Dispose();
            leases.Add(await readers[16]);
            Assert.False(readers[17].IsCompleted);
            leases[1].Dispose();
            leases.Add(await readers[17]);
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    [Fact]
    public async Task Sixteenth_reader_is_the_capacity_and_a_queued_writer_precedes_overflow_readers()
    {
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var readers = new List<FairAsyncReaderWriterAdmissionGate.AdmissionLease>();
        try
        {
            for (var i = 0; i < 16; i++) readers.Add(await gate.AcquireReadAsync());
            var overflow = gate.AcquireReadAsync().AsTask();
            Assert.False(overflow.IsCompleted);
            var writer = gate.AcquireWriteAsync().AsTask();
            foreach (var reader in readers) reader.Dispose();
            await using (var lease = await writer) Assert.False(overflow.IsCompleted);
            await using var admitted = await overflow;
        }
        finally
        {
            foreach (var reader in readers) reader.Dispose();
        }
    }

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
