using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kordra.Api;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowTransitionCodecTests
{
    private static byte[] Golden => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "workflow-transition-requested-v1.json"));
    private static WorkflowTransitionRequestedV1 Read(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        return new WorkflowTransitionRequestedCodec().Read(document.RootElement);
    }
    [Fact]
    public void IndependentVectorMatchesCanonicalBytesAndHash()
    {
        var bytes = Golden; var value = Read(bytes);
        Assert.Equal(WorkflowTransitionKind.Checkpoint, value.TransitionKind); Assert.Null(value.TimerId); Assert.Null(value.Generation);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { new WorkflowTransitionRequestedCodec().Write(writer, value); writer.Flush(); }
        Assert.Equal(bytes, stream.ToArray());
        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "workflow-transition-requested-v1.sha256")).Trim(),
            Convert.ToHexStringLower(SHA256.HashData(stream.ToArray())));
        Assert.DoesNotContain(value.SpecificationHash, value.ToString());
    }

    [Theory]
    [InlineData("instanceId", "\"AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA\"")]
    [InlineData("stepId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("expectedVersion", "0")][InlineData("custodyEpoch", "-1")]
    [InlineData("expectedVersion", "1.0")][InlineData("expectedVersion", "9223372036854775808")]
    [InlineData("templateHash", "\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"")]
    [InlineData("transitionKind", "\"System.String, System.Private.CoreLib\"")]
    [InlineData("timerId", "\"dddddddd-dddd-4ddd-8ddd-dddddddddddd\"")]
    [InlineData("generation", "1")]
    public void InvalidBindingsCannotBecomeClosedCommands(string field, string raw)
    {
        var node = JsonNode.Parse(Golden)!.AsObject(); node[field] = JsonNode.Parse(raw);
        Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Fact]
    public void DuplicateUnknownAuthorityAndMissingNullFieldsRejectWithoutEchoingValues()
    {
        var text = Encoding.UTF8.GetString(Golden);
        var duplicate = text.Replace("\"instanceId\":", "\"instanceId\":\"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\",\"instanceId\":", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(duplicate)));
        var node = JsonNode.Parse(Golden)!.AsObject(); node["tenantId"] = "SYNTHETIC-UNTRUSTED-AUTHORITY";
        var error = Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
        Assert.DoesNotContain("SYNTHETIC", error.ToString());
        node = JsonNode.Parse(Golden)!.AsObject(); node.Remove("timerId");
        Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Fact]
    public void TimerDueRequiresExactTimerGenerationAndOtherKindsCannotCarryIt()
    {
        var node = JsonNode.Parse(Golden)!.AsObject(); node["transitionKind"] = "TimerDue";
        Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
        node["timerId"] = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"; node["generation"] = 1;
        var value = Read(Encoding.UTF8.GetBytes(node.ToJsonString())); Assert.Equal(1, value.Generation);
        node["generation"] = 0; Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
        node["generation"] = 1; node["transitionKind"] = "Cancel";
        Assert.Throws<ArgumentException>(() => Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
}
