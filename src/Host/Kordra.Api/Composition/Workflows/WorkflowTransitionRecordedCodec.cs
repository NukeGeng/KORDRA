using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.WorkflowOrchestration.Contracts;

namespace Kordra.Api;

/// <summary>Closed compiled TimerDue result only. No result consumer or production registration is activated.</summary>
public sealed class WorkflowTransitionRecordedCodec : IMessagePayloadCodec<WorkflowTransitionRecordedV1>
{
    private static readonly string[] Fields = ["instanceId", "stepId", "operationId", "resultingVersion", "specHash", "result", "state", "reason"];

    public WorkflowTransitionRecordedV1 Read(JsonElement payload)
    {
        try
        {
            if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException();
            var fields = payload.EnumerateObject().Select(x => x.Name).ToArray();
            if (fields.Length != Fields.Length || fields.Distinct(StringComparer.Ordinal).Count() != Fields.Length ||
                fields.Any(x => !Fields.Contains(x, StringComparer.Ordinal)) || payload.GetProperty("result").GetString() != "Completed" ||
                payload.GetProperty("state").GetString() != "Running" || payload.GetProperty("reason").GetString() != "TimerDueElapsed")
                throw new ArgumentException();
            return new(Id(payload, "instanceId"), Id(payload, "stepId"), Id(payload, "operationId"),
                payload.GetProperty("resultingVersion").GetInt64(), payload.GetProperty("specHash").GetString()!);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { throw new ArgumentException("Invalid closed workflow transition result; values omitted."); }
    }

    public void Write(Utf8JsonWriter writer, WorkflowTransitionRecordedV1 payload)
    {
        ArgumentNullException.ThrowIfNull(writer); ArgumentNullException.ThrowIfNull(payload);
        writer.WriteStartObject(); writer.WriteString("instanceId", payload.InstanceId.ToString("D"));
        writer.WriteString("stepId", payload.StepId.ToString("D")); writer.WriteString("operationId", payload.OperationId.ToString("D"));
        writer.WriteNumber("resultingVersion", payload.ResultingVersion); writer.WriteString("specHash", payload.SpecificationHash);
        writer.WriteString("result", payload.Result); writer.WriteString("state", payload.State); writer.WriteString("reason", payload.Reason);
        writer.WriteEndObject();
    }
    private static Guid Id(JsonElement payload, string field)
    {
        var text = payload.GetProperty(field).GetString();
        if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text) throw new ArgumentException();
        return id;
    }
}
