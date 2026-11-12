using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Api;
using Kordra.ApprovalGovernance.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ApprovalGovernance;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.InfrastructureInventory;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.Modules.TenantManagement;
using Kordra.Modules.TenantManagement.Contracts;
using Kordra.Modules.TenantManagement.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.ReleaseManagement.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>Dedicated synthetic owner setup. Never registered in Product or used outside its frozen fixture.</summary>
internal sealed class WorkflowStartOwnerFixture : IAsyncDisposable
{
    internal static readonly Guid CaseId = Guid.Parse("d8fc8aef-fcca-4901-9c8e-c8261062e2c8");
    internal static string Schema => "t12_" + CaseId.ToString("N");
    internal Guid ProjectId { get; } = Guid.NewGuid();
    internal Guid EnvironmentId { get; } = Guid.NewGuid();
    internal Guid ApplicationId { get; } = Guid.NewGuid();
    internal Guid TargetId { get; } = Guid.NewGuid();
    internal Guid ArtifactId { get; } = Guid.NewGuid();
    internal Guid ActorId { get; } = Guid.NewGuid();
    internal Guid ReleaseId { get; } = Guid.NewGuid();
    internal Guid RevisionId { get; } = Guid.NewGuid();
    internal Guid ApprovalId { get; } = Guid.NewGuid();
    internal SessionSelection Session { get; private set; } = null!;
    internal WorkflowStartSelection Selection { get; private set; } = null!;
    internal WorkflowStartAdmissionReader Reader { get; private set; } = null!;
    private readonly NpgsqlConnection connection;
    private IWorkflowStartEligibilityReader eligibility = null!;
    private WorkflowActionPolicyReader policies = null!;
    private IAuthorityClock clock = null!;
    internal WorkflowStartAdmissionReader ObserveEligibility(Action observed) =>
        new(CaseId, new ObservedEligibility(eligibility, observed), policies, clock);
    private readonly List<DbContext> contexts = [];
    private WorkflowStartOwnerFixture(NpgsqlConnection connection) => this.connection = connection;

    internal static T Context<T>(NpgsqlConnection connection) where T : DbContext => (T)Activator.CreateInstance(typeof(T),
        new DbContextOptionsBuilder<T>().UseNpgsql(connection).ReplaceService<IModelCustomizer, Customizer>().Options)!;

    internal static Dictionary<string, string> OwnerScripts()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened");
        return new()
        {
            ["iam.sql"] = Script<KordraIdentityDbContext>(), ["ten.sql"] = Script<TenantDbContext>(),
            ["prj.sql"] = Script<ProjectDbContext>(), ["inv.sql"] = Script<InventoryDbContext>(),
            ["art.sql"] = Script<ArtifactDbContext>(), ["rel.sql"] = Script<ReleaseDbContext>(),
            ["apr.sql"] = Script<ApprovalDbContext>()
        };
        string Script<T>() where T : DbContext { using var db = Context<T>(connection); return db.Database.GenerateCreateScript(); }
    }

    /// <summary>Requires the caller's separately approved fresh schema; no password login or external provider IO.</summary>
    internal static async Task<WorkflowStartOwnerFixture> SeedAsync(string locator,
        FairAsyncReaderWriterAdmissionGate gate, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(locator);
        await connection.OpenAsync(ct);
        var fixture = new WorkflowStartOwnerFixture(connection);
        try { await fixture.SeedCoreAsync(locator, gate, ct); return fixture; }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private T Owner<T>() where T : DbContext { var db = Context<T>(connection); contexts.Add(db); return db; }

    private async Task SeedCoreAsync(string locator, FairAsyncReaderWriterAdmissionGate gate, CancellationToken ct)
    {
        await using var held = await gate.AcquireWriteAsync(ct);
        var iam = Owner<KordraIdentityDbContext>(); var ten = Owner<TenantDbContext>();
        var prj = Owner<ProjectDbContext>(); var inv = Owner<InventoryDbContext>();
        var art = Owner<ArtifactDbContext>(); var rel = Owner<ReleaseDbContext>(); var apr = Owner<ApprovalDbContext>();
        await using var seed = await connection.BeginTransactionAsync(ct);
        foreach (var db in contexts) await db.Database.UseTransactionAsync(seed, ct);
        var now = UtcMicroseconds(DateTimeOffset.UtcNow);
        var boot = Guid.NewGuid(); var raw = RandomNumberGenerator.GetBytes(32);
        var reference = Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var referenceHash = SHA256.HashData(raw); CryptographicOperations.ZeroMemory(raw);
        Session = new(ActorId, reference, boot);
        var hasher = new PasswordHasher<KordraIdentityUser>(Options.Create(new PasswordHasherOptions { IterationCount = 220_000 }));
        iam.Principals.Add(WorkflowOperatorAuthorityFixture<StartOwnerAuthorityCase>.Principal(ActorId,
            hasher.HashPassword(new(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))));
        iam.Sessions.Add(new() { SessionId = Guid.NewGuid(), PrincipalId = ActorId, PrincipalRevision = 1,
            SessionReferenceHash = referenceHash, BootGeneration = boot, CreatedAtUtc = now,
            LastActivityAtUtc = now, AbsoluteExpiresAtUtc = now.AddHours(1) });
        var member = Guid.NewGuid();
        ten.Organizations.Add(new() { Id = CaseId, Name = "Synthetic start fixture" });
        ten.Memberships.Add(new() { Id = member, TenantId = CaseId, PrincipalId = ActorId });
        foreach (var scope in new[] { "project", "environment" })
        {
            var definition = Guid.NewGuid();
            var permissions = scope == "project" ? new[] { "release.read" } :
                new[] { "workflow.start", "release.request-approval", "release.approve", "release.approval.read" };
            iam.RoleDefinitions.Add(new() { DefinitionId = definition, Revision = 1, ManifestId = Guid.NewGuid(),
                Name = "Synthetic start " + scope, ContentHash = new string(scope == "project" ? 'a' : 'b', 64),
                RulesJson = JsonSerializer.Serialize(permissions.Select(p => new PermissionRule(p, scope)).ToArray()) });
            ten.Assignments.Add(new() { Id = Guid.NewGuid(), TenantId = CaseId, MembershipId = member,
                DefinitionId = definition, DefinitionRevision = 1, ScopeKind = scope,
                ScopeId = scope == "project" ? ProjectId : EnvironmentId, IsFounding = true, GrantorPrincipalId = ActorId });
        }
        prj.Projects.Add(new() { Id = ProjectId, TenantId = CaseId, Name = "Synthetic start" });
        prj.Applications.Add(new() { Id = ApplicationId, TenantId = CaseId, ProjectId = ProjectId, Name = "Synthetic frontend" });
        prj.Environments.Add(new() { Id = EnvironmentId, TenantId = CaseId, ProjectId = ProjectId, Name = "Synthetic test", Classification = "Test" });
        prj.Associations.Add(new() { TargetId = TargetId, TenantId = CaseId, ProjectId = ProjectId, EnvironmentId = EnvironmentId });
        inv.Targets.Add(new() { Id = TargetId, TenantId = CaseId, ProjectId = ProjectId, EnvironmentId = EnvironmentId,
            Name = "Synthetic target", Kind = "kubernetes" });
        await iam.SaveChangesAsync(ct); await ten.SaveChangesAsync(ct); await prj.SaveChangesAsync(ct); await inv.SaveChangesAsync(ct);
        var startPolicy = await new ResourcePolicyStore(iam).StagePublicationAsync(new Lease(), CaseId, ProjectId, null, 0,
            new(2, "workflow.start", "environment", "active", now.AddMinutes(-1), null, [new("Test", "allow")]), now, ct)
            ?? throw new InvalidOperationException("Synthetic start policy rejected.");
        await iam.SaveChangesAsync(ct);
        if (await new PolicyBindingStore(prj).StageAsync(CaseId, ProjectId,
            new(null, "environment", EnvironmentId, 0, "workflow.start"), new(startPolicy.PolicyId, 1), Guid.NewGuid(), true, ct) is null)
            throw new InvalidOperationException("Synthetic start binding rejected.");
        await prj.SaveChangesAsync(ct);
        await seed.CommitAsync(ct);
        foreach (var db in contexts) await db.Database.UseTransactionAsync(null, ct);
        await SeedArtifactAsync(now, ct);
        var policy = ApprovalPolicy(now);
        var policyId = Guid.NewGuid();
        apr.Heads.Add(new() { InstallationId = CaseId, TenantId = CaseId, ProjectId = ProjectId, PolicyId = policyId, CurrentRevision = 1 });
        apr.Revisions.Add(new() { InstallationId = CaseId, TenantId = CaseId, ProjectId = ProjectId, PolicyId = policyId,
            Revision = 1, CanonicalPayload = policy.GetCanonicalBytes(), PayloadHash = Convert.FromHexString(policy.PayloadHash), PublishedAtUtc = now });
        apr.Bindings.Add(new() { InstallationId = CaseId, TenantId = CaseId, ProjectId = ProjectId, EnvironmentId = EnvironmentId,
            BindingId = Guid.NewGuid(), BindingRevision = 1, PolicyId = policyId, PolicyRevision = 1 });
        await apr.SaveChangesAsync(ct);
        var selections = new ReleaseSelectionReader(new ReleaseCatalogReader(prj), new ReleaseTargetReader(inv),
            new ArtifactQueryStore(art), new ApprovalPolicyStore(apr));
        var candidate = await selections.ReadUnderAdmissionAsync(CaseId, CaseId, ProjectId,
            ReleaseSelection.Create(ApplicationId, EnvironmentId, TargetId, ArtifactId), DateTimeOffset.UtcNow, ct);
        if (candidate.Status != "eligible" || candidate.Intent is null) throw new InvalidOperationException("Synthetic owner selection: " + candidate.Status);
        var intent = candidate.Intent;
        rel.Lineages.Add(new() { Id = ReleaseId, InstallationId = CaseId, TenantId = CaseId, ProjectId = ProjectId, CreatedAtUtc = now });
        rel.Revisions.Add(new() { Id = RevisionId, InstallationId = CaseId, TenantId = CaseId, ProjectId = ProjectId,
            ReleaseId = ReleaseId, RevisionNumber = 1, Version = 2, CreatorPrincipalId = ActorId, Label = "Synthetic frozen release",
            ApplicationId = ApplicationId, EnvironmentId = EnvironmentId, TargetId = TargetId, ArtifactId = ArtifactId,
            State = "Frozen", SpecBytes = intent.GetCanonicalBytes(), SpecHash = Convert.FromHexString(intent.SpecHash),
            CreatedAtUtc = now, UpdatedAtUtc = now, FrozenAtUtc = now });
        await rel.SaveChangesAsync(ct);
        var iamFactory = new Factory<KordraIdentityDbContext>(locator);
        var auth = new LocalAuthenticationService(iamFactory, new NoAuthenticationWrites(), hasher,
            new(CaseId, boot), gate);
        var roles = new RoleDefinitionStore(iamFactory, gate, new NoAuthenticationWrites(), new(CaseId, boot), auth);
        var owners = new Owners(iam, prj);
        var grants = new TenantMembershipService(new Factory<TenantDbContext>(locator), roles, null!, owners, null!, gate, auth);
        var principals = new DurablePrincipalReader(iamFactory);
        var capture = new ApprovalAuthorityCapture(auth, principals, grants, roles, owners, owners, auth);
        var requester = await capture.CaptureRequesterUnderAdmissionAsync(Session, ProjectId, EnvironmentId, ct)
            ?? throw new InvalidOperationException("Synthetic requester capture denied.");
        var approver = await capture.CaptureApproverUnderAdmissionAsync(Session, ProjectId, EnvironmentId, ct)
            ?? throw new InvalidOperationException("Synthetic approver capture denied.");
        var granted = UtcMicroseconds(DateTimeOffset.UtcNow);
        apr.Requests.Add(new() { Id = ApprovalId, InstallationId = CaseId, TenantId = CaseId, ProjectId = ProjectId,
            EnvironmentId = EnvironmentId, ReleaseRevisionId = RevisionId, RequesterPrincipalId = ActorId,
            SpecHash = Convert.FromHexString(intent.SpecHash), RequesterEvidence = ApprovalAuthorityEvidenceCodec.Encode(requester.Evidence),
            Version = 2, State = "Granted", CreatedAtUtc = now, PendingExpiresAtUtc = now.AddMinutes(5),
            GrantedAtUtc = granted, GrantExpiresAtUtc = now.AddMinutes(4), ApproverPrincipalId = ActorId,
            ApproverEvidence = ApprovalAuthorityEvidenceCodec.Encode(approver.Evidence) });
        apr.Decisions.Add(new() { Id = Guid.NewGuid(), InstallationId = CaseId, TenantId = CaseId, RequestId = ApprovalId,
            RequestVersion = 2, Action = "grant", ActorPrincipalId = ActorId, ReasonCode = "approved", OccurredAtUtc = granted });
        await apr.SaveChangesAsync(ct);
        var subjects = new ApprovalSubjectReader(new Factory<ReleaseDbContext>(locator), selections);
        var effective = new ApprovalEligibilityReader(subjects,
            new StoredApprovalAuthorityReader(new DurableApprovalAuthorityReader(principals, grants, roles, auth)), auth);
        Selection = new(CaseId, ProjectId, EnvironmentId, ReleaseId, RevisionId, 1, intent.SpecHash,
            WorkflowSpecification.TemplateId, WorkflowSpecification.TemplateVersion);
        eligibility = new WorkflowStartEligibilityReader(CaseId,
            new WorkflowEnvironmentAuthorityReader(auth, grants, roles, owners, owners, auth),
            new ReleaseProjectAuthorityReader(auth, grants, roles, owners, auth),
            new Factory<ApprovalDbContext>(locator), subjects, effective, auth);
        policies = new WorkflowActionPolicyReader(owners, owners, owners, auth); clock = auth;
        Reader = new(CaseId, eligibility, policies, clock);
    }

    internal static ReleaseApprovalPolicy ApprovalPolicy(DateTimeOffset now) => ReleaseApprovalPolicy.Parse(
        JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, purpose = "release-intent-v1", status = "active",
            effectiveFromUtc = now.AddMinutes(-1).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'"),
            expiresAtUtc = now.AddMinutes(10).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'"),
            rules = new[] { new { classification = "Test", effect = "allow", separationOfDuties = "self_allowed",
                allowUnattestedProvenance = true, pendingLifetimeSeconds = 300, grantLifetimeSeconds = 300, blockedWindowsUtc = Array.Empty<object>() } } }));

    private async Task SeedArtifactAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Internal ART setters remain internal. This INSERT seeds only the frozen disposable schema, not a runtime registration path.
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {Schema}.art_artifacts
            (id,tenant_id,project_id,registry_id,repository,root_digest,platform_json,root_media_type,root_size_bytes,
             selected_manifest_digest,selected_manifest_size_bytes,selected_manifest_media_type,config_digest,config_size_bytes,
             observed_platform_json,variant_evidence,verifier_profile,verified_at_utc,created_at_utc)
            VALUES (@id,@tenant,@project,@registry,'synthetic/frontend',@digest,@platform,@media,100,
                    @digest,100,@media,@config,50,@platform,'none','artifact-registry-v1',@now,@now)
            """, connection);
        command.Parameters.AddWithValue("id", ArtifactId); command.Parameters.AddWithValue("tenant", CaseId);
        command.Parameters.AddWithValue("project", ProjectId); command.Parameters.AddWithValue("registry", Guid.NewGuid());
        command.Parameters.AddWithValue("digest", "sha256:" + new string('a', 64));
        command.Parameters.AddWithValue("config", "sha256:" + new string('b', 64));
        command.Parameters.AddWithValue("platform", "{\"os\":\"linux\",\"architecture\":\"amd64\",\"variant\":null}");
        command.Parameters.AddWithValue("media", "application/vnd.oci.image.manifest.v1+json"); command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static DateTimeOffset UtcMicroseconds(DateTimeOffset now) => new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
    public async ValueTask DisposeAsync()
    { foreach (var db in contexts) await db.DisposeAsync(); await connection.DisposeAsync(); }
    private sealed class Customizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder model, DbContext context)
        { base.Customize(model, context); model.HasDefaultSchema(Schema); foreach (var entity in model.Model.GetEntityTypes()) entity.SetSchema(Schema); }
    }
    private sealed class Factory<T>(string locator) : IDbContextFactory<T> where T : DbContext
    {
        public T CreateDbContext() => (T)Activator.CreateInstance(typeof(T), new DbContextOptionsBuilder<T>()
            .UseNpgsql(new NpgsqlConnection(locator), contextOwnsConnection: true).ReplaceService<IModelCustomizer, Customizer>().Options)!;
    }
    // Fault boundary observes real results only; no fixture fact is manufactured or altered.
    private sealed class ObservedEligibility(IWorkflowStartEligibilityReader inner, Action observed) : IWorkflowStartEligibilityReader
    {
        public async Task<WorkflowStartEligibility?> ReadUnderAdmissionAsync(SessionSelection session, WorkflowStartSelection selection,
            Guid invocation, CancellationToken ct = default)
        {
            var result = await inner.ReadUnderAdmissionAsync(session, selection, invocation, ct);
            if (result is not null) observed();
            return result;
        }
    }
    private sealed class Lease : IOfflineProvisioningLease { public void RequireHeld() { } }
    private sealed class NoAuthenticationWrites : IIdentityAuditTransactionFactory
    {
        public ValueTask<IIdentityAuditTransaction> EnlistAsync(System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction transaction, CancellationToken ct = default) => throw new InvalidOperationException("No fixture authentication writes.");
    }
    private sealed class Owners(KordraIdentityDbContext iam, ProjectDbContext prj) : IReleasePolicyEnvironmentReader,
        IActionPolicyBindingReader, ICurrentResourcePolicyReader, ITenantScopeOwner, IReleaseProjectAuthorityReader, IApprovalEnvironmentAuthorityReader
    {
        public Task<ReleasePolicyEnvironment?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid environment, CancellationToken ct = default) =>
            new ReleasePolicyEnvironmentReader(prj).ReadCurrentUnderAdmissionAsync(tenant, project, environment, ct);
        public Task<PolicyBinding?> ReadCurrentForActionUnderAdmissionAsync(Guid tenant, Guid project, Guid? environment, string action, CancellationToken ct = default) =>
            new PolicyBindingStore(prj).ReadCurrentForActionUnderAdmissionAsync(tenant, project, environment, action, ct);
        public Task<ResourcePolicySnapshot?> ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid policy, long revision, CancellationToken ct = default) =>
            new ResourcePolicyStore(iam).ReadCurrentUnderAdmissionAsync(tenant, project, policy, revision, ct);
        public Task<bool> BelongsToTenantAsync(Guid tenant, TenantScope scope, CancellationToken ct = default) =>
            new PolicyBindingStore(prj).ScopeBelongsToTenantUnderAdmissionAsync(tenant, scope.Kind, scope.Id, ct);
        public async Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid project, CancellationToken ct = default)
        { var p = await new ProjectIdentityReader(prj).ReadCurrentProjectUnderAdmissionAsync(project, ct); return p is null ? null : new(p.TenantId, p.ProjectId, p.Revision); }
        async Task<ApprovalEnvironmentAuthority?> IApprovalEnvironmentAuthorityReader.ReadCurrentUnderAdmissionAsync(Guid tenant, Guid project, Guid environment, CancellationToken ct)
        { var p = await ReadCurrentUnderAdmissionAsync(tenant, project, environment, ct); return p is null ? null : new(tenant, project, environment, p.EnvironmentRevision); }
    }
}

internal sealed class StartOwnerAuthorityCase : IWorkflowOperatorAuthorityCase
{
    public static Guid CaseId => WorkflowStartOwnerFixture.CaseId;
}
