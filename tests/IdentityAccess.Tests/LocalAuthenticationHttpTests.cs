using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class LocalAuthenticationHttpTests : IAsyncLifetime
{
    private const int Port = 5128;
    private const string Origin = "https://localhost:5128";
    private readonly X509Certificate2 _certificate = CreateLocalhostCertificate();
    private WebApplication? _app;
    private HttpClient? _client;

    [Fact]
    public async Task Csrf_endpoint_returns_no_store_token_and_host_cookie_with_required_attributes()
    {
        using var response = await Client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

        using var payload = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, payload.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.NotEqual(Guid.Empty, payload.RootElement.GetProperty("correlationId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("requestToken").GetString()));

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-kordra.antiforgery=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Host_origin_and_forwarded_authority_rejections_are_safe_json_errors()
    {
        using var wrongHost = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf");
        wrongHost.Headers.Host = "127.0.0.1:5128";
        using var hostResponse = await Client.SendAsync(wrongHost);
        await AssertErrorAsync(hostResponse, HttpStatusCode.Forbidden, "REQUEST_REJECTED");

        using var wrongOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf");
        wrongOrigin.Headers.TryAddWithoutValidation("Origin", "https://attacker.example");
        using var originResponse = await Client.SendAsync(wrongOrigin);
        await AssertErrorAsync(originResponse, HttpStatusCode.Forbidden, "REQUEST_REJECTED");

        using var forwarded = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf");
        forwarded.Headers.TryAddWithoutValidation("X-Forwarded-Host", "localhost:5128");
        using var forwardedResponse = await Client.SendAsync(forwarded);
        await AssertErrorAsync(forwardedResponse, HttpStatusCode.Forbidden, "REQUEST_REJECTED");
    }

    [Fact]
    public async Task Login_rejects_missing_or_invalid_csrf_before_calling_authentication_storage()
    {
        using var tokenResponse = await Client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        using var request = CreateLoginRequest("synthetic-secret-marker-12345");
        using var response = await Client.SendAsync(request);
        await AssertErrorAsync(response, HttpStatusCode.Forbidden, "REQUEST_REJECTED");

        using var payload = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("synthetic-secret-marker-12345", payload.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_and_oversized_login_bodies_are_rejected_without_storage_access()
    {
        using var tokenResponse = await Client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenPayload = System.Text.Json.JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var requestToken = tokenPayload.RootElement.GetProperty("requestToken").GetString()!;

        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        };
        AddBrowserHeaders(malformed, requestToken);
        using var malformedResponse = await Client.SendAsync(malformed);
        await AssertErrorAsync(malformedResponse, HttpStatusCode.BadRequest, "INVALID_REQUEST");

        using var oversized = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new ByteArrayContent(new byte[8 * 1024 + 1])
        };
        oversized.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        AddBrowserHeaders(oversized, requestToken);
        using var oversizedResponse = await Client.SendAsync(oversized);
        await AssertErrorAsync(oversizedResponse, HttpStatusCode.BadRequest, "INVALID_REQUEST");
    }

    [Fact]
    public async Task Valid_csrf_with_unavailable_storage_never_issues_a_session_cookie_or_discloses_password()
    {
        using var tokenResponse = await Client.GetAsync("/api/v1/auth/csrf");
        using var tokenPayload = System.Text.Json.JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var requestToken = tokenPayload.RootElement.GetProperty("requestToken").GetString()!;
        using var request = CreateLoginRequest("synthetic-secret-marker-12345");
        AddBrowserHeaders(request, requestToken);

        using var response = await Client.SendAsync(request);
        await AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, "AUTHORITY_UNAVAILABLE");
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("synthetic-secret-marker-12345", body, StringComparison.Ordinal);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            Assert.DoesNotContain(cookies, value => value.StartsWith("__Host-kordra.session=", StringComparison.Ordinal));
        }
    }

    private HttpClient Client => _client ?? throw new InvalidOperationException("The HTTP fixture has not started.");

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(LocalApiHost).Assembly.GetName().Name,
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrelCore().ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, Port, listen =>
            {
                listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
                listen.UseHttps(_certificate);
            });
        });
        builder.Services.AddRoutingCore();
        builder.Services.AddSingleton(CreateUnavailableAuthenticationService());
        LocalAuthenticationEndpoints.AddServices(builder.Services);
        _app = builder.Build();
        _app.Use(LocalAuthenticationEndpoints.RequestGuard("localhost:5128"));
        _app.UseAuthentication();
        LocalAuthenticationEndpoints.Map(_app);
        await _app.StartAsync();

        var certificateHash = _certificate.GetCertHash(HashAlgorithmName.SHA256);
        var handler = new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            PooledConnectionLifetime = TimeSpan.Zero,
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (!string.Equals(context.DnsEndPoint.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                    context.DnsEndPoint.Port != Port)
                {
                    throw new InvalidOperationException("The test HTTP client may connect only to the local pilot listener.");
                }

                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(IPAddress.Loopback, Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is not null && CryptographicOperations.FixedTimeEquals(
                        certificate.GetCertHash(HashAlgorithmName.SHA256), certificateHash)
            }
        };
        _client = new HttpClient(handler) { BaseAddress = new Uri(Origin) };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        _certificate.Dispose();
    }

    private static LocalAuthenticationService CreateUnavailableAuthenticationService()
    {
        var configuration = new LocalAuthenticationConfiguration(Guid.NewGuid(), Guid.NewGuid());
        var passwordHasher = new PasswordHasher<KordraIdentityUser>();
        return new LocalAuthenticationService(
            new UnavailableContextFactory(),
            new UnavailableAuditFactory(),
            passwordHasher,
            configuration,
            new FairAsyncReaderWriterAdmissionGate());
    }

    private static HttpRequestMessage CreateLoginRequest(string password) => new(HttpMethod.Post, "/api/v1/auth/login")
    {
        Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { username = "synthetic-user", password }),
            Encoding.UTF8,
            "application/json")
    };

    private static void AddBrowserHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.TryAddWithoutValidation("Origin", Origin);
        request.Headers.TryAddWithoutValidation("X-Kordra-CSRF", token);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var payload = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, payload.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.NotEqual(Guid.Empty, payload.RootElement.GetProperty("correlationId").GetGuid());
        Assert.Equal(code, payload.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("message").GetString()));
    }

    private static X509Certificate2 CreateLocalhostCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
    }

    private sealed class UnavailableContextFactory : IDbContextFactory<KordraIdentityDbContext>
    {
        public KordraIdentityDbContext CreateDbContext() => throw new InvalidOperationException("Synthetic database outage.");
        public Task<KordraIdentityDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<KordraIdentityDbContext>(new InvalidOperationException("Synthetic database outage."));
    }

    private sealed class UnavailableAuditFactory : IIdentityAuditTransactionFactory
    {
        public ValueTask<IIdentityAuditTransaction> EnlistAsync(
            System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction transaction,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IIdentityAuditTransaction>(new InvalidOperationException("Synthetic audit outage."));
    }
}
