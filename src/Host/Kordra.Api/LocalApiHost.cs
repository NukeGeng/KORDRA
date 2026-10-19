using System.Net;
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
}
