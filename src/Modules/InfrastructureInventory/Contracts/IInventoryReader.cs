namespace Kordra.Modules.InfrastructureInventory.Contracts;

/// <summary>
/// In-process observation seam only. It establishes no tenant, target or permission.
/// HTTP ingress must not use this seam until trusted authority is separately adopted.
/// </summary>
public interface IInventoryReader
{
    ValueTask<InventoryReadResult> ReadAsync(CancellationToken cancellationToken = default);
}
