using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class WireEnvelopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
    private static (MessageEnvelopeCodec Codec, MessageBinding Binding, byte[] Wire, Consumer Consumer) Fixture()
    {
        var consumer = new Consumer();
        var registry = new CompiledMessageRegistry([new CompiledMessageRegistration<Payload>(
            new("fixture.observe", 1, MessageScopeKind.Tenant, true), new Codec(), consumer, "fixture.observe", new())]);
        var codec = new MessageEnvelopeCodec(registry);
        var binding = new MessageBinding(new(Guid.NewGuid(), MessageScope.Tenant(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), 1, "fixture.observe"),
            1, new("target", Guid.NewGuid()), Now, Now.AddSeconds(120));
        return (codec, binding, codec.Write(binding, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), "{\"enabled\":true}"u8), consumer);
    }

    [Fact]
    public void RoundTripCopiesPayloadAndDoesNotRunConsumerOrCreatePermit()
    {
        var f = Fixture(); var result = Read(f.Codec, f.Binding, f.Wire);
        Assert.Equal(f.Binding, result.Binding); Assert.Equal(0, f.Consumer.Calls);
        var copy = result.CopyPayload(); copy[0] = 0;
        Assert.Equal("{\"enabled\":true}", Encoding.UTF8.GetString(result.CopyPayload()));
        var retry = f.Codec.Write(f.Binding, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), "{ \"enabled\" : true }"u8);
        Assert.Equal(result.PayloadHash, Read(f.Codec, f.Binding, retry).PayloadHash);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("payloadHash")]
    [InlineData("installationId")]
    [InlineData("tenantId")]
    [InlineData("scopeKind")]
    [InlineData("resource")]
    [InlineData("operationId")]
    [InlineData("stepId")]
    [InlineData("stepVersion")]
    [InlineData("deadlineUtc")]
    [InlineData("typeId")]
    [InlineData("typeSchemaVersion")]
    [InlineData("schemaVersion")]
    [InlineData("secret")]
    public void TamperedBindingAndUnknownFieldsFailBeforeConsumer(string field)
    {
        var f = Fixture(); var root = JsonNode.Parse(f.Wire)!.AsObject();
        switch (field)
        {
            case "payload": root[field]!["enabled"] = false; break;
            case "payloadHash": root[field] = new string('0', 64); break;
            case "scopeKind": root[field] = "system"; break;
            case "resource": root[field]!["id"] = Guid.NewGuid().ToString("D"); break;
            case "stepVersion": case "typeSchemaVersion": case "schemaVersion": root[field] = 2; break;
            case "deadlineUtc": root[field] = Now.AddSeconds(119).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture); break;
            case "typeId": root[field] = "System.Diagnostics.Process"; break;
            case "secret": root[field] = "synthetic-forbidden-token"; break;
            default: root[field] = Guid.NewGuid().ToString("D"); break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => Read(f.Codec, f.Binding, Encoding.UTF8.GetBytes(root.ToJsonString())));
        Assert.Equal("Message envelope rejected.", error.Message); Assert.Null(error.InnerException); Assert.Equal(0, f.Consumer.Calls);
    }

    [Fact]
    public void DuplicateMissingAndOversizeWireWrongContentTypeScopeAndTimeAreRejected()
    {
        var f = Fixture(); var text = Encoding.UTF8.GetString(f.Wire);
        Assert.Throws<InvalidOperationException>(() => Read(f.Codec, f.Binding, Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"))));
        var missing = JsonNode.Parse(f.Wire)!.AsObject(); missing.Remove("payloadHash");
        Assert.Throws<InvalidOperationException>(() => Read(f.Codec, f.Binding, Encoding.UTF8.GetBytes(missing.ToJsonString())));
        Assert.Throws<InvalidOperationException>(() => Read(f.Codec, f.Binding, new byte[32 * 1024 + 1]));
        Assert.Throws<InvalidOperationException>(() => f.Codec.Read(f.Wire, "application/json", f.Binding.Identity.InstallationId, f.Binding.Identity.Scope, Now));
        Assert.Throws<InvalidOperationException>(() => f.Codec.Read(f.Wire, MessageEnvelopeCodec.ContentType, f.Binding.Identity.InstallationId, MessageScope.System(), Now));
        Assert.Throws<InvalidOperationException>(() => f.Codec.Read(f.Wire, MessageEnvelopeCodec.ContentType, f.Binding.Identity.InstallationId, f.Binding.Identity.Scope, Now.AddSeconds(120)));
        Assert.Throws<InvalidOperationException>(() => f.Codec.Read(f.Wire, MessageEnvelopeCodec.ContentType, f.Binding.Identity.InstallationId, f.Binding.Identity.Scope, Now.AddTicks(-1)));
        Assert.Throws<InvalidOperationException>(() => Read(new(CompiledMessageRegistry.Empty), f.Binding, f.Wire));
    }

    private static VerifiedMessageEnvelope Read(MessageEnvelopeCodec codec, MessageBinding binding, byte[] wire) =>
        codec.Read(wire, MessageEnvelopeCodec.ContentType, binding.Identity.InstallationId, binding.Identity.Scope, Now);
    private sealed record Payload(bool Enabled);
    private sealed class Codec : IMessagePayloadCodec<Payload>
    {
        public Payload Read(JsonElement payload)
        {
            var names = payload.EnumerateObject().Select(x => x.Name).ToArray();
            if (!names.SequenceEqual(new[] { "enabled" }) || payload.GetProperty("enabled").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidOperationException("Invalid fixture payload.");
            return new(payload.GetProperty("enabled").GetBoolean());
        }
        public void Write(Utf8JsonWriter writer, Payload payload)
        { writer.WriteStartObject(); writer.WriteBoolean("enabled", payload.Enabled); writer.WriteEndObject(); }
    }
    private sealed class Consumer : IMessageConsumer<Payload>
    {
        public int Calls { get; private set; }
        public ValueTask HandleAsync(Payload message, CancellationToken cancellationToken) { Calls++; return ValueTask.CompletedTask; }
    }
}
