using System.Net;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Kordra.Api;

/// <summary>ADR-0010 process bootstrap. Loopback connectivity grants no inventory authority.</summary>
public static class LocalApiHost
{
    public const int DefaultPort = 5127;

    /// <summary>
    /// Creates an unstarted host. The caller owns start/stop/disposal; zero requests
    /// an ephemeral port. No CLI, environment, appsettings or provider configuration is loaded.
    /// </summary>
    public static WebApplication Create(int port = DefaultPort)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(LocalApiHost).Assembly.GetName().Name,
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.Host.UseConsoleLifetime(options => options.SuppressStatusMessages = true);
        builder.WebHost.UseKestrelCore().ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, port, listener => listener.Protocols = HttpProtocols.Http1);
        });
        builder.Services.AddMetrics();
        builder.Services.AddRoutingCore();
        builder.Services.AddSingleton<IInventoryReader, UnavailableInventoryReader>();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            // Deliberately independent of DI readers, caller claims and request content.
            if (context.Request.Path.StartsWithSegments("/inventory", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Path.StartsWithSegments("/api/inventory", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!HttpMethods.IsGet(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                context.Response.Headers.Allow = "GET";
                return;
            }

            await next(context);
        });
        app.MapGet("/health/live", (RequestDelegate)(context =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync("alive\n", context.RequestAborted);
        }));
        app.MapFallback((RequestDelegate)(context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }));
        return app;
    }

    /// <summary>
    /// Creates the explicitly selected HTTPS-only local-pilot profile. The installed
    /// options already hold the canonical installation lock before this host is built.
    /// </summary>
    public static WebApplication CreateLocalPilot(LocalPilotHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.RequireRuntimeCustody();
        if (!options.Certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("The local-pilot TLS certificate must include its private key.");
        }

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(LocalApiHost).Assembly.GetName().Name,
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.Host.UseConsoleLifetime(hostOptions => hostOptions.SuppressStatusMessages = true);
        builder.WebHost.UseKestrelCore().ConfigureKestrel(server =>
        {
            server.AddServerHeader = false;
            server.Listen(IPAddress.Loopback, 5128, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
                listen.UseHttps(options.Certificate);
            });
        });

        builder.Services.AddMetrics();
        builder.Services.AddRoutingCore();
        builder.Services.AddSingleton<IInventoryReader, UnavailableInventoryReader>();
        builder.Services.AddSingleton<IIdentityAuditTransactionFactory, IdentityAccessAuditTransactionAdapter>();
        builder.Services.AddLocalAuthenticationServices(
            options.RuntimeConnectionString,
            options.Authentication,
            options.PasswordPolicy);
        LocalAuthenticationEndpoints.AddServices(builder.Services);
        builder.Services.AddLocalResourceAuthority(options.RuntimeConnectionString);
        if (options.NomadTrust is not null || options.KubernetesTrust is not null ||
            options.ArtifactRegistries?.Registries.Any(r => r.AuthMode != "anonymous") == true)
        {
            if (options.CredentialAccess is null) throw new InvalidOperationException("Provider requires explicit credential access.");
            if (options.NomadTrust is not null) builder.Services.AddSingleton(new NomadObservationRegistry(options.NomadTrust));
            if (options.KubernetesTrust is not null) builder.Services.AddSingleton(new KubernetesObservationRegistry(options.KubernetesTrust));
            builder.Services.AddSingleton(options.CredentialAccess);
            builder.Services.AddSingleton<Kordra.Modules.ConnectionManagement.CredentialBroker>();
        }
        if (options.ArtifactConnectionString is not null)
            builder.Services.AddLocalArtifacts(options.ArtifactConnectionString, options.ArtifactRegistries);

        var app = builder.Build();
        app.Use(LocalAuthenticationEndpoints.RequestGuard("localhost:5128"));
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/inventory", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Path.StartsWithSegments("/api/inventory", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });
        app.UseAuthentication();
        app.MapGet("/health/live", (RequestDelegate)(context =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync("alive\n", context.RequestAborted);
        }));
        LocalAuthenticationEndpoints.Map(app);
        ProtectedMetadataEndpoints.Map(app);
        EnvironmentV2Endpoints.Map(app);
        if (options.NomadTrust is not null) NomadWorkloadEndpoints.Map(app);
        if (options.KubernetesTrust is not null) KubernetesWorkloadEndpoints.Map(app);
        if (options.ArtifactConnectionString is not null) ArtifactEndpoints.Map(app);
        app.MapFallback((RequestDelegate)(context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }));
        return app;
    }
}
