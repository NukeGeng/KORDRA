using System.Text;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Wolverine;
using Wolverine.Runtime.Serialization;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeMessageIngressRuleTests
{
    [Theory]
    [InlineData(119, false)]
    [InlineData(120, true)]
    [InlineData(121, true)]
    [InlineData(86400, true)]
    [InlineData(86401, true)]
    public void DeadlineAndRecoveryHorizonNeverMakeAnOldDeliveryNew(int elapsedSeconds, bool rejected)
    {
        var created = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        var installation = Guid.NewGuid();
        var message = NativeWireFixture.Create(installation, Guid.NewGuid(), created);
        var codec = new MessageEnvelopeCodec(NativeWireFixture.Registry());
        var destination = new Uri("postgresql://nativecompatibility");
        var envelope = new Envelope { Id = message.Delivery.MessageId, Destination = destination,
            MessageType = NativeWireFixture.TypeId, ContentType = MessageEnvelopeCodec.ContentType,
            Data = codec.Write(message.Binding, message.Delivery, message.CopyPayload()) };
        var rule = new NativeMessageIngressRule(NativeWireFixture.Registry(), installation,
            message.Binding.Identity.Scope, destination, new FixedTime(created.AddSeconds(elapsedSeconds)));
        rule.Modify(envelope);
        Assert.Equal(message.Delivery.MessageId, envelope.Id);
        Assert.Equal(rejected ? RejectedEnvelopeDiagnostic.TypeId : NativeWireFixture.TypeId, envelope.MessageType);
        var roundTrip = EnvelopeSerializer.Deserialize(EnvelopeSerializer.Serialize(envelope));
        if (rejected)
        {
            Assert.Equal(RejectedEnvelopeDiagnostic.ContentType, roundTrip.ContentType);
            Assert.Throws<InvalidOperationException>(() => codec.Read(roundTrip.Data!, roundTrip.ContentType!,
                installation, message.Binding.Identity.Scope, created.AddSeconds(elapsedSeconds)));
        }
        else
        {
            var decoded = codec.Read(roundTrip.Data!, roundTrip.ContentType!, installation,
                message.Binding.Identity.Scope, created.AddSeconds(elapsedSeconds));
            Assert.Equal(message.Binding, decoded.Binding);
            Assert.Equal(message.PayloadHash, decoded.PayloadHash);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Theory]
    [InlineData("unknown-type")]
    [InlineData("wrong-content-type")]
    [InlineData("wrong-installation")]
    [InlineData("wrong-scope")]
    [InlineData("wrong-destination")]
    [InlineData("oversize")]
    [InlineData("clr-object")]
    public void InvalidNativeEnvelopeBecomesSafeDiagnosticBeforeBinaryPersistence(string scenario)
    {
        const string secret = "synthetic-forbidden-token";
        var installation = Guid.NewGuid(); var logical = Guid.NewGuid();
        var verified = NativeWireFixture.Create(installation, logical, DateTimeOffset.UtcNow);
        var codec = new MessageEnvelopeCodec(NativeWireFixture.Registry());
        var destination = new Uri("postgresql://nativecompatibility");
        var envelope = new Envelope { Id = verified.Delivery.MessageId, Destination = destination,
            Data = codec.Write(verified.Binding, verified.Delivery, verified.CopyPayload()), ContentType = MessageEnvelopeCodec.ContentType,
            MessageType = NativeWireFixture.TypeId, Source = secret, CorrelationId = secret, TenantId = secret,
            UserName = secret, ParentId = secret, SagaId = secret, GroupId = secret, DeduplicationId = secret,
            PartitionKey = secret, TopicName = secret, AcceptedContentTypes = [secret],
            ReplyUri = new Uri("https://example.invalid/" + secret) };
        envelope.Headers[secret] = secret;
        if (scenario == "unknown-type") { envelope.MessageType = secret; envelope.Data = Encoding.UTF8.GetBytes(secret); }
        if (scenario == "wrong-content-type") envelope.ContentType = secret;
        if (scenario == "wrong-destination") envelope.Destination = new Uri("postgresql://elsewhere");
        if (scenario == "oversize") envelope.Data = new byte[32 * 1024 + 1];
        if (scenario == "clr-object") envelope.Message = new { Token = secret };
        var scope = scenario == "wrong-scope" ? MessageScope.System() : MessageScope.Tenant(installation);
        var rule = new NativeMessageIngressRule(NativeWireFixture.Registry(), scenario == "wrong-installation" ? Guid.NewGuid() : installation,
            scope, destination, TimeProvider.System);
        rule.Modify(envelope);
        Assert.Equal(RejectedEnvelopeDiagnostic.TypeId, envelope.MessageType);
        Assert.Equal(destination, envelope.Destination); Assert.Null(envelope.Message); Assert.Empty(envelope.Headers);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(EnvelopeSerializer.Serialize(envelope)));
    }

    [Fact]
    public void ValidWireRetainsFullBindingAndDeliveryWhileDroppingUntrustedNativeMetadata()
    {
        var installation = Guid.NewGuid(); var message = NativeWireFixture.Create(installation, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var codec = new MessageEnvelopeCodec(NativeWireFixture.Registry()); var destination = new Uri("postgresql://nativecompatibility");
        var envelope = new Envelope { Id = message.Delivery.MessageId, Destination = destination, MessageType = NativeWireFixture.TypeId,
            ContentType = MessageEnvelopeCodec.ContentType, Data = codec.Write(message.Binding, message.Delivery, message.CopyPayload()), TenantId = "untrusted" };
        envelope.Headers["token"] = "synthetic-forbidden-token";
        new NativeMessageIngressRule(NativeWireFixture.Registry(), installation, message.Binding.Identity.Scope, destination, TimeProvider.System).Modify(envelope);
        Assert.Equal(NativeWireFixture.TypeId, envelope.MessageType); Assert.Null(envelope.TenantId); Assert.Empty(envelope.Headers);
        var decoded = codec.Read(envelope.Data!, envelope.ContentType!, installation, message.Binding.Identity.Scope, DateTimeOffset.UtcNow);
        Assert.Equal(message.Binding, decoded.Binding); Assert.Equal(message.Delivery, decoded.Delivery); Assert.Equal(message.PayloadHash, decoded.PayloadHash);
    }
}
