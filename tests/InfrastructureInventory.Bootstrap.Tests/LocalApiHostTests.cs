using System.Net;
using System.Net.Sockets;
using Kordra.Api;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

// Synthetic process-wide binding settings are restored before other collections run.
[CollectionDefinition(LocalHostEnvironmentCollection.Name, DisableParallelization = true)]
public sealed class LocalHostEnvironmentCollection
{
    public const string Name = "Local host synthetic environment";
}

[Collection(LocalHostEnvironmentCollection.Name)]
public sealed class LocalApiHostTests
{
    [Fact]
    public async Task Liveness_UsesRealIpv4Loopback_AndListenerIsReleased()
    {
        Uri address;
        await using (var host = await RunningHost.StartAsync())
        {
            address = host.Address;
            Assert.Equal("127.0.0.1", address.Host);
            using var response = await host.Client.GetAsync("/health/live?tenantId=forged");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("alive\n", await response.Content.ReadAsStringAsync());
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.False(response.Headers.Contains("Server"));
            await AssertCannotConnectAsync(IPAddress.Parse("127.0.0.2"), address.Port, allowTimeout: true);
            await AssertCannotConnectAsync(IPAddress.IPv6Loopback, address.Port, allowTimeout: true);
        }

        await AssertCannotConnectAsync(IPAddress.Loopback, address.Port);
        // Rebinding the same port proves ownership was relinquished, not just response shutdown.
        var listener = new TcpListener(IPAddress.Loopback, address.Port);
        try { listener.Start(); }
        finally { listener.Stop(); }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void InvalidPorts_AreRejectedBeforeHostCreation(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalApiHost.Create(port));
    }

    [Fact]
    public async Task Inventory_MissingAndForgedAuthority_IsUniformlyDenied()
    {
        await using var host = await RunningHost.StartAsync();
        string[] paths =
        [
            "/inventory", "/inventory/resources", "/api/inventory", "/api/inventory/targets",
            "/INVENTORY/resources", "/inventory?tenantId=other-tenant",
            "/inventory?scope=platform&role=SuperAdmin", "/inventory?permission=target.read",
            "/inventory?approval=expired&expiresAt=2000-01-01T00:00:00Z"
        ];
        foreach (var path in paths)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer synthetic-invalid-token");
            request.Headers.TryAddWithoutValidation("X-Tenant-Id", "other-tenant");
            request.Headers.TryAddWithoutValidation("X-Scope", "platform");
            request.Headers.TryAddWithoutValidation("X-Role", "SuperAdmin");
            request.Headers.TryAddWithoutValidation("X-Approval-Expires", "2000-01-01");
            using var response = await host.Client.SendAsync(request);
            await AssertEmptyStatusAsync(response, HttpStatusCode.Forbidden);
        }

        using var missing = await host.Client.GetAsync("/inventory");
        await AssertEmptyStatusAsync(missing, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RepeatedAndParallelRequests_CannotChangeDenialOrLiveness()
    {
        await using var host = await RunningHost.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, 24).Select(async index =>
        {
            var inventory = index % 2 == 0;
            using var response = await host.Client.GetAsync(inventory
                ? "/inventory?tenantId=forged&approval=expired" : "/health/live");
            Assert.Equal(inventory ? HttpStatusCode.Forbidden : HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(inventory ? "" : "alive\n", await response.Content.ReadAsStringAsync());
        }));
        for (var i = 0; i < 3; i++)
        {
            using var response = await host.Client.GetAsync("/inventory");
            await AssertEmptyStatusAsync(response, HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task MutationsUnsupportedMethodsAndUnknownRoutes_AreRejected()
    {
        await using var host = await RunningHost.StartAsync();
        foreach (var method in new[] { "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD", "TRACE", "CUSTOM" })
        {
            foreach (var path in new[] { "/inventory", "/api/inventory/targets", "/health/live", "/unknown" })
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (method is "POST" or "PUT" or "PATCH")
                {
                    request.Content = new StringContent("{\"tenantId\":\"forged\",\"role\":\"SuperAdmin\",\"approval\":\"expired\"}");
                }
                using var response = await host.Client.SendAsync(request);
                var inventory = path.Contains("inventory", StringComparison.Ordinal);
                await AssertEmptyStatusAsync(response, inventory ? HttpStatusCode.Forbidden : HttpStatusCode.MethodNotAllowed);
                if (!inventory)
                {
                    Assert.Equal(new[] { "GET" }, response.Content.Headers.Allow);
                }
            }
        }

        foreach (var path in new[] { "/", "/unknown", "/unknown.json", "/health", "/inventory-other" })
        {
            using var response = await host.Client.GetAsync(path);
            await AssertEmptyStatusAsync(response, HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task HostileSyntheticEnvironment_CannotRebindOrEnableProvider()
    {
        var synthetic = new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = "http://0.0.0.0:19",
            ["DOTNET_URLS"] = "http://[::]:19",
            ["ASPNETCORE_HTTP_PORTS"] = "19",
            ["ASPNETCORE_HTTPS_PORTS"] = "20",
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = "Synthetic.DoesNotExist",
            ["Kestrel__Endpoints__Public__Url"] = "http://0.0.0.0:19",
            ["Inventory__UseFakeProvider"] = "true"
        };
        var saved = synthetic.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (key, value) in synthetic) Environment.SetEnvironmentVariable(key, value);
            await using var host = await RunningHost.StartAsync();
            Assert.Equal(Environments.Production, host.App.Environment.EnvironmentName);
            Assert.Equal("127.0.0.1", host.Address.Host);
            Assert.NotEqual(19, host.Address.Port);
            var configuration = host.App.Configuration;
            Assert.Null(configuration["Inventory:UseFakeProvider"]);
            Assert.Null(configuration["Kestrel:Endpoints:Public:Url"]);
            var root = Assert.IsAssignableFrom<IConfigurationRoot>(configuration);
            Assert.DoesNotContain(root.Providers, provider =>
                provider.GetType().Name.Contains("EnvironmentVariables", StringComparison.Ordinal) ||
                provider.GetType().Name.Contains("Json", StringComparison.Ordinal) ||
                provider.GetType().Name.Contains("CommandLine", StringComparison.Ordinal));

            var readers = host.App.Services.GetServices<IInventoryReader>().ToArray();
            var reader = Assert.IsType<UnavailableInventoryReader>(Assert.Single(readers));
            var result = await reader.ReadAsync();
            Assert.False(result.IsAvailable);
            Assert.Empty(result.Resources);
            using var response = await host.Client.GetAsync("/inventory?useFakeProvider=true&urls=http://0.0.0.0:19");
            await AssertEmptyStatusAsync(response, HttpStatusCode.Forbidden);
        }
        finally
        {
            foreach (var (key, value) in saved) Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Fact]
    public async Task BindingConfigurationAddedAfterCreation_CannotOverrideExplicitListener()
    {
        await using var host = await RunningHost.StartAsync(app =>
        {
            app.Configuration["urls"] = "http://0.0.0.0:19";
            app.Configuration["Kestrel:Endpoints:Public:Url"] = "http://[::]:19";
            app.Configuration["Inventory:UseFakeProvider"] = "true";
        });
        Assert.Equal("127.0.0.1", host.Address.Host);
        using var live = await host.Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var inventory = await host.Client.GetAsync("/inventory");
        await AssertEmptyStatusAsync(inventory, HttpStatusCode.Forbidden);
        Assert.IsType<UnavailableInventoryReader>(host.App.Services.GetRequiredService<IInventoryReader>());
    }

    private static async Task AssertEmptyStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertCannotConnectAsync(IPAddress address, int port, bool allowTimeout = false)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        if (!allowTimeout)
        {
            // The listener we stopped must actively refuse, not merely time out.
            await Assert.ThrowsAnyAsync<SocketException>(async () =>
                await client.ConnectAsync(address, port, deadline.Token));
            return;
        }

        // Non-primary loopback aliases can be unrouted on macOS. A bounded lack
        // of connection is valid there; successful connection must still fail.
        try { await client.ConnectAsync(address, port, deadline.Token); }
        catch (SocketException) { return; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { return; }
        Assert.Fail("A non-listening loopback address unexpectedly accepted a connection.");
    }

    private sealed class RunningHost(WebApplication app, Uri address, HttpClient client) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;
        public Uri Address { get; } = address;
        public HttpClient Client { get; } = client;

        public static async Task<RunningHost> StartAsync(Action<WebApplication>? configure = null)
        {
            var app = LocalApiHost.Create(0);
            try
            {
                configure?.Invoke(app);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await app.StartAsync(deadline.Token);
                var addresses = app.Services.GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>();
                Assert.NotNull(addresses);
                var uri = new Uri(Assert.Single(addresses.Addresses));
                Assert.Equal("http", uri.Scheme);
                Assert.Equal("127.0.0.1", uri.Host);
                var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
                {
                    BaseAddress = uri,
                    Timeout = TimeSpan.FromSeconds(5)
                };
                return new RunningHost(app, uri, client);
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await App.StopAsync(deadline.Token);
            }
            finally { await App.DisposeAsync(); }
        }
    }
}
