using Kordra.Modules.IdentityAccess.Authorization;
using System.Net;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

// The controlled observation/audit adapters exist only in this test assembly.
// Current session, grant, policy, ownership, actual AUD writes and HTTPS are real.
public sealed partial class CurrentAuthorityHttpTests
{
    private readonly MetadataProbe _probe = new();
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string MetadataUrl => $"/api/v1/targets/{_target:D}";

    private void ConfigureT07Probe(IServiceCollection services)
    {
        // Check the unmodified Product composition before adding test-only decorators.
        Assert.Equal(typeof(CurrentTargetMetadataReader), services.Last(x => x.ServiceType == typeof(ITargetMetadataReader)).ImplementationType);
        services.AddSingleton<ITargetMetadataReader>(s => new ProbeReader(
            ActivatorUtilities.CreateInstance<CurrentTargetMetadataReader>(s), _probe));
        services.AddSingleton<IAuthorityCommitter>(s => new ProbeAudit(s.GetRequiredService<LocalAuthenticationService>(), _probe));
        services.AddSingleton<IProtectedReadDependencyReader>(s => new NomadFixtureDependencyReader(this,s));
    }

    private async Task CaptureMetadataWrites(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1/targets")) { await next(context); return; }
        var original = context.Response.Body;
        context.Response.Body = new MetadataWriteCounter(original, _probe);
        try
        {
            if (_kubernetesRegistry is not null && context.Request.Path.Value!.EndsWith("/workloads",StringComparison.Ordinal) &&
                Guid.TryParse(context.Request.Path.Value.Split('/')[^2],out var kubernetesTarget))
                await KubernetesWorkloadEndpoints.Handle(context,kubernetesTarget,_kubernetesRegistry,_kubernetesBroker!,_host.Authentication,_host.Services.GetRequiredService<ProtectedReadAdmission>());
            else if (_nomadRegistry is not null && context.Request.Path.Value!.EndsWith("/nomad-workloads",StringComparison.Ordinal) &&
                Guid.TryParse(context.Request.Path.Value.Split('/')[^2],out var nomadTarget))
                await NomadWorkloadEndpoints.Handle(context,nomadTarget,_nomadRegistry,_nomadBroker!,_host.Authentication,_host.Services.GetRequiredService<ProtectedReadAdmission>());
            else if (_credentialRead is not null)
                await CredentialReadHttpHandler.Handle(context, Guid.Parse(context.Request.Path.Value!.Split('/').Last()), _credentialRead, _host.Services.GetRequiredService<ProtectedReadAdmission>());
            else await next(context);
        }
        finally { context.Response.Body = original; }
    }

    [Fact]
    public async Task T07_delayed_adapter_uses_exact_selection_and_only_allowlisted_bytes_after_durable_UD()
    {
        var release = Signal();
        _probe.Read = async (real, selection, ct) =>
        {
            var snapshot = await real.ReadAsync(selection, ct);
            await release.Task.WaitAsync(ct);
            return snapshot! with { Name = "SYNTHETIC_NON_ALLOWLISTED_SECRET" };
        };
        var pending = _client.GetAsync(MetadataUrl, HttpCompletionOption.ResponseHeadersRead);
        try
        {
            await _probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(pending.IsCompleted); Assert.Equal(0, _probe.OutputBytes);
            Assert.Equal(AuthorityStage.Use, Assert.Single(_probe.Audits).Stage);
            var selected = Assert.Single(_probe.Selections);
            Assert.Equal(_tenant, selected.TenantId); Assert.Equal(_target, selected.TargetId);
            Assert.Equal("project", selected.ScopeKind); Assert.Equal(_project, selected.ScopeId);
            Assert.NotEqual(Guid.Empty, selected.InvocationId);
            await using var audit = await _host.CreateAuditContextAsync();
            Assert.Equal("U", (await audit.Entries.SingleAsync(x => x.ResourceRef == _target)).Stage);
        }
        finally { release.TrySetResult(); }
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var body = JsonDocument.Parse(bytes); AssertFixtureFields(0, "success", "bodyFields", body.RootElement);
        Assert.False(body.RootElement.TryGetProperty("name", out _)); Assert.False(body.RootElement.TryGetProperty("tenantId", out _));
        Assert.DoesNotContain("SYNTHETIC_NON_ALLOWLISTED_SECRET", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.Equal(bytes.Length, _probe.OutputBytes); Assert.Single(_probe.Selections);
        Assert.Equal(new[] { AuthorityStage.Use, AuthorityStage.Disclosure }, _probe.Audits.Select(x => x.Stage));
        Assert.Equal(_probe.Selections[0].InvocationId, _probe.Audits[1].InvocationId);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), _probe.Audits[1].ResultDigest);
    }

    [Fact]
    public async Task T07_environment_target_uses_its_explicit_scope_policy_and_grant_without_project_fallback()
    {
        var environment = await ConfigureEnvironmentTarget();
        using var response = await _client.GetAsync(MetadataUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(environment, result.RootElement.GetProperty("environmentId").GetGuid());
        var selected = Assert.Single(_probe.Selections);
        Assert.Equal("environment", selected.ScopeKind); Assert.Equal(environment, selected.ScopeId);
        Assert.Equal(_tenant, selected.TenantId); Assert.Equal(_target, selected.TargetId);
    }

    private async Task<Guid> ConfigureEnvironmentTarget()
    {
        var policy = await PublishEnvironmentPolicy(allowRead: true);
        var json = JsonSerializer.Serialize(new { schemaVersion = 2, correlationId = Guid.NewGuid(), name = "Synthetic T07 environment",
            classification = "Production", resourcePolicy = new { policyId = policy, revision = 1 } });
        using var created = await CreateEnvironment(Guid.NewGuid().ToString("N"), json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var environment = body.RootElement.GetProperty("environment").GetProperty("id").GetGuid();
        var roles = (Kordra.Modules.IdentityAccess.Authorization.RoleDefinitionStore)_host.Services.GetRequiredService<IRoleDefinitionReader>();
        var role = await roles.PublishOfflineAsync(_offline, Guid.NewGuid(), Guid.Empty, 0, "Synthetic environment viewer", [new("target.read", "environment")]);
        Assert.Equal("success", role.Status); _roles.Add(role.DefinitionId);
        long memberRevision;
        await using (var ten = await Tenants.CreateDbContextAsync())
            memberRevision = await ten.Memberships.Where(x => x.TenantId == _tenant && x.Id == _member).Select(x => x.Revision).SingleAsync();
        var grant = await _host.Services.GetRequiredService<Kordra.Modules.TenantManagement.TenantMembershipService>()
            .GrantOfflineAsync(_offline, Guid.NewGuid(), _tenant, _member, memberRevision, role.DefinitionId, role.Revision,
                new("environment", environment), null);
        Assert.Equal("success", grant.Status);
        await using (var writer = await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireWriteAsync())
        {
            await using var prj = await Projects.CreateDbContextAsync();
            var association = await prj.Associations.SingleAsync(x => x.TargetId == _target);
            association.EnvironmentId = environment; association.Revision++; await prj.SaveChangesAsync();
            await using var inv = await Inventory.CreateDbContextAsync();
            var target = await inv.Targets.SingleAsync(x => x.Id == _target);
            target.EnvironmentId = environment; target.AssociationRevision = association.Revision; target.Revision++; await inv.SaveChangesAsync();
        }
        return environment;
    }

    [Theory]
    [InlineData("grant")][InlineData("binding")][InlineData("target")][InlineData("tenant")]
    public async Task T07_delayed_snapshot_cannot_disclose_after_current_owner_change(string owner)
    {
        var captured = Signal(); var release = Signal();
        _probe.Read = async (real, selection, ct) =>
        {
            var snapshot = await real.ReadAsync(selection, ct); Assert.NotNull(snapshot);
            captured.TrySetResult(); await release.Task.WaitAsync(ct); return snapshot;
        };
        var pending = _client.GetAsync(MetadataUrl);
        try
        {
            await captured.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await using (var writer = await _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireWriteAsync())
            {
                // Synthetic fixture writes under the real common fence, not a delivered management endpoint.
                if (owner is "grant" or "tenant")
                {
                    await using var ten = await Tenants.CreateDbContextAsync();
                    if (owner == "grant") { var row = await ten.Assignments.SingleAsync(x => x.Id == _assignment); row.Status = "revoked"; row.Revision++; }
                    else { var row = await ten.Organizations.SingleAsync(x => x.Id == _tenant); row.Status = "suspended"; row.Revision++; }
                    await ten.SaveChangesAsync();
                }
                else if (owner == "binding")
                {
                    await using var prj = await Projects.CreateDbContextAsync();
                    var row = await prj.Set<Kordra.Modules.ProjectEnvironment.Infrastructure.PolicyBindingRecord>().SingleAsync(x => x.ProjectId == _project);
                    row.BindingRevision++; await prj.SaveChangesAsync();
                }
                else
                {
                    await using var inv = await Inventory.CreateDbContextAsync();
                    var row = await inv.Targets.SingleAsync(x => x.Id == _target); row.Status = "disabled"; row.Revision++; await inv.SaveChangesAsync();
                }
                release.TrySetResult(); Assert.False(pending.IsCompleted);
            }
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(0, _probe.OutputBytes); Assert.Single(_probe.Selections);
            Assert.Equal(AuthorityStage.Use, Assert.Single(_probe.Audits).Stage);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(AuthorityStage.Use, AuthorityCommitStatus.Unavailable)]
    [InlineData(AuthorityStage.Disclosure, AuthorityCommitStatus.Unavailable)]
    [InlineData(AuthorityStage.Use, AuthorityCommitStatus.Unknown)]
    [InlineData(AuthorityStage.Disclosure, AuthorityCommitStatus.Unknown)]
    public async Task T07_unavailable_or_unknown_audit_never_releases_protected_body(AuthorityStage stage, AuthorityCommitStatus status)
    {
        _probe.AuditFault = (stage, status);
        using var response = await _client.GetAsync(MetadataUrl);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, _probe.OutputBytes); Assert.Equal(stage == AuthorityStage.Use ? 0 : 1, _probe.Selections.Count);
        var gate = _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>();
        if (status == AuthorityCommitStatus.Unknown)
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await gate.AcquireReadAsync(); });
        else { await using var held = await gate.AcquireReadAsync(); }
    }

    [Theory]
    [InlineData("tenant")][InlineData("target")][InlineData("scope")]
    [InlineData("oversize")][InlineData("inactive")][InlineData("revision")][InlineData("kind-control")][InlineData("exception")]
    public async Task T07_untrusted_observation_cannot_change_selection_or_escape_bounded_allowlist(string fault)
    {
        _probe.Read = async (real, selection, ct) =>
        {
            var row = (await real.ReadAsync(selection, ct))!;
            return fault switch
            {
                "tenant" => row with { TenantId = Guid.NewGuid() },
                "target" => row with { TargetId = Guid.NewGuid() },
                "scope" => row with { ProjectId = Guid.NewGuid() },
                "oversize" => row with { Kind = new string('x', 256 * 1024 + 1) },
                "inactive" => row with { Status = "disabled" },
                "revision" => row with { Revision = 0 },
                "kind-control" => row with { Kind = "metadata\nsecret" },
                _ => throw new IOException("SYNTHETIC_SECRET_IN_PROVIDER_FAILURE")
            };
        };
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(MetadataUrl));
        Assert.Equal(0, _probe.OutputBytes); Assert.Single(_probe.Selections);
        Assert.Equal(AuthorityStage.Use, Assert.Single(_probe.Audits).Stage);
    }

    [Fact]
    public async Task T07_client_cancellation_reaches_pending_adapter_without_retry_or_disclosure()
    {
        _probe.Read = (_, _, ct) => new TaskCompletionSource<TargetMetadata?>().Task.WaitAsync(ct);
        using var cancel = new CancellationTokenSource();
        var pending = _client.GetAsync(MetadataUrl, cancel.Token);
        await _probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await _probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, _probe.OutputBytes); Assert.Single(_probe.Selections);
        Assert.Equal(AuthorityStage.Use, Assert.Single(_probe.Audits).Stage);
    }

    [Fact]
    public async Task T07_provider_deadline_cancels_pending_adapter_without_response_retry()
    {
        _probe.Read = (_, _, ct) => new TaskCompletionSource<TargetMetadata?>().Task.WaitAsync(ct);
        await AssertAbortedOrEmptyUnavailable(_client.GetAsync(MetadataUrl).WaitAsync(TimeSpan.FromSeconds(9)));
        await _probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, _probe.OutputBytes); Assert.Single(_probe.Selections);
        Assert.Equal(AuthorityStage.Use, Assert.Single(_probe.Audits).Stage);
    }

    [Theory]
    [InlineData("anonymous")][InlineData("query-tenant")][InlineData("unknown-target")]
    public async Task T07_untrusted_caller_selections_do_not_reach_adapter(string fault)
    {
        using var anonymous = _host.CreateClient();
        using var response = fault switch
        {
            "anonymous" => await anonymous.GetAsync(MetadataUrl),
            "query-tenant" => await _client.GetAsync(MetadataUrl + "?tenantId=" + Guid.NewGuid()),
            _ => await _client.GetAsync($"/api/v1/targets/{Guid.NewGuid():D}")
        };
        Assert.Equal(fault == "anonymous" ? HttpStatusCode.Unauthorized : fault == "query-tenant" ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_probe.Selections); Assert.Empty(_probe.Audits); Assert.Equal(0, _probe.OutputBytes);
    }

    private static async Task AssertAbortedOrEmptyUnavailable(Task<HttpResponseMessage> pending)
    {
        try
        {
            using var response = await pending;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }
        catch (HttpRequestException) { } // actual pre-header HTTP abort is also a safe transport outcome
    }

    private sealed class MetadataProbe
    {
        public readonly TaskCompletionSource Started = Signal(), Cancelled = Signal();
        public readonly List<TargetMetadataReadSelection> Selections = [];
        public readonly List<AuthorityCommitment> Audits = [];
        public Func<ITargetMetadataReader, TargetMetadataReadSelection, CancellationToken, Task<TargetMetadata?>>? Read;
        public (AuthorityStage Stage, AuthorityCommitStatus Status)? AuditFault;
        public AuthorityCommitStatus? CredentialAuditFault;
        public Action<AuthorityCommitment>? AfterAudit;
        public int OutputBytes;
    }
    private sealed class ProbeReader(ITargetMetadataReader real, MetadataProbe probe) : ITargetMetadataReader
    {
        public async Task<TargetMetadata?> ReadAsync(TargetMetadataReadSelection selection, CancellationToken ct = default)
        {
            probe.Selections.Add(selection); probe.Started.TrySetResult();
            try { return await (probe.Read is { } read ? read(real, selection, ct) : real.ReadAsync(selection, ct)); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { probe.Cancelled.TrySetResult(); throw; }
        }
    }
    private sealed class ProbeAudit(LocalAuthenticationService real, MetadataProbe probe) : IAuthorityCommitter
    {
        public async Task<AuthorityCommitStatus> CommitUnderAdmissionAsync(AuthorityCommitment commitment, CancellationToken ct = default)
        {
            if (commitment.Purpose == AuthorityCommitPurpose.CredentialConsumption && probe.CredentialAuditFault is { } credentialFault) return credentialFault;
            if (probe.AuditFault is { } fault && fault.Stage == commitment.Stage) return fault.Status;
            var status = await real.CommitUnderAdmissionAsync(commitment, ct);
            if (status == AuthorityCommitStatus.Durable) { probe.Audits.Add(commitment); probe.AfterAudit?.Invoke(commitment); }
            return status;
        }
    }
    private sealed class MetadataWriteCounter(Stream inner, MetadataProbe probe) : Stream
    {
        private void Count(int length)
        { Assert.Equal(AuthorityStage.Disclosure, probe.Audits.Last().Stage); Interlocked.Add(ref probe.OutputBytes, length); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { Count(count); inner.Write(buffer, offset, count); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { Count(buffer.Length); return inner.WriteAsync(buffer, ct); }
    }
}
