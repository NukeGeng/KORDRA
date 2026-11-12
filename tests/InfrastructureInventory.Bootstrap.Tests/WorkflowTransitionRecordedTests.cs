using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kordra.Api;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowTransitionRecordedTests
{
    private static byte[] Golden => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "workflow-transition-recorded-v1.json"));
    private static WorkflowTransitionRecordedV1 Read(byte[] bytes)
    { using var document = JsonDocument.Parse(bytes); return new WorkflowTransitionRecordedCodec().Read(document.RootElement); }
    private static byte[] Write(WorkflowTransitionRecordedV1 value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { new WorkflowTransitionRecordedCodec().Write(writer, value); writer.Flush(); }
        return stream.ToArray();
    }

    [Fact]
    public void IndependentResultGoldenPreservesClosedOutcomeAndDoesNotClaimWorkflowSuccess()
    {
        var value = Read(Golden); var emitted = Write(value);
        Assert.Equal(Golden, emitted); Assert.Equal("Completed", value.Result); Assert.Equal("Running", value.State);
        Assert.Equal("TimerDueElapsed", value.Reason);
        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "workflow-transition-recorded-v1.sha256")).Trim(),
            Convert.ToHexStringLower(SHA256.HashData(emitted)));
        Assert.DoesNotContain(value.SpecificationHash, value.ToString());
    }

    [Theory]
    [InlineData("instanceId", "\"AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA\"")]
    [InlineData("operationId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("resultingVersion", "1")][InlineData("resultingVersion", "8.0")]
    [InlineData("resultingVersion", "9223372036854775808")]
    [InlineData("result", "\"Verified\"")][InlineData("state", "\"Succeeded\"")]
    [InlineData("reason", "\"arbitrary outcome override\"")][InlineData("specHash", "\"BAD\"")]
    public void MalformedBindingsOrUncompiledOutcomeTuplesReject(string field, string raw)
    {
        var node = JsonNode.Parse(Golden)!.AsObject(); node[field] = JsonNode.Parse(raw);
        Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Fact]
    public void DuplicateUnknownAndMissingFieldsRejectWithoutEcho()
    {
        var text = Encoding.UTF8.GetString(Golden);
        Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(text.Replace("\"result\":", "\"result\":\"Completed\",\"result\":", StringComparison.Ordinal))));
        var node = JsonNode.Parse(Golden)!.AsObject(); node["token"] = "SYNTHETIC-SECRET";
        Assert.DoesNotContain("SYNTHETIC", Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString()))).ToString());
        node = JsonNode.Parse(Golden)!.AsObject(); node.Remove("reason");
        Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Fact]
    public void PairedResultPreservesLogicalIdentityOriginalWindowAndIndependentVersionsWithoutConsumerCall()
    {
        var wake = Wake(); var consumer = new Refuse(); var registry = WorkflowTransitionResultPublication.CreateRegistry(consumer);
        var result = WorkflowTransitionResultPublication.PrepareTimerDue(wake, 8, Guid.NewGuid(), registry, wake.OccurredAtUtc.AddSeconds(1));
        WorkflowTransitionResultPublication.RequireTimerDueMatch(result, wake, 8);
        var payload = Read(result.CopyPayload());
        Assert.Equal(8, payload.ResultingVersion); Assert.Equal(6, result.Binding.Identity.StepVersion);
        Assert.Equal(wake.WakeOperationId, result.Binding.Identity.OperationId); Assert.Equal(wake.MessageId, result.Delivery.CausationId);
        Assert.Equal(wake.RequestId, result.Delivery.CorrelationId); Assert.Equal(wake.DeadlineUtc, result.Binding.DeadlineUtc);
        Assert.Equal(wake.OccurredAtUtc, result.Binding.OccurredAtUtc); Assert.Equal(0, consumer.Calls);
        Assert.Throws<InvalidOperationException>(() => WorkflowTransitionResultPublication.PrepareTimerDue(wake, 9, Guid.NewGuid(), registry, wake.OccurredAtUtc));
        Assert.Throws<InvalidOperationException>(() => WorkflowTransitionResultPublication.PrepareTimerDue(wake, 8, Guid.NewGuid(), registry, wake.DeadlineUtc));
    }

    [Theory]
    [InlineData("operation")][InlineData("instance")][InlineData("version")][InlineData("hash")][InlineData("step-version")]
    public void TransportValidResultCannotBeReboundToDifferentCompletedOwner(string field)
    {
        var wake = Wake(); var registry = WorkflowTransitionResultPublication.CreateRegistry(new Refuse());
        var original = WorkflowTransitionResultPublication.PrepareTimerDue(wake, 8, Guid.NewGuid(), registry, wake.OccurredAtUtc);
        var payload = new WorkflowTransitionRecordedV1(field == "instance" ? Guid.NewGuid() : wake.InstanceId, wake.StepId,
            field == "operation" ? Guid.NewGuid() : wake.WakeOperationId, field == "version" ? 9 : 8,
            field == "hash" ? new string('b', 64) : wake.SpecificationHash);
        var id = original.Binding.Identity;
        var binding = new MessageBinding(new(id.InstallationId, id.Scope, id.OperationId, id.StepId,
            field == "step-version" ? 4 : id.StepVersion, id.TypeId), 1, original.Binding.Resource, original.Binding.OccurredAtUtc, original.Binding.DeadlineUtc);
        var codec = new MessageEnvelopeCodec(registry);
        var changed = codec.Read(codec.Write(binding, original.Delivery, Write(payload)), MessageEnvelopeCodec.ContentType,
            wake.InstallationId, id.Scope, wake.OccurredAtUtc);
        Assert.Throws<InvalidOperationException>(() => WorkflowTransitionResultPublication.RequireTimerDueMatch(changed, wake, 8));
    }

    private static WorkflowWakeOperation Wake()
    {
        var now = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        var spec = new WorkflowSpecification(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test,
            now, now.AddHours(1), now.AddMinutes(3));
        return new(spec, Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            7, 4, spec.DueAtUtc, spec.DueAtUtc.AddSeconds(120));
    }
    private sealed class Refuse : IMessageConsumer<WorkflowTransitionRecordedV1>
    {
        public int Calls { get; private set; }
        public ValueTask HandleAsync(WorkflowTransitionRecordedV1 message, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Wire factory must not execute consumers."); }
    }
}
