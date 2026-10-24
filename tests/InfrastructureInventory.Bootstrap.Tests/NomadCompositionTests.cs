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

public sealed class NomadCompositionTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Product_host_registers_exact_route_and_complete_DI_only_with_explicit_trust(bool enabled)
    {
        await using var tls=await NomadTlsFixture.Start();var dir=Path.Combine(Path.GetTempPath(),"nomad-composition-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
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
                typeof(LocalPilotHostOptions).GetProperty("NomadTrust")!.SetValue(options,tls.Configuration(installation,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()));
                typeof(LocalPilotHostOptions).GetProperty("CredentialAccess")!.SetValue(options,new KeychainAccessConfiguration(installation,executable,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable))),new Dictionary<Guid,string>()));
            }
            await using var app=LocalApiHost.CreateLocalPilot(options);
            var routes=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().Select(e=>e.RoutePattern.RawText).ToArray();
            Assert.Equal(enabled,routes.Contains("/api/v1/targets/{targetId:guid}/nomad-workloads"));
            Assert.Equal(enabled,app.Services.GetService<NomadObservationRegistry>() is not null);
            if(enabled){Assert.NotNull(app.Services.GetRequiredService<CredentialBroker>());Assert.NotNull(app.Services.GetRequiredService<ProtectedReadAdmission>());}
        }
        finally{Directory.Delete(dir,true);}
    }
}
