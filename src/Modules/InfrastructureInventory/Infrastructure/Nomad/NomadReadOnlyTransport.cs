using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;

internal sealed record NomadResponse(int Status, byte[] Body, bool KnownLeader, string? Index, bool Partial);

/// <summary>One invocation, one exact origin, bounded GET only; no shared token-bearing connection.</summary>
internal sealed class NomadReadOnlyTransport : IDisposable
{
    private readonly NomadTargetTrust _trust;
    private readonly HttpClient _client;
    private readonly Func<DateTimeOffset> _now;
    private readonly X509Certificate2 _ca;
    private int _bytes;
    public NomadReadOnlyTransport(NomadTargetTrust trust, Func<DateTimeOffset> now)
    {
        _trust = trust; _now = now;
        _ca = X509CertificateLoader.LoadCertificate(trust.ExportCaCertificateDer());
        // Apply offline custody to the initial TLS validation too, not only our callback rebuild.
        var nativePolicy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true,
            VerificationTime = now().UtcDateTime };
        nativePolicy.CustomTrustStore.Add(_ca);
        nativePolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        var handler = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false, UseCookies = false, Credentials = null,
            AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 16,
            PooledConnectionLifetime = TimeSpan.Zero, ConnectCallback = Connect,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateChainPolicy = nativePolicy,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = ValidateCertificate
            }
        };
        _client = new(handler) { Timeout = Timeout.InfiniteTimeSpan, DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
    }
    private async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.DnsEndPoint.Host.Trim('[', ']'), _trust.ApiOrigin.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase) ||
            context.DnsEndPoint.Port != _trust.ApiOrigin.Port) throw Unavailable();
        var host = context.DnsEndPoint.Host.Trim('[', ']');
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0 || addresses.Any(ip => !_trust.DestinationIps.Contains(Normalize(ip), StringComparer.Ordinal))) throw Unavailable();
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try { await socket.ConnectAsync(new IPEndPoint(addresses[0], context.DnsEndPoint.Port), ct); return new NetworkStream(socket, ownsSocket: true); }
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
    public async Task<NomadResponse> GetAsync(string path, ReadOnlyMemory<byte> credential, CancellationToken ct)
    {
        var ns = Uri.EscapeDataString(_trust.NamespaceName); var region = Uri.EscapeDataString(_trust.Region);
        if (path != $"/v1/namespace/{ns}?region={region}" && path != $"/v1/jobs?namespace={ns}&region={region}&per_page=25&meta=false" &&
            path != $"/v1/allocations?namespace={ns}&region={region}&per_page=25&resources=false&task_states=false") throw Unavailable();
        ValidateToken(credential.Span);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(1));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_trust.ApiOrigin, path))
        { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.ConnectionClose = true;
        // Accepted narrow post-C exception. Never put this in DefaultRequestHeaders or diagnostic output.
        request.Headers.Add("X-Nomad-Token", Encoding.ASCII.GetString(credential.Span));
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            if (response.Content.Headers.ContentEncoding.Count != 0) throw Unavailable();
            var status = (int)response.StatusCode;
            if (status != 200) return new(status, [], false, null, false);
            if (response.Content.Headers.ContentLength is > 262144) throw Unavailable();
            using var body = await response.Content.ReadAsStreamAsync(budget.Token); using var output = new MemoryStream();
            var buffer = new byte[8192]; int count;
            while ((count = await body.ReadAsync(buffer, budget.Token)) != 0)
            { _bytes += count; if (output.Length + count > 262144 || _bytes > 1048576) throw Unavailable(); output.Write(buffer, 0, count); }
            return new(status, output.ToArray(), Header(response, "X-Nomad-KnownLeader") == "true",
                Header(response, "X-Nomad-Index"), !string.IsNullOrEmpty(Header(response, "X-Nomad-NextToken")));
        }
        finally { request.Headers.Remove("X-Nomad-Token"); }
    }
    private static string? Header(HttpResponseMessage response, string name)
    { if (!response.Headers.TryGetValues(name, out var values)) return null; var a = values.ToArray(); if (a.Length != 1) throw Unavailable(); return a[0]; }
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
    internal static InvalidDataException Unavailable() => new("Nomad observation is unavailable.");
    public void Dispose() { _client.Dispose(); _ca.Dispose(); }
}
