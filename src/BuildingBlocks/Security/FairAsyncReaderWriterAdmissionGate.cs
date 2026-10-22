namespace Kordra.BuildingBlocks.Security;

/// <summary>A bounded, writer-preferring admission gate with FIFO ordering within each lane.</summary>
public sealed class FairAsyncReaderWriterAdmissionGate
{
    public static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly LinkedList<Waiter> _readers = [];
    private readonly LinkedList<Waiter> _writers = [];
    private int _activeReaders;
    private bool _writerActive;
    private bool _closed;
    public const int MaximumReaders = 16;

    /// <summary>Irreversibly closes this process gate when a commit outcome is unresolved.</summary>
    public void Close()
    {
        lock (_sync)
        {
            _closed = true;
            foreach (var queue in new[] { _readers, _writers })
            {
                while (queue.First is { } node)
                {
                    queue.RemoveFirst();
                    node.Value.Node = null;
                    node.Value.Completion.TrySetException(new InvalidOperationException("Authority admission is closed until offline reconciliation/restart."));
                }
            }
        }
    }

    public ValueTask<AdmissionLease> AcquireReadAsync(CancellationToken cancellationToken = default) =>
        AcquireAsync(writer: false, MaximumWait, cancellationToken);

    public ValueTask<AdmissionLease> AcquireWriteAsync(CancellationToken cancellationToken = default) =>
        AcquireAsync(writer: true, MaximumWait, cancellationToken);

    public ValueTask<AdmissionLease> AcquireReadAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        AcquireAsync(writer: false, timeout, cancellationToken);

    public ValueTask<AdmissionLease> AcquireWriteAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        AcquireAsync(writer: true, timeout, cancellationToken);

    private async ValueTask<AdmissionLease> AcquireAsync(
        bool writer,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout > MaximumWait)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Admission wait must be greater than zero and at most one second.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var waiter = new Waiter(writer);
        lock (_sync)
        {
            if (_closed)
            {
                throw new InvalidOperationException("Authority admission is closed until offline reconciliation/restart.");
            }
            if (CanEnterImmediately(writer))
            {
                Grant(waiter);
                return waiter.Completion.Task.Result;
            }

            waiter.Node = writer ? _writers.AddLast(waiter) : _readers.AddLast(waiter);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var registration = timeoutSource.Token.Register(
            static state =>
            {
                var item = (CancellationState)state!;
                item.Gate.Cancel(item.Waiter, item.Token);
            },
            new CancellationState(this, waiter, timeoutSource.Token));

        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    private bool CanEnterImmediately(bool writer) =>
        !_writerActive && _writers.Count == 0 && (writer ? _activeReaders == 0 : _activeReaders < MaximumReaders && _readers.Count == 0);

    private void Cancel(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (waiter.Node is null)
            {
                return;
            }

            (waiter.Writer ? _writers : _readers).Remove(waiter.Node);
            waiter.Node = null;
            waiter.Completion.TrySetCanceled(cancellationToken);
            Drain();
        }
    }

    private void Release(bool writer)
    {
        lock (_sync)
        {
            if (writer)
            {
                if (!_writerActive)
                {
                    throw new InvalidOperationException("The write admission lease has already been released.");
                }

                _writerActive = false;
            }
            else
            {
                if (_activeReaders <= 0)
                {
                    throw new InvalidOperationException("The read admission lease has already been released.");
                }

                _activeReaders--;
            }

            Drain();
        }
    }

    private void Drain()
    {
        if (_closed || _writerActive)
        {
            return;
        }

        if (_activeReaders > 0)
        {
            if (_writers.Count == 0)
            {
                GrantReaders();
            }

            return;
        }

        if (_writers.First is { } nextWriter)
        {
            _writers.RemoveFirst();
            nextWriter.Value.Node = null;
            Grant(nextWriter.Value);
            return;
        }

        GrantReaders();
    }

    private void GrantReaders()
    {
        while (_activeReaders < MaximumReaders && _readers.First is { } node)
        {
            _readers.RemoveFirst();
            node.Value.Node = null;
            Grant(node.Value);
        }
    }

    private void Grant(Waiter waiter)
    {
        if (waiter.Writer)
        {
            _writerActive = true;
        }
        else
        {
            _activeReaders++;
        }

        waiter.Completion.TrySetResult(new AdmissionLease(() => Release(waiter.Writer)));
    }

    private sealed class Waiter(bool writer)
    {
        public bool Writer { get; } = writer;
        public LinkedListNode<Waiter>? Node { get; set; }
        public TaskCompletionSource<AdmissionLease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record CancellationState(
        FairAsyncReaderWriterAdmissionGate Gate,
        Waiter Waiter,
        CancellationToken Token);

    public sealed class AdmissionLease : IDisposable, IAsyncDisposable
    {
        private Action? _release;

        internal AdmissionLease(Action release) => _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
