using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;

namespace Kordra.Modules.Messaging.Infrastructure.Serialization;

/// <summary>Canonicalizes only through a trusted typed codec; no runtime CLR deserialization or authorization.</summary>
public static class CanonicalMessageBinding
{
    public static string ComputeHash<T>(MessageBinding binding, MessageTypeRegistry registry, IMessagePayloadCodec<T> codec,
        ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(codec);
        if (!registry.TryResolve(binding.Identity.TypeId, binding.TypeSchemaVersion, binding.Identity.Scope, binding.Resource is not null, out _))
            throw new InvalidOperationException("Message type/version/scope is not registered.");
        try
        {
            using var emitted = JsonDocument.Parse(CanonicalizePayload(codec, payload), new JsonDocumentOptions { MaxDepth = 8 });
            using var bytes = new BoundedBuffer(32 * 1024);
            using (var writer = new Utf8JsonWriter(bytes))
            {
                var id = binding.Identity;
                writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1);
                writer.WriteString("installationId", id.InstallationId.ToString("D"));
                writer.WriteString("scopeKind", id.Scope.Kind);
                if (id.Scope.TenantId is { } tenant) writer.WriteString("tenantId", tenant.ToString("D")); else writer.WriteNull("tenantId");
                if (binding.Resource is { } resource)
                {
                    writer.WritePropertyName("resource"); writer.WriteStartObject(); writer.WriteString("kind", resource.Kind);
                    writer.WriteString("id", resource.Id.ToString("D")); writer.WriteEndObject();
                }
                else writer.WriteNull("resource");
                writer.WriteString("operationId", id.OperationId.ToString("D")); writer.WriteString("stepId", id.StepId.ToString("D"));
                writer.WriteNumber("stepVersion", id.StepVersion); writer.WriteString("typeId", id.TypeId);
                writer.WriteNumber("typeSchemaVersion", binding.TypeSchemaVersion);
                writer.WriteString("deadlineUtc", binding.DeadlineUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
                writer.WritePropertyName("payload"); emitted.RootElement.WriteTo(writer); writer.WriteEndObject(); writer.Flush();
            }
            return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
        }
        catch (JsonException) { throw new InvalidOperationException("Invalid bounded message payload."); }
        catch (FormatException) { throw new InvalidOperationException("Invalid typed message payload."); }
    }

    internal static byte[] CanonicalizePayload<T>(IMessagePayloadCodec<T> codec, ReadOnlySpan<byte> payload)
    {
        if (payload.Length is < 2 or > 16 * 1024) throw new InvalidOperationException("Invalid bounded payload.");
        using var document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Payload object required.");
        Validate(document.RootElement);
        var typed = codec.Read(document.RootElement);
        using var canonical = new BoundedBuffer(16 * 1024);
        using (var writer = new Utf8JsonWriter(canonical, new JsonWriterOptions { MaxDepth = 8 }))
        { codec.Write(writer, typed); writer.Flush(); }
        var result = canonical.ToArray();
        using var emitted = JsonDocument.Parse(result, new JsonDocumentOptions { MaxDepth = 8 });
        if (emitted.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Canonical payload object required.");
        Validate(emitted.RootElement); return result;
    }

    public static bool Matches(string expectedHash, string actualHash)
    {
        if (!ValidHash(expectedHash) || !ValidHash(actualHash)) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash));
    }

    private static bool ValidHash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Validate(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (Encoding.UTF8.GetByteCount(property.Name) > 256 || !names.Add(property.Name))
                        throw new InvalidOperationException("Invalid payload member.");
                    Validate(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Validate(item);
                break;
            case JsonValueKind.String:
                if (Encoding.UTF8.GetByteCount(element.GetString()!) > 256) throw new InvalidOperationException("Oversize payload string.");
                break;
            case JsonValueKind.Number:
                var text = element.GetRawText();
                if (!element.TryGetInt64(out _) || text.Contains('.') || text.Contains('e') || text.Contains('E'))
                    throw new InvalidOperationException("Integer payload number required.");
                break;
            case JsonValueKind.Null:
            case JsonValueKind.True:
            case JsonValueKind.False: break;
            default: throw new InvalidOperationException("Invalid payload value.");
        }
    }

    private sealed class BoundedBuffer(int limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Position + count > limit) throw new InvalidOperationException("Canonical message exceeds its byte limit.");
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Position + buffer.Length > limit) throw new InvalidOperationException("Canonical message exceeds its byte limit.");
            // MemoryStream's span implementation falls back to virtual Write for derived streams.
            // Call its array implementation directly to avoid recursive dispatch.
            var bytes = buffer.ToArray();
            base.Write(bytes, 0, bytes.Length);
        }
    }
}
