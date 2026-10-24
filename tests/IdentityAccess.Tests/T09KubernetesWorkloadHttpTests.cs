using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.ConnectionManagement.Keychain;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed partial class CurrentAuthorityHttpTests
{
    private KubernetesObservationRegistry? _kubernetesRegistry;
    private CredentialBroker? _kubernetesBroker;
    private string KubernetesUrl => MetadataUrl + "/workloads";
    private async Task EnableKubernetes(KubernetesTlsFixture provider, Guid? scope = null, string scopeKind = "project", Guid? tenant = null)
    {
        await EnableCredentialRead();
        _kubernetesRegistry = new(provider.Configuration(CredentialInstallation, tenant ?? _tenant, _target, scope ?? _project, scopeKind));
        var executable = Path.Combine(FindProductRoot(), "src/Host/Kordra.Api/bin/Release/net10.0/Kordra.Api");
        var access = new KeychainAccessConfiguration(CredentialInstallation, executable, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable))),
            new Dictionary<Guid,string> { [_credentialManifest!.LocatorId!.Value] = "/private/synthetic-do-not-open.keychain" });
        _kubernetesBroker = new(Credentials, _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), _host.Authentication, access, _credentialLookup);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task T091_actual_owner_HTTPS_uses_registered_scope_UCD_and_exact_sanitized_schema(bool environment)
    {
        await using var provider = await KubernetesTlsFixture.Start();
        var scope = environment ? await ConfigureEnvironmentTarget() : _project;
        await EnableKubernetes(provider,scope,environment?"environment":"project");
        provider.Respond=async(c,n)=>
        {
            if(n==2) { await c.Response.WriteAsync("{\"kind\":\"PodList\",\"apiVersion\":\"v1\",\"metadata\":{\"resourceVersion\":\"opaque\"},\"items\":[{\"metadata\":{\"name\":\"sample\",\"uid\":\"pod-uid\",\"namespace\":\"default\"},\"spec\":{\"containers\":[{\"name\":\"main\",\"image\":\"repo:v1\",\"env\":[{\"name\":\"CANARY\",\"value\":\"DO_NOT_DISCLOSE\"}]}]}}]}");return; }
            await KubernetesTlsFixture.StandardResponse(c);
        };
        using var response=await _client.GetAsync(KubernetesUrl);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var bytes=await response.Content.ReadAsByteArrayAsync();using var doc=JsonDocument.Parse(bytes);AssertKubernetesFixture(doc.RootElement);
        Assert.Equal(new[]{"schemaVersion","invocationId","targetId","namespace","capturedAtUtc","completedAtUtc","validUntilUtc","coverage","resourceSets"},doc.RootElement.EnumerateObject().Select(x=>x.Name));
        Assert.Equal(_target,doc.RootElement.GetProperty("targetId").GetGuid());
        Assert.Equal(new[]{AuthorityStage.Use,AuthorityStage.Use,AuthorityStage.Disclosure},_probe.Audits.Select(x=>x.Stage));
        Assert.Equal(AuthorityCommitPurpose.CredentialConsumption,_probe.Audits[1].Purpose);Assert.Equal(scope,_probe.Audits[1].ScopeId);
        Assert.Equal(6,provider.Requests.Count);Assert.Equal(1,_credentialLookup.Calls);Assert.All(_credentialLookup.LastMaterial!,x=>Assert.Equal(0,x));
        Assert.DoesNotContain("DO_NOT_DISCLOSE",System.Text.Encoding.UTF8.GetString(bytes));Assert.DoesNotContain("SYNTHETIC_T08_SECRET",System.Text.Encoding.UTF8.GetString(bytes));Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(bytes.Length,_probe.OutputBytes);
    }
    [Theory]
    [InlineData("scope")][InlineData("tenant")][InlineData("grant")][InlineData("target")]
    public async Task T091_wrong_authority_or_registry_never_calls_Keychain_or_network(string fault)
    {
        await using var provider=await KubernetesTlsFixture.Start();await EnableKubernetes(provider,scope:fault=="scope"?Guid.NewGuid():null,tenant:fault=="tenant"?Guid.NewGuid():null);
        if(fault=="grant")
        { await using var db=await Tenants.CreateDbContextAsync();var a=await db.Assignments.SingleAsync(x=>x.Id==_assignment);a.Status="revoked";a.Revision++;await db.SaveChangesAsync(); }
        using var response=await _client.GetAsync(fault=="target"?$"/api/v1/targets/{Guid.NewGuid():D}/workloads":KubernetesUrl);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.Empty(await response.Content.ReadAsByteArrayAsync());Assert.Equal(0,_credentialLookup.Calls);Assert.Empty(provider.Requests);Assert.Empty(_probe.Audits);
    }
    [Fact]
    public async Task T091_current_revocation_during_provider_withholds_all_HTTP_bytes()
    {
        await using var provider=await KubernetesTlsFixture.Start();await EnableKubernetes(provider);
        provider.Respond=async(c,n)=>
        {
            if(n==2)
            {
                await using var held=await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireWriteAsync();
                await using var db=await Tenants.CreateDbContextAsync();var a=await db.Assignments.SingleAsync(x=>x.Id==_assignment);a.Status="revoked";a.Revision++;await db.SaveChangesAsync();
            }
            await KubernetesTlsFixture.StandardResponse(c);
        };
        using var response=await _client.GetAsync(KubernetesUrl);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.Empty(await response.Content.ReadAsByteArrayAsync());Assert.Equal(0,_probe.OutputBytes);
        Assert.Equal(2,_probe.Audits.Count);
    }
    [Fact]
    public async Task T091_result_expiring_during_D_audit_never_reaches_caller_handoff()
    {
        await using var provider=await KubernetesTlsFixture.Start();await EnableKubernetes(provider);
        _probe.AfterAudit=c=>{if(c.Stage==AuthorityStage.Disclosure)_host.Clock.Advance(TimeSpan.FromSeconds(16));};
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(KubernetesUrl));Assert.Equal(0,_probe.OutputBytes);Assert.Equal(3,_probe.Audits.Count);
    }
    [Theory]
    [InlineData(AuthorityCommitStatus.Unavailable)][InlineData(AuthorityCommitStatus.Unknown)]
    public async Task T091_failed_C_audit_never_transmits_a_credential(AuthorityCommitStatus status)
    {
        await using var provider=await KubernetesTlsFixture.Start();await EnableKubernetes(provider);_probe.CredentialAuditFault=status;
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(KubernetesUrl));Assert.Empty(provider.Requests);Assert.Equal(0,_probe.OutputBytes);
    }
    [Theory]
    [InlineData("?namespace=foreign")][InlineData("?region=other")]
    public async Task T091_browser_provider_overrides_are_rejected_before_authority_or_lookup(string query)
    {
        await using var provider=await KubernetesTlsFixture.Start();await EnableKubernetes(provider);
        using var response=await _client.GetAsync(KubernetesUrl+query);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Empty(provider.Requests);Assert.Equal(0,_credentialLookup.Calls);
    }
    private static void AssertKubernetesFixture(JsonElement body)
    {
        var root=FindProductRoot();using var fixture=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"docs/contracts/fixtures/kubernetes-workloads-v1.http.json")));
        using var api=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"docs/contracts/openapi/kubernetes-workloads-v1.openapi.json")));
        Assert.Equal(fixture.RootElement.GetProperty("bodyFields").EnumerateArray().Select(x=>x.GetString()).Order(),body.EnumerateObject().Select(x=>x.Name).Order());
        Assert.Equal(api.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("KubernetesWorkloadsV1").GetProperty("required").EnumerateArray().Select(x=>x.GetString()).Order(),body.EnumerateObject().Select(x=>x.Name).Order());
        foreach(var set in body.GetProperty("resourceSets").EnumerateArray())
        {
            Assert.Equal(fixture.RootElement.GetProperty("setFields").EnumerateArray().Select(x=>x.GetString()).Order(),set.EnumerateObject().Select(x=>x.Name).Order());
            var key="itemFields";
            foreach(var item in set.GetProperty("items").EnumerateArray())
            {
                Assert.Equal(fixture.RootElement.GetProperty(key).EnumerateArray().Select(x=>x.GetString()).Order(),item.EnumerateObject().Select(x=>x.Name).Order());
                foreach(var image in item.GetProperty("images").EnumerateArray()) Assert.Equal(fixture.RootElement.GetProperty("imageFields").EnumerateArray().Select(x=>x.GetString()).Order(),image.EnumerateObject().Select(x=>x.Name).Order());
            }
        }
        foreach(var source in api.RootElement.GetProperty("x-working-tree-source-sha256").EnumerateObject())
            Assert.Equal(source.Value.GetString(),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,source.Name)))));
    }
    [Fact]
    public async Task T091_unknown_helper_outcome_closes_gate_without_C_or_network()
    {
        await using var provider=await KubernetesTlsFixture.Start();await EnableKubernetes(provider);_credentialLookup.Quiescent=false;
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(KubernetesUrl));Assert.Empty(provider.Requests);Assert.Single(_probe.Audits);
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>{await using var held=await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireReadAsync();});
    }

    private async Task RevokeKubernetesBinding()
    {
        var m=_credentialManifest! with {ManifestId=Guid.NewGuid(),Action="revoke",ExpectedBindingId=_credentialManifest.BindingId,ExpectedRevision=1,
            ProfileId=null,ReferenceId=null,LocatorId=null,NotBeforeUtc=null,ExpiresAtUtc=null};
        Assert.Equal("success",(await ApplyCredential(m)).Status);
    }
    [Fact]
    public async Task T091_revoked_SEC_before_U_prevents_lookup_network_and_disclosure()
    {
        await using var provider=await KubernetesTlsFixture.Start();await EnableKubernetes(provider);await RevokeKubernetesBinding();
        using var response=await _client.GetAsync(KubernetesUrl);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());Assert.Empty(provider.Requests);Assert.Equal(0,_credentialLookup.Calls);Assert.Empty(_probe.Audits);
    }
    [Fact]
    public async Task T091_SEC_revoke_during_lookup_prevents_C_and_any_provider_packet()
    {
        await using var provider=await KubernetesTlsFixture.Start();await EnableKubernetes(provider);
        _credentialLookup.BeforeReturn=async _=>await RevokeKubernetesBinding();
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(KubernetesUrl));Assert.Empty(provider.Requests);Assert.Single(_probe.Audits);Assert.Equal(0,_probe.OutputBytes);
        Assert.All(_credentialLookup.LastMaterial!,x=>Assert.Equal(0,x));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task T091_recreated_namespace_and_oversized_final_projection_never_reach_D(bool oversized)
    {
        await using var provider=await KubernetesTlsFixture.Start();await EnableKubernetes(provider);
        provider.Respond=async(c,n)=>
        {
            if(!oversized&&n==6){await c.Response.WriteAsync("{\"metadata\":{\"name\":\"default\",\"uid\":\"recreated\"},\"status\":{\"phase\":\"Active\"}}");return;}
            if(oversized&&n is >=2 and <=5)
            {
                var kind=n switch{2=>"Pod",3=>"Deployment",4=>"StatefulSet",_=>"DaemonSet"};
                var containers=Enumerable.Range(0,16).Select(i=>new {name="c"+i,image="example.test/"+new string('a',460)+":v1"}).ToArray();
                var items=Enumerable.Range(0,25).Select(i=>new{kind,apiVersion=kind=="Pod"?"v1":"apps/v1",
                    metadata=new{name="item-"+i,uid=kind+"-"+i,@namespace="default"},
                    spec=kind=="Pod"?(object)new{containers}:new{template=new{spec=new{containers}}}}).ToArray();
                await c.Response.WriteAsJsonAsync(new{kind=kind+"List",apiVersion=kind=="Pod"?"v1":"apps/v1",metadata=new{resourceVersion="opaque"},items});return;
            }
            await KubernetesTlsFixture.StandardResponse(c);
        };
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(KubernetesUrl));Assert.Equal(0,_probe.OutputBytes);Assert.Equal(2,_probe.Audits.Count);
        Assert.Equal(6,provider.Requests.Count);Assert.All(_credentialLookup.LastMaterial!,x=>Assert.Equal(0,x));
    }

}
