using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kordra.Modules.IdentityAccess.Tests;

/// <summary>Isolated kordra_test-backed HTTPS host. Never connects to the runtime database.</summary>
public sealed class LocalAuthenticationTestHost : IAsyncDisposable
{
    public const int Port = 5128;
    public const string Origin = "https://localhost:5128";
    public const string ValidPassword = "Synthetic-Operator-Passphrase-123!";
    private const string DeniedPassword = "Synthetic-Common-Password-Only-For-Tests";
    private readonly string _connectionString;
    private readonly Guid _installationId = Guid.NewGuid();
    private readonly X509Certificate2 _certificate;
    private readonly LocalPasswordPolicy _passwordPolicy;
    private readonly string _privateFixtureDirectory;
    private readonly HashSet<string> _syntheticUserNames = new(StringComparer.Ordinal);
    private readonly IIdentityAuditTransactionFactory _auditFactory;
    private readonly bool _initializeBoot;
    private readonly Func<HttpContext, RequestDelegate, Task>? _afterAuthentication;
    private readonly ManualTimeProvider _timeProvider;
    private DbCommandInterceptor? _commandInterceptor;
    private WebApplication? _app;
    private bool _schemaReady;
    private bool _disposed;
    private bool _resourceAuthority;
    private Action<IServiceCollection>? _resourceServices;

    private LocalAuthenticationTestHost(
        string connectionString,
        X509Certificate2 certificate,
        LocalPasswordPolicy passwordPolicy,
        string privateFixtureDirectory,
        IIdentityAuditTransactionFactory auditFactory,
        ManualTimeProvider timeProvider,
        bool initializeBoot,
        Func<HttpContext, RequestDelegate, Task>? afterAuthentication)
    {
        _connectionString = connectionString;
        _certificate = certificate;
        _passwordPolicy = passwordPolicy;
        _privateFixtureDirectory = privateFixtureDirectory;
        _auditFactory = auditFactory;
        _timeProvider = timeProvider;
        _initializeBoot = initializeBoot;
        _afterAuthentication = afterAuthentication;
    }

    public LocalAuthenticationService Authentication =>
        (_app ?? throw new InvalidOperationException("Test host is stopped.")).Services.GetRequiredService<LocalAuthenticationService>();

    public ManualTimeProvider Clock => _timeProvider;
    public IServiceProvider Services => (_app ?? throw new InvalidOperationException("Test host is stopped.")).Services;

    public static async Task<LocalAuthenticationTestHost> StartAsync(
        IIdentityAuditTransactionFactory? auditFactory = null,
        bool initializeBoot = true,
        ManualTimeProvider? timeProvider = null,
        Func<HttpContext, RequestDelegate, Task>? afterAuthentication = null,
        DbCommandInterceptor? commandInterceptor = null,
        bool resourceAuthority = false,
        Action<IServiceCollection>? resourceServices = null)
    {
        var connectionString = TestDatabaseConfiguration.TryGetConnectionString()
            ?? throw new InvalidOperationException(
                $"Required integration-test configuration is missing: set {TestDatabaseConfiguration.ConnectionEnvironmentVariable} with the private exact kordra_test locator. No database is substituted.");

        var fixtureDirectory = Path.Combine(Path.GetTempPath(), $"kordra-auth-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDirectory);
        SetPrivateMode(fixtureDirectory, directory: true);
        var denylistPath = Path.Combine(fixtureDirectory, "denylist.json");
        var denylistBytes = JsonSerializer.SerializeToUtf8Bytes(new[] { DeniedPassword });
        await File.WriteAllBytesAsync(denylistPath, denylistBytes);
        SetPrivateMode(denylistPath, directory: false);
        var denylistHash = Convert.ToHexStringLower(SHA256.HashData(denylistBytes));
        var policy = LocalPasswordPolicy.Load(denylistPath, "synthetic-fixture-v1", "IdentityAccess test fixture", denylistHash);

        var host = new LocalAuthenticationTestHost(
            connectionString,
            CreateLocalhostCertificate(),
            policy,
            fixtureDirectory,
            auditFactory ?? new IdentityAccessAuditTransactionAdapter(),
            timeProvider ?? new ManualTimeProvider(DateTimeOffset.UtcNow),
            initializeBoot,
            afterAuthentication);
        host._commandInterceptor = commandInterceptor;
        host._resourceAuthority = resourceAuthority;
        host._resourceServices = resourceServices;
        try
        {
            await host.EnsureSchemaAsync();
            await host.StartInstanceAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public HttpClient CreateClient()
    {
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
                    throw new InvalidOperationException("The test client may connect only to the Kordra loopback listener.");
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

        return new HttpClient(handler) { BaseAddress = new Uri(Origin) };
    }

    public string NewUserName()
    {
        var userName = $"t03-{Guid.NewGuid():N}";
        _syntheticUserNames.Add(userName);
        return userName;
    }

    public async Task<LocalCommandResult> EnrollAsync(string userName, string? password = null)
    {
        _syntheticUserNames.Add(userName);
        return await Authentication.EnrollOfflineAsync(userName, password ?? ValidPassword);
    }

    public async Task<KordraIdentityUser> SeedPrincipalAsync(string userName, string? password = null)
    {
        _syntheticUserNames.Add(userName);
        var principal = new KordraIdentityUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            AccountStatus = "active",
            Revision = 1,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };
        var hasher = new PasswordHasher<KordraIdentityUser>(Microsoft.Extensions.Options.Options.Create(
            new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = 220_000 }));
        principal.PasswordHash = hasher.HashPassword(principal, password ?? ValidPassword);
        await using var db = CreateIdentityContext();
        db.Principals.Add(principal);
        await db.SaveChangesAsync();
        return principal;
    }

    public async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("requestToken").GetString()
            ?? throw new InvalidOperationException("The local auth endpoint returned no request token.");
    }

    public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password)
    {
        var token = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { username = userName, password }), Encoding.UTF8, "application/json")
        };
        AddBrowserHeaders(request, token);
        return await client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> PostEmptyCsrfAsync(
        HttpClient client,
        string route,
        string? idempotencyKey = null)
    {
        var token = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, route);
        AddBrowserHeaders(request, token);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> ChangePasswordAsync(
        HttpClient client,
        string currentPassword,
        string newPassword,
        string idempotencyKey)
    {
        var token = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { currentPassword, newPassword }), Encoding.UTF8, "application/json")
        };
        AddBrowserHeaders(request, token);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    public async Task<KordraIdentityDbContext> CreateIdentityContextAsync()
    {
        var options = new DbContextOptionsBuilder<KordraIdentityDbContext>()
            .UseNpgsql(_connectionString, postgres => postgres
                .MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, KordraIdentityDbContext.SchemaName)
                .CommandTimeout(2))
            .Options;
        var context = new KordraIdentityDbContext(options);
        await context.Database.OpenConnectionAsync();
        return context;
    }

    public async Task<AuditDbContext> CreateAuditContextAsync()
    {
        var context = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(_connectionString));
        await context.Database.OpenConnectionAsync();
        return context;
    }

    public async Task RestartAsync()
    {
        await StopInstanceAsync();
        await StartInstanceAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopInstanceAsync();
        if (_schemaReady && _syntheticUserNames.Count > 0)
        {
            try
            {
                await CleanupSyntheticRowsAsync();
            }
            finally
            {
                _certificate.Dispose();
                Directory.Delete(_privateFixtureDirectory, recursive: true);
            }
        }
        else
        {
            _certificate.Dispose();
            if (Directory.Exists(_privateFixtureDirectory))
            {
                Directory.Delete(_privateFixtureDirectory, recursive: true);
            }
        }
    }

    private async Task EnsureSchemaAsync()
    {
        var identityOptions = new DbContextOptionsBuilder<KordraIdentityDbContext>()
            .UseNpgsql(_connectionString, postgres => postgres
                .MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, KordraIdentityDbContext.SchemaName)
                .CommandTimeout(2))
            .Options;
        await using (var identity = new KordraIdentityDbContext(identityOptions))
        {
            await identity.Database.MigrateAsync();
        }

        await using (var audit = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(_connectionString)))
        {
            await audit.Database.MigrateAsync();
        }

        _schemaReady = true;
        if (_resourceAuthority)
        {
            await using var tenant = new Kordra.Modules.TenantManagement.Infrastructure.TenantDbContext(Kordra.Modules.TenantManagement.Infrastructure.TenantDbContextFactory.CreateTestOptions(_connectionString));
            await tenant.Database.MigrateAsync();
            await using var projects = new Kordra.Modules.ProjectEnvironment.Infrastructure.ProjectDbContext(Kordra.Modules.ProjectEnvironment.Infrastructure.ProjectDbContextFactory.CreateTestOptions(_connectionString));
            await projects.Database.MigrateAsync();
            await using var inventory = new Kordra.Modules.InfrastructureInventory.Infrastructure.InventoryDbContext(Kordra.Modules.InfrastructureInventory.Infrastructure.InventoryDbContextFactory.CreateTestOptions(_connectionString));
            await inventory.Database.MigrateAsync();
            await using var credentials = new Kordra.Modules.ConnectionManagement.Infrastructure.CredentialDbContext(Kordra.Modules.ConnectionManagement.Infrastructure.CredentialDbContextFactory.CreateTestOptions(_connectionString));
            await credentials.Database.MigrateAsync();
        }
    }

    private async Task StartInstanceAsync()
    {
        var configuration = new LocalAuthenticationConfiguration(_installationId, Guid.NewGuid());
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
                listen.Protocols = HttpProtocols.Http1;
                listen.UseHttps(_certificate);
            });
        });
        builder.Services.AddRoutingCore();
        builder.Services.AddDbContextFactory<KordraIdentityDbContext>(options =>
        {
            options.UseNpgsql(_connectionString, postgres => postgres
                .MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, KordraIdentityDbContext.SchemaName)
                .CommandTimeout(2));
            if (_commandInterceptor is not null) options.AddInterceptors(_commandInterceptor);
        });
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(_passwordPolicy);
        builder.Services.AddSingleton<TimeProvider>(_timeProvider);
        builder.Services.AddSingleton<FairAsyncReaderWriterAdmissionGate>();
        builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.IPasswordHasher<KordraIdentityUser>>(_ =>
            new PasswordHasher<KordraIdentityUser>(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                IterationCount = configuration.PasswordHashIterations
            })));
        builder.Services.AddSingleton<IIdentityAuditTransactionFactory>(_auditFactory);
        builder.Services.AddSingleton<LocalAuthenticationService>();
        LocalAuthenticationEndpoints.AddServices(builder.Services);
        if (_resourceAuthority)
        {
            builder.Services.AddSingleton<IDbContextFactory<Kordra.Modules.TenantManagement.Infrastructure.TenantDbContext>>(
                new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<Kordra.Modules.TenantManagement.Infrastructure.TenantDbContext>(Kordra.Modules.TenantManagement.Infrastructure.TenantDbContextFactory.CreateTestOptions(_connectionString)));
            builder.Services.AddSingleton<IDbContextFactory<Kordra.Modules.ProjectEnvironment.Infrastructure.ProjectDbContext>>(
                new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<Kordra.Modules.ProjectEnvironment.Infrastructure.ProjectDbContext>(Kordra.Modules.ProjectEnvironment.Infrastructure.ProjectDbContextFactory.CreateTestOptions(_connectionString)));
            builder.Services.AddSingleton<IDbContextFactory<Kordra.Modules.InfrastructureInventory.Infrastructure.InventoryDbContext>>(
                new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<Kordra.Modules.InfrastructureInventory.Infrastructure.InventoryDbContext>(Kordra.Modules.InfrastructureInventory.Infrastructure.InventoryDbContextFactory.CreateTestOptions(_connectionString)));
            builder.Services.AddSingleton<IDbContextFactory<Kordra.Modules.ConnectionManagement.Infrastructure.CredentialDbContext>>(
                new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<Kordra.Modules.ConnectionManagement.Infrastructure.CredentialDbContext>(Kordra.Modules.ConnectionManagement.Infrastructure.CredentialDbContextFactory.CreateTestOptions(_connectionString)));
            builder.Services.AddResourceAuthorityOwnerServices();
            _resourceServices?.Invoke(builder.Services);
        }

        _app = builder.Build();
        _app.Use(LocalAuthenticationEndpoints.RequestGuard("localhost:5128"));
        _app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/inventory", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Path.StartsWithSegments("/api/inventory", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });
        _app.UseAuthentication();
        if (_afterAuthentication is not null)
        {
            _app.Use(_afterAuthentication);
        }

        LocalAuthenticationEndpoints.Map(_app);
        if (_resourceAuthority) { ProtectedMetadataEndpoints.Map(_app); EnvironmentV2Endpoints.Map(_app); }
        _app.MapGet("/health/live", () => Results.Text("alive\n", "text/plain; charset=utf-8"));
        if (_initializeBoot || _auditFactory is IdentityAccessAuditTransactionAdapter)
        {
            await Authentication.InitializeBootAsync();
        }

        await _app.StartAsync();
    }

    private async Task StopInstanceAsync()
    {
        if (_app is null)
        {
            return;
        }

        var app = _app;
        _app = null;
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private KordraIdentityDbContext CreateIdentityContext()
    {
        var options = new DbContextOptionsBuilder<KordraIdentityDbContext>()
            .UseNpgsql(_connectionString, postgres => postgres
                .MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, KordraIdentityDbContext.SchemaName)
                .CommandTimeout(2))
            .Options;
        return new KordraIdentityDbContext(options);
    }

    private async Task CleanupSyntheticRowsAsync()
    {
        var names = _syntheticUserNames.ToArray();
        await using var db = CreateIdentityContext();
        var principalIds = await db.Principals.AsNoTracking()
            .Where(principal => names.Contains(principal.UserName!))
            .Select(principal => principal.Id)
            .ToArrayAsync();
        if (principalIds.Length == 0)
        {
            return;
        }

        await db.AuthenticationCommands.Where(command => principalIds.Contains(command.PrincipalId)).ExecuteDeleteAsync();
        await db.Sessions.Where(session => principalIds.Contains(session.PrincipalId)).ExecuteDeleteAsync();
        await db.Principals.Where(principal => principalIds.Contains(principal.Id)).ExecuteDeleteAsync();
    }

    private static void AddBrowserHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.TryAddWithoutValidation("Origin", Origin);
        request.Headers.TryAddWithoutValidation("X-Kordra-CSRF", token);
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

    private static void SetPrivateMode(string path, bool directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, directory
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private readonly object _sync = new();
        private DateTimeOffset _utcNow = initialUtc.ToUniversalTime();
        private long _timestamp;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }

        public override long GetTimestamp()
        {
            lock (_sync)
            {
                return _timestamp;
            }
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan by)
        {
            if (by < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(by));
            }

            lock (_sync)
            {
                _utcNow += by;
                _timestamp += by.Ticks;
            }
        }

        public void StepUtcWithoutAdvancingMonotonicTime(TimeSpan by)
        {
            lock (_sync) _utcNow += by;
        }
    }
}
