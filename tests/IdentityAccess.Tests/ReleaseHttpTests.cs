using System.Net;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.ApprovalGovernance;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.ApprovalGovernance.Domain;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

/// <summary>Actual isolated local HTTPS, session, CSRF, real owner grants and release read paths.</summary>
[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class ReleaseHttpTests : IAsyncLifetime
{
    private LocalAuthenticationTestHost _host = null!; private HttpClient _client = null!;
    private readonly DisclosureClockFault _clockFault = new();
    private ApprovalAuthorityCaptureResult? _captured;
    private ApprovalUseResult? _approvalUse;
    private ApprovalMutationResult? _approvalMutation;
    private ApprovalReply? _approvalReply;
    private string? _approvalDisclosure;
    private IArtifactAuditTransactionFactory? _approvalAudit;
    private readonly Guid _project = Guid.NewGuid(), _release = Guid.NewGuid(), _application = Guid.NewGuid(), _environment = Guid.NewGuid(), _target = Guid.NewGuid(), _artifact = Guid.NewGuid(); private Guid _tenant;
    private string Root => $"/api/v1/projects/{_project:D}";
    public async Task InitializeAsync()
    {
        var locator = TestDatabaseConfiguration.TryGetConnectionString() ?? throw new InvalidOperationException("Private isolated test locator required.");
        var rel = new PooledDbContextFactory<ReleaseDbContext>(ReleaseDbContextFactory.CreateTestOptions(locator));
        var apr = new PooledDbContextFactory<ApprovalDbContext>(ApprovalDbContextFactory.CreateTestOptions(locator));
        var art = new PooledDbContextFactory<ArtifactDbContext>(ArtifactDbContextFactory.CreateTestOptions(locator));
        var aud = new PooledDbContextFactory<AuditDbContext>(AuditDbContextFactory.CreateTestOptions(locator));
        await using (var db = await rel.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await apr.CreateDbContextAsync()) await db.Database.MigrateAsync();
        await using (var db = await art.CreateDbContextAsync()) await db.Database.MigrateAsync();
        _host = await LocalAuthenticationTestHost.StartAsync(resourceAuthority: true, afterAuthentication: async (context, next) =>
        {
            if (context.Request.Path != "/test-approval-capture") { await next(context); return; }
            var subject = LocalAuthenticationEndpoints.CurrentSessionSelection(context);
            if (subject is null) { context.Response.StatusCode = 401; return; }
            var services = context.RequestServices;
            var capture = new ApprovalAuthorityCapture(services.GetRequiredService<ICurrentSubjectReader>(),
                new DurablePrincipalReader(services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>()),
                services.GetRequiredService<IScopedGrantReader>(), services.GetRequiredService<IRoleDefinitionReader>(),
                services.GetRequiredService<IReleaseProjectAuthorityReader>(),
                new ApprovalEnvironmentAuthorityReader(services.GetRequiredService<IDbContextFactory<ProjectDbContext>>()),
                services.GetRequiredService<IAuthorityClock>());
            if (context.Request.Query.ContainsKey("read"))
            {
                var durable = new StoredApprovalAuthorityReader(new DurableApprovalAuthorityReader(
                    new DurablePrincipalReader(services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>()),
                    (IDurableGrantEvidenceReader)services.GetRequiredService<TenantMembershipService>(), services.GetRequiredService<IRoleDefinitionReader>(), services.GetRequiredService<IAuthorityClock>()));
                var eligibility = new ApprovalEligibilityReader(new ApprovalSubjectReader(rel, services.GetRequiredService<ReleaseSelectionReader>()), durable, services.GetRequiredService<IAuthorityClock>());
                var query = new ApprovalReadQuery(Guid.NewGuid(), Guid.TryParse(context.Request.Query["request"], out var requestId) ? requestId : null);
                var response = new ApprovalCallerBoundResponseCommitter(context, subject, _environment, context.Request.Path.Value!,
                    services.GetRequiredService<IAuthorityClock>(), context.Request.QueryString.Value ?? "");
                var status = await new ApprovalReadCoordinator(services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId,
                    services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(), rel, apr, capture, eligibility,
                    services.GetRequiredService<IArtifactAuditTransactionFactory>(), services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(),
                    services.GetRequiredService<IAuthorityClock>(), new ApprovalSchemaReadiness(services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId, apr, services.GetRequiredService<IReleaseSchemaReadiness>()),
                    new ApprovalEnvironmentAuthorityReader(services.GetRequiredService<IDbContextFactory<ProjectDbContext>>()))
                    .ExecuteAsync(subject, Guid.Empty, _environment, query, response);
                if (status != "completed") context.Response.StatusCode = status == "denied" ? 403 : 503;
                return;
            }
            if (context.Request.Query.ContainsKey("use") || context.Request.Query.ContainsKey("mutation"))
            {
                _approvalMutation = null;
                var correlation = Guid.NewGuid();
                var action = context.Request.Query["action"].FirstOrDefault() ?? "request";
                var revisionId = Guid.TryParse(context.Request.Query["revision"], out var supplied) ? supplied : Guid.NewGuid();
                var requestId = context.Request.Query["request"].FirstOrDefault();
                var reasonCode = action == "grant" ? "approved" : action == "reject" ? "changes_required" : "owner_cancelled";
                var body = action == "request" ? JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = correlation, releaseRevisionId = revisionId })
                    : JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = correlation, reasonCode });
                var tag = action == "request" ? Microsoft.Extensions.Primitives.StringValues.Empty : new Microsoft.Extensions.Primitives.StringValues("\"apr-v1:" + requestId + ":" + context.Request.Query["version"] + "\"");
                var command = ApprovalRequestParser.Parse(action, _environment.ToString("D"), requestId, body, tag);
                if (context.Request.Query.ContainsKey("execute"))
                {
                    var install = services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;
                    var identity = services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>();
                    var gate = services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>();
                    var clock = services.GetRequiredService<IAuthorityClock>();
                    var auditFactory = services.GetRequiredService<IArtifactAuditTransactionFactory>();
                    var durable = new StoredApprovalAuthorityReader(new DurableApprovalAuthorityReader(new DurablePrincipalReader(identity),
                        (IDurableGrantEvidenceReader)services.GetRequiredService<TenantMembershipService>(), services.GetRequiredService<IRoleDefinitionReader>(), clock));
                    var subjectReader = new ApprovalSubjectReader(rel, services.GetRequiredService<ReleaseSelectionReader>());
                    var uses = new ApprovalUseAdmission(install, identity, capture, auditFactory, gate, clock);
                    var mutations = new ApprovalMutationTransaction(install, identity, rel, capture, durable, subjectReader, auditFactory, new AuditMutationEvidenceReader(aud), gate, clock);
                    var disclosures = new ApprovalMutationDisclosure(install, identity, rel, apr, capture,
                        new ApprovalEligibilityReader(subjectReader, durable, clock), auditFactory, gate, clock);
                    var response = new ApprovalCallerBoundResponseCommitter(context, subject, command.EnvironmentId, context.Request.Path.Value!, clock, context.Request.QueryString.Value ?? "");
                    _approvalDisclosure = await new ApprovalMutationExecutionCoordinator(uses, mutations, disclosures, gate,
                        new ApprovalSchemaReadiness(install, apr, services.GetRequiredService<IReleaseSchemaReadiness>()),
                        new ApprovalEnvironmentAuthorityReader(services.GetRequiredService<IDbContextFactory<ProjectDbContext>>()))
                        .ExecuteAsync(subject, Guid.Empty, command, "synthetic-approval-key-1234", correlation, response);
                    if (_approvalDisclosure != "completed") context.Abort();
                    return;
                }
                _approvalUse = await new ApprovalUseAdmission(services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId,
                    services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(), capture,
                    _approvalAudit ?? services.GetRequiredService<IArtifactAuditTransactionFactory>(), services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(),
                    services.GetRequiredService<IAuthorityClock>()).BeginAsync(subject, command, _project, "synthetic-approval-key-1234", correlation);
                if (context.Request.Query.ContainsKey("mutation") && _approvalUse.Ticket is { } ticket)
                {
                    var durable = new StoredApprovalAuthorityReader(new DurableApprovalAuthorityReader(
                        new DurablePrincipalReader(services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>()),
                        (IDurableGrantEvidenceReader)services.GetRequiredService<TenantMembershipService>(), services.GetRequiredService<IRoleDefinitionReader>(),
                        services.GetRequiredService<IAuthorityClock>()));
                    _approvalMutation = await new ApprovalMutationTransaction(services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId,
                        services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(), rel, capture, durable,
                        new ApprovalSubjectReader(rel, services.GetRequiredService<ReleaseSelectionReader>()),
                        _approvalAudit ?? services.GetRequiredService<IArtifactAuditTransactionFactory>(), new AuditMutationEvidenceReader(aud),
                        services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(), services.GetRequiredService<IAuthorityClock>()).ExecuteAsync(ticket);
                    _approvalReply = null; _approvalDisclosure = null;
                    if (_approvalMutation.Status is "durable" or "replay")
                    {
                        _approvalReply = new ApprovalReply();
                        _approvalDisclosure = await new ApprovalMutationDisclosure(services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId,
                            services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>(), rel, apr, capture,
                            new ApprovalEligibilityReader(new ApprovalSubjectReader(rel, services.GetRequiredService<ReleaseSelectionReader>()), durable, services.GetRequiredService<IAuthorityClock>()),
                            services.GetRequiredService<IArtifactAuditTransactionFactory>(), services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>(),
                            services.GetRequiredService<IAuthorityClock>()).DiscloseAsync(ticket, _approvalMutation,
                                context.Request.Query.ContainsKey("wire")
                                    ? new ApprovalCallerBoundResponseCommitter(context, subject, command.EnvironmentId,
                                        context.Request.Path.Value!, services.GetRequiredService<IAuthorityClock>(), context.Request.QueryString.Value ?? "")
                                    : _approvalReply);
                        if (context.Request.Query.ContainsKey("wire"))
                        {
                            if (_approvalDisclosure != "completed") context.Abort();
                            return;
                        }
                    }
                }
                context.Response.StatusCode = _approvalUse.Status == "admitted" ? 204 : 403;
                return;
            }
            await using var held = await services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireReadAsync();
            _captured = context.Request.Query.ContainsKey("revoker")
                ? await capture.CaptureRevokerUnderAdmissionAsync(subject, _project, _environment)
                : context.Request.Query.ContainsKey("approver")
                ? await capture.CaptureApproverUnderAdmissionAsync(subject, _project, _environment)
                : await capture.CaptureRequesterUnderAdmissionAsync(subject, _project, _environment);
            context.Response.StatusCode = _captured is null ? 403 : 204;
        }, resourceServices: services =>
        {
            services.AddSingleton<IDbContextFactory<ReleaseDbContext>>(rel); services.AddSingleton<IDbContextFactory<ApprovalDbContext>>(apr);
            services.AddSingleton<IDbContextFactory<ArtifactDbContext>>(art); services.AddSingleton<IDbContextFactory<AuditDbContext>>(aud);
            services.AddSingleton<IArtifactAuditTransactionFactory>(_clockFault);
            services.AddReleaseOwnerServices(); services.AddApprovalOwnerServices();
        });
        var user = _host.NewUserName(); var enrolled = await _host.EnrollAsync(user); Assert.Equal(LocalAuthenticationStatus.Success, enrolled.Status);
        var principal = enrolled.PrincipalId!.Value; var lease = new Lease();
        var roles = (RoleDefinitionStore)_host.Services.GetRequiredService<IRoleDefinitionReader>();
        var founder = await roles.PublishOfflineAsync(lease, Guid.NewGuid(), Guid.Empty, 0, "Synthetic REL founder",
            [new("org.manage", "organization"), new("membership.assign-role", "organization")]);
        var tenants = _host.Services.GetRequiredService<TenantMembershipService>();
        var created = await tenants.InitializeOfflineAsync(lease, Guid.NewGuid(), principal, "Synthetic REL tenant", founder.DefinitionId, 1);
        Assert.Equal("success", created.Status); _tenant = created.TenantId;
        await using (var db = await _host.Services.GetRequiredService<IDbContextFactory<ProjectDbContext>>().CreateDbContextAsync())
        { db.Projects.Add(new() { Id = _project, TenantId = _tenant, Name = "Synthetic REL project" }); await db.SaveChangesAsync(); }
        Guid member;
        await using (var db = await _host.Services.GetRequiredService<IDbContextFactory<TenantDbContext>>().CreateDbContextAsync())
            member = await db.Memberships.Where(x => x.TenantId == _tenant && x.PrincipalId == principal).Select(x => x.Id).SingleAsync();
        var role = await roles.PublishOfflineAsync(lease, Guid.NewGuid(), Guid.Empty, 0, "Synthetic REL project role", [new("release.read", "project"), new("release.create", "project"), new("release.retire", "project")]);
        Assert.Equal("success", (await tenants.GrantOfflineAsync(lease, Guid.NewGuid(), _tenant, member, 1, role.DefinitionId, 1, new("project", _project), null)).Status);
        await using (var db = await _host.Services.GetRequiredService<IDbContextFactory<ProjectDbContext>>().CreateDbContextAsync())
        {
            db.Applications.Add(new() { Id = _application, TenantId = _tenant, ProjectId = _project, Name = "Synthetic app" });
            db.Environments.Add(new() { Id = _environment, TenantId = _tenant, ProjectId = _project, Name = "Synthetic test", Classification = "Test" });
            await db.SaveChangesAsync();
            db.Associations.Add(new() { TargetId = _target, TenantId = _tenant, ProjectId = _project, EnvironmentId = _environment }); await db.SaveChangesAsync();
        }
        await using (var db = await _host.Services.GetRequiredService<IDbContextFactory<InventoryDbContext>>().CreateDbContextAsync())
        { db.Targets.Add(new() { Id = _target, TenantId = _tenant, ProjectId = _project, EnvironmentId = _environment, Name = "Synthetic target", Kind = "kubernetes" }); await db.SaveChangesAsync(); }
        await using (var db = await art.CreateDbContextAsync())
        {
            var registry = Guid.NewGuid(); var digest = "sha256:" + new string('a', 64); var config = "sha256:" + new string('b', 64);
            var platform = "{\"os\":\"linux\",\"architecture\":\"amd64\",\"variant\":null}"; var now = _host.Clock.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO app.art_artifacts (id,tenant_id,project_id,registry_id,repository,root_digest,platform_json,root_media_type,
                root_size_bytes,selected_manifest_digest,selected_manifest_size_bytes,selected_manifest_media_type,config_digest,config_size_bytes,
                observed_platform_json,variant_evidence,verifier_profile,verified_at_utc,created_at_utc)
                VALUES ({_artifact},{_tenant},{_project},{registry},{"synthetic/frontend"},{digest},{platform},{"application/vnd.oci.image.manifest.v1+json"},
                {100L},{digest},{100L},{"application/vnd.oci.image.manifest.v1+json"},{config},{50L},{platform},{"none"},{"artifact-registry-v1"},{now},{now})
                """);
        }
        var installation = _host.Services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;
        await using (var db = await rel.CreateDbContextAsync())
        { db.Lineages.Add(new() { Id = _release, InstallationId = installation, TenantId = _tenant, ProjectId = _project, CreatedAtUtc = _host.Clock.GetUtcNow() }); await db.SaveChangesAsync(); }
        _client = _host.CreateClient(); using var login = await _host.LoginAsync(_client, user, LocalAuthenticationTestHost.ValidPassword); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
    public async Task DisposeAsync() { _client?.Dispose(); if (_host is not null) await _host.DisposeAsync(); }
    [Fact]
    public async Task CaptureUsesRealSessionAndCurrentIamTenantAndProjectOwners()
    {
        using var missingGrant = await _client.GetAsync("/test-approval-capture?use=1");
        Assert.Equal(HttpStatusCode.Forbidden, missingGrant.StatusCode);
        Assert.NotNull(_approvalUse); Assert.Equal("denied", _approvalUse.Status); Assert.Null(_approvalUse.Ticket);
        var tenants = _host.Services.GetRequiredService<TenantMembershipService>();
        var roles = (RoleDefinitionStore)_host.Services.GetRequiredService<IRoleDefinitionReader>();
        Guid member;
        await using (var db = await _host.Services.GetRequiredService<IDbContextFactory<TenantDbContext>>().CreateDbContextAsync())
            member = await db.Memberships.Where(x => x.TenantId == _tenant).Select(x => x.Id).SingleAsync();
        var role = await roles.PublishOfflineAsync(new Lease(), Guid.NewGuid(), Guid.Empty, 0, "Synthetic approval role",
            [new("release.request-approval", "environment"), new("release.approve", "environment"), new("release.approval.read", "environment"), new("release.approval.revoke", "environment")]);
        Assert.Equal("success", (await tenants.GrantOfflineAsync(new Lease(), Guid.NewGuid(), _tenant, member, 2,
            role.DefinitionId, 1, new("environment", _environment), null)).Status);
        using var requester = await _client.GetAsync("/test-approval-capture");
        Assert.Equal(HttpStatusCode.NoContent, requester.StatusCode); Assert.NotNull(_captured);
        Assert.Equal("release.request-approval", _captured.Evidence.EnvironmentGrant.Permission);
        var requesterEvidence = _captured.Evidence;
        await using (var db = await _host.Services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContextAsync())
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var id = Guid.NewGuid(); var now = _host.Clock.GetUtcNow();
            new ApprovalRequestStore(db).StageRequest(new ApprovalRequestRecord
            {
                Id = id, InstallationId = _host.Services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId,
                TenantId = _tenant, ProjectId = _project, EnvironmentId = _environment, ReleaseRevisionId = Guid.NewGuid(),
                RequesterPrincipalId = requesterEvidence.Principal.PrincipalId, SpecHash = new byte[32],
                RequesterEvidence = ApprovalAuthorityEvidenceCodec.Encode(requesterEvidence), CreatedAtUtc = now, PendingExpiresAtUtc = now.AddMinutes(5)
            });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var stored = await db.Requests.AsNoTracking().SingleAsync(x => x.Id == id);
            var restored = ApprovalAuthorityEvidenceCodec.Decode(stored.RequesterEvidence, "requester", _tenant, _project, _environment);
            Assert.Equal(requesterEvidence, restored);
            var services = _host.Services;
            var persistedAuthority = new StoredApprovalAuthorityReader(new DurableApprovalAuthorityReader(
                new DurablePrincipalReader(services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>()),
                (IDurableGrantEvidenceReader)tenants, roles, services.GetRequiredService<IAuthorityClock>()));
            await using (var held = await services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>().AcquireReadAsync())
            {
                Assert.Equal(DurableApprovalAuthorityStatus.Current, await persistedAuthority.CheckUnderAdmissionAsync(
                    stored.RequesterEvidence, "requester", stored.TenantId, stored.ProjectId, stored.EnvironmentId, stored.RequesterPrincipalId));
                Assert.Equal(DurableApprovalAuthorityStatus.Unavailable, await persistedAuthority.CheckUnderAdmissionAsync(
                    stored.RequesterEvidence, "requester", stored.TenantId, stored.ProjectId, stored.EnvironmentId, Guid.NewGuid()));
            }
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        using var approver = await _client.GetAsync("/test-approval-capture?approver=1");
        Assert.Equal(HttpStatusCode.NoContent, approver.StatusCode); Assert.NotNull(_captured);
        Assert.Equal("release.approve", _captured.Evidence.EnvironmentGrant.Permission);
        using var revoker = await _client.GetAsync("/test-approval-capture?revoker=1");
        Assert.Equal(HttpStatusCode.NoContent, revoker.StatusCode); Assert.NotNull(_captured);
        Assert.Equal("release.approval.revoke", _captured.Evidence.EnvironmentGrant.Permission);
        Assert.Throws<ArgumentException>(() => ApprovalAuthorityEvidenceCodec.Encode(_captured.Evidence));
        using var use = await _client.GetAsync("/test-approval-capture?use=1");
        Assert.Equal(HttpStatusCode.NoContent, use.StatusCode); Assert.NotNull(_approvalUse?.Ticket);
        var consume = typeof(ApprovalUseTicket).GetMethod("TryConsume", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.True((bool)consume.Invoke(_approvalUse.Ticket, null)!);
        Assert.False((bool)consume.Invoke(_approvalUse.Ticket, null)!);
        await using (var audit = await _host.Services.GetRequiredService<IDbContextFactory<AuditDbContext>>().CreateDbContextAsync())
            Assert.True(await audit.Entries.AnyAsync(x => x.TenantRef == _tenant && x.Stage == "U" && x.Permission == "release.request-approval"));
        using var anonymous = _host.CreateClient(); using var denied = await anonymous.GetAsync("/test-approval-capture");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        _approvalAudit = new ApprovalUseAuditFault(false);
        using var setupFailure = await _client.GetAsync("/test-approval-capture?use=1");
        Assert.Equal("unavailable", _approvalUse!.Status); Assert.Null(_approvalUse.Ticket);
        var gate = _host.Services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>();
        await using (var held = await gate.AcquireReadAsync()) { }
        _approvalAudit = new ApprovalUseAuditFault(true);
        using var unknown = await _client.GetAsync("/test-approval-capture?use=1");
        Assert.Equal("unknown", _approvalUse!.Status); Assert.Null(_approvalUse.Ticket);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await gate.AcquireReadAsync(); });
    }
    private sealed class ApprovalReply : IApprovalResponseCommitter
    {
        public byte[]? Bytes;
        public ApprovalResponseCommitment? Commitment;
        public Task CommitAsync(ApprovalResponseCommitment selection, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Null(Bytes); Assert.True(bytes.Length <= selection.MaximumBytes);
            Assert.Equal(selection.PayloadSha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes.Span)));
            Bytes = bytes.ToArray(); Commitment = selection; return Task.CompletedTask;
        }
        public void Abort() { Bytes = null; }
    }
    private sealed class ApprovalUseAuditFault(bool unknownCommit) : IArtifactAuditTransactionFactory
    {
        public async ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct)
        {
            if (!unknownCommit) throw new InvalidOperationException("Synthetic precommit setup failure.");
            return new Unknown(await new ArtifactAuditTransactionFactory().EnlistAsync(connection, transaction, ct));
        }
        private sealed class Unknown(IArtifactAuditTransaction inner) : IArtifactAuditTransaction
        {
            public bool CommitAttempted => inner.CommitAttempted;
            public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct) => inner.StageAsync(intent, ct);
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct)
            {
                var result = await inner.CommitAsync(ct);
                Assert.Equal(AuditAppendStatus.Durable, result.Status);
                return AuditAppendOutcome.Unknown();
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
    [Fact]
    public async Task ReadUsesRealCookiePermissionAndSafePagination()
    {
        using var response = await _client.GetAsync(Root + "/releases?limit=1"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore); using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        AssertWire(json.RootElement, "LineageListEnvelope");
        Assert.Equal(_release, json.RootElement.GetProperty("items")[0].GetProperty("releaseId").GetGuid());
        using var absent = await _client.GetAsync(Root + "/release-revisions/" + Guid.NewGuid().ToString("D")); Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        using var invalid = await _client.GetAsync(Root + "/releases?tenantId=" + Guid.NewGuid()); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
    [Fact]
    public async Task CreateDraftUsesActualOwnerFactsAndReplaysOverHttps()
    {
        var content = JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid().ToString("D"), label = "Synthetic release",
            applicationId = _application.ToString("D"), environmentId = _environment.ToString("D"), targetId = _target.ToString("D"), artifactId = _artifact.ToString("D") });
        async Task<HttpResponseMessage> Send()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/releases");
            request.Headers.Add("Origin", LocalAuthenticationTestHost.Origin); request.Headers.Add("Idempotency-Key", "synthetic-release-create-1234");
            request.Headers.Add("X-Kordra-CSRF", await _host.GetCsrfTokenAsync(_client)); request.Content = new StringContent(content, Encoding.UTF8, "application/json");
            return await _client.SendAsync(request);
        }
        using var first = await Send(); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var json = JsonDocument.Parse(await first.Content.ReadAsByteArrayAsync());
        AssertWire(json.RootElement, "RevisionEnvelope");
        var revision = json.RootElement.GetProperty("revision"); AssertWire(revision, "Revision"); Assert.Equal("Draft", revision.GetProperty("recordedState").GetString());
        Assert.False(revision.GetProperty("executionAuthorized").GetBoolean()); Assert.Equal("policy_missing", revision.GetProperty("eligibilityReasonCode").GetString());
        var id = revision.GetProperty("releaseRevisionId").GetGuid();
        using var again = await Send(); Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        using var repeat = JsonDocument.Parse(await again.Content.ReadAsByteArrayAsync());
        Assert.Equal(id, repeat.RootElement.GetProperty("revision").GetProperty("releaseRevisionId").GetGuid());
        using var detail = await _client.GetAsync(Root + "/release-revisions/" + id.ToString("D")); Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var originalTag = detail.Headers.ETag!.ToString();
        async Task<HttpResponseMessage> Replace(string tag)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, Root + "/release-revisions/" + id.ToString("D") + "/draft");
            request.Headers.Add("Origin", LocalAuthenticationTestHost.Origin); request.Headers.Add("Idempotency-Key", "synthetic-release-replace-1234");
            request.Headers.Add("X-Kordra-CSRF", await _host.GetCsrfTokenAsync(_client)); request.Headers.TryAddWithoutValidation("If-Match", tag);
            request.Content = new StringContent(content, Encoding.UTF8, "application/json"); return await _client.SendAsync(request);
        }
        using var stale = await Replace($"\"rel-v1:{id:D}:2\""); Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var updated = await Replace(originalTag); Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal($"\"rel-v1:{id:D}:2\"", updated.Headers.ETag!.ToString());
        using var replayAfterEdit = await Send(); Assert.Equal(HttpStatusCode.OK, replayAfterEdit.StatusCode);
        using var latest = JsonDocument.Parse(await replayAfterEdit.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, latest.RootElement.GetProperty("revision").GetProperty("version").GetInt64());
    }
    [Fact]
    public async Task AnonymousAndForeignProjectRequestsDoNotLeakHistory()
    {
        using var anonymous = _host.CreateClient();
        using var response = await anonymous.GetAsync(Root + "/releases"); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var foreign = await _client.GetAsync($"/api/v1/projects/{Guid.NewGuid():D}/releases"); Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }
    [Theory]
    [InlineData("normal")]
    [InlineData("lost-reply")]
    [InlineData("grant-reject-race")]
    [InlineData("audit-stage-failure")]
    public async Task FreezeAndRetireUseActualPolicyOwnersAndImmutableSubjectOverHttps(string scenario)
    {
        var loseMutationReply = scenario == "lost-reply";
        await SeedPolicy();
        var content = JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid().ToString("D"), label = "Synthetic release",
            applicationId = _application.ToString("D"), environmentId = _environment.ToString("D"), targetId = _target.ToString("D"), artifactId = _artifact.ToString("D") });
        async Task<HttpResponseMessage> Send(string route, HttpMethod method, string key, string body, string? tag = null)
        {
            using var request = new HttpRequestMessage(method, route); request.Headers.Add("Origin", LocalAuthenticationTestHost.Origin);
            request.Headers.Add("Idempotency-Key", key); request.Headers.Add("X-Kordra-CSRF", await _host.GetCsrfTokenAsync(_client));
            if (tag is not null) request.Headers.TryAddWithoutValidation("If-Match", tag);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json"); return await _client.SendAsync(request);
        }
        using var created = await Send(Root + "/releases", HttpMethod.Post, "synthetic-create-release-1234", content);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); using var draft = JsonDocument.Parse(await created.Content.ReadAsByteArrayAsync());
        var rev = draft.RootElement.GetProperty("revision"); Assert.Equal("eligible", rev.GetProperty("selectionStatus").GetString());
        var id = rev.GetProperty("releaseRevisionId").GetGuid(); var selectionHash = rev.GetProperty("selectionHash").GetString();
        var services = _host.Services;
        var subjectReader = new ApprovalSubjectReader(services.GetRequiredService<IDbContextFactory<ReleaseDbContext>>(),
            services.GetRequiredService<ReleaseSelectionReader>());
        var installation = services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;
        var gate = services.GetRequiredService<FairAsyncReaderWriterAdmissionGate>();
        await using (var held = await gate.AcquireReadAsync())
            Assert.Equal("not_frozen", (await subjectReader.ReadEligibleUnderAdmissionAsync(installation, _tenant, _project, _environment, id, _host.Clock.GetUtcNow())).Status);
        var freeze = JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), expectedSelectionHash = selectionHash });
        using var frozen = await Send(Root + $"/release-revisions/{id:D}/freeze", HttpMethod.Post, "synthetic-freeze-release-1234", freeze, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, frozen.StatusCode); using var frozenBody = JsonDocument.Parse(await frozen.Content.ReadAsByteArrayAsync());
        var exact = frozenBody.RootElement.GetProperty("revision"); Assert.Equal("Frozen", exact.GetProperty("recordedState").GetString());
        var hash = exact.GetProperty("specHash").GetString(); Assert.NotNull(hash); AssertWire(exact, "Revision"); Assert.False(exact.GetProperty("executionAuthorized").GetBoolean());
        await using (var held = await gate.AcquireReadAsync())
        {
            var subject = await subjectReader.ReadEligibleUnderAdmissionAsync(installation, _tenant, _project, _environment, id, _host.Clock.GetUtcNow());
            Assert.Equal("eligible", subject.Status); Assert.NotNull(subject.Policy); Assert.Equal(hash, subject.Revision!.SpecHash);
            Assert.Equal("absent", (await subjectReader.ReadEligibleUnderAdmissionAsync(installation, Guid.NewGuid(), _project, _environment, id, _host.Clock.GetUtcNow())).Status);
            Assert.Equal("absent", (await subjectReader.ReadEligibleUnderAdmissionAsync(installation, _tenant, _project, Guid.NewGuid(), id, _host.Clock.GetUtcNow())).Status);
        }
        var readiness = new ApprovalSchemaReadiness(installation, services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>(), services.GetRequiredService<IReleaseSchemaReadiness>());
        Assert.True(await readiness.CheckAsync(default));
        var tenants = services.GetRequiredService<TenantMembershipService>();
        var roles = (RoleDefinitionStore)services.GetRequiredService<IRoleDefinitionReader>();
        var role = await roles.PublishOfflineAsync(new Lease(), Guid.NewGuid(), Guid.Empty, 0, "Synthetic full approval role",
            [new("release.request-approval", "environment"), new("release.approve", "environment"), new("release.approval.read", "environment"), new("release.approval.revoke", "environment")]);
        await using (var tenantDb = await services.GetRequiredService<IDbContextFactory<TenantDbContext>>().CreateDbContextAsync())
        {
            var member = await tenantDb.Memberships.SingleAsync(x => x.TenantId == _tenant);
            Assert.Equal("success", (await tenants.GrantOfflineAsync(new Lease(), Guid.NewGuid(), _tenant, member.Id, member.Revision,
                role.DefinitionId, 1, new("environment", _environment), null)).Status);
        }
        var approvalRoot = $"/api/v1/environments/{_environment:D}/release-approvals";
        using var absentSubject = await Send(approvalRoot, HttpMethod.Post, "synthetic-missing-subject-1234",
            JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), releaseRevisionId = Guid.NewGuid() }));
        Assert.Equal(HttpStatusCode.NotFound, absentSubject.StatusCode);
        using var approval = await Send(approvalRoot, HttpMethod.Post, "synthetic-approval-key-1234",
            JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), releaseRevisionId = id }));
        Assert.Equal(HttpStatusCode.Created, approval.StatusCode); Assert.True(approval.Headers.CacheControl?.NoStore);
        using var approvalDocument = JsonDocument.Parse(await approval.Content.ReadAsByteArrayAsync());
        AssertWire(approvalDocument.RootElement, "ApprovalEnvelope", "release-approval-v1");
        var approvalBody = approvalDocument.RootElement.GetProperty("approval");
        AssertWire(approvalBody, "Approval", "release-approval-v1");
        AssertWire(approvalBody.GetProperty("subject"), "ReleaseIntent", "release-approval-v1");
        Assert.Equal("Pending", approvalBody.GetProperty("effectiveState").GetString());
        Assert.False(approvalBody.GetProperty("executionAuthorized").GetBoolean());
        Assert.False(approvalBody.TryGetProperty("requesterEvidence", out _));
        Assert.False(approvalBody.TryGetProperty("session", out _));
        var approvalId = approvalBody.GetProperty("requestId").GetGuid();
        using var duplicate = await Send(approvalRoot, HttpMethod.Post, "synthetic-duplicate-key-1234",
            JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), releaseRevisionId = id }));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var invalidQuery = await _client.GetAsync(approvalRoot + "?tenantId=" + _tenant.ToString("D"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidQuery.StatusCode);
        using var missing = await _client.GetAsync(approvalRoot + "/" + Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (var action in new[] { "grant", "reject", "revoke" })
        {
            var absentId = Guid.NewGuid();
            using var missingMutation = await Send(approvalRoot + "/" + absentId.ToString("D") + "/" + action,
                HttpMethod.Post, "synthetic-missing-" + action + "-1234",
                JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), reasonCode = action == "grant" ? "approved" : action == "reject" ? "changes_required" : "owner_cancelled" }), $"\"apr-v1:{absentId:D}:1\"");
            Assert.Equal(HttpStatusCode.NotFound, missingMutation.StatusCode);
        }
        using var foreign = await _client.GetAsync($"/api/v1/environments/{Guid.NewGuid():D}/release-approvals");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var anonymous = _host.CreateClient(); using var unauthenticated = await anonymous.GetAsync(approvalRoot);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var noCsrf = await _client.PostAsync(approvalRoot, new StringContent(JsonSerializer.Serialize(new
            { schemaVersion = 1, correlationId = Guid.NewGuid(), releaseRevisionId = id }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using var queue = await _client.GetAsync(approvalRoot);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        using (var queueBody = JsonDocument.Parse(await queue.Content.ReadAsByteArrayAsync()))
        {
            AssertWire(queueBody.RootElement, "ApprovalListEnvelope", "release-approval-v1");
            var item = Assert.Single(queueBody.RootElement.GetProperty("items").EnumerateArray());
            AssertWire(item, "ApprovalListItem", "release-approval-v1");
            Assert.Equal(approvalId, item.GetProperty("requestId").GetGuid()); Assert.False(item.TryGetProperty("subject", out _));
            Assert.False(item.TryGetProperty("requesterEvidence", out _)); Assert.Equal("Pending", item.GetProperty("effectiveState").GetString());
        }
        using var detail = await _client.GetAsync(approvalRoot + "/" + approvalId.ToString("D"));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode); Assert.NotNull(detail.Headers.ETag);
        using var replay = await _client.GetAsync("/test-approval-capture?mutation=1&revision=" + id.ToString("D"));
        Assert.Equal("replay", _approvalMutation?.Status); Assert.Equal(approvalId, _approvalMutation!.RequestId);
        Assert.Equal("completed", _approvalDisclosure); Assert.Equal(200, _approvalReply!.Commitment!.StatusCode);
        using var wire = await _client.GetAsync("/test-approval-capture?mutation=1&wire=1&execute=1&revision=" + id.ToString("D"));
        Assert.Equal(HttpStatusCode.OK, wire.StatusCode); Assert.Equal("completed", _approvalDisclosure);
        Assert.True(wire.Headers.CacheControl?.NoStore); Assert.NotNull(wire.Headers.ETag);
        using (var wireBody = JsonDocument.Parse(await wire.Content.ReadAsByteArrayAsync()))
        {
            Assert.Equal(approvalId, wireBody.RootElement.GetProperty("approval").GetProperty("requestId").GetGuid());
            Assert.Equal("Pending", wireBody.RootElement.GetProperty("approval").GetProperty("effectiveState").GetString());
            Assert.Equal(new[] { "approval", "correlationId", "schemaVersion" }, wireBody.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());
        }
        using var stale = await _client.GetAsync($"/test-approval-capture?mutation=1&action=grant&request={approvalId:D}&version=2");
        Assert.Equal("precondition", _approvalMutation?.Status);
        var durableAuthority = new StoredApprovalAuthorityReader(new DurableApprovalAuthorityReader(
            new DurablePrincipalReader(services.GetRequiredService<IDbContextFactory<KordraIdentityDbContext>>()),
            (IDurableGrantEvidenceReader)tenants, roles, services.GetRequiredService<IAuthorityClock>()));
        var eligibility = new ApprovalEligibilityReader(subjectReader, durableAuthority, services.GetRequiredService<IAuthorityClock>());
        async Task<ApprovalEligibilityResult> Effective()
        {
            await using var held = await gate.AcquireReadAsync();
            await using var db = await services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContextAsync();
            return await eligibility.ReadUnderAdmissionAsync(await db.Requests.AsNoTracking().SingleAsync(x => x.Id == approvalId), installation, _tenant, _project, _environment);
        }
        Assert.Equal(ApprovalEffectiveStatus.Pending, (await Effective()).Effective.Status);
        if (scenario == "grant-reject-race")
        {
            var grantRace = Send(approvalRoot + $"/{approvalId:D}/grant", HttpMethod.Post, "synthetic-race-grant-1234",
                JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), reasonCode = "approved" }), $"\"apr-v1:{approvalId:D}:1\"");
            var rejectRace = Send(approvalRoot + $"/{approvalId:D}/reject", HttpMethod.Post, "synthetic-race-reject-1234",
                JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), reasonCode = "changes_required" }), $"\"apr-v1:{approvalId:D}:1\"");
            var results = await Task.WhenAll(grantRace, rejectRace);
            try
            {
                Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
                Assert.Single(results, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
                await using var db = await services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContextAsync();
                var stored = await db.Requests.SingleAsync(x => x.Id == approvalId);
                Assert.Equal(2, stored.Version); Assert.Contains(stored.State, new[] { "Granted", "Rejected" });
                Assert.Equal(1, await db.Decisions.CountAsync(x => x.RequestId == approvalId));
                Assert.Equal(2, await db.RequestCommands.CountAsync(x => x.RequestId == approvalId));
                var winner = results[0].StatusCode == HttpStatusCode.OK ? "grant" : "reject";
                await VerifyApprovalRecoveryAsync(services, installation, approvalId, approvalRoot + $"/{approvalId:D}/" + winner, winner);
            }
            finally { foreach (var result in results) result.Dispose(); }
            return;
        }
        _clockFault.LoseApprovalMutationReply = loseMutationReply;
        _clockFault.FailApprovalMutationStage = scenario == "audit-stage-failure";
        var grantTask = Send(approvalRoot + $"/{approvalId:D}/grant", HttpMethod.Post, "synthetic-approval-key-1234",
            JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), reasonCode = "approved" }), $"\"apr-v1:{approvalId:D}:1\"");
        if (scenario == "audit-stage-failure")
        {
            using var failed = await grantTask;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            await using (var held = await gate.AcquireReadAsync()) { }
            await using (var db = await services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContextAsync())
            {
                var stored = await db.Requests.SingleAsync(x => x.Id == approvalId);
                Assert.Equal("Pending", stored.State); Assert.Equal(1, stored.Version);
                Assert.Equal(0, await db.Decisions.CountAsync(x => x.RequestId == approvalId));
                Assert.Equal(1, await db.RequestCommands.CountAsync(x => x.RequestId == approvalId));
            }
            await VerifyApprovalRecoveryAsync(services, installation, approvalId, approvalRoot + $"/{approvalId:D}/grant", "grant", false);
            return;
        }
        if (loseMutationReply)
        {
            await Assert.ThrowsAsync<HttpRequestException>(async () => { using var unexpected = await grantTask; });
            Assert.NotNull(_clockFault.Transport); Assert.True(_clockFault.Transport.SentCommit); Assert.True(_clockFault.Transport.DiscardedCommitResponse);
            Assert.True(_clockFault.ApprovalCommitAttempted);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var held = await gate.AcquireReadAsync(); });
            await using (var db = await services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContextAsync())
            {
                var stored = await db.Requests.SingleAsync(x => x.Id == approvalId);
                Assert.Equal("Granted", stored.State); Assert.Equal(2, stored.Version);
                Assert.Equal(1, await db.Decisions.CountAsync(x => x.RequestId == approvalId));
                Assert.Equal(2, await db.RequestCommands.CountAsync(x => x.RequestId == approvalId));
            }
            await VerifyApprovalRecoveryAsync(services, installation, approvalId, approvalRoot + $"/{approvalId:D}/grant", "grant");
            return;
        }
        using var grant = await grantTask;
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        Assert.Equal(ApprovalEffectiveStatus.Granted, (await Effective()).Effective.Status);
        await using (var held = await gate.AcquireWriteAsync())
        await using (var project = await services.GetRequiredService<IDbContextFactory<ProjectDbContext>>().CreateDbContextAsync())
        {
            var environment = await project.Environments.SingleAsync(x => x.Id == _environment);
            environment.Revision++; await project.SaveChangesAsync();
        }
        Assert.Equal(ApprovalEffectiveStatus.Invalidated, (await Effective()).Effective.Status);
        using var revoke = await Send(approvalRoot + $"/{approvalId:D}/revoke", HttpMethod.Post, "synthetic-approval-key-1234",
            JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), reasonCode = "owner_cancelled" }), $"\"apr-v1:{approvalId:D}:2\"");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        await using (var approvalDb = await services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContextAsync())
        {
            var stored = await approvalDb.Requests.SingleAsync(x => x.Id == approvalId);
            Assert.Equal("Revoked", stored.State); Assert.Equal(3, stored.Version);
            Assert.Equal(2, await approvalDb.Decisions.CountAsync(x => x.RequestId == approvalId));
            Assert.Equal(3, await approvalDb.RequestCommands.CountAsync(x => x.RequestId == approvalId));
        }
        Assert.True(await readiness.CheckAsync(default));
        Assert.Equal(ApprovalEffectiveStatus.Revoked, (await Effective()).Effective.Status);
        using (var payload = JsonDocument.Parse(await revoke.Content.ReadAsByteArrayAsync()))
            Assert.Equal("Revoked", payload.RootElement.GetProperty("approval").GetProperty("effectiveState").GetString());
        using var terminalReject = await Send(approvalRoot + $"/{approvalId:D}/reject", HttpMethod.Post, "synthetic-terminal-key-1234",
            JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), reasonCode = "changes_required" }), $"\"apr-v1:{approvalId:D}:3\"");
        Assert.Equal(HttpStatusCode.Conflict, terminalReject.StatusCode);
        using (var error = JsonDocument.Parse(await terminalReject.Content.ReadAsByteArrayAsync()))
            AssertWire(error.RootElement, "ErrorEnvelope", "release-approval-v1");
        var retire = JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), reasonCode = "obsolete" });
        await using (var held = await gate.AcquireReadAsync())
            Assert.Equal("subject_changed", (await subjectReader.ReadEligibleUnderAdmissionAsync(installation, _tenant, _project, _environment, id, _host.Clock.GetUtcNow())).Status);
        using var retired = await Send(Root + $"/release-revisions/{id:D}/retire", HttpMethod.Post, "synthetic-retire-release-1234", retire, frozen.Headers.ETag!.ToString());
        await using var diagnosticRel = await services.GetRequiredService<IDbContextFactory<ReleaseDbContext>>().CreateDbContextAsync();
        var diagnosticState = await diagnosticRel.Revisions.AsNoTracking().Where(x => x.Id == id).Select(x => x.State).SingleAsync();
        Assert.True(retired.StatusCode == HttpStatusCode.OK, "Retire state=" + diagnosticState + "; response: " + await retired.Content.ReadAsStringAsync()); using var retiredBody = JsonDocument.Parse(await retired.Content.ReadAsByteArrayAsync());
        Assert.Equal("Retired", retiredBody.RootElement.GetProperty("revision").GetProperty("recordedState").GetString());
        Assert.Equal(hash, retiredBody.RootElement.GetProperty("revision").GetProperty("specHash").GetString());
        using var retiredSubject = await Send(approvalRoot, HttpMethod.Post, "synthetic-retired-subject-1234",
            JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), releaseRevisionId = id }));
        Assert.Equal(HttpStatusCode.Conflict, retiredSubject.StatusCode);
        await using (var held = await gate.AcquireReadAsync())
            Assert.Equal("not_frozen", (await subjectReader.ReadEligibleUnderAdmissionAsync(installation, _tenant, _project, _environment, id, _host.Clock.GetUtcNow())).Status);
        _clockFault.AfterApprovalDisclosure = () => _host.Clock.Advance(TimeSpan.FromMinutes(30));
        using var expiredDisclosure = await _client.GetAsync($"/test-approval-capture?mutation=1&action=revoke&request={approvalId:D}&version=2");
        Assert.Equal("replay", _approvalMutation?.Status); Assert.Equal("denied", _approvalDisclosure);
        Assert.NotNull(_approvalReply); Assert.Null(_approvalReply.Bytes);
        await using (var held = await gate.AcquireReadAsync()) { }
        var incompleteInstallation = Guid.NewGuid();
        await using (var incomplete = await services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContextAsync())
        await using (var transaction = await incomplete.Database.BeginTransactionAsync())
        {
            new ApprovalRequestStore(incomplete).StageRequest(new ApprovalRequestRecord
            {
                Id = Guid.NewGuid(), InstallationId = incompleteInstallation, TenantId = _tenant, ProjectId = _project,
                EnvironmentId = _environment, ReleaseRevisionId = Guid.NewGuid(), RequesterPrincipalId = Guid.NewGuid(),
                SpecHash = new byte[32], RequesterEvidence = [1], CreatedAtUtc = _host.Clock.GetUtcNow(), PendingExpiresAtUtc = _host.Clock.GetUtcNow().AddMinutes(1)
            });
            await incomplete.SaveChangesAsync(); await transaction.CommitAsync();
        }
        // Deliberately incomplete synthetic history is retained only in kordra_test under its own installation ID.
        Assert.False(await new ApprovalSchemaReadiness(incompleteInstallation, services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>(),
            new ReleaseSchemaReadiness(incompleteInstallation, services.GetRequiredService<IDbContextFactory<ReleaseDbContext>>())).CheckAsync(default));
        await using (var incomplete = await services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContextAsync())
        await using (var transaction = await incomplete.Database.BeginTransactionAsync())
        {
            var row = await incomplete.Requests.SingleAsync(x => x.InstallationId == incompleteInstallation);
            await new ApprovalRequestReceiptStore(incomplete).StageAsync(Guid.NewGuid(), incompleteInstallation, _tenant, _project,
                _environment, row.RequesterPrincipalId, row.Id, Guid.NewGuid(), Guid.NewGuid(), "request", new byte[32],
                new byte[32], Guid.NewGuid(), new byte[32], row.CreatedAtUtc);
            await incomplete.SaveChangesAsync(); await transaction.CommitAsync();
        }
        Assert.False(await new ApprovalSchemaReadiness(incompleteInstallation, services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>(),
            new ReleaseSchemaReadiness(incompleteInstallation, services.GetRequiredService<IDbContextFactory<ReleaseDbContext>>())).CheckAsync(default));
        await VerifyApprovalRecoveryAsync(services, installation, approvalId, approvalRoot, "request");
    }
    private async Task VerifyApprovalRecoveryAsync(IServiceProvider services, Guid installation, Guid approvalId, string resource, string action, bool committed = true)
    {
        ApprovalRequestCommandReceipt recoveryReceipt;
        await using (var db = await services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContextAsync())
            recoveryReceipt = await db.RequestCommands.AsNoTracking().SingleAsync(x => x.RequestId == approvalId && x.Action == (committed ? action : "request"));
        var recoveryIdentity = new PooledDbContextFactory<KordraIdentityDbContext>(new DbContextOptionsBuilder<KordraIdentityDbContext>()
            .UseNpgsql(TestDatabaseConfiguration.TryGetConnectionString()!, p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
        await _host.StopForOfflineFixtureAsync();
        var plan = new ApprovalRecoveryPlan(1, Guid.NewGuid(), installation, recoveryReceipt.BootId, 12345, _tenant,
            recoveryReceipt.PrincipalId, action, resource, Convert.ToHexStringLower(recoveryReceipt.ScopedKeyHash),
            Convert.ToHexStringLower(recoveryReceipt.BusinessHash), null, null);
        if (!committed)
        {
            Assert.Equal("grant", action);
            var command = ApprovalRequestParser.Parse("grant", _environment.ToString("D"), approvalId.ToString("D"),
                JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = Guid.NewGuid(), reasonCode = "approved" }),
                $"\"apr-v1:{approvalId:D}:1\"");
            plan = plan with { ScopedKeyHash = Kordra.ReleaseManagement.Domain.ReleaseCommandHash.ScopedKey(installation, _tenant,
                recoveryReceipt.PrincipalId, action, resource, "synthetic-approval-key-1234"), BusinessHash = command.BusinessHash };
        }
        var custody = new RecoveryCustody();
        var recovery = new OfflineApprovalRecoveryCoordinator(installation, recoveryIdentity, new RecoveryProcesses(true));
        Assert.Equal("denied", (await new OfflineApprovalRecoveryCoordinator(installation, recoveryIdentity, new RecoveryProcesses(false))
            .InspectAsync(custody, plan.CanonicalBytes())).Status);
        var inspected = await recovery.InspectAsync(custody, plan.CanonicalBytes());
        Assert.Equal(committed ? "confirmed" : "no_commit", inspected.Status); Assert.NotNull(inspected.BoundPlan);
        Assert.Equal(committed ? "confirmed" : "no_commit", (await recovery.ApplyAsync(custody, inspected.BoundPlan.CanonicalBytes(), inspected.BoundPlan.Hash())).Status);
        Assert.Equal("denied", (await recovery.ApplyAsync(custody, inspected.BoundPlan.CanonicalBytes(), new string('0', 64))).Status);
        if (committed) Assert.Equal("unknown", (await recovery.InspectAsync(custody, (plan with { BusinessHash = new string('0', 64) }).CanonicalBytes())).Status);
        var noCommit = plan with { ScopedKeyHash = Kordra.ReleaseManagement.Domain.ReleaseCommandHash.ScopedKey(installation,
            _tenant, recoveryReceipt.PrincipalId, action, resource, "synthetic-no-commit-key-1234") };
        var absent = await recovery.InspectAsync(custody, noCommit.CanonicalBytes());
        Assert.Equal("no_commit", absent.Status); Assert.NotNull(absent.BoundPlan);
        Assert.Equal("no_commit", (await recovery.ApplyAsync(custody, absent.BoundPlan.CanonicalBytes(), absent.BoundPlan.Hash())).Status);
        if (OperatingSystem.IsMacOS() && committed && action == "request")
        {
            var workspace = new DirectoryInfo(AppContext.BaseDirectory);
            while (workspace is not null && !File.Exists(Path.Combine(workspace.FullName, "Kordra.slnx"))) workspace = workspace.Parent;
            Assert.NotNull(workspace);
            var root = Path.Combine(workspace.FullName, ".amr-local", "t11-design", "native-approval-recovery-" + Guid.NewGuid().ToString("N"));
            var instance = Path.Combine(root, installation.ToString("D")); Directory.CreateDirectory(instance);
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(instance, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var identityPath = Path.Combine(root, "installation.id"); File.WriteAllText(identityPath, installation.ToString("D"));
            File.SetUnixFileMode(identityPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var owner = typeof(LocalApprovalRecoveryCommand).Assembly.GetType("Kordra.Api.OfflineArtifactInstallation", true)!;
            var acquire = owner.GetMethod("AcquireFromRoot", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            try
            {
                using var held = Assert.IsAssignableFrom<IDisposable>(acquire.Invoke(null, [root]));
                var nativeCustody = Assert.IsAssignableFrom<IOfflineProvisioningLease>(held);
                using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/cat")
                    { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })
                    ?? throw new InvalidOperationException("Synthetic custody process failed to start.");
                try
                {
                    var nativePlan = plan with { OldProcessId = child.Id };
                    var nativeRecovery = new OfflineApprovalRecoveryCoordinator(installation, recoveryIdentity, new ArtifactProcessQuiescence());
                    Assert.Equal("denied", (await nativeRecovery.InspectAsync(nativeCustody, nativePlan.CanonicalBytes())).Status);
                    child.StandardInput.Close(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    var checkedPlan = await nativeRecovery.InspectAsync(nativeCustody, nativePlan.CanonicalBytes());
                    Assert.Equal("confirmed", checkedPlan.Status); Assert.NotNull(checkedPlan.BoundPlan);
                    Assert.Equal("confirmed", (await nativeRecovery.ApplyAsync(nativeCustody, checkedPlan.BoundPlan.CanonicalBytes(), checkedPlan.BoundPlan.Hash())).Status);
                }
                finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }
    // The API fixture is stopped; component-only variants below explicitly use custody/process doubles.
    // The normal macOS variant additionally verifies isolated native custody and real child-process quiescence.
    // This is not an installed-command test.
    // The API fixture is stopped; these are explicit custody/process doubles, not proof of native installation custody.
    private sealed class RecoveryCustody : IOfflineProvisioningLease { public void RequireHeld() { } }
    private sealed class RecoveryProcesses(bool stopped) : IArtifactProcessQuiescence { public bool IsQuiescent(int processId) => stopped && processId == 12345; }
    private async Task SeedPolicy()
    {
        var installation = _host.Services.GetRequiredService<LocalAuthenticationConfiguration>().InstallationId;
        var manifest = new { schemaVersion = 1, manifestId = Guid.NewGuid().ToString("D"), installationId = installation.ToString("D"),
            expectedDatabase = "kordra_test", expectedRole = "kordra_admin", tenantId = _tenant.ToString("D"), projectId = _project.ToString("D"),
            policyId = Guid.NewGuid().ToString("D"), expectedPolicyRevision = 0, payload = new { schemaVersion = 1,
                purpose = "release-intent-v1", status = "active", effectiveFromUtc = "2026-10-27T00:00:00.000000Z", expiresAtUtc = (string?)null,
                rules = new[] { new { classification = "Test", effect = "allow", separationOfDuties = "self_allowed", allowUnattestedProvenance = true,
                    pendingLifetimeSeconds = 3600, grantLifetimeSeconds = 300, blockedWindowsUtc = Array.Empty<object>() } } },
            bindingChanges = new[] { new { environmentId = _environment.ToString("D"), expectedEnvironmentRevision = 1,
                expectedBindingId = (string?)null, expectedBindingRevision = 0 } } };
        var selected = ApprovalPolicyManifest.Parse(JsonSerializer.SerializeToUtf8Bytes(manifest));
        await using var db = await _host.Services.GetRequiredService<IDbContextFactory<ApprovalDbContext>>().CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var now = _host.Clock.GetUtcNow(); now = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        Assert.True(await new ApprovalPolicyStore(db).StagePublicationAsync(new ApprovalLease(), selected,
            [new ApprovalEnvironmentEvidence(_tenant, _project, _environment, 1, 1, "Test")], now));
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    private sealed class ApprovalLease : IOfflineApprovalCustody { public void RequireHeld() { } }

    [Fact]
    public async Task AuthenticatedUserWithoutScopedGrantCannotCreateOrRead()
    {
        var other = _host.NewUserName(); Assert.Equal(LocalAuthenticationStatus.Success, (await _host.EnrollAsync(other)).Status);
        using var client = _host.CreateClient(); using var login = await _host.LoginAsync(client, other, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var read = await client.GetAsync(Root + "/releases"); Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/releases");
        request.Headers.Add("Origin", LocalAuthenticationTestHost.Origin); request.Headers.Add("Idempotency-Key", "synthetic-unauthorized-1234");
        request.Headers.Add("X-Kordra-CSRF", await _host.GetCsrfTokenAsync(client));
        request.Content = new StringContent(JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), label = "Synthetic",
            applicationId = _application, environmentId = _environment, targetId = _target, artifactId = _artifact }), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [Fact]
    public async Task MutationRequiresCsrfAndDoesNotAcceptClientTenant()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/releases");
        request.Headers.Add("Origin", LocalAuthenticationTestHost.Origin); request.Headers.Add("Idempotency-Key", "synthetic-release-http-1234");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var denied = await _client.SendAsync(request); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var invalid = new HttpRequestMessage(HttpMethod.Post, Root + "/releases");
        invalid.Headers.Add("Origin", LocalAuthenticationTestHost.Origin); invalid.Headers.Add("Idempotency-Key", "synthetic-release-http-1234");
        invalid.Headers.Add("X-Kordra-CSRF", await _host.GetCsrfTokenAsync(_client));
        invalid.Content = new StringContent(JsonSerializer.Serialize(new { schemaVersion = 1, correlationId = Guid.NewGuid(), tenantId = _tenant }), Encoding.UTF8, "application/json");
        using var result = await _client.SendAsync(invalid); Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }
    [Fact]
    public async Task HttpsRejectsOversizedMutationAndInvalidPagination()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/releases");
        request.Headers.Add("Origin", LocalAuthenticationTestHost.Origin);
        request.Headers.Add("Idempotency-Key", "synthetic-oversized-request-1234");
        request.Headers.Add("X-Kordra-CSRF", await _host.GetCsrfTokenAsync(_client));
        request.Content = new StringContent(new string('x', ReleaseRequestParser.MaximumBodyBytes + 1), Encoding.UTF8, "application/json");
        using var tooLarge = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.True(tooLarge.Headers.CacheControl?.NoStore);
        foreach (var query in new[] { "?limit=51", "?afterId=not-a-guid", "?limit=1&limit=2" })
        {
            using var invalid = await _client.GetAsync(Root + "/releases" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.True(invalid.Headers.CacheControl?.NoStore);
        }
    }
    [Fact]
    public async Task HttpsWithholdsBodyWhenAuthorityExpiresAfterDisclosureAudit()
    {
        _clockFault.AfterDisclosure = () => _host.Clock.Advance(TimeSpan.FromMinutes(30));
        using var response = await _client.GetAsync(Root + "/releases?limit=1");
        Assert.True(_clockFault.Fired);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("request_denied", body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("items", out _));
    }
    private sealed class DisclosureClockFault : IArtifactAuditTransactionFactory
    {
        public Action? AfterDisclosure;
        public Action? AfterApprovalDisclosure;
        public bool Fired;
        public bool LoseApprovalMutationReply, ApprovalCommitAttempted, FailApprovalMutationStage;
        public ReleaseMutationTransactionTests.CommitReplyStream? Transport;
        public async ValueTask<IArtifactAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct)
            => new Wrapped(await new ArtifactAuditTransactionFactory().EnlistAsync(connection, transaction, ct), this, connection);
        private sealed class Wrapped(IArtifactAuditTransaction inner, DisclosureClockFault owner, DbConnection connection) : IArtifactAuditTransaction
        {
            private bool _disclosure;
            private bool _approvalDisclosure;
            private bool _approvalMutation;
            public bool CommitAttempted => inner.CommitAttempted;
            public Task<AuditTransactionStageOutcome> StageAsync(AuditIntent intent, CancellationToken ct)
            { _disclosure = intent.Stage == AuditIntentStage.Disclosure && intent.Permission == "release.read";
              _approvalDisclosure = intent.Stage == AuditIntentStage.Disclosure && intent.SafeReason.StartsWith("approval-d-", StringComparison.Ordinal);
              _approvalMutation = intent.Stage == AuditIntentStage.Mutation && intent.SafeReason.StartsWith("approval-m-", StringComparison.Ordinal);
              if (_approvalMutation && owner.FailApprovalMutationStage)
              { owner.FailApprovalMutationStage = false; throw new OperationCanceledException("Synthetic precommit audit-stage failure."); }
              return inner.StageAsync(intent, ct); }
            public async Task<AuditAppendOutcome> CommitAsync(CancellationToken ct)
            {
                AuditAppendOutcome result;
                if (_approvalMutation && owner.LoseApprovalMutationReply)
                {
                    owner.LoseApprovalMutationReply = false;
                    owner.Transport = ReleaseMutationTransactionTests.CommitReplyStream.Install(connection);
                    try { result = await inner.CommitAsync(ct); }
                    finally { owner.ApprovalCommitAttempted = inner.CommitAttempted; }
                }
                else result = await inner.CommitAsync(ct);
                if (_disclosure && result.Status == AuditAppendStatus.Durable && owner.AfterDisclosure is { } callback)
                { owner.AfterDisclosure = null; owner.Fired = true; callback(); }
                if (_approvalDisclosure && result.Status == AuditAppendStatus.Durable && owner.AfterApprovalDisclosure is { } approvalCallback)
                { owner.AfterApprovalDisclosure = null; approvalCallback(); }
                return result;
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
    private static void AssertWire(JsonElement value, string schema, string wire = "release-intent-v1")
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kordra.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root!.FullName, $"docs/contracts/fixtures/{wire}.http.json")));
        using var openapi = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, $"docs/contracts/openapi/{wire}.openapi.json")));
        var actual = value.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
        var fields = fixture.RootElement.GetProperty("schemaFields").GetProperty(schema).EnumerateArray().Select(x => x.GetString()).Order(StringComparer.Ordinal).ToArray();
        var definition = openapi.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema);
        Assert.Equal(fields, actual); Assert.Equal(definition.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order(StringComparer.Ordinal).ToArray(), actual);
        Assert.False(definition.GetProperty("additionalProperties").GetBoolean());
        foreach (var source in openapi.RootElement.GetProperty("x-working-tree-source-sha256").EnumerateObject())
            Assert.Equal(source.Value.GetString(), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root.FullName, source.Name)))));
    }
    private sealed class Lease : IOfflineProvisioningLease { public void RequireHeld() { } }
}
