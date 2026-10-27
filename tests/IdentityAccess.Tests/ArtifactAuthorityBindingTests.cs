using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.Modules.ArtifactManagement.Infrastructure.Registry;
using Kordra.Modules.ConnectionManagement.Infrastructure;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class ArtifactAuthorityBindingTests
{
    [Fact]
    public async Task Historical_project_reads_do_not_require_registry_or_SEC_and_pin_canonical_digest()
    {
        var f = new ArtifactAuthorityTestFacts(); var contexts = new NoCredentialDatabase();
        var resolver = new ArtifactAuthorityResolver(f.Installation, f.Reader, contexts, f, null);
        var binding = await resolver.ResolveAsync(f.Selection, f.Project.ProjectId, "artifact.register", f.Invocation);
        Assert.NotNull(binding); Assert.Null(binding.Registry); Assert.Null(binding.Credential); Assert.Equal(0, contexts.Calls);
        var json = "{\"schemaVersion\":1,\"predicate\":\"artifact-registration-v1\",\"invocationId\":\"" + f.Invocation +
            "\",\"projectAuthorityDigest\":\"" + binding.Facts.Digest + "\",\"action\":\"artifact.register\",\"registryConfigurationDigest\":null," +
            "\"credentialBindingDigest\":null,\"originalExpiresAtUtc\":\"" + binding.OriginalUntilUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) + "\"}";
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("artifact-authority-v1\n" + json))), binding.Digest);
        Assert.Equal("ArtifactAuthorityBinding [private]", binding.ToString());
    }

    [Fact]
    public async Task Rechecks_never_extend_original_deadline_even_when_current_expiry_slides()
    {
        var f = new ArtifactAuthorityTestFacts(); var resolver = new ArtifactAuthorityResolver(f.Installation, f.Reader, new NoCredentialDatabase(), f, null);
        var original = await resolver.ResolveAsync(f.Selection, f.Project.ProjectId, "artifact.register", f.Invocation);
        Assert.NotNull(original);
        f.Subject = f.Subject with { IdleExpiresAtUtc = f.Now.AddHours(1) };
        f.Grant = f.Grant with { ExpiresAtUtc = f.Now.AddHours(1) };
        var current = await resolver.RecheckAsync(f.Selection, original, default);
        Assert.NotNull(current); Assert.Equal(original.Digest, current.Digest);
        Assert.Equal(original.ValidUntilUtc, current.ValidUntilUtc);
        f.Grant = f.Grant with { ExpiresAtUtc = f.Now.AddSeconds(1) };
        var shorter = await resolver.RecheckAsync(f.Selection, original, default);
        Assert.NotNull(shorter); Assert.Equal(original.Digest, shorter.Digest); Assert.Equal(f.Now.AddSeconds(1), shorter.ValidUntilUtc);
        f.Now = f.Now.AddSeconds(1);
        Assert.Null(await resolver.RecheckAsync(f.Selection, original, default));
    }

    [Theory]
    [InlineData("project")]
    [InlineData("assignment")]
    [InlineData("membership")]
    [InlineData("principal")]
    public async Task Changed_then_restored_security_identity_still_changes_the_binding(string changed)
    {
        var f = new ArtifactAuthorityTestFacts(); var resolver = new ArtifactAuthorityResolver(f.Installation, f.Reader, new NoCredentialDatabase(), f, null);
        var original = await resolver.ResolveAsync(f.Selection, f.Project.ProjectId, "artifact.register", f.Invocation);
        switch (changed)
        {
            case "project": f.Project = f.Project with { Revision = 3 }; break;
            case "assignment": f.Grant = f.Grant with { AssignmentRevision = 3 }; break;
            case "membership": f.Grant = f.Grant with { MembershipRevision = 3 }; break;
            case "principal": f.Subject = f.Subject with { PrincipalRevision = 3 }; break;
        }
        var current = await resolver.RecheckAsync(f.Selection, original!, default);
        Assert.NotNull(current); Assert.NotEqual(original!.Digest, current.Digest);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("registry")]
    [InlineData("repository")]
    [InlineData("tenant")]
    [InlineData("read-action")]
    public async Task New_provider_selection_requires_exact_connection_and_owning_project(string fault)
    {
        var f = new ArtifactAuthorityTestFacts(); var configuration = Configuration(f); var contexts = new NoCredentialDatabase();
        var resolver = new ArtifactAuthorityResolver(f.Installation, f.Reader, contexts, f, fault == "missing" ? null : configuration);
        if (fault == "tenant") f.Project = f.Project with { TenantId = Guid.NewGuid() };
        if (fault == "read-action") f.SetAction("artifact.read");
        var result = await resolver.ResolveAsync(f.Selection, f.Project.ProjectId, fault == "read-action" ? "artifact.read" : "artifact.register", f.Invocation,
            fault == "registry" ? Guid.NewGuid() : f.Registry, fault == "repository" ? "other/repository" : "team/frontend");
        Assert.Null(result); Assert.Equal(0, contexts.Calls);
    }

    [Fact]
    public async Task Anonymous_connection_has_no_dummy_secret_and_configuration_digest_is_bound()
    {
        var f = new ArtifactAuthorityTestFacts(); var contexts = new NoCredentialDatabase();
        var resolver = new ArtifactAuthorityResolver(f.Installation, f.Reader, contexts, f, Configuration(f));
        var binding = await resolver.ResolveAsync(f.Selection, f.Project.ProjectId, "artifact.register", f.Invocation, f.Registry, "team/frontend");
        Assert.NotNull(binding); Assert.NotNull(binding.Registry); Assert.Null(binding.Credential); Assert.Equal(0, contexts.Calls);
        var changed = new ArtifactAuthorityResolver(f.Installation, f.Reader, contexts, f, Configuration(f, "Changed display"));
        var fresh = await changed.RecheckAsync(f.Selection, binding, default);
        Assert.NotNull(fresh); Assert.NotEqual(binding.Digest, fresh.Digest);
    }

    private static RegistryTrustConfiguration Configuration(ArtifactAuthorityTestFacts f, string display = "Synthetic")
    {
        using var key = ECDsa.Create();
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Synthetic T10 CA", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509KeyUsageExtension(System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.KeyCertSign, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            installationId = f.Installation,
            registries = new[] { new { tenantId = f.Grant.TenantId, projectId = f.Project.ProjectId, registryId = f.Registry,
                displayName = display, repository = "team/frontend", apiOrigin = "https://registry.example.test",
                destinationIps = new[] { "10.42.0.2" }, caCertificateDerBase64 = Convert.ToBase64String(certificate.RawData),
                serverSpkiSha256 = new string('a',64), authMode = "anonymous" } }
        });
        return RegistryTrustConfiguration.Parse(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), f.Installation);
    }
    private sealed class NoCredentialDatabase : IDbContextFactory<CredentialDbContext>
    {
        public int Calls;
        public CredentialDbContext CreateDbContext() { Calls++; throw new InvalidOperationException("No secret dependency is allowed in this fixture."); }
        public Task<CredentialDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }
}

internal sealed class ArtifactAuthorityTestFacts : ICurrentSubjectReader, IScopedGrantReader, IRoleDefinitionReader, IArtifactProjectAuthorityReader, IAuthorityClock
{
    public Guid Installation = Guid.NewGuid(), Invocation = Guid.NewGuid(), Registry = Guid.NewGuid();
    public DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    public CurrentSubject Subject;
    public ScopedGrant Grant;
    public ArtifactProjectAuthority Project;
    public SessionSelection Selection { get; }
    public ArtifactProjectAuthorityReader Reader { get; }
    public ArtifactAuthorityTestFacts()
    {
        Subject = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Now.AddHours(2), Now.AddMinutes(30));
        Project = new(Guid.NewGuid(), Guid.NewGuid(), 1);
        Grant = new(Subject.PrincipalId, Project.TenantId, Guid.NewGuid(), 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(), 1,
            "artifact.register", "project", Project.ProjectId, Now.AddMinutes(15));
        Selection = new(Subject.PrincipalId, "synthetic-session-reference", Subject.BootGeneration);
        Reader = new(this, this, this, this, this);
    }
    public Task<bool> IsActivePrincipalAsync(Guid principalId, CancellationToken ct = default) => Task.FromResult(principalId == Subject.PrincipalId);
    public void SetAction(string action) => Grant = Grant with { Permission = action };
    public DateTimeOffset GetTrustedUtcNow() => Now;
    public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default) => Task.FromResult<CurrentSubject?>(Subject);
    public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission, string scope, Guid scopeId, CancellationToken ct = default) => Task.FromResult<ScopedGrant?>(Grant);
    public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid id, long revision, CancellationToken ct = default) => Task.FromResult<RoleDefinitionSnapshot?>(new(Grant.DefinitionId, Grant.DefinitionRevision, "Label", [new(Grant.Permission, "project")]));
    public Task<ArtifactProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult<ArtifactProjectAuthority?>(Project);
}
