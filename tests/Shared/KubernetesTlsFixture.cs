using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Kordra.Modules.InfrastructureInventory.Infrastructure.Kubernetes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kordra.Tests.Shared;

internal sealed class KubernetesTlsFixture : IAsyncDisposable
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
    private KubernetesTlsFixture(WebApplication app, X509Certificate2 ca, X509Certificate2 leaf, RSA key, int port)
    { _app = app; _ca = ca; _leaf = leaf; _key = key; Port = port; }
    public static async Task<KubernetesTlsFixture> Start(bool wrongName = false)
    {
        var key = RSA.Create(2048); var request = new CertificateRequest("CN=Synthetic Kubernetes CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var signedCa = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        var ca = X509CertificateLoader.LoadCertificate(signedCa.RawData);
        using var leafKey = RSA.Create(2048); var leafRequest = new CertificateRequest("CN=ignored", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); if(wrongName) san.AddDnsName("wrong.invalid"); else san.AddIpAddress(IPAddress.Loopback); leafRequest.CertificateExtensions.Add(san.Build());
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var issued = leafRequest.Create(signedCa, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
        using var withKey = issued.CopyWithPrivateKey(leafKey); var leaf = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pkcs12), null);
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(KubernetesTlsFixture).Assembly.GetName().Name });
        builder.Logging.ClearProviders(); builder.Services.AddRoutingCore(); builder.Services.AddMetrics();
        builder.WebHost.UseKestrelCore().ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(leaf)));
        var app = builder.Build(); KubernetesTlsFixture? fixture = null;
        app.Run(async context =>
        {
            var f = fixture!; f.Requests.Add(context.Request.Path + context.Request.QueryString);
            f.LastToken = context.Request.Headers.Authorization;
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
        var resource = context.Request.Path.Value!.Split('/').Last();
        var kind = resource switch { "pods" => "Pod", "deployments" => "Deployment", "statefulsets" => "StatefulSet", "daemonsets" => "DaemonSet", _ => "Namespace" };
        return context.Response.WriteAsJsonAsync(kind == "Namespace" ?
            (object)new { apiVersion = "v1", kind, metadata = new { name = "default", uid = "synthetic-namespace-uid" }, status = new { phase = "Active" } } :
            new { apiVersion = kind == "Pod" ? "v1" : "apps/v1", kind = kind + "List", metadata = new { resourceVersion = "opaque-rv" }, items = Array.Empty<object>() });
    }
    public KubernetesTrustConfiguration Configuration(Guid installation, Guid tenant, Guid target, Guid scope, string scopeKind = "project", string? pin = null, byte[]? caDer = null, string[]? destinationIps = null, string? host = null)
    {
        var trust = new KubernetesTargetTrustBuilder(installation, tenant, target, scopeKind, scope,
            new Uri($"https://{host ?? "127.0.0.1"}:{Port}"), destinationIps ?? ["127.0.0.1"], caDer ?? _ca.RawData,
            pin ?? Convert.ToHexStringLower(SHA256.HashData(_leaf.PublicKey.ExportSubjectPublicKeyInfo()))).Build();
        var constructor = typeof(KubernetesTrustConfiguration).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        return (KubernetesTrustConfiguration)constructor.Invoke([installation, new string('a',64), new[] { trust }]);
    }
    private sealed record KubernetesTargetTrustBuilder(Guid Installation, Guid Tenant, Guid Target, string ScopeKind, Guid Scope, Uri Origin, string[] Ips, byte[] Ca, string Pin)
    {
        public KubernetesTargetTrust Build() => (KubernetesTargetTrust)typeof(KubernetesTargetTrust)
            .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single()
            .Invoke([Installation,Tenant,Target,ScopeKind,Scope,Origin,Ips,Ca,Pin,"default","synthetic-namespace-uid"]);
    }
    public async ValueTask DisposeAsync() { await _app.DisposeAsync(); _leaf.Dispose(); _ca.Dispose(); _key.Dispose(); }
}
