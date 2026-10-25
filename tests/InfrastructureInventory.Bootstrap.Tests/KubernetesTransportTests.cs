using System.Text;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Kubernetes;
using Kordra.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kordra.Modules.InfrastructureInventory.Bootstrap.Tests;

public sealed class KubernetesTransportTests
{
    [Fact]
    public async Task Exact_six_GETs_verified_TLS_identity_and_empty_observation_preserve_limits()
    {
        await using var f = await KubernetesTlsFixture.Start(); var target = Guid.NewGuid(); var trust = f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid()).Targets.Single();
        var result = await new KubernetesWorkloadReader(trust,()=>DateTimeOffset.UtcNow).ReadAsync(Guid.NewGuid(),target,"synthetic-token"u8.ToArray(),default);
        Assert.Equal("complete",result.Coverage);Assert.Equal(new[]{"Pod","Deployment","StatefulSet","DaemonSet"},result.ResourceSets.Select(x=>x.Kind));Assert.Equal(6,f.Tokens);
        Assert.Equal(new[]{"/api/v1/namespaces/default","/api/v1/namespaces/default/pods?limit=25&timeoutSeconds=1","/apis/apps/v1/namespaces/default/deployments?limit=25&timeoutSeconds=1","/apis/apps/v1/namespaces/default/statefulsets?limit=25&timeoutSeconds=1","/apis/apps/v1/namespaces/default/daemonsets?limit=25&timeoutSeconds=1","/api/v1/namespaces/default"},f.Requests);
        Assert.Equal(TimeSpan.FromSeconds(15),result.ValidUntilUtc-result.CapturedAtUtc);
    }
    [Fact]
    public async Task Wrong_pin_never_transmits_a_token_or_GET_to_the_server()
    {
        await using var f=await KubernetesTlsFixture.Start();var target=Guid.NewGuid();var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid(),pin:new string('0',64)).Targets.Single();
        await Assert.ThrowsAnyAsync<Exception>(()=>new KubernetesWorkloadReader(trust,()=>DateTimeOffset.UtcNow).ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default));
        Assert.Empty(f.Requests);Assert.Equal(0,f.Tokens);
    }
    [Theory]
    [InlineData("name")][InlineData("ca")][InlineData("expired")]
    public async Task Wrong_name_CA_or_certificate_time_never_sends_the_borrowed_token(string fault)
    {
        await using var f=await KubernetesTlsFixture.Start(wrongName:fault=="name");await using var other=await KubernetesTlsFixture.Start();
        var target=Guid.NewGuid();var otherCa=other.Configuration(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()).Targets[0].ExportCaCertificateDer();
        var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid(),caDer:fault=="ca"?otherCa:null).Targets[0];
        await Assert.ThrowsAnyAsync<Exception>(()=>new KubernetesWorkloadReader(trust,()=>DateTimeOffset.UtcNow.AddHours(fault=="expired"?2:0)).ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default));
        Assert.Empty(f.Requests);Assert.Equal(0,f.Tokens);
    }
    [Theory]
    [InlineData("recreated")][InlineData("missing-final")][InlineData("duplicate-json")][InlineData("oversize")][InlineData("encoding")][InlineData("headers")][InlineData("redirect")][InlineData("unauthorized")][InlineData("timeout")]
    public async Task Invalid_identity_or_protocol_failures_withhold_entire_result(string fault)
    {
        await using var f=await KubernetesTlsFixture.Start();var target=Guid.NewGuid();var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid()).Targets.Single();
        f.Respond=async(c,n)=>
        {
            if(fault=="recreated"&&n==6){await c.Response.WriteAsync("{\"metadata\":{\"name\":\"default\",\"uid\":\"recreated\"},\"status\":{\"phase\":\"Active\"}}");return;}
            if(fault=="missing-final"&&n==6){c.Response.StatusCode=404;return;}
            if(n==2)
            {
                if(fault=="duplicate-json"){await c.Response.WriteAsync("{\"items\":[],\"items\":[]}");return;}
                if(fault=="oversize"){c.Response.ContentLength=262145;await c.Response.WriteAsync(new string('x',262145));return;}
                if(fault=="headers"){c.Response.Headers["X-Oversized"]=new string('x',18000);await c.Response.WriteAsync("[]");return;}
                if(fault=="encoding"){c.Response.Headers.ContentEncoding="gzip";await c.Response.WriteAsync("[]");return;}
                if(fault=="redirect"){c.Response.StatusCode=302;c.Response.Headers.Location="https://untrusted.invalid";return;}
                if(fault=="unauthorized"){c.Response.StatusCode=401;return;}
                if(fault=="timeout"){await Task.Delay(1500,c.RequestAborted);return;}
            }
            await KubernetesTlsFixture.StandardResponse(c);
        };
        await Assert.ThrowsAnyAsync<Exception>(()=>new KubernetesWorkloadReader(trust,()=>DateTimeOffset.UtcNow).ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default));
        Assert.InRange(f.Requests.Count,1,6);
    }
    [Theory]
    [InlineData(403)][InlineData(404)][InlineData(429)][InlineData(500)]
    public async Task One_unavailable_collection_is_partial_but_not_a_verified_empty_inventory(int status)
    {
        await using var f=await KubernetesTlsFixture.Start();var target=Guid.NewGuid();var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid()).Targets.Single();
        f.Respond=(c,n)=>{if(n==2){c.Response.StatusCode=status;return Task.CompletedTask;}return KubernetesTlsFixture.StandardResponse(c);};
        var r=await new KubernetesWorkloadReader(trust,()=>DateTimeOffset.UtcNow).ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default);
        Assert.Equal("partial",r.Coverage);Assert.Equal("unavailable",r.ResourceSets[0].Status);Assert.Null(r.ResourceSets[0].ResourceVersion);Assert.Equal("observed",r.ResourceSets[1].Status);
    }
    [Fact]
    public async Task Pagination_is_not_followed_and_invalid_credential_bytes_cause_no_connection()
    {
        await using var f=await KubernetesTlsFixture.Start();var target=Guid.NewGuid();var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid()).Targets.Single();
        var reader=new KubernetesWorkloadReader(trust,()=>DateTimeOffset.UtcNow);
        await Assert.ThrowsAnyAsync<Exception>(()=>reader.ReadAsync(Guid.NewGuid(),target,Encoding.ASCII.GetBytes("token\r\nInjected: yes"),default));Assert.Empty(f.Requests);
        f.Respond=async(c,n)=>{if(n==2){await c.Response.WriteAsync("{\"metadata\":{\"resourceVersion\":\"1\",\"continue\":\"SYNTHETIC_NEVER_FOLLOW\"},\"items\":[]}");return;}await KubernetesTlsFixture.StandardResponse(c);};
        var r=await reader.ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default);Assert.Equal("partial",r.Coverage);Assert.Equal(6,f.Requests.Count);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Unapproved_literal_or_DNS_address_is_rejected_before_HTTP_and_credentials(bool dns)
    {
        await using var f=await KubernetesTlsFixture.Start();var target=Guid.NewGuid();
        var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid(),destinationIps:["10.0.0.1"],host:dns?"localhost":null).Targets.Single();
        await Assert.ThrowsAnyAsync<Exception>(()=>new KubernetesWorkloadReader(trust,()=>DateTimeOffset.UtcNow).ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),default));
        Assert.Empty(f.Requests);Assert.Equal(0,f.Tokens);
    }
    [Theory]
    [InlineData("chunked")][InlineData("total")][InlineData("all-unavailable")][InlineData("deleting")][InlineData("inactive")][InlineData("wrong-first")][InlineData("cancelled")]
    public async Task Whole_attempt_limits_identity_and_cancellation_are_fail_closed(string fault)
    {
        await using var f=await KubernetesTlsFixture.Start();var target=Guid.NewGuid();var trust=f.Configuration(Guid.NewGuid(),Guid.NewGuid(),target,Guid.NewGuid()).Targets.Single();
        using var ct=new CancellationTokenSource();
        f.Respond=async(c,n)=>
        {
            if(fault=="cancelled"&&n==2){ct.Cancel();await Task.Delay(100,c.RequestAborted);return;}
            if(fault=="all-unavailable"&&n is >=2 and <=5){c.Response.StatusCode=403;return;}
            if(fault=="wrong-first"&&n==1){await c.Response.WriteAsync("{\"metadata\":{\"name\":\"default\",\"uid\":\"other\"},\"status\":{\"phase\":\"Active\"}}");return;}
            if(fault is "deleting" or "inactive" && n==6)
            {await c.Response.WriteAsync("{\"metadata\":{\"name\":\"default\",\"uid\":\"synthetic-namespace-uid\",\"deletionTimestamp\":\"2026-01-01T00:00:00Z\"},\"status\":{\"phase\":\"Terminating\"}}");return;}
            if(fault=="chunked"&&n==2)
            {await c.Response.StartAsync();await c.Response.WriteAsync(new string('x',262145));return;}
            if(fault=="total"&&n is >=2 and <=5)
            {var body="{\"metadata\":{\"resourceVersion\":\"opaque\"},\"items\":[]}";await c.Response.WriteAsync(body.PadRight(262144));return;}
            await KubernetesTlsFixture.StandardResponse(c);
        };
        await Assert.ThrowsAnyAsync<Exception>(()=>new KubernetesWorkloadReader(trust,()=>DateTimeOffset.UtcNow).ReadAsync(Guid.NewGuid(),target,"synthetic"u8.ToArray(),ct.Token));
        Assert.InRange(f.Requests.Count,1,6);
    }

}
