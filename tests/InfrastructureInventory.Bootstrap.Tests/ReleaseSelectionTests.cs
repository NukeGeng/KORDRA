using System.Text;
using Kordra.Api;
using Kordra.ApprovalGovernance.Domain;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.Modules.ArtifactManagement.Contracts;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.ReleaseManagement.Domain;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ReleaseSelectionTests
{
    private readonly Guid _installation = Guid.NewGuid(), _tenant = Guid.NewGuid(), _project = Guid.NewGuid();
    private readonly Guid _app = Guid.NewGuid(), _environment = Guid.NewGuid(), _target = Guid.NewGuid(), _artifact = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private ReleaseSelection Selection => ReleaseSelection.Create(_app, _environment, _target, _artifact);

    [Fact]
    public async Task EligibleSelectionPinsAllOwnersWithoutConferringExecutionAuthority()
    {
        var fixture = Create();
        var result = await fixture.Reader.ReadUnderAdmissionAsync(_installation, _tenant, _project, Selection, _now);
        Assert.Equal("eligible", result.Status); Assert.NotNull(result.Intent);
        var json = Encoding.UTF8.GetString(result.Intent.GetCanonicalBytes());
        Assert.Contains("\"executionAuthorized\":false", json);
        Assert.Contains(_artifact.ToString("D"), json);
        Assert.Contains(fixture.Binding!.PayloadHash, json);
    }
    [Fact]
    public async Task ChangedAssociationAndForeignArtifactsFailClosed()
    {
        var fixture = Create(); fixture.Target = fixture.Target! with { AssociationRevision = 2 };
        Assert.Equal("owner_mismatch", (await fixture.Reader.ReadUnderAdmissionAsync(_installation, _tenant, _project, Selection, _now)).Status);
        fixture = Create(); fixture.Artifact = fixture.Artifact! with { ProjectId = Guid.NewGuid() };
        Assert.Equal("owner_mismatch", (await fixture.Reader.ReadUnderAdmissionAsync(_installation, _tenant, _project, Selection, _now)).Status);
    }
    [Fact]
    public async Task ProductionAndMissingOrNonPermittingPolicyNeverBecomeEligible()
    {
        var fixture = Create(); fixture.Catalog = fixture.Catalog! with { Classification = "Production" };
        Assert.Equal("qualification_required", (await fixture.Reader.ReadUnderAdmissionAsync(_installation, _tenant, _project, Selection, _now)).Status);
        fixture = Create(); fixture.Binding = null;
        Assert.Equal("policy_missing", (await fixture.Reader.ReadUnderAdmissionAsync(_installation, _tenant, _project, Selection, _now)).Status);
        fixture = Create(false);
        Assert.Equal("policy_denied", (await fixture.Reader.ReadUnderAdmissionAsync(_installation, _tenant, _project, Selection, _now)).Status);
    }
    [Fact]
    public async Task OwnerUnavailabilityAndPolicyTamperingAreNotTreatedAsEmptyOrPermit()
    {
        var fixture = Create(); fixture.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Reader.ReadUnderAdmissionAsync(_installation, _tenant, _project, Selection, _now));
        fixture = Create(); var old = fixture.Binding!;
        fixture.Binding = new(old.InstallationId, old.TenantId, old.ProjectId, old.EnvironmentId, old.BindingId, old.BindingRevision,
            old.PolicyId, old.PolicyRevision, new string('0', 64), old.BindingDigest, old.GetCanonicalPayload());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Reader.ReadUnderAdmissionAsync(_installation, _tenant, _project, Selection, _now));
    }
    private Fixture Create(bool allowUnattested = true)
    {
        var payload = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"purpose\":\"release-intent-v1\",\"status\":\"active\",\"effectiveFromUtc\":\"2026-01-01T00:00:00.000000Z\",\"expiresAtUtc\":null,\"rules\":[{\"classification\":\"Test\",\"effect\":\"allow\",\"separationOfDuties\":\"self_allowed\",\"allowUnattestedProvenance\":" + (allowUnattested ? "true" : "false") + ",\"pendingLifetimeSeconds\":300,\"grantLifetimeSeconds\":300,\"blockedWindowsUtc\":[]}]}");
        var policy = ReleaseApprovalPolicy.Parse(payload);
        var digest = "sha256:" + new string('a', 64); var platform = new ArtifactPlatformV1("linux", "amd64", null);
        return new Fixture
        {
            Catalog = new(_tenant, _project, 1, _app, 1, _environment, 1, "Test", _target, 1),
            Target = new(_tenant, _project, _environment, _target, "kubernetes", 1, 1),
            Artifact = new(_artifact, _project, Guid.NewGuid(), "team/frontend", digest, platform,
                "application/vnd.oci.image.manifest.v1+json", 100, digest, 100,
                "application/vnd.oci.image.manifest.v1+json", "sha256:" + new string('b', 64), 50, platform,
                "none", "artifact-registry-v1", _now, _now, "manifest_config_hash_verified", "not_checked", "not_checked"),
            Binding = new(_installation, _tenant, _project, _environment, Guid.NewGuid(), 1, Guid.NewGuid(), 1,
                policy.PayloadHash, new string('c', 64), policy.GetCanonicalBytes())
        };
    }
    private sealed class Fixture : IReleaseCatalogReader, IReleaseTargetReader, IReleaseArtifactReader, IReleaseApprovalPolicyReader
    {
        public ReleaseCatalogSelection? Catalog { get; set; }
        public ReleaseTargetIdentity? Target { get; set; }
        public ArtifactV1? Artifact { get; set; }
        public ReleaseApprovalPolicyBinding? Binding { get; set; }
        public bool Fail { get; set; }
        public ReleaseSelectionReader Reader => new(this, this, this, this);
        public Task<ReleaseCatalogSelection?> ReadUnderAdmissionAsync(Guid t, Guid p, Guid a, Guid e, Guid target, CancellationToken ct = default) =>
            Fail ? throw new InvalidOperationException("unavailable") : Task.FromResult(Catalog);
        public Task<ReleaseTargetIdentity?> ReadUnderAdmissionAsync(Guid t, Guid p, Guid e, Guid target, CancellationToken ct = default) => Task.FromResult(Target);
        public Task<ArtifactV1?> ReadReleaseArtifactUnderAdmissionAsync(Guid t, Guid p, Guid a, CancellationToken ct = default) => Task.FromResult(Artifact);
        public Task<ReleaseApprovalPolicyBinding?> ReadCurrentUnderAdmissionAsync(Guid i, Guid t, Guid p, Guid e, CancellationToken ct = default) => Task.FromResult(Binding);
    }
}
