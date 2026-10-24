using Kordra.Modules.ConnectionManagement.Keychain;
using Xunit;

namespace Kordra.Credentials.Tests;

public sealed class KeychainProtocolTests
{
    [Fact]
    public void Runtime_native_imports_cannot_create_update_or_delete_Keychain_items()
    {
        var names=typeof(NativeKeychain).GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)
            .Where(x=>x.GetCustomAttributes(typeof(System.Runtime.InteropServices.DllImportAttribute),false).Length>0).Select(x=>x.Name).ToArray();
        Assert.Contains("SecItemCopyMatching",names);
        foreach(var forbidden in new[]{"SecItemAdd","SecItemUpdate","SecItemDelete","SecKeychainCreate","SecKeychainDelete","SecAccessCreate"})Assert.DoesNotContain(forbidden,names);
    }
    [Fact]
    public async Task Binary_secret_roundtrip_preserves_NUL_and_nonUTF8_and_clears_owned_bytes()
    {
        var id = Guid.NewGuid(); byte[] bytes = [0, 255, 254, 10, 13, 42];
        using var stream = new MemoryStream(); await KeychainProtocol.WriteResultAsync(stream, id, bytes, default); stream.Position = 0;
        var material = await KeychainProtocol.ReadResultAsync(stream, id, default); Assert.True(bytes.AsSpan().SequenceEqual(material.Bytes));
        material.Dispose(); Assert.All(material.Bytes!, x => Assert.Equal(0, x));
        Assert.DoesNotContain("255", material.ToString());
    }
    [Theory]
    [InlineData("version")][InlineData("identity")][InlineData("oversize")][InlineData("trailing")][InlineData("truncated")][InlineData("status")][InlineData("magic")]
    public async Task Malformed_frames_never_return_secret_material(string fault)
    {
        var id = Guid.NewGuid(); using var output = new MemoryStream(); await KeychainProtocol.WriteResultAsync(output, id, [1, 2, 3], default);
        var data = output.ToArray();
        if (fault == "version") data[4] = 2; if (fault == "identity") data[6] ^= 255;
        if (fault == "oversize") System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(22), 16385);
        if (fault == "status") data[5] = 1; if (fault == "magic") data[0] = 0;
        if (fault == "trailing") data = [.. data, 0]; if (fault == "truncated") data = data[..^1];
        if (fault == "truncated") await Assert.ThrowsAsync<EndOfStreamException>(() => KeychainProtocol.ReadResultAsync(new MemoryStream(data), id, default));
        else await Assert.ThrowsAsync<InvalidDataException>(() => KeychainProtocol.ReadResultAsync(new MemoryStream(data), id, default));
    }
    [Fact]
    public async Task Input_frame_is_exactly_bounded_private_metadata_and_rejects_duplicate_keys()
    {
        var r = new KeychainRequest(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "/private/fixture.keychain");
        using var stream = new MemoryStream(); await KeychainProtocol.WriteRequestAsync(stream,r,default); stream.Position=0;
        Assert.Equal(r, await KeychainProtocol.ReadRequestAsync(stream,default));
        var json=System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1}");
        var header=new byte[4];System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header,(uint)json.Length);
        Assert.Null(await KeychainProtocol.ReadRequestAsync(new MemoryStream([..header,..json]),default));
        Assert.DoesNotContain("fixture.keychain",r.ToString());
    }
    [Fact]
    public async Task NonMac_child_mode_is_unsupported_without_loading_Keychain()
    {
        if (OperatingSystem.IsMacOS()) return; // Native conformance has a separate explicit macOS fact.
        Assert.Equal(2,await KeychainChild.RunAsync(new MemoryStream(),new MemoryStream()));
    }
}
