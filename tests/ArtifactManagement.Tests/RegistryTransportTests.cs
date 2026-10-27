using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Kordra.ArtifactManagement.Domain;
using Kordra.Modules.ArtifactManagement.Infrastructure.Registry;
using Xunit;

namespace Kordra.Artifacts.Tests;

public sealed class RegistryTransportTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"schemaVersion\":2}");
    private static OCIImageDigest Digest(byte[]? bytes = null) => OCIImageDigest.Parse("sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes ?? Body)));

    [Theory]
    [InlineData("anonymous", "")]
    [InlineData("basic", "user:páss:word")]
    [InlineData("static-bearer", "synthetic.read-only==")]
    public async Task Fixed_path_TLS_pin_hash_and_request_local_authorization(string mode, string value)
    {
        await using var server = new Fixture();
        using var client = new RegistryReadOnlyTransport(server.Trust(mode), () => DateTimeOffset.UtcNow);
        var response = await client.GetAsync(true, Digest(), Encoding.UTF8.GetBytes(value), default);
        Assert.Equal(Body, response.Body); Assert.Equal("application/json", response.MediaType);
        var request = Assert.Single(server.Requests);
        Assert.StartsWith("GET /v2/team/frontend/manifests/" + Digest().Value + " HTTP/1.1", request);
        if (mode == "anonymous") Assert.DoesNotContain("Authorization:", request, StringComparison.OrdinalIgnoreCase);
        else Assert.Contains("Authorization: " + (mode == "basic" ? "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) : "Bearer " + value), request);
        Assert.Contains("Connection: close", request, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("ca")]
    [InlineData("expired-clock")]
    [InlineData("hostname")]
    [InlineData("eku")]
    public async Task TLS_failure_never_sends_HTTP_or_authorization(string fault)
    {
        await using var server = new Fixture(wrongName: fault == "hostname", missingEku: fault == "eku");
        var trust = server.Trust("basic", wrongPin: fault == "pin", wrongCa: fault == "ca");
        using var client = new RegistryReadOnlyTransport(trust,
            () => fault == "expired-clock" ? DateTimeOffset.UtcNow.AddDays(5) : DateTimeOffset.UtcNow);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(true, Digest(), "user:secret"u8.ToArray(), default));
        Assert.Empty(server.Requests); Assert.DoesNotContain("secret", error.ToString());
    }

    [Theory]
    [InlineData("basic", "missing-colon")]
    [InlineData("basic", ":password")]
    [InlineData("basic", "user:")]
    [InlineData("basic", "user:pass\rword")]
    [InlineData("basic", "user:pass\u0085word")]
    [InlineData("static-bearer", "invalid token")]
    [InlineData("static-bearer", "=invalid")]
    [InlineData("static-bearer", "abc=def")]
    [InlineData("anonymous", "unexpected")]
    public async Task Invalid_secret_never_connects(string mode, string secret)
    {
        await using var server = new Fixture();
        using var client = new RegistryReadOnlyTransport(server.Trust(mode), () => DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(true, Digest(), Encoding.UTF8.GetBytes(secret), default));
        Assert.Empty(server.Requests); Assert.Equal(0, server.Connections);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("encoding")]
    [InlineData("oversize")]
    [InlineData("digest")]
    [InlineData("redirect")]
    public async Task Bad_response_does_not_retry_follow_redirect_or_expose_content(string fault)
    {
        await using var server = new Fixture();
        server.Response = fault switch
        {
            "status" => "HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Bearer realm=\"http://127.0.0.1/token\"\r\nContent-Length: 0\r\n\r\n",
            "encoding" => "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Encoding: gzip\r\nContent-Length: 0\r\n\r\n",
            "oversize" => "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1048577\r\n\r\n",
            "digest" => "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nDocker-Content-Digest: sha256:" + new string('0', 64) + "\r\nContent-Length: " + Body.Length + "\r\n\r\n" + Encoding.UTF8.GetString(Body),
            _ => "HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1/secret\r\nContent-Length: 0\r\n\r\n"
        };
        using var client = new RegistryReadOnlyTransport(server.Trust("anonymous"), () => DateTimeOffset.UtcNow);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync(true, Digest(), ReadOnlyMemory<byte>.Empty, default));
        Assert.Equal(fault == "digest" ? typeof(RegistryContentMismatchException) : typeof(InvalidDataException), error.GetType());
        Assert.Single(server.Requests); Assert.Equal(1, server.Connections);
        Assert.Null(error.InnerException); Assert.DoesNotContain("127.0.0.1", error.Message);
    }

    [Fact]
    public async Task Different_supported_header_algorithm_is_verified_and_request_count_is_bounded()
    {
        await using var server = new Fixture();
        server.Response = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nDocker-Content-Digest: " + Digest().Value +
            "\r\nContent-Length: " + Body.Length + "\r\n\r\n" + Encoding.UTF8.GetString(Body);
        using var client = new RegistryReadOnlyTransport(server.Trust("anonymous"), () => DateTimeOffset.UtcNow);
        var sha512 = OCIImageDigest.Parse("sha512:" + Convert.ToHexStringLower(SHA512.HashData(Body)));
        Assert.Equal(Body, (await client.GetAsync(false, sha512, ReadOnlyMemory<byte>.Empty, default)).Body);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(false, sha512, ReadOnlyMemory<byte>.Empty, default));
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Cancellation_and_disallowed_address_prevent_provider_use()
    {
        await using var server = new Fixture();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var client = new RegistryReadOnlyTransport(server.Trust("anonymous"), () => DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(true, Digest(), ReadOnlyMemory<byte>.Empty, cancelled.Token));
        using var denied = new RegistryReadOnlyTransport(server.Trust("anonymous", wrongIp: true), () => DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidDataException>(() => denied.GetAsync(true, Digest(), ReadOnlyMemory<byte>.Empty, default));
        Assert.Equal(0, server.Connections); Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("stream-limit")]
    [InlineData("header-limit")]
    [InlineData("partial")]
    public async Task Streaming_deadline_and_header_bounds_hold_without_content_length(string fault)
    {
        await using var server = new Fixture();
        if (fault == "timeout") server.Delay = TimeSpan.FromSeconds(3);
        if (fault == "stream-limit")
            server.Response = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\n100001\r\n" + new string('x', 1048577) + "\r\n0\r\n\r\n";
        if (fault == "header-limit")
            server.Response = "HTTP/1.1 200 OK\r\nX-Synthetic: " + new string('x', 17000) + "\r\nContent-Type: application/json\r\nContent-Length: 0\r\n\r\n";
        if (fault == "partial")
            server.Response = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Range: bytes 0-3/100\r\nContent-Length: 4\r\n\r\nnull";
        using var client = new RegistryReadOnlyTransport(server.Trust("anonymous"), () => DateTimeOffset.UtcNow);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(true, Digest(), ReadOnlyMemory<byte>.Empty, default));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2.5));
        Assert.Equal(1, server.Connections); Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Public_verifier_proves_image_over_real_synthetic_TLS_with_two_exact_GETs()
    {
        var config = "{\"os\":\"linux\",\"architecture\":\"amd64\"}"u8.ToArray();
        var root = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            mediaType = "application/vnd.oci.image.manifest.v1+json",
            config = new { mediaType = "application/vnd.oci.image.config.v1+json", digest = Digest(config).Value, size = config.Length },
            layers = Array.Empty<object>()
        });
        await using var server = new Fixture();
        server.Handler = request =>
        {
            var blob = request.StartsWith("GET /v2/team/frontend/blobs/", StringComparison.Ordinal);
            var body = blob ? config : root;
            return "HTTP/1.1 200 OK\r\nContent-Type: " + (blob ? "application/octet-stream" : "application/vnd.oci.image.manifest.v1+json") +
                "\r\nContent-Length: " + body.Length + "\r\n\r\n" + Encoding.UTF8.GetString(body);
        };
        var trust = server.Trust("static-bearer");
        var identity = ArtifactIdentity.Create(trust.TenantId, trust.ProjectId, trust.RegistryId, trust.Repository,
            Digest(root), ArtifactPlatform.Create("linux", "amd64", null));
        var proof = await new ArtifactRegistryVerifier(trust, () => DateTimeOffset.UtcNow)
            .VerifyAsync(identity, "synthetic.read-only"u8.ToArray(), default);
        Assert.Equal(identity, proof.Identity); Assert.Equal(Digest(config), proof.ConfigDigest);
        Assert.Equal(2, server.Requests.Count); Assert.Equal(2, server.Connections);
        Assert.All(server.Requests, request => Assert.Contains("Authorization: Bearer synthetic.read-only", request));
        var foreign = ArtifactIdentity.Create(Guid.NewGuid(), trust.ProjectId, trust.RegistryId, trust.Repository,
            Digest(root), ArtifactPlatform.Create("linux", "amd64", null));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ArtifactRegistryVerifier(trust, () => DateTimeOffset.UtcNow)
            .VerifyAsync(foreign, "synthetic.read-only"u8.ToArray(), default));
        Assert.Equal(2, server.Requests.Count);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly X509Certificate2 _ca, _leaf;
        private readonly Task _serve;
        private int _connections;
        public int Connections => Volatile.Read(ref _connections);
        public List<string> Requests { get; } = [];
        public TimeSpan Delay { get; set; }
        public Func<string, string>? Handler { get; set; }
        public Func<string, Task<string>>? AsyncHandler { get; set; }
        public string Response { get; set; } = "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + Body.Length + "\r\n\r\n" + Encoding.UTF8.GetString(Body);
        public Fixture(bool wrongName = false, bool missingEku = false)
        {
            using var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var caRequest = new CertificateRequest("CN=T10 Test CA", caKey, HashAlgorithmName.SHA256);
            caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            _ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
            var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(wrongName ? IPAddress.Parse("127.0.0.2") : IPAddress.Loopback); request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            if (!missingEku) request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var unsigned = request.Create(_ca, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
            _leaf = unsigned.CopyWithPrivateKey(key);
            _listener.Start(); _serve = Serve();
        }
        public RegistryTrust Trust(string mode, bool wrongPin = false, bool wrongCa = false, bool wrongIp = false, Guid? installation = null, Guid? tenant = null, Guid? project = null, Guid? registry = null) =>
            new(installation ?? Guid.NewGuid(), tenant ?? Guid.NewGuid(), project ?? Guid.NewGuid(), registry ?? Guid.NewGuid(), "Synthetic", "team/frontend",
                new Uri("https://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port),
                [wrongIp ? "10.42.0.2" : "127.0.0.1"], wrongCa ? _leaf.RawData : _ca.RawData,
                wrongPin ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(_leaf.PublicKey.ExportSubjectPublicKeyInfo())), mode);
        private async Task Serve()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var socket = await _listener.AcceptTcpClientAsync(_stop.Token); Interlocked.Increment(ref _connections);
                    await using var tls = new SslStream(socket.GetStream(), false);
                    try
                    {
                        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                        { ServerCertificate = _leaf, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, _stop.Token);
                        using var header = new MemoryStream(); var one = new byte[1];
                        while (header.Length < 16384)
                        {
                            if (await tls.ReadAsync(one, _stop.Token) == 0) break;
                            header.WriteByte(one[0]);
                            if (header.Length >= 4 && header.GetBuffer().AsSpan((int)header.Length - 4, 4).SequenceEqual("\r\n\r\n"u8)) break;
                        }
                        if (header.Length == 0) continue;
                        var request = Encoding.UTF8.GetString(header.ToArray());
                        Requests.Add(request);
                        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, _stop.Token);
                        var outgoing = AsyncHandler is null ? Handler?.Invoke(request) ?? Response : await AsyncHandler(request);
                        await tls.WriteAsync(Encoding.UTF8.GetBytes(outgoing), _stop.Token);
                    }
                    catch (Exception e) when (e is IOException or AuthenticationException or OperationCanceledException) { }
                }
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }
        public async ValueTask DisposeAsync()
        { await _stop.CancelAsync(); _listener.Stop(); await _serve; _leaf.Dispose(); _ca.Dispose(); _stop.Dispose(); }
    }
}
