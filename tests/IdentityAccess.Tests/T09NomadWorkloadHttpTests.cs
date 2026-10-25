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
    private NomadObservationRegistry? _nomadRegistry;
    private CredentialBroker? _nomadBroker;
    private string NomadUrl => MetadataUrl + "/nomad-workloads";
    private async Task EnableNomad(NomadTlsFixture provider, Guid? scope = null, string scopeKind = "project", Guid? tenant = null)
    {
        await EnableCredentialRead();
        _nomadRegistry = new(provider.Configuration(CredentialInstallation, tenant ?? _tenant, _target, scope ?? _project, scopeKind));
        var executable = Path.Combine(FindProductRoot(), "src/Host/Kordra.Api/bin/Release/net10.0/Kordra.Api");
        var access = new KeychainAccessConfiguration(CredentialInstallation, executable, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable))),
            new Dictionary<Guid,string> { [_credentialManifest!.LocatorId!.Value] = "/private/synthetic-do-not-open.keychain" });
        _nomadBroker = new(Credentials, _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), _host.Authentication, access, _credentialLookup);
    }
    private sealed class NomadFixtureDependencyReader(CurrentAuthorityHttpTests fixture, IServiceProvider services) : IProtectedReadDependencyReader
    {
        public Task<ReadDependencySnapshot?> ReadCurrentUnderAdmissionAsync(ReadDependencySelection selection, CancellationToken ct = default) =>
            new CurrentCredentialDependencyReader(services.GetRequiredService<IDbContextFactory<CredentialDbContext>>(),
                services.GetRequiredService<LocalAuthenticationConfiguration>(), services.GetRequiredService<IAuthorityClock>(), fixture._nomadRegistry, fixture._kubernetesRegistry)
                .ReadCurrentUnderAdmissionAsync(selection, ct);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task T09_actual_owner_HTTPS_uses_registered_scope_UCD_and_exact_sanitized_schema(bool environment)
    {
        await using var provider = await NomadTlsFixture.Start();
        var scope = environment ? await ConfigureEnvironmentTarget() : _project;
        await EnableNomad(provider,scope,environment?"environment":"project");
        using var response=await _client.GetAsync(NomadUrl);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var bytes=await response.Content.ReadAsByteArrayAsync();using var doc=JsonDocument.Parse(bytes);AssertNomadFixture(doc.RootElement);
        Assert.Equal(new[]{"schemaVersion","invocationId","targetId","provider","namespace","region","capturedAtUtc","completedAtUtc","validUntilUtc","coverage","resourceSets"},doc.RootElement.EnumerateObject().Select(x=>x.Name));
        Assert.Equal(_target,doc.RootElement.GetProperty("targetId").GetGuid());Assert.Equal("nomad",doc.RootElement.GetProperty("provider").GetString());
        Assert.Equal(new[]{AuthorityStage.Use,AuthorityStage.Use,AuthorityStage.Disclosure},_probe.Audits.Select(x=>x.Stage));
        Assert.Equal(AuthorityCommitPurpose.CredentialConsumption,_probe.Audits[1].Purpose);Assert.Equal(scope,_probe.Audits[1].ScopeId);
        Assert.Equal(4,provider.Requests.Count);Assert.Equal(1,_credentialLookup.Calls);Assert.All(_credentialLookup.LastMaterial!,x=>Assert.Equal(0,x));
        Assert.DoesNotContain("SYNTHETIC_T08_SECRET",System.Text.Encoding.UTF8.GetString(bytes));Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(bytes.Length,_probe.OutputBytes);
    }
    [Theory]
    [InlineData("scope")][InlineData("tenant")][InlineData("grant")][InlineData("target")]
    public async Task T09_wrong_authority_or_registry_never_calls_Keychain_or_network(string fault)
    {
        await using var provider=await NomadTlsFixture.Start();await EnableNomad(provider,scope:fault=="scope"?Guid.NewGuid():null,tenant:fault=="tenant"?Guid.NewGuid():null);
        if(fault=="grant")
        { await using var db=await Tenants.CreateDbContextAsync();var a=await db.Assignments.SingleAsync(x=>x.Id==_assignment);a.Status="revoked";a.Revision++;await db.SaveChangesAsync(); }
        using var response=await _client.GetAsync(fault=="target"?$"/api/v1/targets/{Guid.NewGuid():D}/nomad-workloads":NomadUrl);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.Empty(await response.Content.ReadAsByteArrayAsync());Assert.Equal(0,_credentialLookup.Calls);Assert.Empty(provider.Requests);Assert.Empty(_probe.Audits);
    }
    [Fact]
    public async Task T09_current_revocation_during_provider_withholds_all_HTTP_bytes()
    {
        await using var provider=await NomadTlsFixture.Start();await EnableNomad(provider);
        provider.Respond=async(c,n)=>
        {
            if(n==2)
            {
                await using var held=await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireWriteAsync();
                await using var db=await Tenants.CreateDbContextAsync();var a=await db.Assignments.SingleAsync(x=>x.Id==_assignment);a.Status="revoked";a.Revision++;await db.SaveChangesAsync();
            }
            await NomadTlsFixture.StandardResponse(c);
        };
        using var response=await _client.GetAsync(NomadUrl);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.Empty(await response.Content.ReadAsByteArrayAsync());Assert.Equal(0,_probe.OutputBytes);
        Assert.Equal(2,_probe.Audits.Count);
    }
    [Fact]
    public async Task T09_result_expiring_during_D_audit_never_reaches_caller_handoff()
    {
        await using var provider=await NomadTlsFixture.Start();await EnableNomad(provider);
        _probe.AfterAudit=c=>{if(c.Stage==AuthorityStage.Disclosure)_host.Clock.Advance(TimeSpan.FromSeconds(16));};
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(NomadUrl));Assert.Equal(0,_probe.OutputBytes);Assert.Equal(3,_probe.Audits.Count);
    }
    [Theory]
    [InlineData(AuthorityCommitStatus.Unavailable)][InlineData(AuthorityCommitStatus.Unknown)]
    public async Task T09_failed_C_audit_never_transmits_a_credential(AuthorityCommitStatus status)
    {
        await using var provider=await NomadTlsFixture.Start();await EnableNomad(provider);_probe.CredentialAuditFault=status;
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(NomadUrl));Assert.Empty(provider.Requests);Assert.Equal(0,_probe.OutputBytes);
    }
    [Theory]
    [InlineData("?namespace=foreign")][InlineData("?region=other")]
    public async Task T09_browser_provider_overrides_are_rejected_before_authority_or_lookup(string query)
    {
        await using var provider=await NomadTlsFixture.Start();await EnableNomad(provider);
        using var response=await _client.GetAsync(NomadUrl+query);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Empty(provider.Requests);Assert.Equal(0,_credentialLookup.Calls);
    }
    private static void AssertNomadFixture(JsonElement body)
    {
        var root=FindProductRoot();using var fixture=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"docs/contracts/fixtures/nomad-workloads-v1/http.json")));
        using var api=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"docs/contracts/openapi/nomad-workloads-v1.openapi.json")));
        Assert.Equal(fixture.RootElement.GetProperty("bodyFields").EnumerateArray().Select(x=>x.GetString()).Order(),body.EnumerateObject().Select(x=>x.Name).Order());
        Assert.Equal(api.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("NomadWorkloadsV1").GetProperty("required").EnumerateArray().Select(x=>x.GetString()).Order(),body.EnumerateObject().Select(x=>x.Name).Order());
        foreach(var set in body.GetProperty("resourceSets").EnumerateArray())
        {
            Assert.Equal(fixture.RootElement.GetProperty("setFields").EnumerateArray().Select(x=>x.GetString()).Order(),set.EnumerateObject().Select(x=>x.Name).Order());
            var key=set.GetProperty("kind").GetString()=="Jobs"?"jobFields":"allocationFields";
            foreach(var item in set.GetProperty("items").EnumerateArray())Assert.Equal(fixture.RootElement.GetProperty(key).EnumerateArray().Select(x=>x.GetString()).Order(),item.EnumerateObject().Select(x=>x.Name).Order());
        }
        foreach(var source in api.RootElement.GetProperty("x-working-tree-source-sha256").EnumerateObject())
            Assert.Equal(source.Value.GetString(),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,source.Name)))));
    }
    [Fact]
    public async Task T09_unknown_helper_outcome_closes_gate_without_C_or_network()
    {
        await using var provider=await NomadTlsFixture.Start();await EnableNomad(provider);_credentialLookup.Quiescent=false;
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(NomadUrl));Assert.Empty(provider.Requests);Assert.Single(_probe.Audits);
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>{await using var held=await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireReadAsync();});
    }

}
