using System.Text.Json;
using System.Text.Json.Serialization;
using Wolverine;
using Wolverine.Runtime.Serialization;

namespace Kordra.Messaging.Durability.Tests;

/// <summary>Only the synthetic PoC type is accepted. Not a production message contract.</summary>
public sealed class ClosedFixtureSerializer : IMessageSerializer
{
    private const int MaximumBytes = 16 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public string ContentType => "application/vnd.kordra.t12-fixture+json";
    public byte[] Write(Envelope envelope) => WriteMessage(envelope.Message!);
    public byte[] WriteMessage(object message)
    {
        if (message is not DeliveryProbe probe) throw new FixtureRejectedException();
        var data = JsonSerializer.SerializeToUtf8Bytes(probe, Options);
        if (data.Length > MaximumBytes) throw new FixtureRejectedException();
        return data;
    }
    public object ReadFromData(Type messageType, Envelope envelope)
    {
        if (messageType != typeof(DeliveryProbe)) throw new FixtureRejectedException();
        return ReadFromData(envelope.Data!);
    }
    public object ReadFromData(byte[] data)
    {
        if (data.Length > MaximumBytes) throw new FixtureRejectedException();
        try
        {
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FixtureRejectedException();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) throw new FixtureRejectedException();
            if (names.Count != 6) throw new FixtureRejectedException();
            return JsonSerializer.Deserialize<DeliveryProbe>(data, Options) ?? throw new FixtureRejectedException();
        }
        catch (JsonException) { throw new FixtureRejectedException(); }
    }
}
