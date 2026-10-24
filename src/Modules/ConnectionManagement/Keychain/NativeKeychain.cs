using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Kordra.Modules.ConnectionManagement.Keychain;

/// <summary>Native file-based Keychain operations. Reads run only inside the bounded child process.</summary>
internal static class NativeKeychain
{
    internal const string Security = "/System/Library/Frameworks/Security.framework/Security";
    internal const string Core = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly nint Sec = NativeLibrary.Load(Security), Cf = NativeLibrary.Load(Core);
    internal static nint Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(Sec, name));
    internal static nint True => Marshal.ReadIntPtr(NativeLibrary.GetExport(Cf, "kCFBooleanTrue"));
    internal static nint String(string text) => CFStringCreateWithCString(0, text, 0x08000100);
    internal static nint Array(params nint[] values) => CFArrayCreate(0, values, values.Length, NativeLibrary.GetExport(Cf, "kCFTypeArrayCallBacks"));
    internal static nint Dictionary(params (nint Key, nint Value)[] entries) => CFDictionaryCreate(0, entries.Select(x => x.Key).ToArray(), entries.Select(x => x.Value).ToArray(), entries.Length,
        NativeLibrary.GetExport(Cf, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(Cf, "kCFTypeDictionaryValueCallBacks"));
    internal static byte[]? Read(KeychainRequest request)
    {
        nint keychain = 0, service = 0, account = 0, search = 0, query = 0, data = 0;
        try
        {
            if (!request.IsValid || SecKeychainSetUserInteractionAllowed(0) != 0 || SecKeychainOpen(request.KeychainPath, out keychain) != 0) return null;
            service = String("vn.id.kordra.credentials/" + request.InstallationId.ToString("D")); account = String(request.ReferenceId.ToString("D")); search = Array(keychain);
            query = Dictionary((Constant("kSecClass"), Constant("kSecClassGenericPassword")), (Constant("kSecAttrService"), service),
                (Constant("kSecAttrAccount"), account), (Constant("kSecMatchSearchList"), search), (Constant("kSecReturnData"), True),
                (Constant("kSecMatchLimit"), Constant("kSecMatchLimitOne")));
            if (SecItemCopyMatching(query, out data) != 0 || data == 0 || CFGetTypeID(data) != CFDataGetTypeID()) return null;
            var size = CFDataGetLength(data); if (size is <= 0 or > 16384) return null;
            var bytes = new byte[(int)size]; Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length); return bytes;
        }
        finally { foreach (var item in new[] { data, query, search, account, service, keychain }) Release(item); }
    }
    internal static void Release(nint value) { if (value != 0) CFRelease(value); }
    [DllImport(Security)] internal static extern int SecKeychainSetUserInteractionAllowed(byte allowed);
    [DllImport(Security)] internal static extern int SecKeychainOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out nint keychain);
    [DllImport(Security)] internal static extern int SecItemCopyMatching(nint query, out nint data);
    [DllImport(Core)] internal static extern nint CFStringCreateWithCString(nint allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(Core)] internal static extern nint CFArrayCreate(nint allocator, nint[] values, nint count, nint callbacks);
    [DllImport(Core)] internal static extern nint CFDictionaryCreate(nint allocator, nint[] keys, nint[] values, nint count, nint keyCallbacks, nint valueCallbacks);
    [DllImport(Core)] internal static extern void CFRelease(nint value);
    [DllImport(Core)] internal static extern nuint CFGetTypeID(nint value);
    [DllImport(Core)] internal static extern nuint CFDataGetTypeID();
    [DllImport(Core)] internal static extern nint CFDataGetLength(nint data);
    [DllImport(Core)] internal static extern nint CFDataGetBytePtr(nint data);

}

public static class KeychainChild
{
    /// <summary>Read-only private IPC mode. Does not load API configuration or create any service.</summary>
    public static async Task<int> RunAsync(Stream input, Stream output, CancellationToken ct = default)
    {
        byte[]? secret = null;
        try
        {
            if (!OperatingSystem.IsMacOS()) return 2;
            var request = await KeychainProtocol.ReadRequestAsync(input, ct); if (request is null) return 2;
            secret = NativeKeychain.Read(request);
            await KeychainProtocol.WriteResultAsync(output, request.RequestId, secret, ct); return 0;
        }
        catch { return 2; } // fixed exit only; never disclose native diagnostic/secret material
        finally { if (secret is not null) CryptographicOperations.ZeroMemory(secret); }
    }
}
