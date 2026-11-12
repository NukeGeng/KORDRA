using System.Text.Json;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;

namespace Kordra.Api;

/// <summary>Inactive compiled wire factory. No DI, authority, dispatch or transaction is performed.</summary>
public static class WorkflowWakePublication
{
    public const string TypeId = "workflow.transition-requested.v1";
    public const string Route = "workflow-transitions-v1";

    public static CompiledMessageRegistry CreateRegistry(IMessageConsumer<WorkflowTransitionRequestedV1> consumer)
        => new([new CompiledMessageRegistration<WorkflowTransitionRequestedV1>(
            new(TypeId, 1, MessageScopeKind.Tenant, true), new WorkflowTransitionRequestedCodec(), consumer, Route, new())]);

    public static WorkflowWakeDeliveryExpectation ReadDelivery(VerifiedMessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var binding = envelope.Binding; var identity = binding.Identity;
        if (identity.TypeId != TypeId || binding.TypeSchemaVersion != 1 || identity.Scope.TenantId is not { } tenant ||
            binding.Resource is not { Kind: "workflow" } resource)
            throw new InvalidOperationException("Exact tenant workflow wake registration required.");
        using var document = JsonDocument.Parse(envelope.CopyPayload());
        var request = new WorkflowTransitionRequestedCodec().Read(document.RootElement);
        if (request.OperationId != identity.OperationId || request.StepId != identity.StepId)
            throw new InvalidOperationException("Wake envelope and closed payload identity conflict.");
        return new(new(identity.InstallationId, tenant, resource.Id), request, envelope.Delivery.MessageId,
            envelope.Delivery.CorrelationId, envelope.Delivery.CausationId, identity.StepVersion, binding.OccurredAtUtc, binding.DeadlineUtc);
    }

    public static VerifiedMessageEnvelope Prepare(WorkflowSpecification specification, WorkflowWakeOperation operation,
        CompiledMessageRegistry registry, DateTimeOffset effectiveUtc)
    {
        ArgumentNullException.ThrowIfNull(operation);
        // Reading against the owner specification verifies the immutable scope/hash/time binding.
        var wake = WorkflowWakeOperation.ReadCanonical(operation.WriteCanonical(), specification);
        var payload = new WorkflowTransitionRequestedV1(wake.InstanceId, wake.StepId, wake.ExpectedWorkflowVersion,
            wake.CustodyEpoch, wake.WakeOperationId, wake.SpecificationHash, wake.TemplateHash,
            WorkflowTransitionKind.TimerDue, wake.TimerId, wake.Generation);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        { new WorkflowTransitionRequestedCodec().Write(writer, payload); writer.Flush(); }
        var scope = MessageScope.Tenant(wake.TenantId);
        var binding = new MessageBinding(new(wake.InstallationId, scope, wake.WakeOperationId, wake.StepId,
            wake.StepVersion, TypeId), 1, new("workflow", wake.InstanceId), wake.OccurredAtUtc, wake.DeadlineUtc);
        var delivery = new DeliveryIdentity(wake.MessageId, wake.RequestId, wake.SchedulerOperationId);
        var codec = new MessageEnvelopeCodec(registry);
        var wire = codec.Write(binding, delivery, stream.ToArray());
        // effectiveUtc validates freshness only; never refresh the frozen occurrence/deadline or delivery IDs.
        return codec.Read(wire, MessageEnvelopeCodec.ContentType, wake.InstallationId, scope, effectiveUtc);
    }
}
