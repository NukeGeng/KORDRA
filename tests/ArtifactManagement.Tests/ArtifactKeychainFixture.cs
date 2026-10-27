using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Xunit;

namespace Kordra.Artifacts.Tests;

/// <summary>Creates/deletes only a new UUID store in the already approved synthetic fixture root.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
internal sealed class ArtifactKeychainFixture : IDisposable
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string Core = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly nint Sec = NativeLibrary.Load(Security), Cf = NativeLibrary.Load(Core);
    private nint _keychain;
    public string Path { get; }
    public ArtifactKeychainFixture(Guid installation, Guid reference, string executable, byte[] secret)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Kordra.slnx"))) directory = directory.Parent;
        var root = System.IO.Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Product root unavailable."), ".amr-local/t08/fixtures");
        if (Environment.GetEnvironmentVariable("KORDRA_CREDENTIAL_KEYCHAIN_FIXTURE_ROOT") != root)
            throw new InvalidOperationException("Approved synthetic Keychain fixture scope required.");
        Directory.CreateDirectory(root);
        if (new DirectoryInfo(root).LinkTarget is not null) throw new InvalidOperationException("Fixture root must not be a symlink.");
        var own = System.IO.Path.Combine(root, Guid.NewGuid().ToString("D")); Directory.CreateDirectory(own);
        File.SetUnixFileMode(own, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Path = System.IO.Path.Combine(own, "artifact-synthetic.keychain");
        var password = RandomNumberGenerator.GetBytes(32);
        nint app = 0, apps = 0, description = 0, access = 0, service = 0, account = 0, data = 0, attributes = 0, item = 0;
        try
        {
            Assert.Equal(0, SecKeychainSetUserInteractionAllowed(0));
            Assert.Equal(0, SecKeychainCreate(Path, (uint)password.Length, password, 0, 0, out _keychain));
            RemoveOwnedSearchEntry();
            Assert.Equal(0, SecTrustedApplicationCreateFromPath(executable, out app)); apps = Array(app);
            description = String("Kordra isolated artifact fixture"); Assert.Equal(0, SecAccessCreate(description, apps, out access));
            service = String("vn.id.kordra.credentials/" + installation.ToString("D")); account = String(reference.ToString("D"));
            data = CFDataCreate(0, secret, secret.Length);
            attributes = Dictionary((Constant("kSecClass"), Constant("kSecClassGenericPassword")), (Constant("kSecAttrService"), service),
                (Constant("kSecAttrAccount"), account), (Constant("kSecValueData"), data),
                (Constant("kSecUseKeychain"), _keychain), (Constant("kSecAttrAccess"), access));
            Assert.Equal(0, SecItemAdd(attributes, out item));
        }
        catch { Dispose(); throw; }
        finally
        {
            foreach (var pointer in new[] { item, attributes, data, account, service, access, description, apps, app }) Release(pointer);
            CryptographicOperations.ZeroMemory(password);
        }
    }
    private void RemoveOwnedSearchEntry()
    {
        Assert.Equal(0, SecKeychainCopySearchList(out var current)); nint replacement = 0;
        try
        {
            var entries = new List<nint>();
            for (nint i = 0; i < CFArrayGetCount(current); i++)
            { var entry = CFArrayGetValueAtIndex(current, i); if (CFEqual(entry, _keychain) == 0) entries.Add(entry); }
            replacement = Array(entries.ToArray()); Assert.Equal(0, SecKeychainSetSearchList(replacement));
        }
        finally { Release(replacement); Release(current); }
    }
    public void Dispose()
    {
        var own = Interlocked.Exchange(ref _keychain, 0); if (own == 0) return;
        var result = SecKeychainDelete(own); Release(own); Assert.Equal(0, result);
        Assert.False(File.Exists(Path)); Assert.False(File.Exists(Path + "-db"));
    }
    private static nint Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(Sec, name));
    private static nint String(string text) => CFStringCreateWithCString(0, text, 0x08000100);
    private static nint Array(params nint[] values) => CFArrayCreate(0, values, values.Length, NativeLibrary.GetExport(Cf, "kCFTypeArrayCallBacks"));
    private static nint Dictionary(params (nint Key, nint Value)[] values) => CFDictionaryCreate(0, values.Select(x => x.Key).ToArray(), values.Select(x => x.Value).ToArray(),
        values.Length, NativeLibrary.GetExport(Cf, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(Cf, "kCFTypeDictionaryValueCallBacks"));
    private static void Release(nint value) { if (value != 0) CFRelease(value); }
    [DllImport(Security)] private static extern int SecKeychainSetUserInteractionAllowed(byte allowed);
    [DllImport(Security)] private static extern int SecKeychainCreate([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint length, byte[] password, byte prompt, nint access, out nint keychain);
    [DllImport(Security)] private static extern int SecKeychainDelete(nint keychain);
    [DllImport(Security)] private static extern int SecTrustedApplicationCreateFromPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out nint application);
    [DllImport(Security)] private static extern int SecAccessCreate(nint descriptor, nint trustedApps, out nint access);
    [DllImport(Security)] private static extern int SecItemAdd(nint attributes, out nint result);
    [DllImport(Security)] private static extern int SecKeychainCopySearchList(out nint list);
    [DllImport(Security)] private static extern int SecKeychainSetSearchList(nint list);
    [DllImport(Core)] private static extern nint CFStringCreateWithCString(nint allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(Core)] private static extern nint CFArrayCreate(nint allocator, nint[] values, nint count, nint callbacks);
    [DllImport(Core)] private static extern nint CFDictionaryCreate(nint allocator, nint[] keys, nint[] values, nint count, nint keyCallbacks, nint valueCallbacks);
    [DllImport(Core)] private static extern nint CFDataCreate(nint allocator, byte[] data, nint length);
    [DllImport(Core)] private static extern nint CFArrayGetCount(nint array);
    [DllImport(Core)] private static extern nint CFArrayGetValueAtIndex(nint array, nint index);
    [DllImport(Core)] private static extern byte CFEqual(nint first, nint second);
    [DllImport(Core)] private static extern void CFRelease(nint value);
}
