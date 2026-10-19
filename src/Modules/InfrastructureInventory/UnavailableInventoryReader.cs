using Kordra.Modules.InfrastructureInventory.Contracts;

namespace Kordra.Modules.InfrastructureInventory;

/// <summary>Production bootstrap has no provider and never fabricates observations.</summary>
public sealed class UnavailableInventoryReader : IInventoryReader
{
    public ValueTask<InventoryReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(InventoryReadResult.Unavailable);
    }
}
