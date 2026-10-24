using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Nomad;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kordra.Tests.Shared;

internal sealed class NomadTlsFixture : IAsyncDisposable
{
    private readonly RSA _key;
    private readonly X509Certificate2 _ca;
    private readonly X509Certificate2 _leaf;
    private readonly WebApplication _app;
    public int Port { get; }
    public List<string> Requests { get; } = [];
    public int Tokens { get; private set; }
    public Func<HttpContext, int, Task>? Respond { get; set; }
    public string? LastToken { get; private set; }
    private NomadTlsFixture(WebApplication app, X509Certificate2 ca, X509Certificate2 leaf, RSA key, int port)
    { _app = app; _ca = ca; _leaf = leaf; _key = key; Port = port; }
    public static async Task<NomadTlsFixture> Start(bool wrongName = false)
    {
        var key = RSA.Create(2048); var request = new CertificateRequest("CN=Synthetic Nomad CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var signedCa = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        var ca = X509CertificateLoader.LoadCertificate(signedCa.RawData);
        using var leafKey = RSA.Create(2048); var leafRequest = new CertificateRequest("CN=ignored", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); if(wrongName) san.AddDnsName("wrong.invalid"); else san.AddIpAddress(IPAddress.Loopback); leafRequest.CertificateExtensions.Add(san.Build());
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var issued = leafRequest.Create(signedCa, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
        using var withKey = issued.CopyWithPrivateKey(leafKey); var leaf = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pkcs12), null);
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(NomadTlsFixture).Assembly.GetName().Name });
        builder.Logging.ClearProviders(); builder.Services.AddRoutingCore(); builder.Services.AddMetrics();
        builder.WebHost.UseKestrelCore().ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(leaf)));
        var app = builder.Build(); NomadTlsFixture? fixture = null;
        app.Run(async context =>
        {
            var f = fixture!; f.Requests.Add(context.Request.Path + context.Request.QueryString);
            f.LastToken = context.Request.Headers["X-Nomad-Token"];
            if (!string.IsNullOrEmpty(f.LastToken)) f.Tokens++;
            if (f.Respond is not null) { await f.Respond(context, f.Requests.Count); return; }
            await StandardResponse(context);
        });
        await app.StartAsync(); var port = new Uri(app.Urls.Single()).Port;
        fixture = new(app, ca, leaf, key, port); return fixture;
    }
    public static Task StandardResponse(HttpContext context)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers["X-Nomad-KnownLeader"] = "true"; context.Response.Headers["X-Nomad-Index"] = "7";
        return context.Response.WriteAsync(context.Request.Path.StartsWithSegments("/v1/namespace") ? "{\"Name\":\"default\",\"CreateIndex\":1}" : "[]");
    }
    public NomadTrustConfiguration Configuration(Guid installation, Guid tenant, Guid target, Guid scope, string scopeKind = "project", string? pin = null, byte[]? caDer = null)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, installationId = installation,
            targets = new[] { new { tenantId = tenant, targetId = target, scopeKind, scopeId = scope,
                apiOrigin = $"https://127.0.0.1:{Port}", destinationIps = new[] { "127.0.0.1" }, caCertificateDerBase64 = Convert.ToBase64String(caDer ?? _ca.RawData),
                serverSpkiSha256 = pin ?? Convert.ToHexStringLower(SHA256.HashData(_leaf.PublicKey.ExportSubjectPublicKeyInfo())), region = "global",
                namespaceName = "default", namespaceCreateIndex = "1", destinationKind = "personal-loopback" } } });
        return NomadTrustConfiguration.Parse(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), installation);
    }
    public async ValueTask DisposeAsync() { await _app.DisposeAsync(); _leaf.Dispose(); _ca.Dispose(); _key.Dispose(); }
}
