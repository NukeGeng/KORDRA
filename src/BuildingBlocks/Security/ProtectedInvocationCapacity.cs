using System.Runtime.CompilerServices;

namespace Kordra.BuildingBlocks.Security;

/// <summary>One technical capacity per process admission gate, shared by every protected consumer.</summary>
public static class ProtectedInvocationCapacity
{
    public const int MaximumInvocations = 16;
    private static readonly ConditionalWeakTable<FairAsyncReaderWriterAdmissionGate, SemaphoreSlim> Capacities = new();

    public static Reservation? TryReserve(FairAsyncReaderWriterAdmissionGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        var capacity = Capacities.GetValue(gate, _ => new(MaximumInvocations, MaximumInvocations));
        return capacity.Wait(0) ? new(capacity) : null;
    }

    public sealed class Reservation : IDisposable
    {
        private SemaphoreSlim? _capacity;
        internal Reservation(SemaphoreSlim capacity) => _capacity = capacity;
        public void Dispose() => Interlocked.Exchange(ref _capacity, null)?.Release();
    }
}
