using System.Globalization;
using System.Text;
using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;

namespace Kordra.Modules.Messaging.Infrastructure.Serialization;

public sealed record DeliveryIdentity(Guid MessageId, Guid CorrelationId, Guid CausationId);

/// <summary>Validated data only; never a permit or a dispatched business operation.</summary>
public sealed class VerifiedMessageEnvelope
{
    public MessageBinding Binding { get; }
    public DeliveryIdentity Delivery { get; }
    public string PayloadHash { get; }
    private readonly byte[] _payload;
    internal VerifiedMessageEnvelope(MessageBinding binding, DeliveryIdentity delivery, string hash, byte[] payload)
    { Binding = binding; Delivery = delivery; PayloadHash = hash; _payload = (byte[])payload.Clone(); }
    public byte[] CopyPayload() => (byte[])_payload.Clone();
    public override string ToString() => "VerifiedMessageEnvelope [private]";
}

public sealed class MessageEnvelopeCodec(CompiledMessageRegistry registry)
{
    public const string ContentType = "application/vnd.kordra.message.v1+json";
    private const string Timestamp = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private static readonly string[] Fields = ["schemaVersion", "messageId", "typeId", "typeSchemaVersion", "installationId",
        "scopeKind", "tenantId", "resource", "operationId", "stepId", "stepVersion", "correlationId", "causationId",
        "occurredAtUtc", "deadlineUtc", "payloadHash", "payload"];
    private readonly CompiledMessageRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public byte[] Write(MessageBinding binding, DeliveryIdentity delivery, ReadOnlySpan<byte> payload)
    {
        RequireDelivery(delivery);
        var canonical = _registry.CanonicalizePayload(binding, payload);
        var hash = _registry.ComputeHash(binding, canonical);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            var id = binding.Identity;
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); GuidValue("messageId", delivery.MessageId);
            writer.WriteString("typeId", id.TypeId); writer.WriteNumber("typeSchemaVersion", binding.TypeSchemaVersion);
            GuidValue("installationId", id.InstallationId); writer.WriteString("scopeKind", id.Scope.Kind);
            if (id.Scope.TenantId is { } tenant) GuidValue("tenantId", tenant); else writer.WriteNull("tenantId");
            if (binding.Resource is { } resource)
            { writer.WritePropertyName("resource"); writer.WriteStartObject(); writer.WriteString("kind", resource.Kind); GuidValue("id", resource.Id); writer.WriteEndObject(); }
            else writer.WriteNull("resource");
            GuidValue("operationId", id.OperationId); GuidValue("stepId", id.StepId); writer.WriteNumber("stepVersion", id.StepVersion);
            GuidValue("correlationId", delivery.CorrelationId); GuidValue("causationId", delivery.CausationId);
            writer.WriteString("occurredAtUtc", binding.OccurredAtUtc.UtcDateTime.ToString(Timestamp, CultureInfo.InvariantCulture));
            writer.WriteString("deadlineUtc", binding.DeadlineUtc.UtcDateTime.ToString(Timestamp, CultureInfo.InvariantCulture));
            writer.WriteString("payloadHash", hash); writer.WritePropertyName("payload"); writer.WriteRawValue(canonical);
            writer.WriteEndObject(); writer.Flush();
            void GuidValue(string name, Guid value) => writer.WriteString(name, value.ToString("D"));
        }
        if (stream.Length > 32 * 1024) throw new InvalidOperationException("Message exceeds wire byte limit.");
        return stream.ToArray();
    }

    public VerifiedMessageEnvelope Read(ReadOnlySpan<byte> wire, string contentType, Guid expectedInstallation,
        MessageScope expectedScope, DateTimeOffset effectiveNow)
    {
        if (wire.Length is < 2 or > 32 * 1024 || contentType != ContentType || expectedInstallation == Guid.Empty || expectedScope is null || effectiveNow == default)
            throw new InvalidOperationException("Invalid bounded message envelope.");
        try
        {
            using var document = JsonDocument.Parse(wire.ToArray(), new JsonDocumentOptions { MaxDepth = 10 });
            var root = document.RootElement; RequireFields(root, Fields);
            if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidOperationException();
            var tenant = root.GetProperty("tenantId");
            var scope = String(root, "scopeKind") switch
            {
                "tenant" when tenant.ValueKind == JsonValueKind.String => MessageScope.Tenant(Id(root, "tenantId")),
                "system" when tenant.ValueKind == JsonValueKind.Null => MessageScope.System(),
                _ => throw new InvalidOperationException()
            };
            var identity = new MessageIdentity(Id(root, "installationId"), scope, Id(root, "operationId"), Id(root, "stepId"),
                root.GetProperty("stepVersion").GetInt64(), String(root, "typeId"));
            if (identity.InstallationId != expectedInstallation || scope != expectedScope) throw new InvalidOperationException();
            MessageResource? resource = null; var rawResource = root.GetProperty("resource");
            if (rawResource.ValueKind != JsonValueKind.Null)
            { RequireFields(rawResource, ["kind", "id"]); resource = new(String(rawResource, "kind"), Id(rawResource, "id")); }
            var binding = new MessageBinding(identity, root.GetProperty("typeSchemaVersion").GetInt32(), resource,
                Time(root, "occurredAtUtc"), Time(root, "deadlineUtc"));
            if (binding.OccurredAtUtc > effectiveNow || binding.DeadlineUtc <= effectiveNow) throw new InvalidOperationException();
            var delivery = new DeliveryIdentity(Id(root, "messageId"), Id(root, "correlationId"), Id(root, "causationId")); RequireDelivery(delivery);
            var rawPayload = Encoding.UTF8.GetBytes(root.GetProperty("payload").GetRawText());
            var computed = _registry.ComputeHash(binding, rawPayload); var hash = String(root, "payloadHash");
            if (!CanonicalMessageBinding.Matches(hash, computed)) throw new InvalidOperationException();
            return new(binding, delivery, hash, _registry.CanonicalizePayload(binding, rawPayload));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        { throw new InvalidOperationException("Message envelope rejected."); }
    }

    private static void RequireFields(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name) || !expected.Contains(property.Name, StringComparer.Ordinal)) throw new InvalidOperationException();
        if (names.Count != expected.Length) throw new InvalidOperationException();
    }
    private static string String(JsonElement root, string name)
    {
        var value = root.GetProperty(name).GetString() ?? throw new InvalidOperationException();
        if (Encoding.UTF8.GetByteCount(value) > 256) throw new InvalidOperationException();
        return value;
    }
    private static Guid Id(JsonElement root, string name)
    {
        var text = String(root, name);
        if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text) throw new InvalidOperationException();
        return id;
    }
    private static DateTimeOffset Time(JsonElement root, string name) => DateTimeOffset.ParseExact(String(root, name), Timestamp,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    private static void RequireDelivery(DeliveryIdentity delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.MessageId == Guid.Empty || delivery.CorrelationId == Guid.Empty || delivery.CausationId == Guid.Empty)
            throw new InvalidOperationException("Nonempty delivery identity required.");
    }
}
