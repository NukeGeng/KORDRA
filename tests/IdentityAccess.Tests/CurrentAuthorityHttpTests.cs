using System.Net;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Contracts;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed partial class CurrentAuthorityHttpTests : IAsyncLifetime
{
    private LocalAuthenticationTestHost _host = null!;
    private HttpClient _client = null!;
    private readonly Guid _project = Guid.NewGuid(), _target = Guid.NewGuid();
    private Guid _tenant, _principal, _member, _assignment, _policy;
    private readonly List<Guid> _roles = [];
    private readonly List<Guid> _policies = [];
    private readonly List<string> _catalogKeys = [];
    private readonly TestOfflineLease _offline = new();
    private IDbContextFactory<ProjectDbContext> Projects => _host.Services.GetRequiredService<IDbContextFactory<ProjectDbContext>>();
    private IDbContextFactory<TenantDbContext> Tenants => _host.Services.GetRequiredService<IDbContextFactory<TenantDbContext>>();
    private IDbContextFactory<InventoryDbContext> Inventory => _host.Services.GetRequiredService<IDbContextFactory<InventoryDbContext>>();
    private IDbContextFactory<KordraIdentityDbContext> Identity => _host.Services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>();

    public async Task InitializeAsync()
    {
        _host = await LocalAuthenticationTestHost.StartAsync(resourceAuthority: true,
            resourceServices: ConfigureT07Probe, afterAuthentication: CaptureMetadataWrites);
        var user = _host.NewUserName(); var enrolled = await _host.EnrollAsync(user);
        Assert.Equal(LocalAuthenticationStatus.Success, enrolled.Status); _principal = enrolled.PrincipalId!.Value;
        var roles = (RoleDefinitionStore)_host.Services.GetRequiredService<IRoleDefinitionReader>();
        var founder = await roles.PublishOfflineAsync(_offline, Guid.NewGuid(), Guid.Empty, 0, "Synthetic founder", [new("org.manage", "organization"), new("membership.assign-role", "organization")]);
        Assert.Equal("success", founder.Status); _roles.Add(founder.DefinitionId);
        var tenants = _host.Services.GetRequiredService<TenantMembershipService>();
        var created = await tenants.InitializeOfflineAsync(_offline, Guid.NewGuid(), _principal, "Synthetic HTTP tenant", founder.DefinitionId, 1);
        Assert.Equal("success", created.Status); _tenant = created.TenantId;
        // Synthetic catalog rows are fixture setup only; this is not a delivered provisioning CLI.
        await using (var prj = await Projects.CreateDbContextAsync())
        {
            prj.Projects.Add(new() { Id = _project, TenantId = _tenant, Name = "Synthetic HTTP project" });
            prj.Associations.Add(new() { TargetId = _target, TenantId = _tenant, ProjectId = _project, Revision = 1 });
            await prj.SaveChangesAsync();
        }
        await using (var inv = await Inventory.CreateDbContextAsync())
        {
            inv.Targets.Add(new() { Id = _target, TenantId = _tenant, ProjectId = _project, Name = "Synthetic target", Kind = "metadata" });
            await inv.SaveChangesAsync();
        }
        await using (var ten = await Tenants.CreateDbContextAsync())
            _member = await ten.Memberships.Where(x => x.TenantId == _tenant && x.PrincipalId == _principal).Select(x => x.Id).SingleAsync();
        var viewer = await roles.PublishOfflineAsync(_offline, Guid.NewGuid(), Guid.Empty, 0, "Synthetic scoped viewer", [new("target.read", "project"), new("project.manage", "project")]);
        Assert.Equal("success", viewer.Status); _roles.Add(viewer.DefinitionId);
        var grant = await tenants.GrantOfflineAsync(_offline, Guid.NewGuid(), _tenant, _member, 1, viewer.DefinitionId, 1, new("project", _project), null);
        Assert.Equal("success", grant.Status); _assignment = grant.ResourceId;
        var payload = new ResourcePolicyPayload(1, "target.read", "project_without_environment", "active",
            DateTimeOffset.FromUnixTimeMilliseconds(_host.Clock.GetUtcNow().AddMinutes(-1).ToUnixTimeMilliseconds()), null, [new("NoEnvironment", "allow")]);
        var manifest = new ResourcePolicyManifest(1, Guid.NewGuid(), _host.Services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId,
            new("kordra_test", "kordra_test"), _tenant, _project, "create", null, 0, payload, [new(null, "project_without_environment", null, 0)]);
        var coordinator = new OfflineResourcePolicyCoordinator(manifest.InstallationId, Identity, tenants,
            new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(TestDatabaseConfiguration.TryGetConnectionString()!))),
            _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), _host.Authentication);
        var published = await coordinator.ExecuteAsync(_offline, Encoding.UTF8.GetString(manifest.CanonicalBytes()), manifest.Hash());
        Assert.Equal("success", published.Status); _policy = published.PolicyId; _policies.Add(_policy);
        _client = _host.CreateClient();
        using var login = await _host.LoginAsync(_client, user, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Current_PostgreSQL_owner_facts_real_grant_policy_cookie_and_UD_protect_actual_metadata()
    {
        using var response = await _client.GetAsync($"/api/v1/targets/{_target:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(_target, body.RootElement.GetProperty("targetId").GetGuid());
        Assert.Equal(_project, body.RootElement.GetProperty("projectId").GetGuid());
        Assert.Equal("metadata", body.RootElement.GetProperty("kind").GetString());
        Assert.False(body.RootElement.TryGetProperty("tenantId", out _));
        Assert.False(body.RootElement.TryGetProperty("name", out _));
        AssertFixtureFields(0, "success", "bodyFields", body.RootElement);
        await using var audit = await _host.CreateAuditContextAsync();
        var intents = await audit.Entries.Where(x => x.ResourceRef == _target).OrderBy(x => x.RecordedAtUtc).ToArrayAsync();
        Assert.Equal(2, intents.Length); Assert.Contains(intents, x => x.Stage == "U"); Assert.Contains(intents, x => x.Stage == "D");
        Assert.All(intents, x => Assert.StartsWith("authority-", x.SafeReason));
    }

    [Theory]
    [InlineData("missing-grant")][InlineData("missing-binding")][InlineData("suspended-tenant")][InlineData("suspended-project")]
    [InlineData("disabled-target")][InlineData("foreign-association")]
    public async Task Current_invalid_owner_state_never_emits_protected_output(string state)
    {
        // Controlled fixture fault injection before a request; not an authorized runtime mutation endpoint.
        await using (var writer = await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireWriteAsync())
        {
            if (state is "missing-grant" or "suspended-tenant")
            {
                await using var ten = await Tenants.CreateDbContextAsync();
                if (state == "missing-grant") { var row = await ten.Assignments.SingleAsync(x => x.Id == _assignment); row.Status = "revoked"; row.Revision++; }
                else { var row = await ten.Organizations.SingleAsync(x => x.Id == _tenant); row.Status = "suspended"; row.Revision++; }
                await ten.SaveChangesAsync();
            }
            else if (state == "disabled-target")
            { await using var inv = await Inventory.CreateDbContextAsync(); var row = await inv.Targets.SingleAsync(x => x.Id == _target); row.Status = "disabled"; row.Revision++; await inv.SaveChangesAsync(); }
            else
            {
                await using var prj = await Projects.CreateDbContextAsync();
                if (state == "missing-binding")
                {
                    await prj.Set<PolicyBindingHistoryRecord>().Where(x => x.ProjectId == _project).ExecuteDeleteAsync();
                    await prj.Set<PolicyBindingRecord>().Where(x => x.ProjectId == _project).ExecuteDeleteAsync();
                }
                else if (state == "suspended-project") { var row = await prj.Projects.SingleAsync(x => x.Id == _project); row.Status = "suspended"; row.Revision++; }
                else { var row = await prj.Associations.SingleAsync(x => x.TargetId == _target); row.Revision++; }
                await prj.SaveChangesAsync();
            }
        }
        using var response = await _client.GetAsync($"/api/v1/targets/{_target:D}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        await using var audit = await _host.CreateAuditContextAsync(); Assert.False(await audit.Entries.AnyAsync(x => x.ResourceRef == _target));
    }

    [Fact]
    public async Task Real_logout_writer_invalidates_metadata_session_and_old_legacy_inventory_stays_denied()
    {
        using var logout = await _host.PostEmptyCsrfAsync(_client, "/api/v1/auth/logout-all", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var response = await _client.GetAsync($"/api/v1/targets/{_target:D}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        using var legacy = await _client.GetAsync("/inventory"); Assert.Equal(HttpStatusCode.Forbidden, legacy.StatusCode);
    }

    private async Task<Guid> PublishEnvironmentPolicy(bool allowRead = false)
    {
        var installation = _host.Services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;
        var payload = new ResourcePolicyPayload(1, "target.read", "environment", "active",
            DateTimeOffset.FromUnixTimeMilliseconds(_host.Clock.GetUtcNow().AddMinutes(-1).ToUnixTimeMilliseconds()), null, [new("Production", allowRead ? "allow" : "deny")]);
        var manifest = new ResourcePolicyManifest(1, Guid.NewGuid(), installation, new("kordra_test", "kordra_test"),
            _tenant, _project, "create", null, 0, payload, []);
        var coordinator = new OfflineResourcePolicyCoordinator(installation, Identity, _host.Services.GetRequiredService<TenantMembershipService>(),
            new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(TestDatabaseConfiguration.TryGetConnectionString()!))),
            _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), _host.Authentication);
        var result = await coordinator.ExecuteAsync(_offline, Encoding.UTF8.GetString(manifest.CanonicalBytes()), manifest.Hash());
        Assert.Equal("success", result.Status); _policies.Add(result.PolicyId); return result.PolicyId;
    }
    private async Task<HttpResponseMessage> CreateEnvironment(string key, string json, bool csrf = true)
    {
        _catalogKeys.Add(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key))));
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/projects/{_project:D}/environments") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("Origin", LocalAuthenticationTestHost.Origin); request.Headers.Add("Idempotency-Key", key);
        if (csrf) request.Headers.Add("X-Kordra-CSRF", await _host.GetCsrfTokenAsync(_client));
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task V2_environment_creation_uses_explicit_policy_binding_with_audited_replay_and_no_legacy_authority()
    {
        var policy = await PublishEnvironmentPolicy(); var correlation = Guid.NewGuid(); var key = Guid.NewGuid().ToString("N");
        var json = JsonSerializer.Serialize(new { schemaVersion = 2, correlationId = correlation, name = "Synthetic Production", classification = "Production", resourcePolicy = new { policyId = policy, revision = 1 } });
        using var response = await CreateEnvironment(key, json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal("\"1\"", response.Headers.ETag!.Tag);
        var original = await response.Content.ReadAsStringAsync(); using var parsed = JsonDocument.Parse(original);
        var environmentId = parsed.RootElement.GetProperty("environment").GetProperty("id").GetGuid();
        AssertFixtureFields(1, "success", "bodyFields", parsed.RootElement);
        AssertFixtureFields(1, "success", "environmentFields", parsed.RootElement.GetProperty("environment"));
        AssertFixtureFields(1, "success", "bindingFields", parsed.RootElement.GetProperty("policyBinding"));
        Assert.Equal(policy, parsed.RootElement.GetProperty("policyBinding").GetProperty("policyId").GetGuid());
        Assert.Equal(correlation, parsed.RootElement.GetProperty("correlationId").GetGuid());
        await using (var project = await Projects.CreateDbContextAsync())
        {
            var row = await project.Environments.SingleAsync(x => x.Id == environmentId); Assert.Null(row.PolicyRevision);
            Assert.Single(await project.Set<PolicyBindingRecord>().Where(x => x.EnvironmentId == environmentId).ToArrayAsync());
            // Fixture invalidation proves completed command reconciliation does not require today's grant.
            var parent = await project.Projects.SingleAsync(x => x.Id == _project); parent.Status = "suspended"; parent.Revision++; await project.SaveChangesAsync();
        }
        using var replay = await CreateEnvironment(key, json);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        using var conflict = await CreateEnvironment(key, json.Replace("Synthetic Production", "Changed")); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var denied = await CreateEnvironment(Guid.NewGuid().ToString("N"), json); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await using var audit = await _host.CreateAuditContextAsync();
        Assert.Single(await audit.Entries.Where(x => x.ResourceRef == environmentId && x.Stage == "mutation").ToArrayAsync());
    }

    [Theory]
    [InlineData("legacy-version")][InlineData("unknown-tenant")][InlineData("duplicate-property")][InlineData("wrong-type")][InlineData("missing-csrf")]
    public async Task V2_rejects_legacy_unknown_duplicate_fields_and_missing_antiforgery_before_mutation(string fault)
    {
        var policy = await PublishEnvironmentPolicy();
        var json = JsonSerializer.Serialize(new { schemaVersion = 2, correlationId = Guid.NewGuid(), name = "Synthetic", classification = "Production", resourcePolicy = new { policyId = policy, revision = 1 } });
        json = fault switch
        {
            "legacy-version" => json.Replace("schemaVersion\":2", "schemaVersion\":1"),
            "unknown-tenant" => json.Insert(1, "\"tenantId\":\"" + _tenant.ToString("D") + "\","),
            "duplicate-property" => json.Insert(1, "\"schemaVersion\":2,"),
            "wrong-type" => json.Replace("revision\":1", "revision\":true"),
            _ => json
        };
        using var response = await CreateEnvironment(Guid.NewGuid().ToString("N"), json, csrf: fault != "missing-csrf");
        Assert.Equal(fault == "missing-csrf" ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertFixtureFields(1, "error", "bodyFields", error.RootElement);
        AssertFixtureFields(1, "error", "errorFields", error.RootElement.GetProperty("error"));
        await using var db = await Projects.CreateDbContextAsync(); Assert.False(await db.Environments.AnyAsync(x => x.ProjectId == _project));
    }

    private static void AssertFixtureFields(int route, string section, string fieldList, JsonElement actual)
    {
        DirectoryInfo? root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kordra.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "docs/contracts/fixtures/resource-policy-v1.http.json")));
        var expected = fixture.RootElement.GetProperty("routes")[route].GetProperty(section).GetProperty(fieldList).EnumerateArray().Select(x => x.GetString()).Order(StringComparer.Ordinal);
        Assert.Equal(expected, actual.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        using var openApi = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "docs/contracts/openapi/resource-policy-v1.openapi.json")));
        var schemaName = section == "error" ? fieldList == "bodyFields" ? "ErrorV2" : null : fieldList switch
        { "bodyFields" => route == 0 ? "TargetMetadataV1" : "EnvironmentCreatedV2", "environmentFields" => "Environment", "bindingFields" => "PolicyBinding", _ => null };
        if (schemaName is not null)
        {
            var schema = openApi.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
            Assert.Equal(schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order(StringComparer.Ordinal), actual.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        }
        foreach (var source in openApi.RootElement.GetProperty("x-working-tree-source-sha256").EnumerateObject())
            Assert.Equal(source.Value.GetString(), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root.FullName, source.Name)))));
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null) await CleanupCredentials();
        if (_host is null) { _offline.Dispose(); return; }
        await using (var inv = await Inventory.CreateDbContextAsync()) await inv.Targets.Where(x => x.Id == _target).ExecuteDeleteAsync();
        await using (var prj = await Projects.CreateDbContextAsync())
        {
            await prj.Set<PolicyBindingHistoryRecord>().Where(x => x.ProjectId == _project).ExecuteDeleteAsync();
            await prj.Set<PolicyBindingRecord>().Where(x => x.ProjectId == _project).ExecuteDeleteAsync();
            await prj.Associations.Where(x => x.TargetId == _target).ExecuteDeleteAsync();
            await prj.Commands.Where(x => _catalogKeys.Contains(x.KeyHash)).ExecuteDeleteAsync();
            await prj.Environments.Where(x => x.ProjectId == _project && x.TenantId == _tenant).ExecuteDeleteAsync();
            await prj.Projects.Where(x => x.Id == _project).ExecuteDeleteAsync();
        }
        await using (var ten = await Tenants.CreateDbContextAsync())
        {
            await ten.Commands.Where(x => x.TenantId == _tenant).ExecuteDeleteAsync(); await ten.Assignments.Where(x => x.TenantId == _tenant).ExecuteDeleteAsync();
            await ten.Memberships.Where(x => x.TenantId == _tenant).ExecuteDeleteAsync(); await ten.Organizations.Where(x => x.Id == _tenant).ExecuteDeleteAsync();
        }
        await using (var iam = await Identity.CreateDbContextAsync())
        {
            await iam.Set<ResourcePolicyHead>().Where(x => _policies.Contains(x.PolicyId)).ExecuteDeleteAsync();
            await iam.Set<ResourcePolicyRule>().Where(x => _policies.Contains(x.PolicyId)).ExecuteDeleteAsync();
            await iam.Set<ResourcePolicyRevision>().Where(x => _policies.Contains(x.PolicyId)).ExecuteDeleteAsync();
            await iam.Set<ResourcePolicyIdentity>().Where(x => _policies.Contains(x.PolicyId)).ExecuteDeleteAsync();
            await iam.RoleDefinitions.Where(x => _roles.Contains(x.DefinitionId)).ExecuteDeleteAsync();
        }
        _offline.Dispose(); await _host.DisposeAsync();
    }
}
