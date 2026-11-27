using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.ApprovalGovernance.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.ArtifactManagement.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.ConnectionManagement.Commands;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.InfrastructureInventory.Contracts;
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
using Kordra.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Primitives;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>
/// 12.3c-3 (D4) on actual PostgreSQL (operation-gated; ADR-0025 disposable database + run-scoped roles): the remaining registry
/// writers — the three 12.3b IAM functions (service manifest, delegation revocation and reissue), offline credential binding,
/// offline approval-policy publication, release mutation and approval request — each called through its REAL entry point with the enforced
/// fence, (a) wait while the installation fence is held, (b) advance the epoch by exactly one with every committed change,
/// after which a permit issued before them is rejected (KD005), and (c) leave the epoch unchanged when nothing commits.
/// </summary>
public sealed class AuthorityFenceRemainingWriterDatabaseTests
{
    /// <summary>Registry scenarios this class proves; the completeness test pins it to <see cref="AuthorityWriterRegistry"/>.</summary>
    internal static readonly string[] Scenarios = ["iam.service-manifest", "iam.delegation-revoke", "iam.delegation-reissue",
        "sec.binding", "approval.policy", "release.mutate", "approval.mutate"];
    private const string ManifestVariable = "KORDRA_FENCE_REMAINING_MANIFEST";
    private const string LocatorVariable = "KORDRA_FENCE_REMAINING_CONNECTION_STRING";

    [Fact]
    public void ExportFenceSqlWithoutDatabaseAccess()
    {
        var sql = AuthorityFenceIamWriterDatabaseTests.Script("0123456789ab");
        Assert.Contains("CREATE SCHEMA iam_workflow AUTHORIZATION kordra_iam_owner_r0123456789ab", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_FENCE_REMAINING_EXPORT") is not { Length: > 0 } directory) return;
        var suffix = Environment.GetEnvironmentVariable("KORDRA_FENCE_REMAINING_SUFFIX") ?? throw new InvalidOperationException("Run suffix required.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "iam-workflow-authority-v3.sql"), AuthorityFenceIamWriterDatabaseTests.Script(suffix));
    }

    /// <summary>
    /// Every static manifest, policy and command the database race builds after its proven steps parses offline, so an input
    /// defect never costs another approved disposable batch.
    /// </summary>
    [Fact]
    public void RaceInputsParseWithoutDatabaseAccess()
    {
        var run = Guid.NewGuid();
        var profile = (RunId: run, SourceSha256: new string('a', 64), OperationManifestSha256: new string('b', 64),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(1));
        var manifest = ApprovalPolicyManifest.ParseDisposableTest(ApprovalPolicyBytes(Guid.NewGuid(), "kordra_test_" + run.ToString("N"),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1), profile);
        Assert.Contains(manifest.Policy.Rules, rule => rule.Classification == "Staging" && rule.Effect == "allow");
        Assert.Single(manifest.BindingChanges);
        var facts = new ReleaseFacts(Guid.NewGuid());
        facts.AllowPilot(TimeSpan.FromMinutes(5));
        Assert.NotNull(facts.Policy);
        var correlation = Guid.NewGuid();
        Assert.Equal(correlation, ApprovalRequest(facts.Environment, correlation, Guid.NewGuid()).CorrelationId);
    }

    [AuthorityFenceRemainingDatabaseFact]
    public async Task RemainingWritersHoldTheFenceAndAdvanceTheEpochOnEveryCommit()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        var ct = budget.Token;
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable(ManifestVariable)!));
        Assert.Equal("approved_for_execution", approval.RootElement.GetProperty("status").GetString());
        var suffix = approval.RootElement.GetProperty("suffix").GetString()!;
        var script = AuthorityFenceIamWriterDatabaseTests.Script(suffix);
        Assert.Equal(approval.RootElement.GetProperty("sha256").GetProperty("iam-workflow-authority-v3.sql").GetString(), OwnerFunctionPoc.Sha256(script));
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile() ?? throw new InvalidOperationException("Disposable runner profile required.");
        var locator = Environment.GetEnvironmentVariable(LocatorVariable) ?? throw new InvalidOperationException("Approved fixture locator required.");
        var selection = new ProductPostgreSqlFixtureSelection(locator, profile);
        var identity = new PooledDbContextFactory<KordraIdentityDbContext>(selection.Identity());
        var tenants = new PooledDbContextFactory<TenantDbContext>(selection.Tenant());
        var projects = new PooledDbContextFactory<ProjectDbContext>(selection.Project());
        var inventory = new PooledDbContextFactory<InventoryDbContext>(selection.Inventory());
        var audits = new PooledDbContextFactory<AuditDbContext>(selection.Audit());

        // Owner schemas through their own migrations, then the reviewed IAM v2 artifact (fence, services, permits).
        await using (var db = await identity.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using (var db = await audits.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using (var db = await tenants.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using (var db = await projects.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using (var db = await inventory.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using (var db = new CredentialDbContext(selection.Credential())) await db.Database.MigrateAsync(ct);
        await using (var db = new ReleaseDbContext(selection.Release())) await db.Database.MigrateAsync(ct);
        await using (var db = new ApprovalDbContext(selection.Approval())) await db.Database.MigrateAsync(ct);
        await using var race = await AuthorityFenceRaceHarness.StartAsync(locator, suffix, script, ct);
        var installation = race.InstallationId;
        var fence = new PostgresAuthorityFence(installation);
        var gate = new FairAsyncReaderWriterAdmissionGate();
        var clock = new UtcClock();
        var lease = new HeldLease();

        // 12.3b IAM functions on the API login's own roots. Service manifest: one pin rotation of the registered worker
        // service, then its exact duplicate, answered from the receipt before the fence (no wait, no change).
        var rotation = new WorkflowServiceManifest(Hex(), "rotate", installation, race.WorkerServiceId, null, null, Hex(),
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1), [], Guid.NewGuid());
        await race.RaceAsync("iam.service-manifest", true, () => InApiRoot(async (connection, root) =>
            !(await new WorkflowAuthorityFunctionPort(connection, root).ApplyServiceManifestAsync(rotation, ct)).Duplicate));
        var beforeDuplicate = await race.EpochAsync();
        Assert.True(await InApiRoot(async (connection, root) =>
            (await new WorkflowAuthorityFunctionPort(connection, root).ApplyServiceManifestAsync(rotation, ct)).Duplicate));
        Assert.Equal(beforeDuplicate, await race.EpochAsync());

        // Delegation revocation, then the repeated revocation that only finds it already revoked under the fence.
        var revoked = await NewDelegation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Hex(), Hex(), 2);
        await race.RaceAsync("iam.delegation-revoke", true, () => InApiRoot(async (connection, root) =>
            !(await new WorkflowAuthorityFunctionPort(connection, root).RevokeDelegationAsync(revoked, "fence.revoke", ct)).Duplicate));
        await race.RaceAsync("iam.delegation-revoke", false, () => InApiRoot(async (connection, root) =>
            (await new WorkflowAuthorityFunctionPort(connection, root).RevokeDelegationAsync(revoked, "fence.revoke", ct)).Duplicate));

        // Delegation reissue through the production reissuer: generation 2, then a stale principal refused after the fence.
        var actor = Guid.NewGuid(); var tenantId = Guid.NewGuid(); var projectId = Guid.NewGuid(); var environmentId = Guid.NewGuid();
        var instance = Guid.NewGuid(); var releaseId = Guid.NewGuid(); var releaseHash = Hex(); var specification = Hex();
        await NewDelegation(tenantId, instance, actor, projectId, environmentId, releaseId, releaseHash, specification, 2);
        var principals = new FixedPrincipals { Value = new(actor, 2, true, new string('d', 64)) };
        var membership = Guid.NewGuid();
        ScopedGrant Grant(string permission, string kind, Guid scope) =>
            new(actor, tenantId, membership, 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(), 1, permission, kind, scope, null);
        var reissue = new WorkflowDelegationReissueRequest(installation, tenantId, projectId, environmentId, instance, actor, releaseId, 1,
            releaseHash, specification, DateTimeOffset.UtcNow.AddMinutes(20), Guid.NewGuid(),
            new(actor, Guid.NewGuid(), Guid.NewGuid(), 2, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddMinutes(30)),
            Grant("workflow.start", "environment", environmentId), Grant("release.read", "project", projectId));
        var reissuer = new IamWorkflowDelegationReissuer(principals);
        await race.RaceAsync("iam.delegation-reissue", true, () => InApiRoot(async (connection, root) =>
            await reissuer.StageReissueAsync(connection, root, reissue, ct) is { Generation: 2 }));
        principals.Value = principals.Value with { PrincipalRevision = 3 };
        await race.RaceAsync("iam.delegation-reissue", false, () => InApiRoot(async (connection, root) =>
            await reissuer.StageReissueAsync(connection, root, reissue, ct) is null));

        // Owner setup for the offline writers with the inactive fence (setup only): a tenant, a project with an environment v2
        // bound to a published resource policy, and a registered target.
        var roles = new RoleDefinitionStore(identity, gate, selection.IdentityAudit());
        var subject = new FixedSubject();
        var ten = new TenantMembershipService(tenants, roles, subject, new ScopeOwner(projects), selection.TenantAudit(), gate, clock);
        var policyReader = new CurrentResourcePolicyReader(identity, projects, clock);
        var floor = CatalogClockFloorWriter.CreateDisposableTestWriter(new LocalAuthenticationConfiguration(installation, Guid.NewGuid()),
            clock, profile.OwnerBinding);
        var catalog = CatalogMutationCoordinator.CreateDisposableTestCoordinator(projects, new CatalogAuthority(subject, ten), policyReader,
            gate, clock, selection.CatalogAudit(), profile.OwnerBinding, floor);
        var auditLookup = selection.AuditStore(audits);
        var resourcePolicies = OfflineResourcePolicyCoordinator.CreateDisposableTestCoordinator(installation, identity, ten, auditLookup,
            gate, clock, null, profile.OwnerBinding);
        var owner = await NewPrincipal("fence-remaining-owner");
        subject.Principal = owner;
        var founder = await Define("Fence founder", new("project.manage", "organization"), new("org.manage", "organization"),
            new("membership.assign-role", "organization"));
        var tenant = await ten.InitializeOfflineAsync(lease, Guid.NewGuid(), owner, "Fence remaining tenant", founder.DefinitionId, founder.Revision, ct);
        Assert.Equal("success", tenant.Status);
        var project = await catalog.ExecuteAsync(new CatalogCommand(Key("fence-project"), "create", "project", tenant.TenantId, Name: "Fence project"), ct);
        Assert.Equal("success", project.Status);
        var projectRole = await Define("Fence project", new("project.manage", "project"), new("project.read", "project"),
            new("target.manage", "project"), new("target.read", "project"));
        var ownerMembership = await MembershipOf(tenant.TenantId, owner);
        Assert.Equal("success", (await ten.GrantOfflineAsync(lease, Guid.NewGuid(), tenant.TenantId, ownerMembership.Id, ownerMembership.Revision,
            projectRole.DefinitionId, projectRole.Revision, new("project", project.Resource!.Id), null, ct)).Status);
        var target = await catalog.ExecuteAsync(new TargetCommand(Key("fence-target"), "register", tenant.TenantId, project.Resource.Id,
            Name: "Fence target", Kind: "metadata"), ct);
        Assert.Equal("success", target.Status);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var resourcePolicy = new ResourcePolicyManifest(1, Guid.NewGuid(), installation, new(selection.Database, "kordra_test"), tenant.TenantId,
            project.Resource.Id, "create", null, 0, new(1, "target.read", "environment", "active", now.AddMinutes(-1), null,
                [new("Staging", "allow")]), Array.Empty<PolicyBindingChange>());
        var published = await resourcePolicies.ExecuteAsync(lease, Encoding.UTF8.GetString(selection.PolicyBytes(resourcePolicy)),
            selection.PolicyHash(resourcePolicy), ct);
        Assert.Equal("success", published.Status);
        // The target is project-scoped, so its authority resolves only through a current project-level target.read binding
        // (published as the current-authority fixtures do); without it the credential writer correctly answers denied.
        var projectPolicy = new ResourcePolicyManifest(1, Guid.NewGuid(), installation, new(selection.Database, "kordra_test"), tenant.TenantId,
            project.Resource.Id, "create", null, 0, new(1, "target.read", "project_without_environment", "active", now.AddMinutes(-1), null,
                [new("NoEnvironment", "allow")]), [new(null, "project_without_environment", null, 0)]);
        Assert.True(Outcome("success", (await resourcePolicies.ExecuteAsync(lease, Encoding.UTF8.GetString(selection.PolicyBytes(projectPolicy)),
            selection.PolicyHash(projectPolicy), ct)).Status));
        var environment = await catalog.ExecuteEnvironmentV2Async(new EnvironmentCreateV2Command(Key("fence-environment"), tenant.TenantId,
            project.Resource.Id, "Fence staging", "Staging", new(published.PolicyId, published.Revision), Guid.NewGuid()), ct);
        Assert.Equal("success", environment.Status);

        // Offline credential binding, then its exact command replay, answered before the root begins (no fence, no change).
        var targetAuthority = new CurrentTargetAuthorityReader(inventory, new CurrentProjectOwnershipReader(projects, policyReader));
        Assert.NotNull(await targetAuthority.ReadTargetUnderAdmissionAsync(target.Resource!.TargetId, ct));
        var credentials = OfflineCredentialCoordinator.CreateDisposableTestCoordinator(installation, identity, new TenantOrganizationAuthority(tenants),
            targetAuthority, auditLookup, gate, clock, null, profile.OwnerBinding, fence);
        Assert.Equal(AuthorityFenceMode.Enforced, credentials.FenceMode);
        var binding = new CredentialManifest(1, Guid.NewGuid(), installation, tenant.TenantId, target.Resource.TargetId, "inventory.readonly.v1",
            "target.read", "create", null, 0, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(-1), now.AddMinutes(5));
        await race.RaceAsync("sec.binding", true, async () =>
            Outcome("success", (await credentials.ExecuteAsync(lease, binding.CanonicalBytes(), binding.Hash(), ct)).Status));
        var beforeCredentialReplay = await race.EpochAsync();
        Assert.True(Outcome("success", (await credentials.ExecuteAsync(lease, binding.CanonicalBytes(), binding.Hash(), ct)).Status));
        Assert.Equal(beforeCredentialReplay, await race.EpochAsync());

        // Offline approval-policy publication bound to the environment, then its exact replay, which reads its receipt under
        // the fence and commits nothing.
        var approvals = OfflineApprovalPolicyCoordinator.CreateDisposableTestCoordinator(installation, identity, auditLookup, gate, clock, null,
            profile.OwnerBinding, fence);
        Assert.Equal(AuthorityFenceMode.Enforced, approvals.FenceMode);
        var approvalPolicy = ApprovalPolicyBytes(installation, selection.Database, tenant.TenantId, project.Resource.Id, environment.Environment!.Id,
            environment.Environment.Revision);
        var approvalHash = ApprovalPolicyManifest.ParseDisposableTest(approvalPolicy, profile.OwnerBinding).CommandHash;
        await race.RaceAsync("approval.policy", true, async () =>
            Outcome("success", (await approvals.ApplyAsync(lease, approvalPolicy, approvalHash, ct)).Status));
        await race.RaceAsync("approval.policy", false, async () =>
        {
            var replay = await approvals.ApplyAsync(lease, approvalPolicy, approvalHash, ct);
            return Outcome("success", replay.Status) && Outcome("replayed", replay.Replayed ? "replayed" : "fresh");
        });

        // Release mutation (synthetic project authority, real REL/IAM/AUD owners): a draft, then its exact replay.
        var releaseContexts = new PooledDbContextFactory<ReleaseDbContext>(selection.Release());
        var facts = new ReleaseFacts(installation);
        var releaseAuthority = new ReleaseProjectAuthorityReader(facts, facts, facts, facts, facts);
        var uses = ReleaseUseAdmission.CreateDisposableTestComposition(installation, identity, releaseAuthority, selection.ArtifactAudit(),
            gate, facts, profile.OwnerBinding);
        var releases = ReleaseMutationTransaction.CreateDisposableTestTransaction(installation, identity, releaseAuthority,
            new ReleaseSelectionReader(facts, facts, facts, facts), selection.ArtifactAudit(), new AuditMutationEvidenceReader(audits),
            gate, facts, profile.OwnerBinding, fence);
        Assert.Equal(AuthorityFenceMode.Enforced, releases.FenceMode);
        var draft = ReleaseMutationCommand.Create(facts.Project, "Fence release", facts.Selection);
        var releaseKey = Key("fence-release");
        Guid revision = default;
        await race.RaceAsync("release.mutate", true, async () =>
        {
            var (status, created) = await Mutate(draft, releaseKey);
            revision = created ?? Guid.Empty;
            return Outcome("durable", status);
        });
        await race.RaceAsync("release.mutate", false, async () => Outcome("replay", (await Mutate(draft, releaseKey)).Status));

        // Approval request for the draft after it is frozen (setup through the same fenced writer, outside the race), then its
        // exact replay, which verifies its receipt under the fence and commits nothing.
        facts.AllowPilot(TimeSpan.FromMinutes(5));
        var preview = await new ReleaseSelectionReader(facts, facts, facts, facts).ReadUnderAdmissionAsync(installation, facts.Tenant,
            facts.Project, facts.Selection, facts.Now, ct);
        var freeze = ReleaseMutationCommand.Freeze(facts.Project, revision, 1, $"\"rel-v1:{revision:D}:1\"",
            ReleasePreviewHash.Compute(revision, 1, preview.Intent!.SpecHash));
        Assert.True(Outcome("durable", (await Mutate(freeze, Key("fence-freeze"))).Status));
        var capture = new ApprovalAuthorityCapture(facts, facts, facts, facts, facts, facts, facts);
        var approvalUses = ApprovalUseAdmission.CreateDisposableTestComposition(installation, identity, capture, selection.ArtifactAudit(),
            gate, facts, profile.OwnerBinding);
        var approvalMutations = ApprovalMutationTransaction.CreateDisposableTestTransaction(installation, identity, releaseContexts, capture,
            new StoredApprovalAuthorityReader(new DurableApprovalAuthorityReader(facts, facts, facts, facts)),
            new ApprovalSubjectReader(releaseContexts, new ReleaseSelectionReader(facts, facts, facts, facts)), selection.ArtifactAudit(),
            new AuditMutationEvidenceReader(audits), gate, facts, profile.OwnerBinding, fence);
        Assert.Equal(AuthorityFenceMode.Enforced, approvalMutations.FenceMode);
        var correlation = Guid.NewGuid();
        var approvalRequest = ApprovalRequest(facts.Environment, correlation, revision);
        var approvalKey = Key("fence-approval");
        await race.RaceAsync("approval.mutate", true, async () => Outcome("durable", await Approve()));
        await race.RaceAsync("approval.mutate", false, async () => Outcome("replay", await Approve()));
        Assert.Equal(Scenarios.Order(StringComparer.Ordinal), race.Proven.Order(StringComparer.Ordinal));

        async Task<(string Status, Guid? Revision)> Mutate(ReleaseMutationCommand command, string key)
        {
            var use = await uses.BeginAsync(facts.Session, command, key, Guid.NewGuid(), ct);
            if (use is not { Status: "admitted", Ticket: { } ticket }) return (use.Status, null);
            var result = await releases.ExecuteAsync(ticket, ct);
            return (result.Status, result.RevisionId);
        }

        async Task<string> Approve()
        {
            var use = await approvalUses.BeginAsync(facts.Session, approvalRequest, facts.Project, approvalKey, correlation, ct);
            return use is { Status: "admitted", Ticket: { } ticket } ? (await approvalMutations.ExecuteAsync(ticket, ct)).Status : use.Status;
        }

        async Task<bool> InApiRoot(Func<NpgsqlConnection, NpgsqlTransaction, Task<bool>> action)
        {
            await using var connection = new NpgsqlConnection(locator);
            await connection.OpenAsync(ct);
            await using var root = await connection.BeginTransactionAsync(ct);
            var expected = await action(connection, root);
            if (expected) await root.CommitAsync(ct); else await root.RollbackAsync(ct);
            return expected;
        }

        async Task<Guid> NewDelegation(Guid tenantFor, Guid instanceFor, Guid actorFor, Guid projectFor, Guid environmentFor, Guid release,
            string hash, string specificationHash, long principalRevision)
        {
            var id = Guid.NewGuid();
            Assert.True(await InApiRoot(async (connection, root) =>
            {
                await new WorkflowAuthorityFunctionPort(connection, root).CreateDelegationAsync(new(id, tenantFor, instanceFor, actorFor,
                    Guid.NewGuid(), Guid.NewGuid(), principalRevision, projectFor, environmentFor, release, 1, hash, WorkflowSpecification.TemplateId,
                    WorkflowSpecification.TemplateVersion, specificationHash, ["admit"], Encoding.UTF8.GetBytes("{\"fence\":1}"),
                    DateTimeOffset.UtcNow.AddMinutes(30)), ct);
                return true;
            }));
            return id;
        }

        async Task<Guid> NewPrincipal(string prefix)
        {
            var id = Guid.NewGuid(); var name = prefix + "-" + id.ToString("N")[..12];
            await using var db = await identity.CreateDbContextAsync(ct);
            db.Principals.Add(new() { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(),
                PasswordHash = "synthetic-not-a-login-hash", SecurityStamp = Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync(ct);
            return id;
        }

        async Task<DefinitionWriteResult> Define(string name, params PermissionRule[] rules)
        {
            var defined = await roles.PublishOfflineAsync(lease, Guid.NewGuid(), Guid.Empty, 0, name, rules, false, ct);
            Assert.Equal("success", defined.Status);
            return defined;
        }

        async Task<TenantMembership> MembershipOf(Guid tenantFor, Guid principal)
        {
            await using var db = await tenants.CreateDbContextAsync(ct);
            return await db.Memberships.AsNoTracking().SingleAsync(x => x.TenantId == tenantFor && x.PrincipalId == principal, ct);
        }
    }

    /// <summary>
    /// Fails with letter-only <c>Expected:</c>/<c>Actual:</c> status tokens: the disposable batch runner redacts failure text
    /// and keeps only such tokens, so an unexpected writer outcome stays identifiable from its receipt.
    /// </summary>
    private static bool Outcome(string expected, string? actual)
    {
        if (actual != expected)
            Assert.Fail($"Unexpected writer status{Environment.NewLine}Expected: {Letters(expected)}{Environment.NewLine}Actual: {Letters(actual)}");
        return true;
    }

    private static string Letters(string? value)
    {
        var letters = new string((value ?? "").Where(char.IsAsciiLetter).Take(40).ToArray());
        return letters.Length > 0 ? letters : "none";
    }

    private static string Key(string prefix) => prefix + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
    private static string Hex() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static ParsedApprovalMutation ApprovalRequest(Guid environment, Guid correlation, Guid revision) =>
        ApprovalRequestParser.Parse("request", environment.ToString("D"), null, Encoding.UTF8.GetBytes(
            $"{{\"schemaVersion\":1,\"correlationId\":\"{correlation:D}\",\"releaseRevisionId\":\"{revision:D}\"}}"), StringValues.Empty);

    private static byte[] ApprovalPolicyBytes(Guid installation, string database, Guid tenant, Guid project, Guid environment, long environmentRevision) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schemaVersion = 1, manifestId = Guid.NewGuid().ToString("D"), installationId = installation.ToString("D"),
            expectedDatabase = database, expectedRole = "kordra_test", tenantId = tenant.ToString("D"), projectId = project.ToString("D"),
            policyId = Guid.NewGuid().ToString("D"), expectedPolicyRevision = 0,
            payload = new
            {
                schemaVersion = 1, purpose = "release-intent-v1", status = "active", effectiveFromUtc = "2026-01-01T00:00:00.000000Z",
                expiresAtUtc = (string?)null,
                // Staging (like Production) requires independent separation and attested provenance.
                rules = new[] { new { classification = "Staging", effect = "allow", separationOfDuties = "independent",
                    allowUnattestedProvenance = false, pendingLifetimeSeconds = 86400, grantLifetimeSeconds = 1800,
                    blockedWindowsUtc = Array.Empty<object>() } }
            },
            bindingChanges = new[] { new { environmentId = environment.ToString("D"), expectedEnvironmentRevision = environmentRevision,
                expectedBindingId = (string?)null, expectedBindingRevision = 0 } }
        }));

    private sealed class UtcClock : IAuthorityClock { public DateTimeOffset GetTrustedUtcNow() => DateTimeOffset.UtcNow; }

    private sealed class HeldLease : IOfflineProvisioningLease { public void RequireHeld() { } }

    private sealed class FixedPrincipals : IDurablePrincipalReader
    {
        public DurablePrincipal Value = null!;
        public Task<DurablePrincipal?> ReadUnderAdmissionAsync(Guid principalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DurablePrincipal?>(principalId == Value.PrincipalId ? Value : null);
    }

    private sealed class FixedSubject : ITenantSubjectReader
    {
        public Guid? Principal;
        public Task<Guid?> CurrentPrincipalAsync(CancellationToken cancellationToken = default) => Task.FromResult(Principal);
    }

    // Mirrors the Host's private CurrentTenantScopeOwner and CurrentCatalogAuthority adapters (trusted composition, not writers).
    private sealed class ScopeOwner(IDbContextFactory<ProjectDbContext> contexts) : ITenantScopeOwner
    {
        public async Task<bool> BelongsToTenantAsync(Guid tenantId, TenantScope scope, CancellationToken cancellationToken = default)
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            return await new PolicyBindingStore(db).ScopeBelongsToTenantUnderAdmissionAsync(tenantId, scope.Kind, scope.Id, cancellationToken);
        }
    }

    private sealed class CatalogAuthority(ITenantSubjectReader subjects, TenantMembershipService tenants) : ICatalogAuthority, IAuthenticatedCatalogSubjectReader
    {
        public Task<Guid?> CurrentPrincipalUnderAdmissionAsync(CancellationToken ct = default) => subjects.CurrentPrincipalAsync(ct);
        public async Task<Guid?> ResolvePrincipalAsync(Guid tenantId, Guid projectId, Guid? environmentId, string permission, CancellationToken cancellationToken = default)
        {
            var principal = await subjects.CurrentPrincipalAsync(cancellationToken);
            if (principal is null) return null;
            var scope = environmentId is not null ? new TenantScope("environment", environmentId.Value) :
                projectId == Guid.Empty ? new("organization", tenantId) : new("project", projectId);
            return await tenants.ReadEffectiveUnderAdmissionAsync(tenantId, principal.Value, permission, scope, cancellationToken) is null ? null : principal;
        }
    }

    /// <summary>
    /// Synthetic release and approval authority, mirroring the release transaction tests' facts (not a writer): one subject whose
    /// grants and role definitions exist for exactly the permission and scope each reader asks for.
    /// </summary>
    private sealed class ReleaseFacts : ICurrentSubjectReader, IScopedGrantReader, IRoleDefinitionReader, IReleaseProjectAuthorityReader,
        IAuthorityClock, IReleaseCatalogReader, IReleaseTargetReader, IReleaseArtifactReader, IReleaseApprovalPolicyReader,
        IDurablePrincipalReader, IApprovalEnvironmentAuthorityReader, IDurableGrantEvidenceReader
    {
        public readonly Guid Installation, Tenant = Guid.NewGuid(), Project = Guid.NewGuid(), Environment = Guid.NewGuid();
        private readonly Guid _membership = Guid.NewGuid(), _application = Guid.NewGuid(), _target = Guid.NewGuid(), _artifact = Guid.NewGuid(), _registry = Guid.NewGuid();
        private readonly Dictionary<(string Permission, string ScopeKind, Guid ScopeId), ScopedGrant> _grants = [];
        private readonly DateTimeOffset _captured;
        public DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public CurrentSubject Subject; public SessionSelection Session;
        public ReleaseApprovalPolicyBinding? Policy;
        public ReleaseSelection Selection => ReleaseSelection.Create(_application, Environment, _target, _artifact);
        public ReleaseFacts(Guid installation)
        {
            Installation = installation; _captured = Now;
            Subject = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Now.AddHours(1), Now.AddMinutes(30));
            Session = new(Subject.PrincipalId, "synthetic-session", Subject.BootGeneration);
        }
        public DateTimeOffset GetTrustedUtcNow() => Now;
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection s, CancellationToken ct = default) => Task.FromResult<CurrentSubject?>(Subject);
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission, string scopeKind, Guid scopeId, CancellationToken ct = default)
        {
            lock (_grants)
            {
                if (!_grants.TryGetValue((permission, scopeKind, scopeId), out var grant))
                    _grants[(permission, scopeKind, scopeId)] = grant = new(Subject.PrincipalId, Tenant, _membership, 1, 1, Guid.NewGuid(), 1,
                        Guid.NewGuid(), 1, permission, scopeKind, scopeId, Now.AddMinutes(10));
                return Task.FromResult<ScopedGrant?>(principal == Subject.PrincipalId && tenant == Tenant ? grant : null);
            }
        }
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken ct = default)
        {
            lock (_grants)
                return Task.FromResult(_grants.Values.SingleOrDefault(x => x.DefinitionId == id && x.DefinitionRevision == revision) is { } grant
                    ? new RoleDefinitionSnapshot(grant.DefinitionId, grant.DefinitionRevision, "Synthetic", [new(grant.Permission, grant.ScopeKind)]) : null);
        }
        public Task<bool> IsActivePrincipalAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<DurablePrincipal?> ReadUnderAdmissionAsync(Guid principalId, CancellationToken ct = default) =>
            Task.FromResult<DurablePrincipal?>(principalId == Subject.PrincipalId ? new(principalId, Subject.PrincipalRevision, true, new string('d', 64)) : null);
        public Task<ApprovalEnvironmentAuthority?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid environmentId, CancellationToken ct = default) =>
            Task.FromResult<ApprovalEnvironmentAuthority?>(tenantId == Tenant && projectId == Project && environmentId == Environment ? new(Tenant, Project, Environment, 1) : null);
        // Durable grant comparison belongs to approval grants, which this race does not exercise.
        public Task<DurableGrantEvidenceStatus> CompareUnderAdmissionAsync(ScopedGrant expected, CancellationToken ct = default) =>
            throw new NotSupportedException("Only approval requests are exercised.");
        public Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid p, CancellationToken ct = default) => Task.FromResult<ReleaseProjectAuthority?>(new(Tenant, Project, 1));
        public Task<ReleaseCatalogSelection?> ReadUnderAdmissionAsync(Guid t, Guid p, Guid a, Guid e, Guid target, CancellationToken ct = default) => Task.FromResult<ReleaseCatalogSelection?>(new(Tenant, Project, 1, _application, 1, Environment, 1, "Test", _target, 1));
        public Task<ReleaseTargetIdentity?> ReadUnderAdmissionAsync(Guid t, Guid p, Guid e, Guid target, CancellationToken ct = default) => Task.FromResult<ReleaseTargetIdentity?>(new(Tenant, Project, Environment, _target, "kubernetes", 1, 1));
        public Task<ArtifactV1?> ReadReleaseArtifactUnderAdmissionAsync(Guid t, Guid p, Guid a, CancellationToken ct = default)
        {
            var platform = new ArtifactPlatformV1("linux", "amd64", null); var digest = "sha256:" + new string('a', 64);
            return Task.FromResult<ArtifactV1?>(new(_artifact, Project, _registry, "synthetic/frontend", digest, platform,
                "application/vnd.oci.image.manifest.v1+json", 100, digest, 100, "application/vnd.oci.image.manifest.v1+json",
                "sha256:" + new string('b', 64), 50, platform, "none", "artifact-registry-v1", _captured, _captured,
                "manifest_config_hash_verified", "not_checked", "not_checked"));
        }
        public Task<ReleaseApprovalPolicyBinding?> ReadCurrentUnderAdmissionAsync(Guid i, Guid t, Guid p, Guid e, CancellationToken ct = default) => Task.FromResult(Policy);
        public void AllowPilot(TimeSpan lifetime)
        {
            string Time(DateTimeOffset x) => x.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
            var json = "{\"schemaVersion\":1,\"purpose\":\"release-intent-v1\",\"status\":\"active\",\"effectiveFromUtc\":\"" + Time(Now.AddSeconds(-1)) +
                "\",\"expiresAtUtc\":\"" + Time(Now.Add(lifetime)) + "\",\"rules\":[{\"classification\":\"Test\",\"effect\":\"allow\",\"separationOfDuties\":\"self_allowed\",\"allowUnattestedProvenance\":true,\"pendingLifetimeSeconds\":300,\"grantLifetimeSeconds\":300,\"blockedWindowsUtc\":[]}]}";
            var policy = ReleaseApprovalPolicy.Parse(Encoding.UTF8.GetBytes(json));
            Policy = new(Installation, Tenant, Project, Environment, Guid.NewGuid(), 1, Guid.NewGuid(), 1,
                policy.PayloadHash, new string('c', 64), policy.GetCanonicalBytes());
        }
    }
}

public sealed class AuthorityFenceRemainingDatabaseFactAttribute : FactAttribute
{
    public AuthorityFenceRemainingDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_FENCE_REMAINING_MANIFEST")))
            Skip = "Requires the exact approved 12.3c-3 fence fixture manifest, disposable database and run roles.";
    }
}
