using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;

namespace Kordra.Api;

/// <summary>Inactive paired-result wire factory. No consumer callback, new authority or deadline refresh.</summary>
public static class WorkflowTransitionResultPublication
{
    public const string TypeId = "workflow.transition-recorded.v1";
    public const string Route = "workflow-results-v1";
    public static CompiledMessageRegistry CreateRegistry(IMessageConsumer<WorkflowTransitionRecordedV1> consumer)
        => new([new CompiledMessageRegistration<WorkflowTransitionRecordedV1>(new(TypeId, 1, MessageScopeKind.Tenant, true),
            new WorkflowTransitionRecordedCodec(), consumer, Route, new())]);

    public static VerifiedMessageEnvelope PrepareTimerDue(WorkflowWakeOperation wake, long resultingVersion, Guid messageId,
        CompiledMessageRegistry registry, DateTimeOffset effectiveUtc)
    {
        ArgumentNullException.ThrowIfNull(wake);
        if (wake.ExpectedWorkflowVersion == long.MaxValue || resultingVersion != wake.ExpectedWorkflowVersion + 1 || wake.StepVersion > long.MaxValue - 2)
            throw new InvalidOperationException("Timer result must match the newly staged owner versions.");
        var result = new WorkflowTransitionRecordedV1(wake.InstanceId, wake.StepId, wake.WakeOperationId, resultingVersion, wake.SpecificationHash);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output)) { new WorkflowTransitionRecordedCodec().Write(writer, result); writer.Flush(); }
        var scope = MessageScope.Tenant(wake.TenantId);
        var binding = new MessageBinding(new(wake.InstallationId, scope, wake.WakeOperationId, wake.StepId, wake.StepVersion + 2, TypeId),
            1, new("workflow", wake.InstanceId), wake.OccurredAtUtc, wake.DeadlineUtc);
        var codec = new MessageEnvelopeCodec(registry);
        var wire = codec.Write(binding, new(messageId, wake.RequestId, wake.MessageId), output.ToArray());
        return codec.Read(wire, MessageEnvelopeCodec.ContentType, wake.InstallationId, scope, effectiveUtc);
    }

    public static void RequireTimerDueMatch(VerifiedMessageEnvelope envelope, WorkflowWakeOperation wake, long resultingVersion)
    {
        ArgumentNullException.ThrowIfNull(envelope); ArgumentNullException.ThrowIfNull(wake);
        var binding = envelope.Binding; var id = binding.Identity;
        if (wake.ExpectedWorkflowVersion == long.MaxValue || resultingVersion != wake.ExpectedWorkflowVersion + 1 ||
            wake.StepVersion > long.MaxValue - 2 || id.TypeId != TypeId || binding.TypeSchemaVersion != 1 ||
            id.InstallationId != wake.InstallationId || id.Scope != MessageScope.Tenant(wake.TenantId) ||
            id.OperationId != wake.WakeOperationId || id.StepId != wake.StepId || id.StepVersion != wake.StepVersion + 2 ||
            binding.Resource != new MessageResource("workflow", wake.InstanceId) || binding.OccurredAtUtc != wake.OccurredAtUtc ||
            binding.DeadlineUtc != wake.DeadlineUtc || envelope.Delivery.CorrelationId != wake.RequestId || envelope.Delivery.CausationId != wake.MessageId)
            throw new InvalidOperationException("Result envelope differs from the completed immutable TimerDue binding.");
        using var document = JsonDocument.Parse(envelope.CopyPayload());
        var result = new WorkflowTransitionRecordedCodec().Read(document.RootElement);
        if (result.InstanceId != wake.InstanceId || result.StepId != wake.StepId || result.OperationId != wake.WakeOperationId ||
            result.ResultingVersion != resultingVersion || result.SpecificationHash != wake.SpecificationHash)
            throw new InvalidOperationException("Result payload differs from the newly completed TimerDue transition.");
    }
}
