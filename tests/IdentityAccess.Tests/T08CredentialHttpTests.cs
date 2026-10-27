using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.TenantManagement;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Commands;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.ConnectionManagement.Keychain;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed partial class CurrentAuthorityHttpTests
{
    private IProtectedCredentialRead? _credentialRead;
    private CredentialManifest? _credentialManifest;
    private readonly HashSet<Guid> _credentialCommands = [];
    private readonly ControlledLookup _credentialLookup = new();
    private readonly SyntheticConsumer _credentialConsumer = new();
    private IDbContextFactory<CredentialDbContext> Credentials => _host.Services.GetRequiredService<IDbContextFactory<CredentialDbContext>>();
    private Guid CredentialInstallation => _host.Services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;

    private OfflineCredentialCoordinator CredentialCoordinator() => new(CredentialInstallation, Identity,
        new TenantOrganizationAuthority(Tenants), _host.Services.GetRequiredService<ITargetAuthorityReader>(),
        new AuditAppendStore(new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(TestDatabaseConfiguration.TryGetConnectionString()!))),
        _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), _host.Authentication);
    private async Task<CredentialOperationResult> ApplyCredential(CredentialManifest m)
    {
        _credentialCommands.Add(m.ManifestId); return await CredentialCoordinator().ExecuteAsync(_offline, m.CanonicalBytes(), m.Hash());
    }
    private async Task EnableCredentialRead(Func<KeychainAccessConfiguration, ISecretLookup>? createLookup = null)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(_host.Clock.GetUtcNow().ToUnixTimeMilliseconds());
        var m = new CredentialManifest(1, Guid.NewGuid(), CredentialInstallation, _tenant, _target, "inventory.readonly.v1", "target.read", "create", null, 0,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(-1), now.AddMinutes(5));
        Assert.Equal("success", (await ApplyCredential(m)).Status); _credentialManifest = m;
        var root = FindProductRoot(); var executable = Path.Combine(root, "src/Host/Kordra.Api/bin/Release/net10.0/Kordra.Api");
        var configuration = new KeychainAccessConfiguration(CredentialInstallation, executable,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable))), new Dictionary<Guid, string> { [m.LocatorId!.Value] = "/private/synthetic-do-not-open.keychain" });
        var broker = new CredentialBroker(Credentials, _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), _host.Authentication, configuration, createLookup?.Invoke(configuration) ?? _credentialLookup);
        _credentialConsumer.Read = (_, context, ct) => _host.Services.GetRequiredService<TargetMetadataRead>().Admit(new(context.InvocationId, context.Selection.TenantId,
            context.Selection.TargetId, context.ScopeKind, context.ScopeId), ct);
        _credentialRead = new CredentialTargetRead(CredentialInstallation, broker, _credentialConsumer);
    }
    [Fact]
    public async Task T08_actual_https_has_distinct_U_C_D_and_never_discloses_secret_or_locator()
    {
        await EnableCredentialRead();
        using var response = await _client.GetAsync(MetadataUrl); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(); using var doc = JsonDocument.Parse(bytes); AssertFixtureFields(0, "success", "bodyFields", doc.RootElement);
        var text = Encoding.UTF8.GetString(bytes); Assert.DoesNotContain("SYNTHETIC_T08_SECRET", text); Assert.DoesNotContain("keychain", text);
        Assert.Equal(1, _credentialLookup.Calls); Assert.Equal(1, _credentialConsumer.Calls);
        Assert.Equal(_probe.Audits[0].InvocationId, Assert.Single(_probe.Selections).InvocationId);
        Assert.Equal(new[] { AuthorityStage.Use, AuthorityStage.Use, AuthorityStage.Disclosure }, _probe.Audits.Select(x => x.Stage));
        Assert.Equal(AuthorityCommitPurpose.CredentialConsumption, _probe.Audits[1].Purpose);
        Assert.Equal(3, _probe.Audits.Select(x => x.IntentId).Distinct().Count()); Assert.Single(_probe.Audits.Select(x => x.InvocationId).Distinct());
        Assert.All(_credentialLookup.LastMaterial!, x => Assert.Equal(0, x));
        await using var aud = await _host.CreateAuditContextAsync();
        var intents = await aud.Entries.Where(x => x.ResourceRef == _target && x.Permission == "target.read").ToArrayAsync();
        Assert.Equal(3, intents.Length); Assert.Single(intents, x => x.SafeReason.StartsWith("credential-use-"));
        Assert.DoesNotContain("SYNTHETIC_T08_SECRET", JsonSerializer.Serialize(intents));
    }
    [Fact]
    public async Task T08_environment_credential_read_preserves_exact_scope_and_separate_C()
    {
        var environment = await ConfigureEnvironmentTarget();
        await EnableCredentialRead();
        using var response = await _client.GetAsync(MetadataUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertFixtureFields(0, "success", "bodyFields", body.RootElement);
        Assert.Equal(environment, body.RootElement.GetProperty("environmentId").GetGuid());
        var selection = Assert.Single(_probe.Selections);
        Assert.Equal("environment", selection.ScopeKind); Assert.Equal(environment, selection.ScopeId);
        Assert.Equal(_tenant, selection.TenantId); Assert.Equal(_target, selection.TargetId);
        Assert.Equal(1, _credentialLookup.Calls); Assert.Equal(1, _credentialConsumer.Calls);
        Assert.Equal(new[] { AuthorityStage.Use, AuthorityStage.Use, AuthorityStage.Disclosure }, _probe.Audits.Select(x => x.Stage));
        Assert.Equal(AuthorityCommitPurpose.CredentialConsumption, _probe.Audits[1].Purpose);
        Assert.All(_probe.Audits, x => Assert.Equal(selection.InvocationId, x.InvocationId));
        Assert.All(_credentialLookup.LastMaterial!, x => Assert.Equal(0, x));
    }

    [Theory]
    [InlineData("rotate")]
    [InlineData("revoke")]
    [InlineData("grant")]
    public async Task T08_invalidation_during_lookup_withholds_credential_from_consumer(string mutation)
    {
        await EnableCredentialRead(); var entered = Signal(); var release = Signal();
        _credentialLookup.BeforeReturn = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
        var pending = _client.GetAsync(MetadataUrl);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (mutation == "grant")
            {
                await using var held = await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireWriteAsync();
                await using var ten = await Tenants.CreateDbContextAsync(); var assignment = await ten.Assignments.SingleAsync(x => x.Id == _assignment);
                assignment.Status = "revoked"; assignment.Revision++; await ten.SaveChangesAsync();
            }
            else
            {
                var m = _credentialManifest! with
                {
                    ManifestId = Guid.NewGuid(),
                    Action = mutation,
                    ExpectedBindingId = _credentialManifest.BindingId,
                    ExpectedRevision = 1,
                    ReferenceId = mutation == "rotate" ? Guid.NewGuid() : null,
                    ProfileId = mutation == "rotate" ? _credentialManifest.ProfileId : null,
                    LocatorId = mutation == "rotate" ? _credentialManifest.LocatorId : null,
                    NotBeforeUtc = mutation == "rotate" ? _credentialManifest.NotBeforeUtc : null,
                    ExpiresAtUtc = mutation == "rotate" ? _credentialManifest.ExpiresAtUtc : null
                };
                Assert.Equal("success", (await ApplyCredential(m)).Status);
            }
            release.TrySetResult(); await AssertAbortedOrEmptyUnavailable(pending);
            Assert.Equal(0, _credentialConsumer.Calls); Assert.Equal(0, _probe.OutputBytes);
            Assert.Equal(AuthorityStage.Use, Assert.Single(_probe.Audits).Stage);
            Assert.All(_credentialLookup.LastMaterial!, x => Assert.Equal(0, x));
        }
        finally { release.TrySetResult(); }
    }
    [Fact]
    public async Task T08_revocation_after_C_admission_discards_delayed_consumer_result_at_D()
    {
        await EnableCredentialRead(); var entered = Signal(); var release = Signal();
        _credentialConsumer.Read = async (_, context, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return await _host.Services.GetRequiredService<TargetMetadataRead>().Admit(new(context.InvocationId, context.Selection.TenantId, context.Selection.TargetId, context.ScopeKind, context.ScopeId), ct); };
        var pending = _client.GetAsync(MetadataUrl);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var m = _credentialManifest! with
            {
                ManifestId = Guid.NewGuid(),
                Action = "revoke",
                ExpectedBindingId = _credentialManifest.BindingId,
                ExpectedRevision = 1,
                ProfileId = null,
                ReferenceId = null,
                LocatorId = null,
                NotBeforeUtc = null,
                ExpiresAtUtc = null
            };
            Assert.Equal("success", (await ApplyCredential(m)).Status); release.TrySetResult();
            using var response = await pending; Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(1, _credentialConsumer.Calls); Assert.Equal(0, _probe.OutputBytes); Assert.Equal(2, _probe.Audits.Count);
            Assert.All(_credentialLookup.LastMaterial!, x => Assert.Equal(0, x));
        }
        finally { release.TrySetResult(); }
    }
    [Theory]
    [InlineData(AuthorityCommitStatus.Unavailable)]
    [InlineData(AuthorityCommitStatus.Rejected)]
    [InlineData(AuthorityCommitStatus.Unknown)]
    public async Task T08_failed_C_audit_never_calls_consumer(AuthorityCommitStatus fault)
    {
        await EnableCredentialRead(); _probe.CredentialAuditFault = fault;
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(MetadataUrl)); Assert.Equal(0, _credentialConsumer.Calls); Assert.Equal(0, _probe.OutputBytes);
        Assert.All(_credentialLookup.LastMaterial!, x => Assert.Equal(0, x));
        if (fault == AuthorityCommitStatus.Unknown) await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireReadAsync(); });
    }
    [Fact]
    public async Task T08_two_helper_reservations_bound_capacity_before_audit_or_lookup()
    {
        await EnableCredentialRead(); using var a = _credentialRead!.TryReserve(); using var b = _credentialRead.TryReserve(); Assert.NotNull(a); Assert.NotNull(b);
        using var response = await _client.GetAsync(MetadataUrl); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(_probe.Audits); Assert.Equal(0, _credentialLookup.Calls); Assert.Equal(0, _probe.OutputBytes);
    }
    [Fact]
    public async Task T08_known_failed_launch_releases_slots_and_preserves_unrelated_admission()
    {
        var factory = new FailedLaunchFactory();
        await EnableCredentialRead(configuration => new PortableNativeLookup(new(configuration, factory)));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await AssertAbortedOrEmptyUnavailable(_client.GetAsync(MetadataUrl));
            Assert.Equal(0, _probe.OutputBytes);
            Assert.Equal(attempt, factory.Starts); // More attempts than the two slots: none may leak.
            await using var writer = await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireWriteAsync();
        }
        Assert.Equal(0, _credentialConsumer.Calls);
        Assert.Equal(3, _probe.Audits.Count);
        Assert.All(_probe.Audits, x => Assert.Equal(AuthorityCommitPurpose.ReadAdmission, x.Purpose));
        _credentialRead = null; // Exercise the unchanged Product metadata route after the failures.
        using var metadata = await _client.GetAsync(MetadataUrl);
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        Assert.Equal(3, factory.Starts);
    }

    private sealed class FailedLaunchFactory : IKeychainProcessFactory
    {
        public int Starts;
        public IKeychainProcess? Start(System.Diagnostics.ProcessStartInfo start)
        {
            Interlocked.Increment(ref Starts);
            throw new System.ComponentModel.Win32Exception(13, "Synthetic execute permission denied.");
        }
    }
    private sealed class PortableNativeLookup(NativeSecretLookup inner) : ISecretLookup
    {
        public Task<SecretLookup> ReadAsync(KeychainRequest request, CancellationToken ct) => inner.ReadCoreAsync(request, ct);
    }

    [Fact]
    public async Task T08_unresolved_lookup_closes_admission_without_starting_C()
    {
        await EnableCredentialRead(); _credentialLookup.Quiescent = false;
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(MetadataUrl)); Assert.Equal(0, _credentialConsumer.Calls); Assert.Equal(0, _probe.OutputBytes);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireReadAsync(); });
    }
    [Theory]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("unsupported-consumer")]
    [InlineData("query-tenant")]
    [InlineData("unknown-target")]
    public async Task T08_invalid_initial_binding_or_selection_does_not_launch_lookup(string fault)
    {
        await EnableCredentialRead();
        if (fault == "revoked")
        {
            var m = _credentialManifest! with
            {
                ManifestId = Guid.NewGuid(),
                Action = "revoke",
                ExpectedBindingId = _credentialManifest.BindingId,
                ExpectedRevision = 1,
                ProfileId = null,
                ReferenceId = null,
                LocatorId = null,
                NotBeforeUtc = null,
                ExpiresAtUtc = null
            };
            Assert.Equal("success", (await ApplyCredential(m)).Status);
        }
        if (fault == "expired") _host.Clock.Advance(TimeSpan.FromMinutes(6));
        if (fault == "unsupported-consumer") _credentialConsumer.RegisteredId = "unsupported";
        var url = fault == "query-tenant" ? MetadataUrl + "?tenantId=" + Guid.NewGuid() : fault == "unknown-target" ? $"/api/v1/targets/{Guid.NewGuid():D}" : MetadataUrl;
        using var response = await _client.GetAsync(url);
        Assert.Equal(fault == "query-tenant" ? HttpStatusCode.BadRequest : fault == "unsupported-consumer" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync()); Assert.Equal(0, _credentialLookup.Calls); Assert.Equal(0, _credentialConsumer.Calls); Assert.Empty(_probe.Audits);
    }
    [Fact]
    public async Task T08_expiry_while_lookup_waits_prevents_C_and_zeros_secret()
    {
        await EnableCredentialRead(); _credentialLookup.BeforeReturn = _ => { _host.Clock.Advance(TimeSpan.FromMinutes(6)); return Task.CompletedTask; };
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(MetadataUrl)); Assert.Equal(0, _credentialConsumer.Calls); Assert.Equal(0, _probe.OutputBytes);
        Assert.All(_credentialLookup.LastMaterial!, x => Assert.Equal(0, x));
    }
    [Fact]
    public async Task T08_client_cancellation_unwinds_lookup_without_consumer_or_response()
    {
        await EnableCredentialRead(); var entered = Signal(); var stopped = Signal();
        _credentialLookup.BeforeReturn = async ct => { entered.TrySetResult(); try { await new TaskCompletionSource().Task.WaitAsync(ct); } finally { stopped.TrySetResult(); } };
        using var cancellation = new CancellationTokenSource(); var pending = _client.GetAsync(MetadataUrl, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(0, _credentialConsumer.Calls); Assert.Equal(0, _probe.OutputBytes);
    }
    private async Task CleanupCredentials()
    {
        if (_credentialCommands.Count == 0) return;
        await using var db = await Credentials.CreateDbContextAsync(); var install = CredentialInstallation;
        await db.Heads.Where(x => x.InstallationId == install && x.TargetId == _target).ExecuteDeleteAsync();
        await db.Revisions.Where(x => x.InstallationId == install && x.TargetId == _target).ExecuteDeleteAsync();
        var profiles = await db.Profiles.Where(x => x.InstallationId == install && x.TargetId == _target).Select(x => x.ProfileId).ToArrayAsync();
        await db.References.Where(x => x.InstallationId == install && profiles.Contains(x.ProfileId)).ExecuteDeleteAsync();
        await db.ReferenceAllocations.Where(x => x.InstallationId == install && x.InventoryProfileId != null && profiles.Contains(x.InventoryProfileId.Value)).ExecuteDeleteAsync();
        await db.Profiles.Where(x => x.InstallationId == install && x.TargetId == _target).ExecuteDeleteAsync(); await db.Commands.Where(x => _credentialCommands.Contains(x.ManifestId)).ExecuteDeleteAsync();
        await using var aud = await _host.CreateAuditContextAsync(); await aud.Entries.Where(x => _credentialCommands.Contains(x.InvocationId)).ExecuteDeleteAsync();
    }
    private static string FindProductRoot() { var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kordra.slnx"))) root = root.Parent; return root?.FullName ?? throw new InvalidOperationException("Product root missing."); }
    private sealed class ControlledLookup : ISecretLookup
    {
        public int Calls; public byte[]? LastMaterial; public bool Quiescent = true; public Func<CancellationToken, Task>? BeforeReturn;
        public async Task<SecretLookup> ReadAsync(KeychainRequest request, CancellationToken ct)
        { Interlocked.Increment(ref Calls); if (BeforeReturn is not null) await BeforeReturn(ct); LastMaterial = Encoding.UTF8.GetBytes("SYNTHETIC_T08_SECRET"); return new(LastMaterial, Quiescent); }
    }
    private sealed class SyntheticConsumer : CredentialConsumer<PrivateReadBuffer>
    {
        public string RegisteredId = "inventory.readonly.v1"; public override string Id => RegisteredId; public int Calls; public Func<ReadOnlyMemory<byte>, CredentialUseContextV1, CancellationToken, Task<PrivateReadBuffer>> Read = null!;
        public override Task<PrivateReadBuffer> ReadAsync(ReadOnlyMemory<byte> bytes, CredentialUseContextV1 context, CancellationToken ct) { Interlocked.Increment(ref Calls); return Read(bytes, context, ct); }
    }
}
