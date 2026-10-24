using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.Credentials.Tests;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Keychain;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class NomadPersonalLabFactAttribute : FactAttribute
{
    public NomadPersonalLabFactAttribute()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("KORDRA_NOMAD_PERSONAL_LAB_CASE") != "T09-NOMAD-LOCAL-002")
            Skip = "Optional operational validation requires explicit new personal lab case, isolated Keychain and Mac; never company access.";
    }
}
public sealed partial class CurrentAuthorityHttpTests
{
    [NomadPersonalLabFact]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    public async Task T09_personal_lab_native_Keychain_actual_owner_UCD_and_live_Nomad_observation()
    {
        var root=FindProductRoot();var lab=Path.Combine(root,".amr-local/t09/nomad-lab");
        using var preflight=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(lab,"product-lab-preflight.json")));
        Assert.Equal("T09-NOMAD-LOCAL-002",preflight.RootElement.GetProperty("case_id").GetString());
        foreach(var pair in new[]{("ca.pem","ca_sha256"),("agent.pem","agent_certificate_sha256")})
            Assert.Equal(preflight.RootElement.GetProperty(pair.Item2).GetString(),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(lab,pair.Item1)))));
        var accountsPath=Path.Combine(lab,"accounts.json");Assert.Null(new FileInfo(accountsPath).LinkTarget);
        Assert.Equal((UnixFileMode)0,File.GetUnixFileMode(accountsPath)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.OtherRead|UnixFileMode.OtherWrite));
        using var accounts=JsonDocument.Parse(File.ReadAllBytes(accountsPath));Assert.Equal("kordra-nomad-t09",accounts.RootElement.GetProperty("vm").GetString());
        var read=accounts.RootElement.GetProperty("readonly");Assert.Equal("client",read.GetProperty("Type").GetString());Assert.Equal(new[]{"kordra-readonly"},read.GetProperty("Policies").EnumerateArray().Select(x=>x.GetString()));
        Assert.True(read.GetProperty("ExpirationTime").GetDateTimeOffset()>DateTimeOffset.UtcNow);
        await EnableCredentialRead();var m=_credentialManifest!;
        var directory=Path.Combine(root,".amr-local/t09/fixtures",Guid.NewGuid().ToString("D"));Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var path=Path.Combine(directory,"personal-lab.keychain");var evidence=Path.Combine(directory,"receipt.json");
        var executable=Path.Combine(root,"src/Host/Kordra.Api/bin/Release/net10.0/Kordra.Api");var executableHash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable)));
        void Record(string state)=>File.WriteAllText(evidence,JsonSerializer.Serialize(new {caseId="T09-NOMAD-LOCAL-002",state,installation=CredentialInstallation,reference=m.ReferenceId,keychain=path,executableSha256=executableHash,scope="new own-lab read-only token/new UUID Keychain/test-only owner facts; company and existing stores excluded"}));
        Record("authorized-before-create");
        var observationPassed=false;
        var secret=Encoding.ASCII.GetBytes(read.GetProperty("SecretID").GetString()!);var password=RandomNumberGenerator.GetBytes(32);
        nint keychain=0,app=0,apps=0,description=0,access=0,service=0,account=0,data=0,attributes=0,item=0;
        try
        {
            Assert.Equal(0,NativeKeychain.SecKeychainSetUserInteractionAllowed(0));
            Assert.Equal(0,FixtureKeychainNative.SecKeychainCreate(path,(uint)password.Length,password,0,0,out keychain));
            RemoveNewPersonalStoreFromSearch(keychain);
            Assert.Equal(0,FixtureKeychainNative.SecTrustedApplicationCreateFromPath(executable,out app));apps=NativeKeychain.Array(app);
            description=NativeKeychain.String("Kordra isolated personal Nomad lab");Assert.Equal(0,FixtureKeychainNative.SecAccessCreate(description,apps,out access));
            service=NativeKeychain.String("vn.id.kordra.credentials/"+CredentialInstallation.ToString("D"));account=NativeKeychain.String(m.ReferenceId!.Value.ToString("D"));data=FixtureKeychainNative.CFDataCreate(0,secret,secret.Length);
            attributes=NativeKeychain.Dictionary((NativeKeychain.Constant("kSecClass"),NativeKeychain.Constant("kSecClassGenericPassword")),(NativeKeychain.Constant("kSecAttrService"),service),
                (NativeKeychain.Constant("kSecAttrAccount"),account),(NativeKeychain.Constant("kSecValueData"),data),(NativeKeychain.Constant("kSecUseKeychain"),keychain),(NativeKeychain.Constant("kSecAttrAccess"),access));
            Assert.Equal(0,FixtureKeychainNative.SecItemAdd(attributes,out item));Record("created-new-ACL-bound-item");
            using var ca=X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(lab,"ca.pem")));
            using var leaf=X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(lab,"agent.pem")));
            var bytes=JsonSerializer.SerializeToUtf8Bytes(new {schemaVersion=1,installationId=CredentialInstallation,targets=new[]{new {tenantId=_tenant,targetId=_target,scopeKind="project",scopeId=_project,
                apiOrigin=preflight.RootElement.GetProperty("origin").GetString(),destinationIps=new[]{"127.0.0.1"},caCertificateDerBase64=Convert.ToBase64String(ca.RawData),serverSpkiSha256=Convert.ToHexStringLower(SHA256.HashData(leaf.PublicKey.ExportSubjectPublicKeyInfo())),
                region="global",namespaceName="default",namespaceCreateIndex=preflight.RootElement.GetProperty("namespaceCreateIndex").GetString(),destinationKind="personal-loopback"}}});
            _nomadRegistry=new(NomadTrustConfiguration.Parse(bytes,Convert.ToHexStringLower(SHA256.HashData(bytes)),CredentialInstallation));
            var config=new KeychainAccessConfiguration(CredentialInstallation,executable,executableHash,new Dictionary<Guid,string>{{m.LocatorId!.Value,path}});
            _nomadBroker=new CredentialBroker(Credentials,_host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(),_host.Authentication,config);
            using var response=await _client.GetAsync(NomadUrl);Assert.Equal(System.Net.HttpStatusCode.OK,response.StatusCode);
            var body=await response.Content.ReadAsByteArrayAsync();using var result=JsonDocument.Parse(body);AssertNomadFixture(result.RootElement);
            Assert.Equal("nomad",result.RootElement.GetProperty("provider").GetString());Assert.Equal(_target,result.RootElement.GetProperty("targetId").GetGuid());
            Assert.False(Encoding.UTF8.GetString(body).Contains(read.GetProperty("SecretID").GetString()!,StringComparison.Ordinal),"Credential appeared in public result");
            Assert.Equal(new[]{Kordra.Modules.IdentityAccess.Contracts.AuthorityStage.Use,Kordra.Modules.IdentityAccess.Contracts.AuthorityStage.Use,Kordra.Modules.IdentityAccess.Contracts.AuthorityStage.Disclosure},_probe.Audits.Select(x=>x.Stage));
            observationPassed=true;Record("native-Keychain-owner-UCD-live-Nomad-passed");
        }
        finally
        {
            foreach(var handle in new[]{item,attributes,data,account,service,access,description,apps,app})NativeKeychain.Release(handle);
            if(keychain!=0){var status=FixtureKeychainNative.SecKeychainDelete(keychain);NativeKeychain.Release(keychain);Assert.Equal(0,status);Assert.False(File.Exists(path));Assert.False(File.Exists(path+"-db"));Record(observationPassed?"passed-and-isolated-Keychain-deleted":"failed-and-isolated-Keychain-deleted");}
            CryptographicOperations.ZeroMemory(secret);CryptographicOperations.ZeroMemory(password);
        }
    }
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void RemoveNewPersonalStoreFromSearch(nint own)
    {
        Assert.Equal(0,FixtureKeychainNative.SecKeychainCopySearchList(out var list));nint replacement=0;
        try{var keep=new List<nint>();for(nint i=0;i<FixtureKeychainNative.CFArrayGetCount(list);i++){var v=FixtureKeychainNative.CFArrayGetValueAtIndex(list,i);if(FixtureKeychainNative.CFEqual(v,own)==0)keep.Add(v);}replacement=NativeKeychain.Array(keep.ToArray());Assert.Equal(0,FixtureKeychainNative.SecKeychainSetSearchList(replacement));}
        finally{NativeKeychain.Release(replacement);NativeKeychain.Release(list);}
    }
}
