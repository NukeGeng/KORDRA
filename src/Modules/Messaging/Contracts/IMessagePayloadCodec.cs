using System.Text.Json;

namespace Kordra.Modules.Messaging.Contracts;

/// <summary>
/// Trusted, compile-time type-specific schema codec. Implementations must reject unknown/missing
/// fields and emit fixed property order and invariant scalar formats. Never resolved from a CLR wire name.
/// </summary>
public interface IMessagePayloadCodec<T>
{
    T Read(JsonElement payload);
    void Write(Utf8JsonWriter writer, T payload);
}
