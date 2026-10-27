using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Kordra.Modules.ArtifactManagement.Infrastructure.Registry;

using Kordra.ArtifactManagement.Domain;

internal sealed class RegistryContentMismatchException() : Exception("Registry content identity is invalid.");

internal sealed record RegistryResponse(string MediaType, byte[] Body);

/// <summary>One invocation, one exact origin, bounded GET only; no shared token-bearing connection.</summary>
internal sealed class RegistryReadOnlyTransport : IDisposable
{
    private readonly RegistryTrust _trust;
    private readonly HttpClient _client;
    private readonly Func<DateTimeOffset> _now;
    private readonly X509Certificate2 _ca;
    private int _bytes, _manifests, _blobs;
    public RegistryReadOnlyTransport(RegistryTrust trust, Func<DateTimeOffset> now)
    {
        _trust = trust; _now = now;
        _ca = X509CertificateLoader.LoadCertificate(trust.ExportCaCertificateDer());
        // Apply offline custody to the initial TLS validation too, not only our callback rebuild.
        var nativePolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
            VerificationTime = now().UtcDateTime
        };
        nativePolicy.CustomTrustStore.Add(_ca);
        nativePolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            AutomaticDecompression = DecompressionMethods.None,
            MaxResponseHeadersLength = 16,
            PooledConnectionLifetime = TimeSpan.Zero,
            ConnectCallback = Connect,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateChainPolicy = nativePolicy,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = ValidateCertificate
            }
        };
        _client = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
    }
    private async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.DnsEndPoint.Host.Trim('[', ']'), _trust.ApiOrigin.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase) ||
            context.DnsEndPoint.Port != _trust.ApiOrigin.Port) throw Unavailable();
        var host = context.DnsEndPoint.Host.Trim('[', ']');
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0 || addresses.Any(ip => !_trust.DestinationIps.Contains(Normalize(ip), StringComparer.Ordinal))) throw Unavailable();
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(addresses[0], context.DnsEndPoint.Port), ct);
            if (socket.RemoteEndPoint is not IPEndPoint remote || remote.Port != _trust.ApiOrigin.Port ||
                !_trust.DestinationIps.Contains(Normalize(remote.Address), StringComparer.Ordinal)) throw Unavailable();
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch { socket.Dispose(); throw Unavailable(); }
    }
    private static string Normalize(IPAddress ip) => (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    private bool ValidateCertificate(object sender, X509Certificate? cert, X509Chain? supplied, SslPolicyErrors errors)
    {
        try
        {
            if (cert is null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
            using var leaf = X509CertificateLoader.LoadCertificate(cert.GetRawCertData());
            var now = _now(); if (now.Offset != TimeSpan.Zero) return false;
            if (!leaf.MatchesHostname(_trust.ApiOrigin.Host.Trim('[', ']'), allowWildcards: false, allowCommonName: false) ||
                leaf.NotBefore.ToUniversalTime() > now.UtcDateTime || leaf.NotAfter.ToUniversalTime() <= now.UtcDateTime ||
                Convert.ToHexStringLower(SHA256.HashData(leaf.PublicKey.ExportSubjectPublicKeyInfo())) != _trust.ServerSpkiSha256) return false;
            var eku = leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
            if (eku is null || !eku.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.1")) return false;
            using var ca = X509CertificateLoader.LoadCertificate(_trust.ExportCaCertificateDer());
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(ca);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.VerificationTime = now.UtcDateTime;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            if (supplied is not null) foreach (var element in supplied.ChainElements.Cast<X509ChainElement>()) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
            return chain.Build(leaf) && chain.ChainElements[^1].Certificate.RawData.AsSpan().SequenceEqual(ca.RawData);
        }
        catch { return false; }
    }
    public async Task<RegistryResponse> GetAsync(bool manifest, OCIImageDigest digest, ReadOnlyMemory<byte> credential, CancellationToken ct)
    {
        if (digest is null || (manifest ? Interlocked.Increment(ref _manifests) > 2 : Interlocked.Increment(ref _blobs) > 1)) throw Unavailable();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(1));
        var path = "/v2/" + _trust.Repository + (manifest ? "/manifests/" : "/blobs/") + digest.Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_trust.ApiOrigin, path))
        { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.ConnectionClose = true;
        request.Headers.Accept.ParseAdd(manifest
            ? "application/vnd.oci.image.manifest.v1+json, application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.v2+json, application/vnd.docker.distribution.manifest.list.v2+json"
            : "application/vnd.oci.image.config.v1+json, application/vnd.docker.container.image.v1+json, application/octet-stream, application/json");
        try
        {
            // Only the post-C caller can lend secret bytes here. Never retain an Authorization field/client default.
            if (_trust.AuthMode == "anonymous")
            { if (!credential.IsEmpty) throw Unavailable(); }
            else if (_trust.AuthMode == "static-bearer")
            { ValidateToken(credential.Span); request.Headers.Authorization = new("Bearer", Encoding.ASCII.GetString(credential.Span)); }
            else if (_trust.AuthMode == "basic")
            {
                ValidateBasic(credential.Span);
                request.Headers.Authorization = new("Basic", Convert.ToBase64String(credential.Span));
            }
            else throw Unavailable();
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0 ||
                response.Content.Headers.ContentRange is not null || response.Content.Headers.ContentLength is < 1 or > 1048576) throw Unavailable();
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrEmpty(mediaType)) throw Unavailable();
            using var body = await response.Content.ReadAsStreamAsync(budget.Token); using var output = new MemoryStream();
            var buffer = new byte[8192];
            byte[]? result = null;
            try
            {
                int count;
                while ((count = await body.ReadAsync(buffer, budget.Token)) != 0)
                {
                    if (Interlocked.Add(ref _bytes, count) > 3145728 || output.Length + count > 1048576) throw Unavailable();
                    output.Write(buffer, 0, count);
                }
                result = output.ToArray();
                if (result.Length == 0 || !digest.MatchesContent(result, result.Length)) throw new RegistryContentMismatchException();
                if (response.Headers.TryGetValues("Docker-Content-Digest", out var values))
                {
                    var declared = values.ToArray();
                    if (declared.Length != 1 || !OCIImageDigest.TryParse(declared[0], out var header) || !header.MatchesContent(result, result.Length)) throw new RegistryContentMismatchException();
                }
                var ready = new RegistryResponse(mediaType, result); result = null; return ready;
            }
            finally
            {
                if (result is not null) CryptographicOperations.ZeroMemory(result);
                CryptographicOperations.ZeroMemory(buffer);
                if (output.TryGetBuffer(out var owned)) CryptographicOperations.ZeroMemory(owned.AsSpan());
            }
        }
        catch (RegistryContentMismatchException) { throw; }
        catch { throw Unavailable(); }
        finally { request.Headers.Authorization = null; }
    }
    private static void ValidateBasic(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 3 or > 16384) throw Unavailable();
        // Validate strict UTF-8 without creating another plaintext string.
        try
        {
            var remaining = bytes;
            while (!remaining.IsEmpty)
            {
                var status = Rune.DecodeFromUtf8(remaining, out var rune, out var consumed);
                if (status != System.Buffers.OperationStatus.Done || Rune.IsControl(rune)) throw Unavailable();
                remaining = remaining[consumed..];
            }
        }
        catch { throw Unavailable(); }
        var separator = bytes.IndexOf((byte)':');
        if (separator < 1 || separator == bytes.Length - 1) throw Unavailable();
    }
    private static void ValidateToken(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > 4096) throw Unavailable();
        var padding = false;
        foreach (var b in bytes)
        {
            if (b == '=') { padding = true; continue; }
            if (padding || !(b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~' or (byte)'+' or (byte)'/')) throw Unavailable();
        }
        if (bytes[0] == '=') throw Unavailable();
    }
    internal static InvalidDataException Unavailable() => new("Registry observation is unavailable.");
    public void Dispose() { _client.Dispose(); _ca.Dispose(); }
}
