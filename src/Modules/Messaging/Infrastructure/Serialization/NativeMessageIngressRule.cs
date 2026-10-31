using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Runtime.Serialization;

namespace Kordra.Modules.Messaging.Infrastructure.Serialization;

/// <summary>
/// Public native incoming-envelope rule. Must run before persistence/deserialization on a dedicated
/// registered business endpoint. Not automatically installed by the empty runtime profile. An
/// installation/endpoint's trusted scope is supplied by composition, never extracted as authority
/// from the inbound message. Native DLQ conformance is required before activating a consumer.
/// </summary>
public sealed class NativeMessageIngressRule : IEnvelopeRule
{
    private readonly CompiledMessageRegistry _registry;
    private readonly Guid _installationId;
    private readonly MessageScope _scope;
    private readonly Uri _destination;
    private readonly TimeProvider _time;

    public NativeMessageIngressRule(CompiledMessageRegistry registry, Guid installationId, MessageScope scope,
        Uri trustedDestination, TimeProvider time)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ArgumentNullException.ThrowIfNull(trustedDestination);
        if (installationId == Guid.Empty || trustedDestination.Scheme != "postgresql" ||
            !string.IsNullOrEmpty(trustedDestination.UserInfo) || !string.IsNullOrEmpty(trustedDestination.Query) ||
            !string.IsNullOrEmpty(trustedDestination.Fragment))
            throw new ArgumentException("Trusted installation and dedicated native destination required.");
        _installationId = installationId; _destination = trustedDestination;
    }

    public void Modify(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        byte[] observed = [];
        VerifiedMessageEnvelope? verified = null;
        try
        {
            // Ingress accepts raw transport envelopes only. Do not invoke a supplied CLR message or serializer.
            if (envelope.Message is not null || envelope.Serializer is not null || envelope.Id == Guid.Empty)
                throw new InvalidOperationException();
            observed = envelope.Data ?? [];
            verified = new MessageEnvelopeCodec(_registry).Read(observed, envelope.ContentType ?? "",
                _installationId, _scope, _time.GetUtcNow());
            if (envelope.Id != verified.Delivery.MessageId || envelope.MessageType != verified.Binding.Identity.TypeId ||
                envelope.Destination != _destination || _destination != new Uri("postgresql://" + _registry.RouteFor(verified.Binding)))
                throw new InvalidOperationException();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or WolverineSerializationException)
        {
            // Retain transport identity for settlement; malformed empty IDs cannot be persisted as real deliveries.
            if (envelope.Id == Guid.Empty) envelope.Id = Guid.NewGuid();
            RejectedEnvelopeDiagnostic.ReplaceInPlace(envelope, observed, _installationId, _destination, MessageQuarantineReason.InvalidEnvelope);
            return;
        }
        ClearUntrustedMetadata(envelope);
        // Canonical, typed payload only; no raw field ordering, unused headers or caller-selected serializer survive.
        envelope.Data = new MessageEnvelopeCodec(_registry).Write(verified.Binding, verified.Delivery, verified.CopyPayload());
        envelope.ContentType = MessageEnvelopeCodec.ContentType; envelope.MessageType = verified.Binding.Identity.TypeId;
        envelope.Destination = _destination;
    }

    internal static void ClearUntrustedMetadata(Envelope envelope)
    {
        envelope.Message = null; envelope.Serializer = null; envelope.Failure = null; envelope.Headers.Clear();
        envelope.Source = null; envelope.CorrelationId = null; envelope.ParentId = null;
        envelope.TenantId = null; envelope.SagaId = null; envelope.UserName = null;
        envelope.GroupId = null; envelope.DeduplicationId = null; envelope.ReplyUri = null;
        envelope.PartitionKey = null; envelope.TopicName = null;
        envelope.AcceptedContentTypes = [MessageEnvelopeCodec.ContentType];
        envelope.ConversationId = Guid.Empty; envelope.IsResponse = false;
        EnvelopeSerializer.ReadDataElement(envelope, EnvelopeConstants.ReplyRequestedKey, "");
        EnvelopeSerializer.ReadDataElement(envelope, EnvelopeConstants.AckRequestedKey, "false");
    }
}
