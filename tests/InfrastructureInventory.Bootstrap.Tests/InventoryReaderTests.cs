using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class InventoryReaderTests
{
    [Fact]
    public void Local_resource_composition_registers_only_the_real_metadata_source()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Kordra.Api.ResourceAuthorityServices.AddResourceAuthorityOwnerServices(services);
        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(ITargetMetadataReader));
        Assert.Equal(typeof(CurrentTargetMetadataReader), descriptor.ImplementationType);
    }

    [Fact]
    public async Task MissingProvider_RepeatedAndParallelReads_RemainUnavailable()
    {
        IInventoryReader reader = new UnavailableInventoryReader();
        for (var i = 0; i < 3; i++)
        {
            AssertUnavailable(await reader.ReadAsync());
        }

        var reads = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(async () => await reader.ReadAsync())));
        Assert.All(reads, AssertUnavailable);
    }

    [Fact]
    public async Task CancelledRead_ThrowsWithoutChangingSubsequentReads()
    {
        IInventoryReader reader = new UnavailableInventoryReader();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.ReadAsync(cancellation.Token));
        AssertUnavailable(await reader.ReadAsync());
    }

    [Fact]
    public async Task PositiveFixture_IsOnlyAnInProcessTestConsumer()
    {
        IInventoryReader reader = new FixtureReader();
        var result = await reader.ReadAsync();
        Assert.True(result.IsAvailable);
        var resource = Assert.Single(result.Resources);
        Assert.Equal("fixture-kind", resource.Kind);
        Assert.Equal("fixture-only", resource.Name);
    }

    [Fact]
    public void Result_DefensivelyCopiesInput_AndRejectsCollectionMutation()
    {
        var original = new InventoryResource("fixture-kind", "original");
        var input = new[] { original };
        var result = InventoryReadResult.Available(input);
        input[0] = new InventoryResource("fixture-kind", "replacement");
        Assert.Same(original, Assert.Single(result.Resources));
        var collection = Assert.IsAssignableFrom<IList<InventoryResource>>(result.Resources);
        Assert.Throws<NotSupportedException>(() => collection[0] = input[0]);
        Assert.Throws<NotSupportedException>(() => collection.Add(input[0]));
        Assert.Throws<NotSupportedException>(() => collection.Clear());

        var list = new List<InventoryResource> { original };
        var copy = InventoryReadResult.Available(list);
        list.Clear();
        Assert.Same(original, Assert.Single(copy.Resources));
        Assert.All(typeof(InventoryResource).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.All(typeof(InventoryReadResult).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void EmptyObservation_IsDistinctFromMissingObservation()
    {
        var empty = InventoryReadResult.Available([]);
        Assert.True(empty.IsAvailable);
        Assert.Empty(empty.Resources);
        AssertUnavailable(InventoryReadResult.Unavailable);
        var missing = Assert.IsAssignableFrom<IList<InventoryResource>>(InventoryReadResult.Unavailable.Resources);
        Assert.Throws<NotSupportedException>(() => missing.Add(new("fixture-kind", "fixture-only")));
    }

    [Fact]
    public void InvalidResultInputs_AreRejectedPredictably()
    {
        Assert.Throws<ArgumentNullException>(() => InventoryReadResult.Available(null!));
        Assert.Throws<ArgumentException>(() => InventoryReadResult.Available(new InventoryResource[] { null! }));
        Assert.Throws<ArgumentNullException>(() => new InventoryResource(null!, "name"));
        Assert.Throws<ArgumentNullException>(() => new InventoryResource("kind", null!));
        Assert.Throws<ArgumentException>(() => new InventoryResource(" ", "name"));
        Assert.Throws<ArgumentException>(() => new InventoryResource("kind", ""));
    }

    private static void AssertUnavailable(InventoryReadResult result)
    {
        Assert.False(result.IsAvailable);
        Assert.Empty(result.Resources);
    }

    // The only positive reader implementation. Never registered by product composition.
    private sealed class FixtureReader : IInventoryReader
    {
        public ValueTask<InventoryReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(InventoryReadResult.Available(
                [new InventoryResource("fixture-kind", "fixture-only")]));
        }
    }
}
