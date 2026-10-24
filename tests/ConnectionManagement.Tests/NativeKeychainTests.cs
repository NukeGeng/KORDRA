using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Modules.ConnectionManagement.Keychain;
using Xunit;

namespace Kordra.Credentials.Tests;

public sealed class MacKeychainFactAttribute : FactAttribute
{
    public MacKeychainFactAttribute() { if (!OperatingSystem.IsMacOS()) Skip = "Native Keychain fixture requires macOS; unsupported on this platform."; }
}
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class NativeKeychainTests
{
    [MacKeychainFact]
    public async Task Isolated_native_store_intended_apphost_ACL_raw_bytes_locked_and_missing_items_fail_closed()
    {
        var root = FindRoot(); var allowed=Path.GetFullPath(Path.Combine(root,".amr-local/t08/fixtures"));
        if (Environment.GetEnvironmentVariable("KORDRA_CREDENTIAL_KEYCHAIN_FIXTURE_ROOT") != allowed)
            throw new InvalidOperationException("Explicit approved isolated Keychain fixture root is required.");
        Directory.CreateDirectory(allowed); if (new DirectoryInfo(allowed).LinkTarget is not null) throw new InvalidOperationException("Fixture root must not be a symlink.");
        var directory=Path.Combine(allowed,Guid.NewGuid().ToString("D"));Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var path=Path.Combine(directory,"synthetic.keychain");
        var executable=Path.Combine(root,"src/Host/Kordra.Api/bin/Release/net10.0/Kordra.Api");
        var hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable)));
        var installation=Guid.NewGuid();var collisionInstallation=Guid.NewGuid();var reference=Guid.NewGuid();var locator=Guid.NewGuid();
        var password=RandomNumberGenerator.GetBytes(32);var secret=RandomNumberGenerator.GetBytes(256);secret[0]=0;secret[1]=255;
        nint keychain=0,service=0,account=0,data=0,app=0,apps=0,descriptor=0,access=0,attributes=0,added=0,decoyService=0,decoyData=0,decoyAttributes=0,decoyItem=0;
        var evidence=Path.Combine(directory,"evidence.json");
        void Record(string state) { File.WriteAllText(evidence,JsonSerializer.Serialize(new {state,installation,collisionInstallation,reference,path,executable,executableSha256=hash,scope="new isolated synthetic fixture only"})); File.SetUnixFileMode(evidence,UnixFileMode.UserRead|UnixFileMode.UserWrite); }
        Record("authorized-before-create");
        try
        {
            Assert.Equal(0,NativeKeychain.SecKeychainSetUserInteractionAllowed(0));
            Assert.Equal(0,FixtureKeychainNative.SecKeychainCreate(path,(uint)password.Length,password,0,0,out keychain));
            // SecKeychainCreate may append the new store to the search list. Remove only our exact reference.
            RemoveOwnedSearchEntry(keychain);
            Assert.Equal(0,FixtureKeychainNative.SecTrustedApplicationCreateFromPath(executable,out app));apps=NativeKeychain.Array(app);
            descriptor=NativeKeychain.String("Kordra isolated synthetic fixture");Assert.Equal(0,FixtureKeychainNative.SecAccessCreate(descriptor,apps,out access));
            service=NativeKeychain.String("vn.id.kordra.credentials/"+installation.ToString("D"));account=NativeKeychain.String(reference.ToString("D"));data=FixtureKeychainNative.CFDataCreate(0,secret,secret.Length);
            // Same account in the same isolated store, different service. Exact service selection must not return this decoy.
            decoyService=NativeKeychain.String("vn.id.kordra.credentials/"+collisionInstallation.ToString("D"));
            byte[] decoy=[7,7,7];decoyData=FixtureKeychainNative.CFDataCreate(0,decoy,decoy.Length);CryptographicOperations.ZeroMemory(decoy);
            decoyAttributes=NativeKeychain.Dictionary((NativeKeychain.Constant("kSecClass"),NativeKeychain.Constant("kSecClassGenericPassword")),
                (NativeKeychain.Constant("kSecAttrService"),decoyService),(NativeKeychain.Constant("kSecAttrAccount"),account),
                (NativeKeychain.Constant("kSecValueData"),decoyData),(NativeKeychain.Constant("kSecUseKeychain"),keychain),(NativeKeychain.Constant("kSecAttrAccess"),access));
            Assert.Equal(0,FixtureKeychainNative.SecItemAdd(decoyAttributes,out decoyItem));
            attributes=NativeKeychain.Dictionary((NativeKeychain.Constant("kSecClass"),NativeKeychain.Constant("kSecClassGenericPassword")),
                (NativeKeychain.Constant("kSecAttrService"),service),(NativeKeychain.Constant("kSecAttrAccount"),account),
                (NativeKeychain.Constant("kSecValueData"),data),(NativeKeychain.Constant("kSecUseKeychain"),keychain),(NativeKeychain.Constant("kSecAttrAccess"),access));
            Assert.Equal(0,FixtureKeychainNative.SecItemAdd(attributes,out added));Record("created-ACL-bound-item");
            var lookup=new NativeSecretLookup(new(installation,executable,hash,new Dictionary<Guid,string>{{locator,path}}));
            using(var result=await lookup.ReadAsync(new(1,Guid.NewGuid(),installation,reference,path),default))
            {Assert.True(result.Quiescent);Assert.NotNull(result.Bytes);Assert.True(secret.AsSpan().SequenceEqual(result.Bytes));}
            using(var missing=await lookup.ReadAsync(new(1,Guid.NewGuid(),installation,Guid.NewGuid(),path),default))
            {Assert.True(missing.Quiescent);Assert.Null(missing.Bytes);}
            Assert.Equal(0,FixtureKeychainNative.SecKeychainLock(keychain));
            using(var locked=await lookup.ReadAsync(new(1,Guid.NewGuid(),installation,reference,path),default))
            {Assert.True(locked.Quiescent);Assert.Null(locked.Bytes);}
            Record("native-roundtrip-missing-and-locked-passed");
        }
        finally
        {
            foreach(var value in new[]{added,attributes,decoyItem,decoyAttributes,decoyData,decoyService,access,descriptor,apps,app,data,account,service})NativeKeychain.Release(value);
            if(keychain!=0)
            {
                var deleted=FixtureKeychainNative.SecKeychainDelete(keychain);NativeKeychain.Release(keychain);
                Record(deleted==0?"fixture-deleted":"cleanup-needs-investigation");
                Assert.Equal(0,deleted);
                Assert.False(File.Exists(path)); Assert.False(File.Exists(path + "-db"));
            }
            CryptographicOperations.ZeroMemory(password);CryptographicOperations.ZeroMemory(secret);
        }
    }
    private static void RemoveOwnedSearchEntry(nint own)
    {
        Assert.Equal(0,FixtureKeychainNative.SecKeychainCopySearchList(out var current));nint replacement=0;
        try
        {
            var entries=new List<nint>();
            for(nint i=0;i<FixtureKeychainNative.CFArrayGetCount(current);i++)
            {var value=FixtureKeychainNative.CFArrayGetValueAtIndex(current,i);if(FixtureKeychainNative.CFEqual(value,own)==0)entries.Add(value);}
            replacement=NativeKeychain.Array(entries.ToArray());Assert.Equal(0,FixtureKeychainNative.SecKeychainSetSearchList(replacement));
        }
        finally{NativeKeychain.Release(replacement);NativeKeychain.Release(current);}
    }
    private static string FindRoot(){var p=new DirectoryInfo(AppContext.BaseDirectory);while(p is not null&&!File.Exists(Path.Combine(p.FullName,"Kordra.slnx")))p=p.Parent;return p?.FullName??throw new InvalidOperationException("Product root missing.");}
}
