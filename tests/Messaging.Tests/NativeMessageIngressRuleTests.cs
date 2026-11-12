using System.Text;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Wolverine;
using Wolverine.Runtime.Serialization;
using Wolverine.Postgresql.Transport;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeMessageIngressRuleTests
{
    [Theory]
    [InlineData(0)][InlineData(120)]
    public void NativeSanitizedRouteMatchesPublisherAndPreservesExpiryClassification(int elapsed)
    {
        var registry = NativeWireFixture.Registry("native-compatibility");
        var installation = Guid.NewGuid(); var created = DateTimeOffset.UtcNow;
        var message = NativeWireFixture.Create(installation, Guid.NewGuid(), created);
        var wire = new MessageEnvelopeCodec(registry).Write(message.Binding, message.Delivery, message.CopyPayload());
        var transport = new WolverineOptions().Transports.GetOrCreate<PostgresqlTransport>();
        var destination = new Uri("postgresql://" + transport.SanitizeIdentifier("native-compatibility"));
        var native = new Envelope { Id = message.Delivery.MessageId, Data = wire, Destination = destination,
            ContentType = MessageEnvelopeCodec.ContentType, MessageType = NativeWireFixture.TypeId };
        new NativeMessageIngressRule(registry, installation, message.Binding.Identity.Scope, destination,
            new FixedTime(created.AddSeconds(elapsed)), transport).Modify(native);
        Assert.Equal(elapsed == 0 ? NativeWireFixture.TypeId : RejectedEnvelopeDiagnostic.TypeId, native.MessageType);
        if (elapsed != 0)
            Assert.True(RejectedEnvelopeDiagnostic.MatchesExpired(native, wire, installation, message.Delivery.MessageId, destination));
    }

    [Theory]
    [InlineData("route")][InlineData("identity")][InlineData("wire")][InlineData("future")]
    public void HistoricalInspectionCannotTurnMalformedOrFutureInputIntoExpiryEvidence(string changed)
    {
        var at = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var installation = Guid.NewGuid();
        var original = NativeWireFixture.Create(installation, Guid.NewGuid(), at);
        var codec = new MessageEnvelopeCodec(NativeWireFixture.Registry());
        var wire = codec.Write(original.Binding, original.Delivery, original.CopyPayload());
        var destination = new Uri("postgresql://nativecompatibility");
        var envelope = new Envelope {
            Id = changed == "identity" ? Guid.NewGuid() : original.Delivery.MessageId,
            Destination = changed == "route" ? new Uri("postgresql://elsewhere") : destination,
            MessageType = NativeWireFixture.TypeId, ContentType = MessageEnvelopeCodec.ContentType,
            Data = changed == "wire" ? "{}"u8.ToArray() : wire
        };
        new NativeMessageIngressRule(NativeWireFixture.Registry(), installation, original.Binding.Identity.Scope,
            destination, new FixedTime(changed == "future" ? at.AddTicks(-1) : at.AddMinutes(3))).Modify(envelope);
        Assert.Equal(RejectedEnvelopeDiagnostic.TypeId, envelope.MessageType);
        Assert.False(RejectedEnvelopeDiagnostic.MatchesExpired(envelope, wire, installation, original.Delivery.MessageId, destination));
        Assert.Throws<InvalidOperationException>(() => codec.Read(wire, MessageEnvelopeCodec.ContentType, installation,
            original.Binding.Identity.Scope, at.AddMinutes(3)));
    }

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
            var original = codec.Write(message.Binding, message.Delivery, message.CopyPayload());
            Assert.True(RejectedEnvelopeDiagnostic.MatchesExpired(roundTrip, original, installation, message.Delivery.MessageId, destination));
            Assert.False(RejectedEnvelopeDiagnostic.MatchesExpired(roundTrip, original, Guid.NewGuid(), message.Delivery.MessageId, destination));
            Assert.False(RejectedEnvelopeDiagnostic.MatchesExpired(roundTrip, "{}"u8, installation, message.Delivery.MessageId, destination));
            var inspected = codec.ReadForInspection(original, MessageEnvelopeCodec.ContentType, installation, message.Binding.Identity.Scope);
            Assert.Equal(message.Binding, inspected.Binding);
            Assert.Equal(message.Delivery, inspected.Delivery);
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
