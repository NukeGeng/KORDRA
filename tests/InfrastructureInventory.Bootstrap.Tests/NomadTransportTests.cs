using System.Text;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;
using Kordra.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kordra.Modules.InfrastructureInventory.Bootstrap.Tests;

public sealed class NomadTransportTests
{
    [Fact]
    public async Task Exact_four_GETs_verified_TLS_identity_and_empty_observation_preserve_limits()
    {
        await using var f = await NomadTlsFixture.Start(); var target = Guid.NewGuid(); var trust = f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid()).Targets.Single();
        var result = await new NomadWorkloadReader(trust,()=>DateTimeOffset.UtcNow).ReadAsync(Guid.NewGuid(),target,"synthetic-token"u8.ToArray(),default);
        Assert.Equal("complete",result.Coverage);Assert.Equal(new[]{"Jobs","Allocations"},result.ResourceSets.Select(x=>x.Kind));Assert.Equal(4,f.Tokens);
        Assert.Equal(new[]{"/v1/namespace/default?region=global","/v1/jobs?namespace=default&region=global&per_page=25&meta=false","/v1/allocations?namespace=default&region=global&per_page=25&resources=false&task_states=false","/v1/namespace/default?region=global"},f.Requests);
        Assert.Equal(TimeSpan.FromSeconds(15),result.ValidUntilUtc-result.CapturedAtUtc);
    }
    [Fact]
    public async Task Wrong_pin_never_transmits_a_token_or_GET_to_the_server()
    {
        await using var f=await NomadTlsFixture.Start();var target=Guid.NewGuid();var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid(),pin:new string('0',64)).Targets.Single();
        await Assert.ThrowsAnyAsync<Exception>(()=>new NomadWorkloadReader(trust,()=>DateTimeOffset.UtcNow).ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default));
        Assert.Empty(f.Requests);Assert.Equal(0,f.Tokens);
    }
    [Theory]
    [InlineData("name")][InlineData("ca")][InlineData("expired")]
    public async Task Wrong_name_CA_or_certificate_time_never_sends_the_borrowed_token(string fault)
    {
        await using var f=await NomadTlsFixture.Start(wrongName:fault=="name");await using var other=await NomadTlsFixture.Start();
        var target=Guid.NewGuid();var otherCa=other.Configuration(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()).Targets[0].ExportCaCertificateDer();
        var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid(),caDer:fault=="ca"?otherCa:null).Targets[0];
        await Assert.ThrowsAnyAsync<Exception>(()=>new NomadWorkloadReader(trust,()=>DateTimeOffset.UtcNow.AddHours(fault=="expired"?2:0)).ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default));
        Assert.Empty(f.Requests);Assert.Equal(0,f.Tokens);
    }
    [Theory]
    [InlineData("recreated")][InlineData("missing-final")][InlineData("duplicate-json")][InlineData("oversize")][InlineData("encoding")][InlineData("headers")][InlineData("redirect")][InlineData("unauthorized")][InlineData("timeout")]
    public async Task Invalid_identity_or_protocol_failures_withhold_entire_result(string fault)
    {
        await using var f=await NomadTlsFixture.Start();var target=Guid.NewGuid();var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid()).Targets.Single();
        f.Respond=async(c,n)=>
        {
            if(fault=="recreated"&&n==4){await c.Response.WriteAsync("{\"Name\":\"default\",\"CreateIndex\":2}");return;}
            if(fault=="missing-final"&&n==4){c.Response.StatusCode=404;return;}
            if(n==2)
            {
                c.Response.Headers["X-Nomad-KnownLeader"]="true";c.Response.Headers["X-Nomad-Index"]="1";
                if(fault=="duplicate-json"){await c.Response.WriteAsync("[{\"ID\":\"a\",\"ID\":\"b\"}]");return;}
                if(fault=="oversize"){c.Response.ContentLength=262145;await c.Response.WriteAsync(new string('x',262145));return;}
                if(fault=="headers"){c.Response.Headers["X-Oversized"]=new string('x',18000);await c.Response.WriteAsync("[]");return;}
                if(fault=="encoding"){c.Response.Headers.ContentEncoding="gzip";await c.Response.WriteAsync("[]");return;}
                if(fault=="redirect"){c.Response.StatusCode=302;c.Response.Headers.Location="https://untrusted.invalid";return;}
                if(fault=="unauthorized"){c.Response.StatusCode=401;return;}
                if(fault=="timeout"){await Task.Delay(1500,c.RequestAborted);return;}
            }
            await NomadTlsFixture.StandardResponse(c);
        };
        await Assert.ThrowsAnyAsync<Exception>(()=>new NomadWorkloadReader(trust,()=>DateTimeOffset.UtcNow).ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default));
        Assert.InRange(f.Requests.Count,1,4);
    }
    [Theory]
    [InlineData(403)][InlineData(404)][InlineData(429)][InlineData(500)]
    public async Task One_unavailable_collection_is_partial_but_not_a_verified_empty_inventory(int status)
    {
        await using var f=await NomadTlsFixture.Start();var target=Guid.NewGuid();var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid()).Targets.Single();
        f.Respond=(c,n)=>{if(n==2){c.Response.StatusCode=status;return Task.CompletedTask;}return NomadTlsFixture.StandardResponse(c);};
        var r=await new NomadWorkloadReader(trust,()=>DateTimeOffset.UtcNow).ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default);
        Assert.Equal("partial",r.Coverage);Assert.Equal("unavailable",r.ResourceSets[0].Status);Assert.Null(r.ResourceSets[0].ObservationIndex);Assert.Equal("observed",r.ResourceSets[1].Status);
    }
    [Fact]
    public async Task Pagination_is_not_followed_and_invalid_credential_bytes_cause_no_connection()
    {
        await using var f=await NomadTlsFixture.Start();var target=Guid.NewGuid();var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid()).Targets.Single();
        var reader=new NomadWorkloadReader(trust,()=>DateTimeOffset.UtcNow);
        await Assert.ThrowsAnyAsync<Exception>(()=>reader.ReadAsync(Guid.NewGuid(),target,Encoding.ASCII.GetBytes("token\r\nInjected: yes"),default));Assert.Empty(f.Requests);
        f.Respond=async(c,n)=>{if(n==2)c.Response.Headers["X-Nomad-NextToken"]="SYNTHETIC_NEVER_FOLLOW";await NomadTlsFixture.StandardResponse(c);};
        var r=await reader.ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default);Assert.Equal("partial",r.Coverage);Assert.Equal(4,f.Requests.Count);
    }
}
