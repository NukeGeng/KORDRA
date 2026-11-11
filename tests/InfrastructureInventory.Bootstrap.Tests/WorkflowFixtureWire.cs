using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Wolverine;
using Wolverine.Runtime.Serialization;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

internal static class WorkflowFixtureWire
{
    internal const string InputType = "fixture.workflow-start";
    internal const string OutputType = "fixture.workflow-result";
    internal static CompiledMessageRegistry Registry() => new([
        new CompiledMessageRegistration<Guid>(new(InputType, 1, MessageScopeKind.Tenant, true), new Codec(), new Refuse(), "workflow-transitions-v1", new()),
        new CompiledMessageRegistration<Guid>(new(OutputType, 1, MessageScopeKind.Tenant, true), new Codec(), new Refuse(), "workflow-results-v1", new())
    ]);

    internal sealed class Codec : IMessagePayloadCodec<Guid>
    {
        public Guid Read(JsonElement data)
        {
            if (data.ValueKind != JsonValueKind.Object || !data.EnumerateObject().Select(x => x.Name).SequenceEqual(new[] { "instanceId" }))
                throw new ArgumentException("Closed fixture identity required.");
            var text = data.GetProperty("instanceId").GetString();
            if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text)
                throw new ArgumentException("Canonical fixture identity required.");
            return id;
        }
        public void Write(Utf8JsonWriter writer, Guid id)
        { writer.WriteStartObject(); writer.WriteString("instanceId", id.ToString("D")); writer.WriteEndObject(); }
    }
    private sealed class Refuse : IMessageConsumer<Guid>
    {
        public ValueTask HandleAsync(Guid message, CancellationToken ct) => throw new InvalidOperationException("Schema registry cannot dispatch fixture effects.");
    }
    internal sealed class Serializer(WorkflowFixtureState? state) : IMessageSerializer
    {
        public string ContentType => MessageEnvelopeCodec.ContentType;
        public byte[] Write(Envelope envelope) => WriteMessage(envelope.Message!);
        public byte[] WriteMessage(object message)
        {
            if (message is not WorkflowFixtureIncoming { Verified: { } value })
                throw new InvalidOperationException("Only verified fixture wire is supported.");
            return new MessageEnvelopeCodec(Registry()).Write(value.Binding, value.Delivery, value.CopyPayload());
        }
        public object ReadFromData(byte[] data) => throw new InvalidOperationException("Envelope-aware fixture decoding required.");
        public object ReadFromData(Type type, Envelope envelope)
        {
            if (state is null || type != typeof(WorkflowFixtureIncoming) || envelope.Data is null)
                throw new InvalidOperationException("No admitted fixture execution frame.");
            var verified = new MessageEnvelopeCodec(Registry()).Read(envelope.Data, envelope.ContentType ?? "", state.CaseId,
                MessageScope.Tenant(state.CaseId), DateTimeOffset.UtcNow);
            if (verified.Binding.Identity.TypeId != InputType || envelope.Id != verified.Delivery.MessageId)
                throw new InvalidOperationException("Wrong fixture delivery/type.");
            using var document = JsonDocument.Parse(verified.CopyPayload()); var instance = new Codec().Read(document.RootElement);
            var invocation = state.Require(instance);
            if (verified.Binding.Resource?.Kind != "workflow" || verified.Binding.Resource.Id != instance ||
                verified.Binding.Identity.OperationId != invocation.Specification.RequestId)
                throw new InvalidOperationException("Fixture owner binding mismatch.");
            return new WorkflowFixtureIncoming(instance) { Verified = verified };
        }
    }
}
