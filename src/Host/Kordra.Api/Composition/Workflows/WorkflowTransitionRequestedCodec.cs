using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.WorkflowOrchestration.Contracts;

namespace Kordra.Api;

/// <summary>Explicit WFL v1 schema codec only. No registration, CLR discovery or handler activation.</summary>
public sealed class WorkflowTransitionRequestedCodec : IMessagePayloadCodec<WorkflowTransitionRequestedV1>
{
    private static readonly string[] Fields = ["instanceId", "stepId", "expectedVersion", "custodyEpoch", "operationId",
        "specHash", "templateHash", "transitionKind", "timerId", "generation"];

    public WorkflowTransitionRequestedV1 Read(JsonElement payload)
    {
        try
        {
            if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException();
            var names = payload.EnumerateObject().Select(x => x.Name).ToArray();
            if (names.Length != Fields.Length || names.Distinct(StringComparer.Ordinal).Count() != Fields.Length ||
                names.Any(x => !Fields.Contains(x, StringComparer.Ordinal))) throw new ArgumentException();
            var kind = payload.GetProperty("transitionKind").GetString();
            var transition = kind switch { "Checkpoint" => WorkflowTransitionKind.Checkpoint,
                "TimerDue" => WorkflowTransitionKind.TimerDue, "Cancel" => WorkflowTransitionKind.Cancel,
                _ => throw new ArgumentException() };
            var timer = payload.GetProperty("timerId"); var generation = payload.GetProperty("generation");
            return new(Id(payload.GetProperty("instanceId")), Id(payload.GetProperty("stepId")),
                payload.GetProperty("expectedVersion").GetInt64(), payload.GetProperty("custodyEpoch").GetInt64(),
                Id(payload.GetProperty("operationId")), payload.GetProperty("specHash").GetString()!,
                payload.GetProperty("templateHash").GetString()!, transition,
                timer.ValueKind == JsonValueKind.Null ? null : Id(timer),
                generation.ValueKind == JsonValueKind.Null ? null : generation.GetInt64());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { throw new ArgumentException("Invalid closed workflow transition payload; values omitted."); }
    }

    public void Write(Utf8JsonWriter writer, WorkflowTransitionRequestedV1 payload)
    {
        ArgumentNullException.ThrowIfNull(writer); ArgumentNullException.ThrowIfNull(payload);
        writer.WriteStartObject(); writer.WriteString("instanceId", payload.InstanceId.ToString("D"));
        writer.WriteString("stepId", payload.StepId.ToString("D")); writer.WriteNumber("expectedVersion", payload.ExpectedVersion);
        writer.WriteNumber("custodyEpoch", payload.CustodyEpoch); writer.WriteString("operationId", payload.OperationId.ToString("D"));
        writer.WriteString("specHash", payload.SpecificationHash); writer.WriteString("templateHash", payload.TemplateHash);
        writer.WriteString("transitionKind", payload.TransitionKind.ToString());
        if (payload.TimerId is { } timer) writer.WriteString("timerId", timer.ToString("D")); else writer.WriteNull("timerId");
        if (payload.Generation is { } generation) writer.WriteNumber("generation", generation); else writer.WriteNull("generation");
        writer.WriteEndObject();
    }

    private static Guid Id(JsonElement element)
    {
        var text = element.GetString();
        if (text is null || !Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text)
            throw new ArgumentException();
        return id;
    }
}
