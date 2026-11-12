using System.Text.Json;
using Kordra.Api;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowWakePublicationTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
    private static WorkflowSpecification Spec(Guid? tenant = null, Guid? installation = null, Guid? instance = null,
        string? releaseHash = null) => new(installation ?? Id(1), tenant ?? Id(2), instance ?? Id(3), Id(4),
        Id(5), Id(6), Id(7), Id(8), Id(9), 1, releaseHash ?? new string('a', 64), WorkflowEnvironment.Test,
        Created, Created.AddHours(1), Created.AddMinutes(3));
    private static WorkflowWakeOperation Wake(WorkflowSpecification spec) => new(spec, Id(11), Id(12), 3, 3, 5,
        Id(100), Id(101), Id(102), 7, 4, spec.DueAtUtc, spec.DueAtUtc.AddSeconds(120));

    [Fact]
    public void WirePreservesFrozenScopeIdsSeparateVersionsEpochAndTimerWithoutDispatch()
    {
        var consumer = new CountingConsumer(); var registry = WorkflowWakePublication.CreateRegistry(consumer);
        var spec = Spec(); var wake = Wake(spec);
        var first = WorkflowWakePublication.Prepare(spec, wake, registry, wake.OccurredAtUtc);
        var later = WorkflowWakePublication.Prepare(spec, wake, registry, wake.DeadlineUtc.AddTicks(-1));
        Assert.Equal(first.PayloadHash, later.PayloadHash); Assert.Equal(first.Delivery, later.Delivery);
        Assert.Equal(wake.MessageId, first.Delivery.MessageId); Assert.Equal(spec.RequestId, first.Delivery.CorrelationId);
        Assert.Equal(wake.SchedulerOperationId, first.Delivery.CausationId);
        Assert.Equal(wake.WakeOperationId, first.Binding.Identity.OperationId);
        Assert.NotEqual(wake.SchedulerOperationId, first.Binding.Identity.OperationId);
        Assert.Equal(wake.StepVersion, first.Binding.Identity.StepVersion);
        Assert.Equal(wake.StepId, first.Binding.Identity.StepId);
        Assert.Equal(spec.InstallationId, first.Binding.Identity.InstallationId);
        Assert.Equal(MessageScope.Tenant(spec.TenantId), first.Binding.Identity.Scope);
        Assert.Equal(new MessageResource("workflow", spec.InstanceId), first.Binding.Resource);
        Assert.Equal(wake.OccurredAtUtc, later.Binding.OccurredAtUtc); Assert.Equal(wake.DeadlineUtc, later.Binding.DeadlineUtc);
        using var document = JsonDocument.Parse(first.CopyPayload());
        var payload = new WorkflowTransitionRequestedCodec().Read(document.RootElement);
        Assert.Equal(wake.ExpectedWorkflowVersion, payload.ExpectedVersion);
        Assert.NotEqual(first.Binding.Identity.StepVersion, payload.ExpectedVersion);
        Assert.Equal(wake.CustodyEpoch, payload.CustodyEpoch); Assert.Equal(wake.TimerId, payload.TimerId);
        Assert.Equal(wake.Generation, payload.Generation); Assert.Equal(WorkflowTransitionKind.TimerDue, payload.TransitionKind);
        Assert.Equal(spec.SpecificationHash, payload.SpecificationHash); Assert.Equal(WorkflowSpecification.TemplateHash, payload.TemplateHash);
        Assert.Equal(wake.WakeOperationId, payload.OperationId); Assert.Equal(spec.InstanceId, payload.InstanceId);
        var delivery = WorkflowWakePublication.ReadDelivery(first);
        Assert.Equal(payload, delivery.Request); Assert.Equal(wake.MessageId, delivery.MessageId);
        Assert.Equal(spec.RequestId, delivery.CorrelationId); Assert.Equal(wake.SchedulerOperationId, delivery.CausationId);
        Assert.Equal(wake.StepVersion, delivery.StepVersion); Assert.Equal(wake.DeadlineUtc, delivery.DeadlineUtc);
        Assert.Equal(0, consumer.Calls);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public void TransportValidButDifferentEnvelopeLogicalIdsCannotBecomeTimerDelivery(bool operation)
    {
        var spec = Spec(); var wake = Wake(spec); var registry = WorkflowWakePublication.CreateRegistry(new CountingConsumer());
        var original = WorkflowWakePublication.Prepare(spec, wake, registry, wake.OccurredAtUtc);
        var id = original.Binding.Identity;
        var changed = new MessageBinding(new(id.InstallationId, id.Scope, operation ? Id(99) : id.OperationId,
            operation ? id.StepId : Id(99), id.StepVersion, id.TypeId), 1, original.Binding.Resource,
            original.Binding.OccurredAtUtc, original.Binding.DeadlineUtc);
        var codec = new MessageEnvelopeCodec(registry);
        var verified = codec.Read(codec.Write(changed, original.Delivery, original.CopyPayload()), MessageEnvelopeCodec.ContentType,
            spec.InstallationId, MessageScope.Tenant(spec.TenantId), wake.OccurredAtUtc);
        Assert.Throws<InvalidOperationException>(() => WorkflowWakePublication.ReadDelivery(verified));
    }

    [Fact]
    public void TransportValidButDifferentWorkflowResourceCannotBecomeTimerDelivery()
    {
        var spec = Spec(); var wake = Wake(spec); var registry = WorkflowWakePublication.CreateRegistry(new CountingConsumer());
        var original = WorkflowWakePublication.Prepare(spec, wake, registry, wake.OccurredAtUtc);
        var changed = new MessageBinding(original.Binding.Identity, 1, new("workflow", Id(99)),
            original.Binding.OccurredAtUtc, original.Binding.DeadlineUtc);
        var codec = new MessageEnvelopeCodec(registry);
        var verified = codec.Read(codec.Write(changed, original.Delivery, original.CopyPayload()), MessageEnvelopeCodec.ContentType,
            spec.InstallationId, MessageScope.Tenant(spec.TenantId), wake.OccurredAtUtc);
        Assert.Throws<ArgumentException>(() => WorkflowWakePublication.ReadDelivery(verified));
    }

    [Theory]
    [InlineData("tenant")][InlineData("installation")][InlineData("instance")][InlineData("specification")]
    public void DescriptorCannotBeReboundToAnotherOwnerSpecification(string change)
    {
        var spec = Spec(); var changed = Spec(tenant: change == "tenant" ? Id(99) : null,
            installation: change == "installation" ? Id(99) : null, instance: change == "instance" ? Id(99) : null,
            releaseHash: change == "specification" ? new string('b', 64) : null);
        Assert.Throws<ArgumentException>(() => WorkflowWakePublication.Prepare(changed, Wake(spec),
            WorkflowWakePublication.CreateRegistry(new CountingConsumer()), spec.DueAtUtc));
    }

    [Theory]
    [InlineData(-1)][InlineData(120)]
    public void FutureOrExpiredWakeRejectsRatherThanRefreshingFrozenTime(int seconds)
    {
        var spec = Spec(); var wake = Wake(spec);
        Assert.Throws<InvalidOperationException>(() => WorkflowWakePublication.Prepare(spec, wake,
            WorkflowWakePublication.CreateRegistry(new CountingConsumer()), wake.OccurredAtUtc.AddSeconds(seconds)));
    }

    [Theory]
    [InlineData("type")][InlineData("version")][InlineData("scope")][InlineData("empty")]
    public void MissingOrIncompatibleCompiledRegistrationCannotPrepareWake(string change)
    {
        var registry = change == "empty" ? CompiledMessageRegistry.Empty : new CompiledMessageRegistry([
            new CompiledMessageRegistration<WorkflowTransitionRequestedV1>(new(
                change == "type" ? "unregistered.workflow" : WorkflowWakePublication.TypeId,
                change == "version" ? 2 : 1, change == "scope" ? MessageScopeKind.System : MessageScopeKind.Tenant, true),
                new WorkflowTransitionRequestedCodec(), new CountingConsumer(), WorkflowWakePublication.Route, new())]);
        var spec = Spec();
        Assert.Throws<InvalidOperationException>(() => WorkflowWakePublication.Prepare(spec, Wake(spec), registry, spec.DueAtUtc));
    }

    private sealed class CountingConsumer : IMessageConsumer<WorkflowTransitionRequestedV1>
    {
        public int Calls { get; private set; }
        public ValueTask HandleAsync(WorkflowTransitionRequestedV1 message, CancellationToken ct)
        { Calls++; return ValueTask.CompletedTask; }
    }
}
