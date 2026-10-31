using System.Text;
using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class CanonicalBindingTests
{
    private static readonly MessageTypeRegistry Registry = new([new("fixture.observe", 1, MessageScopeKind.Tenant, true)]);
    private static MessageBinding Binding()
    {
        var id = new MessageIdentity(Guid.NewGuid(), MessageScope.Tenant(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), 1, "fixture.observe");
        var now = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        return new(id, 1, new MessageResource("target", Guid.NewGuid()), now, now.AddSeconds(120));
    }
    private static string Hash(MessageBinding binding, string json) => CanonicalMessageBinding.ComputeHash(binding, Registry, new ProbeCodec(), Encoding.UTF8.GetBytes(json));

    [Fact]
    public void PropertyOrderWhitespaceAndUtcRepresentationHaveSameBusinessHash()
    {
        var binding = Binding();
        var hash = Hash(binding, "{\"value\":12,\"enabled\":true}");
        Assert.Equal(hash, Hash(binding, "{ \"enabled\" : true, \"value\" : 12 }"));
        Assert.Equal(hash, Hash(new(binding.Identity, 1, binding.Resource, binding.OccurredAtUtc.ToOffset(TimeSpan.FromHours(7)), binding.DeadlineUtc.ToOffset(TimeSpan.FromHours(7))), "{\"value\":12,\"enabled\":true}"));
        Assert.True(CanonicalMessageBinding.Matches(hash, hash));
        Assert.False(CanonicalMessageBinding.Matches(hash.ToUpperInvariant(), hash));
        Assert.False(CanonicalMessageBinding.Matches("bad", hash));
    }

    [Fact]
    public void PayloadResourceDeadlineAndScopeCannotChangeUnderSameBindingHash()
    {
        var b = Binding(); const string json = "{\"value\":12,\"enabled\":true}"; var hash = Hash(b, json);
        Assert.NotEqual(hash, Hash(b, "{\"value\":13,\"enabled\":true}"));
        Assert.NotEqual(hash, Hash(new(b.Identity, 1, new MessageResource("target", Guid.NewGuid()), b.OccurredAtUtc, b.DeadlineUtc), json));
        Assert.NotEqual(hash, Hash(new(b.Identity, 1, b.Resource, b.OccurredAtUtc, b.DeadlineUtc.AddSeconds(-1)), json));
        var other = new MessageIdentity(b.Identity.InstallationId, MessageScope.Tenant(Guid.NewGuid()), b.Identity.OperationId, b.Identity.StepId, 1, b.Identity.TypeId);
        Assert.NotEqual(hash, Hash(new(other, 1, b.Resource, b.OccurredAtUtc, b.DeadlineUtc), json));
        Assert.Throws<ArgumentException>(() => new MessageBinding(b.Identity, 1, b.Resource, b.OccurredAtUtc, b.DeadlineUtc.AddSeconds(1)));
    }

    [Theory]
    [InlineData("{\"value\":12,\"enabled\":true,\"token\":\"synthetic\"}")]
    [InlineData("{\"value\":12,\"value\":13,\"enabled\":true}")]
    [InlineData("{\"value\":12.0,\"enabled\":true}")]
    [InlineData("{\"value\":1e1,\"enabled\":true}")]
    [InlineData("{\"value\":12}")]
    [InlineData("null")]
    [InlineData("{\"value\":[[[[[[[[[12]]]]]]]]],\"enabled\":true}")]
    public void InvalidOrSecretShapedPayloadIsRejectedWithoutEchoingRawBody(string json)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Hash(Binding(), json));
        Assert.DoesNotContain(json, error.Message);
    }

    [Fact]
    public void EmptyRegistryAndOversizeBodyCannotBeCanonicalized()
    {
        Assert.Throws<InvalidOperationException>(() => CanonicalMessageBinding.ComputeHash(Binding(), MessageTypeRegistry.Empty, new ProbeCodec(), "{}"u8));
        Assert.Throws<InvalidOperationException>(() => Hash(Binding(), new string(' ', 16 * 1024 + 1)));
    }

    private sealed record Probe(long Value, bool Enabled);
    [Fact]
    public void CompiledRegistryBindsCodecConsumerRouteAndFailurePolicyWithoutExecutingConsumer()
    {
        var consumer = new ProbeConsumer(); var policy = new MessageFailurePolicy();
        var registration = new CompiledMessageRegistration<Probe>(new("fixture.observe", 1, MessageScopeKind.Tenant, true),
            new ProbeCodec(), consumer, "fixture.observe", policy);
        var input = new List<ICompiledMessageRegistration> { registration };
        var registry = new CompiledMessageRegistry(input); input.Clear();
        var binding = Binding(); const string json = "{\"value\":12,\"enabled\":true}";
        Assert.Equal(Hash(binding, json), registry.ComputeHash(binding, Encoding.UTF8.GetBytes(json)));
        Assert.Equal(0, consumer.Calls);
        Assert.Throws<InvalidOperationException>(() => CompiledMessageRegistry.Empty.ComputeHash(binding, Encoding.UTF8.GetBytes(json)));
        Assert.Throws<ArgumentException>(() => new CompiledMessageRegistry([registration, registration]));
        Assert.Equal("fixture.observe", registration.Route);
        Assert.Null(policy.RetryDelay(MessageFailureKind.UnknownCommit, 1));
        Assert.Null(policy.RetryDelay(MessageFailureKind.Reject, 1));
        Assert.Null(policy.RetryDelay(MessageFailureKind.Transient, 5));
        Assert.Equal(TimeSpan.FromSeconds(60), policy.RetryDelay(MessageFailureKind.Transient, 4));
    }

    private sealed class ProbeConsumer : IMessageConsumer<Probe>
    {
        public int Calls { get; private set; }
        public ValueTask HandleAsync(Probe message, CancellationToken cancellationToken) { Calls++; return ValueTask.CompletedTask; }
    }
    private sealed class ProbeCodec : IMessagePayloadCodec<Probe>
    {
        public Probe Read(JsonElement payload)
        {
            var fields = payload.EnumerateObject().Select(x => x.Name).Order().ToArray();
            if (!fields.SequenceEqual(new[] { "enabled", "value" }) || !payload.GetProperty("value").TryGetInt64(out var value) ||
                payload.GetProperty("enabled").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidOperationException("Invalid probe schema.");
            return new(value, payload.GetProperty("enabled").GetBoolean());
        }
        public void Write(Utf8JsonWriter writer, Probe payload)
        {
            writer.WriteStartObject(); writer.WriteNumber("value", payload.Value); writer.WriteBoolean("enabled", payload.Enabled); writer.WriteEndObject();
        }
    }
}
