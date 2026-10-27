using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.ArtifactManagement.Infrastructure.Registry;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Commands;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.ConnectionManagement.Keychain;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.IdentityAccess.Tests;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kordra.Artifacts.Tests;

/// <summary>Actual loopback HTTPS/cookie/CSRF, current IAM/TEN/PRJ owners, ART/AUD commits and synthetic TLS registry.</summary>
public sealed class ArtifactHttpTests : IAsyncLifetime
{
    private LocalAuthenticationTestHost _host = null!;
    private HttpClient _client = null!;
    private RegistryTransportTests.Fixture _registry = null!;
    private RegistryTrustConfiguration? _configuration;
    private KeychainAccessConfiguration? _access;
    private readonly Guid _project = Guid.NewGuid(), _registryId = Guid.NewGuid();
    private Guid _tenant, _principal, _assignment, _membership;
    private string _user = "", _digest = "";
    private const string Key = "synthetic-http-registration-0001";
    private readonly Lease _lease = new();
    private string Root => $"/api/v1/projects/{_project:D}";
    private IDbContextFactory<T> Factory<T>() where T : DbContext => _host.Services.GetRequiredService<IDbContextFactory<T>>();

    public async Task InitializeAsync()
    {
        var locator = TestDatabaseConfiguration.TryGetConnectionString() ?? throw new InvalidOperationException("Private isolated test locator required.");
        var art = new PooledDbContextFactory<ArtifactDbContext>(ArtifactDbContextFactory.CreateTestOptions(locator));
        await using (var db = await art.CreateDbContextAsync()) await db.Database.MigrateAsync();
        _host = await LocalAuthenticationTestHost.StartAsync(resourceAuthority: true, resourceServices: services =>
        {
            services.AddSingleton<IDbContextFactory<ArtifactDbContext>>(art);
            services.AddArtifactRuntimeOwnerServices(_configuration);
            if (_access is not null) { services.AddSingleton(_access); services.AddSingleton<CredentialBroker>(); }
        });
        _user = _host.NewUserName(); var enrolled = await _host.EnrollAsync(_user);
        Assert.Equal(LocalAuthenticationStatus.Success, enrolled.Status); _principal = enrolled.PrincipalId!.Value;
        var roles = (RoleDefinitionStore)_host.Services.GetRequiredService<IRoleDefinitionReader>();
        var founder = await roles.PublishOfflineAsync(_lease, Guid.NewGuid(), Guid.Empty, 0, "Synthetic ART founder",
            [new("org.manage", "organization"), new("membership.assign-role", "organization")]);
        Assert.Equal("success", founder.Status);
        var tenants = _host.Services.GetRequiredService<TenantMembershipService>();
        var created = await tenants.InitializeOfflineAsync(_lease, Guid.NewGuid(), _principal, "Synthetic ART tenant", founder.DefinitionId, 1);
        Assert.Equal("success", created.Status); _tenant = created.TenantId;
        await using (var prj = await Factory<ProjectDbContext>().CreateDbContextAsync())
        { prj.Projects.Add(new() { Id = _project, TenantId = _tenant, Name = "Synthetic ART HTTP project" }); await prj.SaveChangesAsync(); }
        Guid member;
        await using (var ten = await Factory<TenantDbContext>().CreateDbContextAsync())
            member = await ten.Memberships.Where(x => x.TenantId == _tenant && x.PrincipalId == _principal).Select(x => x.Id).SingleAsync();
        _membership = member;
        var role = await roles.PublishOfflineAsync(_lease, Guid.NewGuid(), Guid.Empty, 0, "Synthetic ART project",
            [new("artifact.read", "project"), new("artifact.register", "project")]);
        Assert.Equal("success", role.Status);
        var grant = await tenants.GrantOfflineAsync(_lease, Guid.NewGuid(), _tenant, member, 1, role.DefinitionId, 1, new("project", _project), null);
        Assert.Equal("success", grant.Status); _assignment = grant.ResourceId;
        _registry = new();
        var config = "{\"os\":\"linux\",\"architecture\":\"amd64\"}"u8.ToArray();
        var image = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 2, mediaType = "application/vnd.oci.image.manifest.v1+json",
            config = new { mediaType = "application/vnd.oci.image.config.v1+json", digest = Hash(config), size = config.Length }, layers = Array.Empty<object>() });
        _digest = Hash(image);
        _registry.Handler = request =>
        {
            var blob = request.StartsWith("GET /v2/team/frontend/blobs/", StringComparison.Ordinal);
            var bytes = blob ? config : image;
            return "HTTP/1.1 200 OK\r\nContent-Type: " + (blob ? "application/octet-stream" : "application/vnd.oci.image.manifest.v1+json") +
                "\r\nContent-Length: " + bytes.Length + "\r\n\r\n" + Encoding.UTF8.GetString(bytes);
        };
        var installation = _host.Services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;
        _configuration = new(installation, new string('a', 64), [_registry.Trust("anonymous", installation: installation, tenant: _tenant, project: _project, registry: _registryId)]);
        await _host.RestartAsync(); // Immutable configuration at boot, never an HTTP mutation/reload seam.
        _client = _host.CreateClient();
        using var login = await _host.LoginAsync(_client, _user, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
    public async Task DisposeAsync()
    { _client?.Dispose(); if (_host is not null) await _host.DisposeAsync(); if (_registry is not null) await _registry.DisposeAsync(); }
    private static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    private string Body(Guid? correlation = null) => JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = correlation ?? Guid.NewGuid(), registryId = _registryId,
        repository = "team/frontend", digest = _digest, platform = new { os = "linux", architecture = "amd64", variant = (string?)null }, sourceClaim = (object?)null });
    private async Task<HttpResponseMessage> Post(string? body = null, bool csrf = true, string key = Key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/artifacts");
        request.Headers.Add("Origin", LocalAuthenticationTestHost.Origin); request.Headers.Add("Idempotency-Key", key);
        if (csrf) request.Headers.Add("X-Kordra-CSRF", await _host.GetCsrfTokenAsync(_client));
        request.Content = new StringContent(body ?? Body(), Encoding.UTF8, "application/json");
        return await _client.SendAsync(request);
    }
    private static async Task<JsonDocument> Json(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Real_login_grants_CSRF_UCMD_catalog_receipt_and_historical_replay()
    {
        var correlation = Guid.NewGuid();
        using var created = await Post(Body(correlation)); using var first = await Json(created, HttpStatusCode.Created);
        AssertContract(first.RootElement, "RegistrationEnvelope");
        Assert.Equal(correlation, first.RootElement.GetProperty("correlationId").GetGuid());
        var registration = first.RootElement.GetProperty("registration");
        AssertContract(registration, "Registration");
        AssertContract(registration.GetProperty("artifact"), "Artifact");
        AssertContract(registration.GetProperty("artifact").GetProperty("platform"), "Platform");
        Assert.Equal("Registered", registration.GetProperty("status").GetString());
        var artifact = registration.GetProperty("artifact").GetProperty("artifactId").GetGuid();
        var receipt = registration.GetProperty("registrationId").GetGuid(); Assert.Equal(2, _registry.Requests.Count);
        using var detail = await _client.GetAsync(Root + $"/artifacts/{artifact:D}"); using var detailJson = await Json(detail, HttpStatusCode.OK);
        Assert.Equal(artifact, detailJson.RootElement.GetProperty("artifact").GetProperty("artifactId").GetGuid());
        using var list = await _client.GetAsync(Root + "/artifacts?limit=1"); using var listJson = await Json(list, HttpStatusCode.OK);
        AssertContract(listJson.RootElement, "ArtifactListEnvelope");
        AssertContract(listJson.RootElement.GetProperty("items")[0], "ArtifactSummary");
        Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, listJson.RootElement.GetProperty("nextAfterArtifactId").ValueKind);
        using var status = await _client.GetAsync(Root + $"/artifact-registrations/{receipt:D}"); using var receiptJson = await Json(status, HttpStatusCode.OK);
        Assert.Equal(receipt, receiptJson.RootElement.GetProperty("registration").GetProperty("registrationId").GetGuid());
        _configuration = null; _client.Dispose(); await _host.RestartAsync(); _client = _host.CreateClient();
        using var login = await _host.LoginAsync(_client, _user, LocalAuthenticationTestHost.ValidPassword); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var replay = await Post(); using var replayJson = await Json(replay, HttpStatusCode.OK);
        Assert.Equal(receipt, replayJson.RootElement.GetProperty("registration").GetProperty("registrationId").GetGuid());
        using var historical = await _client.GetAsync(Root + $"/artifacts/{artifact:D}"); Assert.Equal(HttpStatusCode.OK, historical.StatusCode);
        using var selectors = await _client.GetAsync(Root + "/artifact-registries"); using var selectorsJson = await Json(selectors, HttpStatusCode.ServiceUnavailable);
        using var disabled = await Post(key: "synthetic-new-key-after-disable"); using var disabledJson = await Json(disabled, HttpStatusCode.ServiceUnavailable);
        Assert.Equal(2, _registry.Requests.Count);
    }

    [Theory]
    [InlineData("csrf", HttpStatusCode.Forbidden)]
    [InlineData("duplicate", HttpStatusCode.BadRequest)]
    [InlineData("tenant", HttpStatusCode.BadRequest)]
    [InlineData("large", HttpStatusCode.RequestEntityTooLarge)]
    [InlineData("key", HttpStatusCode.BadRequest)]
    public async Task Invalid_HTTP_never_reaches_registry(string failure, HttpStatusCode expected)
    {
        var body = Body();
        body = failure switch { "duplicate" => body[..^1] + ",\"schemaVersion\":1}", "tenant" => body[..^1] + ",\"tenantId\":\"anything\"}",
            "large" => new string(' ', ArtifactRequestParser.MaximumBodyBytes + 1), _ => body };
        using var result = await Post(body, failure != "csrf", failure == "key" ? "invalid~key~000000" : Key);
        using var json = await Json(result, expected);
        AssertContract(json.RootElement, "ErrorEnvelope");
        Assert.Equal(4, json.RootElement.EnumerateObject().Count()); Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("registration").ValueKind);
        Assert.Empty(_registry.Requests);
    }

    [Fact]
    public async Task Session_and_current_project_grant_are_required_at_HTTP_boundary()
    {
        using var anonymous = _host.CreateClient();
        using var denied = await anonymous.GetAsync(Root + "/artifacts"); using var deniedJson = await Json(denied, HttpStatusCode.Unauthorized);
        await using (var held = await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireWriteAsync())
        await using (var ten = await Factory<TenantDbContext>().CreateDbContextAsync())
        { var assignment = await ten.Assignments.SingleAsync(x => x.Id == _assignment); assignment.Status = "revoked"; assignment.Revision++; await ten.SaveChangesAsync(); }
        using var revoked = await Post(); using var revokedJson = await Json(revoked, HttpStatusCode.Forbidden);
        Assert.Empty(_registry.Requests);
    }

    [Theory]
    [InlineData("tenant", HttpStatusCode.Forbidden)]
    [InlineData("membership", HttpStatusCode.Forbidden)]
    [InlineData("expired", HttpStatusCode.Forbidden)]
    [InlineData("project", HttpStatusCode.Forbidden)]
    [InlineData("principal", HttpStatusCode.Unauthorized)]
    public async Task Current_owner_state_is_checked_before_any_registry_use(string invalid, HttpStatusCode expected)
    {
        await using (var held = await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireWriteAsync())
        {
            if (invalid is "tenant" or "membership" or "expired")
            {
                await using var db = await Factory<TenantDbContext>().CreateDbContextAsync();
                if (invalid == "tenant") { var row = await db.Organizations.SingleAsync(x => x.Id == _tenant); row.Status = "suspended"; row.Revision++; }
                if (invalid == "membership") { var row = await db.Memberships.SingleAsync(x => x.Id == _membership); row.Status = "revoked"; row.Revision++; }
                if (invalid == "expired") { var row = await db.Assignments.SingleAsync(x => x.Id == _assignment); row.ExpiresAtUtc = _host.Clock.GetUtcNow(); row.Revision++; }
                await db.SaveChangesAsync();
            }
            if (invalid == "project")
            { await using var db = await Factory<ProjectDbContext>().CreateDbContextAsync(); var row = await db.Projects.SingleAsync(x => x.Id == _project); row.Status = "suspended"; row.Revision++; await db.SaveChangesAsync(); }
            if (invalid == "principal")
            { await using var db = await Factory<KordraIdentityDbContext>().CreateDbContextAsync(); var row = await db.Principals.SingleAsync(x => x.Id == _principal); row.AccountStatus = "disabled"; row.Revision++; await db.SaveChangesAsync(); }
        }
        using var response = await Post(); using var body = await Json(response, expected);
        Assert.Empty(_registry.Requests);
    }

    [Fact]
    public async Task Selectors_are_minimal_and_foreign_records_queries_do_not_disclose()
    {
        using var selectors = await _client.GetAsync(Root + "/artifact-registries"); using var json = await Json(selectors, HttpStatusCode.OK);
        AssertContract(json.RootElement, "RegistryListEnvelope");
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        AssertContract(item, "RegistrySelector");
        Assert.Equal(new[] { "registryId", "displayName", "repository", "authRequired" }, item.EnumerateObject().Select(p => p.Name));
        using var unknown = await _client.GetAsync(Root + $"/artifacts/{Guid.NewGuid():D}"); using var missing = await Json(unknown, HttpStatusCode.NotFound);
        using var foreign = await _client.GetAsync($"/api/v1/projects/{Guid.NewGuid():D}/artifacts"); using var forbidden = await Json(foreign, HttpStatusCode.Forbidden);
        using var query = await _client.GetAsync(Root + "/artifacts?tenantId=foreign"); using var invalid = await Json(query, HttpStatusCode.BadRequest);
        Assert.Empty(_registry.Requests);
    }
    private sealed class Lease : IOfflineProvisioningLease { public void RequireHeld() { } }

    private static void AssertContract(JsonElement body, string schema)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kordra.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root!.FullName, "docs/contracts/fixtures/artifact-registration-v1.http.json")));
        using var api = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, "docs/contracts/openapi/artifact-registration-v1.openapi.json")));
        var actual = body.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
        var fixtureFields = fixture.RootElement.GetProperty("schemaFields").GetProperty(schema).EnumerateArray().Select(x => x.GetString()).Order(StringComparer.Ordinal).ToArray();
        var schemaObject = api.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema);
        var required = schemaObject.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(fixtureFields, actual); Assert.Equal(required, actual);
        Assert.False(schemaObject.GetProperty("additionalProperties").GetBoolean());
        foreach (var source in api.RootElement.GetProperty("x-working-tree-source-sha256").EnumerateObject())
            Assert.Equal(source.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root.FullName, source.Name)))));
    }

    [MacArtifactFact]
    public async Task Native_helper_authenticated_registration_after_C_uses_only_new_synthetic_item()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var installation = _host.Services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;
        var reference = Guid.NewGuid(); var locator = Guid.NewGuid();
        var executable = Path.Combine(AppContext.BaseDirectory, "Kordra.Api");
        var secret = Encoding.UTF8.GetBytes("synthetic:" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24)));
        using var keychain = new ArtifactKeychainFixture(installation, reference, executable, secret);
        try
        {
            _access = new(installation, executable, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable))), new Dictionary<Guid, string> { [locator] = keychain.Path });
            _configuration = new(installation, new string('b', 64), [_registry.Trust("basic", installation: installation, tenant: _tenant, project: _project, registry: _registryId)]);
            var manifest = new RegistryCredentialManifest(1, Guid.NewGuid(), installation, _tenant, _project, _registryId, "team/frontend",
                "artifact.registry.read.v1", "artifact.register", "create", null, 0, Guid.NewGuid(), Guid.NewGuid(), reference, locator,
                _host.Clock.GetUtcNow().AddMinutes(-1), _host.Clock.GetUtcNow().AddHours(1));
            await using (var db = await Factory<CredentialDbContext>().CreateDbContextAsync())
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                Assert.NotNull(await new RegistryCredentialBindingStore(db).StageAsync(_lease, manifest, _host.Clock.GetUtcNow()));
                await db.SaveChangesAsync(); await transaction.CommitAsync();
            }
            _client.Dispose(); await _host.RestartAsync(); _client = _host.CreateClient();
            using var login = await _host.LoginAsync(_client, _user, LocalAuthenticationTestHost.ValidPassword); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var handler = _registry.Handler;
            _registry.AsyncHandler = async request =>
            {
                await using var aud = await _host.CreateAuditContextAsync();
                Assert.Equal(1, await aud.Entries.CountAsync(e => e.Permission == "artifact.register" && e.SubjectRef == _principal &&
                    e.ScopeId == _project && e.SafeReason.StartsWith("artifact-c-")));
                return handler!(request);
            };
            using var response = await Post(); using var json = await Json(response, HttpStatusCode.Created);
            Assert.Equal(2, _registry.Requests.Count);
            var header = "Authorization: Basic " + Convert.ToBase64String(secret);
            Assert.All(_registry.Requests, request => Assert.True(request.Contains(header, StringComparison.OrdinalIgnoreCase), "Expected fixture-only authorization header."));
            var payload = json.RootElement.GetRawText();
            Assert.True(!payload.Contains(Convert.ToBase64String(secret), StringComparison.Ordinal) && !payload.Contains(Encoding.UTF8.GetString(secret), StringComparison.Ordinal));
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
}

public sealed class MacArtifactFactAttribute : FactAttribute
{
    public MacArtifactFactAttribute() { if (!OperatingSystem.IsMacOS()) Skip = "Synthetic native Keychain fixture requires macOS."; }
}
