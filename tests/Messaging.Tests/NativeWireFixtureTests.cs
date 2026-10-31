using System.Text.Json.Nodes;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Wolverine;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeWireFixtureTests
{
    [Fact]
    public void NativeSerializerChecksDeliveryAndKeepsOriginalBindingAcrossRedelivery()
    {
        var caseId = Guid.NewGuid(); var logical = Guid.NewGuid(); var created = DateTimeOffset.UtcNow;
        var original = NativeWireFixture.Create(caseId, logical, created);
        var repeated = NativeWireFixture.Create(caseId, logical, created);
        Assert.Equal(original.PayloadHash, repeated.PayloadHash);
        Assert.NotEqual(original.Delivery.MessageId, repeated.Delivery.MessageId);
        var serializer = new NativeWireFixture.Serializer(caseId);
        var wire = serializer.WriteMessage(new NativeCompatibilityMessage(caseId, logical) { Verified = original });
        var native = new Envelope { Id = original.Delivery.MessageId, Data = wire, ContentType = MessageEnvelopeCodec.ContentType };
        var decoded = Assert.IsType<NativeCompatibilityMessage>(serializer.ReadFromData(typeof(NativeCompatibilityMessage), native));
        Assert.Equal(logical, decoded.LogicalId); Assert.Equal(original.Binding, decoded.Verified!.Binding);
        Assert.Equal(original.PayloadHash, decoded.Verified.PayloadHash);
        Assert.Throws<InvalidOperationException>(() => serializer.ReadFromData(wire));
    }

    [Theory]
    [InlineData("delivery")]
    [InlineData("installation")]
    [InlineData("content-type")]
    [InlineData("unknown-field")]
    [InlineData("payload-hash")]
    public void KnownVendorPathScrubsRejectedBodyWithoutEchoingSyntheticSecret(string failure)
    {
        const string secret = "synthetic-forbidden-token";
        var caseId = Guid.NewGuid(); var logical = Guid.NewGuid();
        var verified = NativeWireFixture.Create(caseId, logical, DateTimeOffset.UtcNow);
        var serializer = new NativeWireFixture.Serializer(failure == "installation" ? Guid.NewGuid() : caseId);
        var writer = new NativeWireFixture.Serializer(caseId);
        var wire = writer.WriteMessage(new NativeCompatibilityMessage(caseId, logical) { Verified = verified });
        if (failure is "unknown-field" or "payload-hash")
        {
            var root = JsonNode.Parse(wire)!.AsObject();
            if (failure == "unknown-field") root["payload"]!["secret"] = secret;
            else root["payloadHash"] = new string('0', 64);
            wire = System.Text.Encoding.UTF8.GetBytes(root.ToJsonString());
        }
        var native = new Envelope { Id = failure == "delivery" ? Guid.NewGuid() : verified.Delivery.MessageId,
            Data = wire, ContentType = failure == "content-type" ? "application/json" : MessageEnvelopeCodec.ContentType };
        native.Headers["untrusted"] = secret;
        var error = Assert.Throws<InvalidOperationException>(() => serializer.ReadFromData(typeof(NativeCompatibilityMessage), native));
        Assert.Equal("Native fixture envelope rejected.", error.Message); Assert.Null(error.InnerException);
        Assert.Equal("{}", System.Text.Encoding.UTF8.GetString(native.Data!)); Assert.Empty(native.Headers);
        Assert.DoesNotContain(secret, error.ToString());
    }
}
