using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowWakeOperationTests
{
    private static byte[] Golden => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "timer-wake-v1.json"));
    private static WorkflowSpecification Spec() => WorkflowSpecification.ReadCanonical(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "checkpoint-v1.json")));
    private static WorkflowWakeOperation Read(byte[] bytes) => WorkflowWakeOperation.ReadCanonical(bytes, Spec());

    [Fact]
    public void IndependentVectorPreservesDistinctLogicalIdsVersionsCanonicalBytesAndHash()
    {
        var bytes = Golden; var value = Read(bytes);
        Assert.Equal(bytes, value.WriteCanonical());
        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "timer-wake-v1.sha256")).Trim(), value.DescriptorHash);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), value.DescriptorHash);
        Assert.NotEqual(value.SchedulerOperationId, value.WakeOperationId);
        Assert.Equal(7, value.ExpectedWorkflowVersion); Assert.Equal(4, value.StepVersion);
        Assert.Equal(5, value.CustodyEpoch); Assert.Equal(Spec().RequestId, value.RequestId);
        Assert.Equal(3, value.Generation); Assert.Equal(3, value.SchedulerAttempt);
        Assert.Equal(TimeSpan.FromSeconds(120), value.DeadlineUtc - value.OccurredAtUtc);
        var returned = value.WriteCanonical(); returned[0] = 0;
        Assert.Equal(bytes, value.WriteCanonical());
        Assert.DoesNotContain(value.SpecificationHash, value.ToString()); Assert.DoesNotContain(value.TimerId.ToString("D"), value.ToString());
    }

    [Theory]
    [InlineData("schemaVersion", "2")][InlineData("schemaVersion", "\"1\"")]
    [InlineData("installationId", "\"00000000-0000-0000-0000-000000000099\"")]
    [InlineData("tenantId", "\"00000000-0000-0000-0000-000000000099\"")]
    [InlineData("instanceId", "\"00000000-0000-0000-0000-000000000099\"")]
    [InlineData("stepId", "\"AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA\"")]
    [InlineData("timerId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("messageId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("generation", "0")][InlineData("generation", "1.0")]
    [InlineData("schedulerAttempt", "0")][InlineData("schedulerAttempt", "11")]
    [InlineData("expectedVersion", "0")][InlineData("expectedVersion", "9223372036854775808")]
    [InlineData("stepVersion", "-1")]
    [InlineData("custodyEpoch", "0")][InlineData("custodyEpoch", "1.0")]
    [InlineData("requestId", "\"00000000-0000-0000-0000-000000000099\"")]
    [InlineData("wakeOperationId", "\"00000000-0000-0000-0000-000000000100\"")]
    [InlineData("specHash", "\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"")]
    [InlineData("templateHash", "\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"")]
    [InlineData("occurredAtUtc", "\"2026-10-31T00:02:59.9999999Z\"")]
    [InlineData("occurredAtUtc", "\"2026-10-31T07:03:00.0000000+07:00\"")]
    [InlineData("deadlineUtc", "\"2026-10-31T00:05:00.0000001Z\"")]
    [InlineData("deadlineUtc", "\"2026-10-31T00:03:00.0000000Z\"")]
    public void InvalidScopeVersionHashCounterIdentityOrTimeCannotBecomeCanonicalBinding(string field, string raw)
    {
        var node = JsonNode.Parse(Golden)!.AsObject(); node[field] = JsonNode.Parse(raw);
        var error = Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
        Assert.Equal("Workflow wake descriptor rejected.", error.Message);
    }

    [Fact]
    public void SameDataInAnotherWorkflowOrChangedImmutableSpecIsRejected()
    {
        var node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "checkpoint-v1.json")))!.AsObject();
        foreach (var field in new[] { "tenantId", "instanceId", "installationId" })
        {
            var changed = node.DeepClone().AsObject(); changed[field] = "00000000-0000-0000-0000-000000000099";
            var spec = WorkflowSpecification.ReadCanonical(Encoding.UTF8.GetBytes(changed.ToJsonString()));
            Assert.Throws<ArgumentException>(() => WorkflowWakeOperation.ReadCanonical(Golden, spec));
        }
    }

    [Fact]
    public void DuplicateUnknownAuthorityMissingAndNoncanonicalJsonRejectWithoutEchoingData()
    {
        var text = Encoding.UTF8.GetString(Golden);
        var duplicate = text.Replace("\"schemaVersion\":1,", "\"schemaVersion\":1,\"schemaVersion\":1,", StringComparison.Ordinal);
        var node = JsonNode.Parse(Golden)!.AsObject(); node["token"] = "SYNTHETIC-SECRET-NOT-AUTHORITY";
        var error = Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
        Assert.DoesNotContain("SYNTHETIC", error.ToString());
        var missing = JsonNode.Parse(Golden)!.AsObject(); missing.Remove("stepVersion");
        foreach (var invalid in new[] { duplicate, " " + text, text + "\n", missing.ToJsonString(), "[]", "{}", "null", "[", new string(' ', 16385) })
            Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(invalid)));
        var reordered = new JsonObject(); foreach (var field in JsonNode.Parse(Golden)!.AsObject().Reverse()) reordered.Add(field.Key, field.Value?.DeepClone());
        Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(reordered.ToJsonString())));
    }

    [Fact]
    public void OriginalDeadlineCannotReachWorkflowEndOrBeRefreshedByHistoricalRead()
    {
        var spec = Spec(); var original = Read(Golden); var now = spec.DeadlineUtc.AddSeconds(-1);
        Assert.Throws<ArgumentException>(() => Build(original, spec, now, spec.DeadlineUtc));
        var shortened = Build(original, spec, now, spec.DeadlineUtc.AddTicks(-1));
        Assert.True(shortened.DeadlineUtc < spec.DeadlineUtc);
        var before = original.DescriptorHash; var reread = Read(original.WriteCanonical());
        Assert.Equal(original.OccurredAtUtc, reread.OccurredAtUtc); Assert.Equal(original.DeadlineUtc, reread.DeadlineUtc);
        Assert.Equal(before, reread.DescriptorHash); Assert.Equal(Encoding.UTF8.GetString(Golden), Encoding.UTF8.GetString(reread.WriteCanonical()));
    }

    [Fact]
    public void NewOperationOrDeliveryChangesBindingWithoutChangingBusinessSpecification()
    {
        var spec = Spec(); var value = Read(Golden);
        var altered = new WorkflowWakeOperation(spec, value.StepId, value.TimerId, value.Generation, value.SchedulerAttempt,
            value.CustodyEpoch, value.SchedulerOperationId, value.WakeOperationId, Guid.NewGuid(), value.ExpectedWorkflowVersion, value.StepVersion,
            value.OccurredAtUtc, value.DeadlineUtc);
        Assert.NotEqual(value.DescriptorHash, altered.DescriptorHash); Assert.Equal(value.SpecificationHash, altered.SpecificationHash);
    }

    private static WorkflowWakeOperation Build(WorkflowWakeOperation value, WorkflowSpecification spec, DateTimeOffset occurred, DateTimeOffset deadline)
        => new(spec, value.StepId, value.TimerId, value.Generation, value.SchedulerAttempt, value.CustodyEpoch, value.SchedulerOperationId,
            value.WakeOperationId, value.MessageId, value.ExpectedWorkflowVersion, value.StepVersion, occurred, deadline);
}
