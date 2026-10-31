using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Wolverine;
using Wolverine.Runtime.Serialization;
using MsgIdentity = Kordra.Modules.Messaging.Contracts.MessageIdentity;

namespace Kordra.Messaging.Tests;

internal static class NativeWireFixture
{
    internal const string TypeId = "fixture.native";
    internal sealed record Payload(Guid CaseId, Guid LogicalId, bool Enabled);
    internal static CompiledMessageRegistry Registry() => new([new CompiledMessageRegistration<Payload>(
        new(TypeId, 1, MessageScopeKind.Tenant, false), new Codec(), new RefuseConsumer(), "nativecompatibility", new())]);

    internal static VerifiedMessageEnvelope Create(Guid caseId, Guid logicalId, DateTimeOffset occurredAt, bool enabled = true)
    {
        var binding = new MessageBinding(new MsgIdentity(caseId, MessageScope.Tenant(caseId), logicalId, caseId, 1, TypeId),
            1, null, occurredAt, occurredAt.AddSeconds(120));
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes)) { new Codec().Write(writer, new(caseId, logicalId, enabled)); writer.Flush(); }
        var codec = new MessageEnvelopeCodec(Registry());
        var wire = codec.Write(binding, new(Guid.NewGuid(), caseId, caseId), bytes.ToArray());
        return codec.Read(wire, MessageEnvelopeCodec.ContentType, caseId, binding.Identity.Scope, occurredAt);
    }

    internal sealed class Serializer(Guid caseId) : IMessageSerializer
    {
        public string ContentType => MessageEnvelopeCodec.ContentType;
        public byte[] Write(Envelope envelope) => WriteMessage(envelope.Message ?? throw new InvalidOperationException("Verified fixture message required."));
        public byte[] WriteMessage(object message)
        {
            if (message is not NativeCompatibilityMessage { Verified: { } verified })
                throw new InvalidOperationException("Verified fixture message required.");
            return new MessageEnvelopeCodec(Registry()).Write(verified.Binding, verified.Delivery, verified.CopyPayload());
        }
        public object ReadFromData(byte[] data) => throw new InvalidOperationException("Envelope-aware fixture decoding required.");
        public object ReadFromData(Type messageType, Envelope envelope)
        {
            try
            {
                if (messageType != typeof(NativeCompatibilityMessage) || envelope.Data is null)
                    throw new InvalidOperationException();
                var verified = new MessageEnvelopeCodec(Registry()).Read(envelope.Data, envelope.ContentType ?? "",
                    caseId, MessageScope.Tenant(caseId), DateTimeOffset.UtcNow);
                if (envelope.Id != verified.Delivery.MessageId) throw new InvalidOperationException();
                using var document = JsonDocument.Parse(verified.CopyPayload());
                var payload = new Codec().Read(document.RootElement);
                if (payload.CaseId != caseId || payload.LogicalId != verified.Binding.Identity.OperationId)
                    throw new InvalidOperationException();
                return new NativeCompatibilityMessage(payload.CaseId, payload.LogicalId) { Verified = verified };
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException or FormatException)
            {
                // Known vendor/type path only; not a claim about unknown-native-type/default DLQ paths.
                envelope.Data = "{}"u8.ToArray(); envelope.Headers.Clear();
                throw new InvalidOperationException("Native fixture envelope rejected.");
            }
        }
    }

    private sealed class Codec : IMessagePayloadCodec<Payload>
    {
        public Payload Read(JsonElement payload)
        {
            var names = payload.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
            if (!names.SequenceEqual(new[] { "caseId", "enabled", "logicalId" })) throw new InvalidOperationException("Closed fixture payload required.");
            return new(Id("caseId"), Id("logicalId"), payload.GetProperty("enabled").GetBoolean());
            Guid Id(string name)
            {
                var text = payload.GetProperty(name).GetString();
                if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text)
                    throw new InvalidOperationException("Canonical fixture identity required.");
                return id;
            }
        }
        public void Write(Utf8JsonWriter writer, Payload payload)
        {
            writer.WriteStartObject(); writer.WriteString("caseId", payload.CaseId.ToString("D"));
            writer.WriteString("logicalId", payload.LogicalId.ToString("D")); writer.WriteBoolean("enabled", payload.Enabled); writer.WriteEndObject();
        }
    }
    private sealed class RefuseConsumer : IMessageConsumer<Payload>
    { public ValueTask HandleAsync(Payload message, CancellationToken cancellationToken) => throw new InvalidOperationException("Codec cannot dispatch fixture effects."); }
}
