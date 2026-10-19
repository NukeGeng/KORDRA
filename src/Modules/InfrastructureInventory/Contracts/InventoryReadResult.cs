namespace Kordra.Modules.InfrastructureInventory.Contracts;

/// <summary>An immutable observation, not evidence of authority or target health.</summary>
public sealed class InventoryReadResult
{
    private InventoryReadResult(bool isAvailable, IReadOnlyList<InventoryResource> resources)
    {
        IsAvailable = isAvailable;
        Resources = resources;
    }

    public bool IsAvailable { get; }
    public IReadOnlyList<InventoryResource> Resources { get; }

    /// <summary>Missing observation is distinct from an available, empty collection.</summary>
    public static InventoryReadResult Unavailable { get; } =
        new(false, Array.AsReadOnly(Array.Empty<InventoryResource>()));

    public static InventoryReadResult Available(IEnumerable<InventoryResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var snapshot = resources.ToArray();
        if (snapshot.Any(resource => resource is null))
        {
            throw new ArgumentException("Resources cannot contain null entries.", nameof(resources));
        }

        return new(true, Array.AsReadOnly(snapshot));
    }
}

/// <summary>Display metadata only; neither field is a trusted resource identity.</summary>
public sealed class InventoryResource
{
    public InventoryResource(string kind, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Kind = kind;
        Name = name;
    }

    public string Kind { get; }
    public string Name { get; }
}
