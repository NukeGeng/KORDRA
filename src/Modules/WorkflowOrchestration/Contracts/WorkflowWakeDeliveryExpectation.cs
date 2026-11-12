namespace Kordra.Modules.WorkflowOrchestration.Contracts;

/// <summary>Closed native-delivery data to match against the owner record, never current authority.</summary>
public sealed record WorkflowWakeDeliveryExpectation
{
    public WorkflowIdentity Identity { get; }
    public WorkflowTransitionRequestedV1 Request { get; }
    public Guid MessageId { get; }
    public Guid CorrelationId { get; }
    public Guid CausationId { get; }
    public long StepVersion { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public DateTimeOffset DeadlineUtc { get; }

    public WorkflowWakeDeliveryExpectation(WorkflowIdentity identity, WorkflowTransitionRequestedV1 request,
        Guid messageId, Guid correlationId, Guid causationId, long stepVersion, DateTimeOffset occurredAtUtc, DateTimeOffset deadlineUtc)
    {
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(request);
        if (request.InstanceId != identity.InstanceId || request.TransitionKind != WorkflowTransitionKind.TimerDue ||
            messageId == Guid.Empty || correlationId == Guid.Empty || causationId == Guid.Empty || stepVersion < 1 ||
            occurredAtUtc == default || deadlineUtc <= occurredAtUtc || deadlineUtc - occurredAtUtc > TimeSpan.FromSeconds(120))
            throw new ArgumentException("Exact bounded timer delivery binding required.");
        Identity = identity; Request = request; MessageId = messageId; CorrelationId = correlationId; CausationId = causationId;
        StepVersion = stepVersion; OccurredAtUtc = occurredAtUtc.ToUniversalTime(); DeadlineUtc = deadlineUtc.ToUniversalTime();
    }
    public override string ToString() => "WorkflowWakeDeliveryExpectation [private, not authority]";
}
