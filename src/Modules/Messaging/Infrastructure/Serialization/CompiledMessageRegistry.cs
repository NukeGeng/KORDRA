using Kordra.Modules.Messaging.Contracts;

namespace Kordra.Modules.Messaging.Infrastructure.Serialization;

/// <summary>Trusted compile-time binding; wire inputs cannot choose a codec, CLR type, consumer or route.</summary>
public interface ICompiledMessageRegistration
{
    MessageTypeRegistration Metadata { get; }
    string Route { get; }
    MessageFailurePolicy FailurePolicy { get; }
    string ComputeHash(MessageBinding binding, ReadOnlySpan<byte> payload);
    byte[] CanonicalizePayload(ReadOnlySpan<byte> payload);
}

public sealed class CompiledMessageRegistration<T> : ICompiledMessageRegistration
{
    public MessageTypeRegistration Metadata { get; }
    public string Route { get; }
    public MessageFailurePolicy FailurePolicy { get; }
    private readonly IMessagePayloadCodec<T> _codec;
    private readonly IMessageConsumer<T> _consumer;
    private readonly MessageTypeRegistry _metadata;

    public CompiledMessageRegistration(MessageTypeRegistration metadata, IMessagePayloadCodec<T> codec,
        IMessageConsumer<T> consumer, string route, MessageFailurePolicy failurePolicy)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(consumer); ArgumentNullException.ThrowIfNull(failurePolicy);
        if (string.IsNullOrEmpty(route) || route.Length > 256 || route.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
            throw new ArgumentException("Invalid registered logical route.", nameof(route));
        Metadata = metadata; _codec = codec; _consumer = consumer; Route = route; FailurePolicy = failurePolicy;
        _metadata = new MessageTypeRegistry([metadata]);
    }

    public string ComputeHash(MessageBinding binding, ReadOnlySpan<byte> payload) =>
        CanonicalMessageBinding.ComputeHash(binding, _metadata, _codec, payload);

    public byte[] CanonicalizePayload(ReadOnlySpan<byte> payload) => CanonicalMessageBinding.CanonicalizePayload(_codec, payload);

    // Deliberately no Execute/DeserializeAndHandle entry point. Activation needs accepted authority/dispatch wiring.
    internal IMessageConsumer<T> Consumer => _consumer;
}

public sealed class CompiledMessageRegistry
{
    private readonly Dictionary<(string, int), ICompiledMessageRegistration> _registrations = [];
    public static CompiledMessageRegistry Empty { get; } = new([]);
    public CompiledMessageRegistry(IEnumerable<ICompiledMessageRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (_registrations.Count >= 256 || !_registrations.TryAdd((registration.Metadata.TypeId, registration.Metadata.SchemaVersion), registration))
                throw new ArgumentException("Duplicate or excessive compiled registrations.");
        }
    }

    public string ComputeHash(MessageBinding binding, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!_registrations.TryGetValue((binding.Identity.TypeId, binding.TypeSchemaVersion), out var registration))
            throw new InvalidOperationException("Message type/version is not registered.");
        return registration.ComputeHash(binding, payload);
    }
    public byte[] CanonicalizePayload(MessageBinding binding, ReadOnlySpan<byte> payload)
    {
        // Exact registration and scope/resource checks precede codec invocation.
        ComputeHash(binding, payload);
        return _registrations[(binding.Identity.TypeId, binding.TypeSchemaVersion)].CanonicalizePayload(payload);
    }

    internal string RouteFor(MessageBinding binding) =>
        _registrations.TryGetValue((binding.Identity.TypeId, binding.TypeSchemaVersion), out var registration)
            ? registration.Route : throw new InvalidOperationException("Message route is not registered.");

    internal IEnumerable<string> Routes => _registrations.Values.Select(r => r.Route).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

}
