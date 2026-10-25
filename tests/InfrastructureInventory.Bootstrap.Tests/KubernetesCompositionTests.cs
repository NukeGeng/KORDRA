using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kordra.Api;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Keychain;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Tests.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kordra.Modules.InfrastructureInventory.Bootstrap.Tests;

public sealed class KubernetesCompositionTests
{
    [Theory]
    [InlineData(false,false)][InlineData(true,false)][InlineData(true,true)]
    public async Task Product_host_registers_exact_route_and_complete_DI_only_with_explicit_trust(bool enabled, bool nomad)
    {
        await using var tls=await KubernetesTlsFixture.Start();var dir=Path.Combine(Path.GetTempPath(),"kubernetes-composition-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Kordra.slnx")))root=root.Parent;Assert.NotNull(root);
            var installation=Guid.NewGuid();var executable=Path.Combine(root.FullName,"src/Host/Kordra.Api/bin/Release/net10.0/Kordra.Api");
            var denylist=Path.Combine(dir,"denylist.json");File.WriteAllBytes(denylist,"[\"SyntheticDeniedPassword\"]"u8.ToArray());
            var policy=LocalPasswordPolicy.Load(denylist,"synthetic-v1","synthetic test",Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(denylist))));
            using var key=RSA.Create(2048);var request=new CertificateRequest("CN=localhost",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
            var create=typeof(LocalPilotHostOptions).GetMethod("CreateForTests",BindingFlags.NonPublic|BindingFlags.Static)!;
            using var options=(LocalPilotHostOptions)create.Invoke(null,["Host=127.0.0.1;Port=5432;Database=kordra;Username=kordra_app",cert,new LocalAuthenticationConfiguration(installation,Guid.NewGuid()),policy,Path.Combine(dir,"process.lock")])!;
            if(enabled)
            {
                typeof(LocalPilotHostOptions).GetProperty("KubernetesTrust")!.SetValue(options,tls.Configuration(installation,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()));
                if(nomad) { await using var other=await NomadTlsFixture.Start(); typeof(LocalPilotHostOptions).GetProperty("NomadTrust")!.SetValue(options,other.Configuration(installation,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid())); }
                typeof(LocalPilotHostOptions).GetProperty("CredentialAccess")!.SetValue(options,new KeychainAccessConfiguration(installation,executable,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable))),new Dictionary<Guid,string>()));
            }
            await using var app=LocalApiHost.CreateLocalPilot(options);
            var routes=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().Select(e=>e.RoutePattern.RawText).ToArray();
            Assert.Equal(enabled,routes.Contains("/api/v1/targets/{targetId:guid}/workloads"));
            Assert.Equal(enabled,app.Services.GetService<KubernetesObservationRegistry>() is not null);
            Assert.Equal(nomad,routes.Contains("/api/v1/targets/{targetId:guid}/nomad-workloads"));
            Assert.Equal(nomad,app.Services.GetService<NomadObservationRegistry>() is not null);
            if(enabled){Assert.NotNull(app.Services.GetRequiredService<CredentialBroker>());Assert.NotNull(app.Services.GetRequiredService<ProtectedReadAdmission>());}
        }
        finally{Directory.Delete(dir,true);}
    }
    [Fact]
    public async Task Registry_requires_exact_installation_tenant_target_scope_and_provider_digest()
    {
        await using var tls=await KubernetesTlsFixture.Start();var installation=Guid.NewGuid();var tenant=Guid.NewGuid();var target=Guid.NewGuid();var scope=Guid.NewGuid();
        var config=tls.Configuration(installation,tenant,target,scope);var registry=new KubernetesObservationRegistry(config);
        var plan=new Kordra.Modules.IdentityAccess.Contracts.ProtectedReadPlan("credential-bound-v1",installation,"inventory.readonly.v1",config.Targets.Single().ConfigurationDigest);
        var selected=new Kordra.Modules.IdentityAccess.Contracts.ReadDependencySelection(plan,tenant,target,"project",scope);
        Assert.True(registry.Matches(selected));
        foreach(var invalid in new[]{selected with{TenantId=Guid.NewGuid()},selected with{TargetId=Guid.NewGuid()},selected with{ScopeId=Guid.NewGuid()},selected with{ScopeKind="environment"},
            selected with{Plan=plan with{InstallationId=Guid.NewGuid()}},selected with{Plan=plan with{ConsumerConfigurationDigest=new string('0',64)}},selected with{Plan=plan with{ConsumerConfigurationDigest=null}}})
            Assert.False(registry.Matches(invalid));
    }

}
