using System.Text;
using Wolverine;
using Xunit;

namespace Kordra.Messaging.Durability.Tests;

public sealed class ClosedFixtureSerializerTests
{
    private readonly ClosedFixtureSerializer _serializer = new();
    [Fact]
    public void OnlyClosedSyntheticTypeCanRoundTrip()
    {
        var message = new DeliveryProbe(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(120), false);
        Assert.Equal(message, _serializer.ReadFromData(_serializer.WriteMessage(message)));
        Assert.Throws<FixtureRejectedException>(() => _serializer.WriteMessage(new { Token = "synthetic-forbidden" }));
        Assert.Throws<FixtureRejectedException>(() => _serializer.ReadFromData(typeof(string), new Envelope()));
    }
    [Theory]
    [InlineData("{\"Token\":\"synthetic-forbidden\"}")]
    [InlineData("{\"CaseId\":null,\"CaseId\":null}")]
    [InlineData("{\"CaseId\":{},\"LogicalId\":{},\"SchemaVersion\":1,\"TenantId\":{},\"DeadlineUtc\":{},\"Transient\":false}")]
    [InlineData("[[[[[[[[[0]]]]]]]]]")]
    [InlineData("null")]
    public void SecretFieldsDuplicateKeysInvalidTypesAndDeepPayloadAreRejected(string json) =>
        Assert.Throws<FixtureRejectedException>(() => _serializer.ReadFromData(Encoding.UTF8.GetBytes(json)));
    [Fact]
    public void OversizeBodyIsRejectedBeforeParsing() =>
        Assert.Throws<FixtureRejectedException>(() => _serializer.ReadFromData(new byte[16 * 1024 + 1]));
}
