using System.Text;
using System.Text.Json.Nodes;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowSpecificationTests
{
    private static byte[] Golden => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","checkpoint-v1.json"));
    [Fact]
    public void IndependentGoldenVectorRoundTripsWithExactBytesAndHash()
    {
        var bytes=Golden; var spec=WorkflowSpecification.ReadCanonical(bytes);
        Assert.Equal(bytes,spec.WriteCanonical());
        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","checkpoint-v1.sha256")).Trim(),spec.SpecificationHash);
        var copy=spec.WriteCanonical();copy[0]=0;
        Assert.Equal(bytes,spec.WriteCanonical());
        Assert.DoesNotContain(spec.ReleaseHash,spec.ToString());
    }
    [Theory]
    [InlineData("tenantId","00000000-0000-0000-0000-000000000099")]
    [InlineData("installationId","00000000-0000-0000-0000-000000000099")]
    [InlineData("targetId","00000000-0000-0000-0000-000000000099")]
    [InlineData("releaseHash","bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("deadlineUtc","2026-10-31T23:00:00.0000000Z")]
    public void BusinessSubstitutionChangesHash(string field,string value)
    {
        var json=Encoding.UTF8.GetString(Golden); var original=JsonNode.Parse(json)![field]!.GetValue<string>();
        var changed=WorkflowSpecification.ReadCanonical(Encoding.UTF8.GetBytes(json.Replace(original,value,StringComparison.Ordinal)));
        Assert.NotEqual(WorkflowSpecification.ReadCanonical(Golden).SpecificationHash,changed.SpecificationHash);
    }
    [Theory]
    [InlineData("templateVersion",2)] [InlineData("schemaVersion",2)] [InlineData("releaseRevision",0)]
    public void UnsupportedVersionOrRevisionIsRejected(string field,int value)
    {
        var json=JsonNode.Parse(Golden)!;json[field]=value;
        Assert.Throws<ArgumentException>(()=>WorkflowSpecification.ReadCanonical(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
    [Theory]
    [InlineData("Production")] [InlineData("Staging")] [InlineData("development")]
    public void UnadoptedEnvironmentIsRejected(string environment)
        => Assert.Throws<ArgumentException>(()=>WorkflowSpecification.ReadCanonical(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Golden).Replace("Development",environment,StringComparison.Ordinal))));
    [Theory]
    [InlineData("2026-10-31T00:00:00.0000000Z")] [InlineData("2026-11-01T00:00:00.0000001Z")]
    public void DeadlineMustBePositiveAndAtMost24Hours(string deadline)
    {
        var json=Encoding.UTF8.GetString(Golden).Replace("2026-11-01T00:00:00.0000000Z",deadline,StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(()=>WorkflowSpecification.ReadCanonical(Encoding.UTF8.GetBytes(json)));
    }
    [Theory]
    [InlineData("{\"schemaVersion\":1,")]
    [InlineData("{\"delegationId\":\"untrusted\",")]
    [InlineData("{\"secret\":\"SYNTHETIC-NOT-STORED\",")]
    public void DuplicateUnknownAndAuthorityFieldsAreRejected(string prefix)
    {
        var bytes=Encoding.UTF8.GetBytes(prefix+Encoding.UTF8.GetString(Golden)[1..]);
        var error=Assert.Throws<ArgumentException>(()=>WorkflowSpecification.ReadCanonical(bytes));
        Assert.Equal("Workflow specification rejected.",error.Message);
    }
    [Fact]
    public void InvalidStepOrderNullSemanticsWhitespaceOversizeAndMalformedJsonAreRejected()
    {
        var json=Encoding.UTF8.GetString(Golden);
        foreach(var bad in new[]{json.Replace("\"ordinal\":0","\"ordinal\":2"),json.Replace("\"dueAtUtc\":null","\"dueAtUtc\":\"2026-10-31T00:00:00.0000000Z\"")," "+json,"{}","[",new string(' ',65537)})
            Assert.Throws<ArgumentException>(()=>WorkflowSpecification.ReadCanonical(Encoding.UTF8.GetBytes(bad)));
    }
    private static WorkflowSpecification Build(string? hash = null, WorkflowEnvironment environment = WorkflowEnvironment.Development,
        int emptyId = -1, long revision = 7, int lifetimeHours = 24, int dueMinutes = 3, int offsetHours = 0)
    {
        var ids=Enumerable.Range(1,9).Select(i=>Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}")).ToArray();
        if(emptyId>=0)ids[emptyId]=Guid.Empty;
        var created=new DateTimeOffset(2026,10,31,0,0,0,TimeSpan.Zero).ToOffset(TimeSpan.FromHours(offsetHours));
        return new(ids[0],ids[1],ids[2],ids[3],ids[4],ids[5],ids[6],ids[7],ids[8],revision,hash??new string('a',64),environment,
            created,created.AddHours(lifetimeHours),created.AddMinutes(dueMinutes));
    }
    [Fact]
    public void TypedConstructionNormalizesOffsetAndMatchesIndependentCanonicalVector()
    {
        Assert.Equal(Golden,Build(offsetHours:7).WriteCanonical());
        Assert.Equal(TimeSpan.Zero,Build(offsetHours:7).CreatedAtUtc.Offset);
        Assert.NotEqual(Build().SpecificationHash,Build(environment:WorkflowEnvironment.Test).SpecificationHash);
        Assert.NotEqual(Build().SpecificationHash,Build(dueMinutes:4).SpecificationHash);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void EveryBusinessResourceIdentityIsMandatory(int index)
        => Assert.Throws<ArgumentException>(()=>Build(emptyId:index));
    [Fact]
    public void TypedConstructionRejectsUnsupportedBindingAndScheduleWithoutReader()
    {
        foreach(var hash in new[]{"",new string('A',64),new string('a',63),new string('g',64)})
            Assert.Throws<ArgumentException>(()=>Build(hash:hash));
        Assert.Throws<ArgumentException>(()=>Build(environment:(WorkflowEnvironment)0));
        Assert.Throws<ArgumentException>(()=>Build(environment:(WorkflowEnvironment)99));
        Assert.Throws<ArgumentException>(()=>Build(revision:0));
        Assert.Throws<ArgumentException>(()=>Build(lifetimeHours:0));
        Assert.Throws<ArgumentException>(()=>Build(lifetimeHours:25));
        Assert.Throws<ArgumentException>(()=>Build(dueMinutes:-1));
        Assert.Throws<ArgumentException>(()=>Build(dueMinutes:1440));
    }
}
