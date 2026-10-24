using System.Runtime.InteropServices;
namespace Kordra.Credentials.Tests;

// Only test-owned isolated fixture setup/cleanup. No mutating Keychain import ships in SEC runtime.
internal static class FixtureKeychainNative
{
    private const string Security="/System/Library/Frameworks/Security.framework/Security";
    private const string Core="/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [DllImport(Core)] internal static extern nint CFDataCreate(nint allocator, byte[] bytes, nint length);
    [DllImport(Security)] internal static extern int SecKeychainCreate([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint length, byte[] password, byte prompt, nint access, out nint keychain);
    [DllImport(Security)] internal static extern int SecKeychainDelete(nint keychain);
    [DllImport(Security)] internal static extern int SecKeychainLock(nint keychain);
    [DllImport(Security)] internal static extern int SecKeychainUnlock(nint keychain, uint length, byte[] password, byte usePassword);
    [DllImport(Security)] internal static extern int SecItemAdd(nint attributes, out nint result);
    [DllImport(Security)] internal static extern int SecTrustedApplicationCreateFromPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out nint application);
    [DllImport(Security)] internal static extern int SecAccessCreate(nint descriptor, nint trustedApps, out nint access);
    [DllImport(Security)] internal static extern int SecKeychainCopySearchList(out nint list);
    [DllImport(Security)] internal static extern int SecKeychainSetSearchList(nint list);
    [DllImport(Core)] internal static extern nint CFArrayGetCount(nint array);
    [DllImport(Core)] internal static extern nint CFArrayGetValueAtIndex(nint array, nint index);
    [DllImport(Core)] internal static extern byte CFEqual(nint first, nint second);
}
