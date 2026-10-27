using Kordra.ArtifactManagement.Domain;
using Xunit;

namespace Kordra.ArtifactManagement.Domain.Tests;

public sealed class ArtifactRegistrationTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Project = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Registry = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Installation = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Principal = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const string Key = "request-00000001";

    private static ArtifactIdentity Identity(string repository = "team/frontend", Guid? tenant = null,
        Guid? project = null, Guid? registry = null, ArtifactPlatform? platform = null, string? digest = null) =>
        ArtifactIdentity.Create(tenant ?? Tenant, project ?? Project, registry ?? Registry,
            repository, OCIImageDigest.Parse(digest ?? "sha256:" + new string('a', 64)),
            platform ?? ArtifactPlatform.Create("linux", "arm64", null));

    [Theory]
    [InlineData("frontend")]
    [InlineData("team/frontend")]
    [InlineData("team_1/frontend__service.v1--beta")]
    [InlineData("registry.example/team/frontend")]
    public void Repository_is_preserved_exactly(string repository) =>
        Assert.Equal(repository, Identity(repository).Repository);

    [Theory]
    [InlineData("")]
    [InlineData("Team/frontend")]
    [InlineData("team/frontend:latest")]
    [InlineData("https://registry.example/frontend")]
    [InlineData("/team/frontend")]
    [InlineData("team//frontend")]
    [InlineData("team/../frontend")]
    [InlineData("team/./frontend")]
    [InlineData("team/frontend/")]
    [InlineData("team/%66rontend")]
    [InlineData("team\\frontend")]
    [InlineData("team/frontend?token=canary")]
    [InlineData("team/frontend#fragment")]
    [InlineData("team/frontend\r\n")]
    [InlineData(" team/frontend")]
    [InlineData("team/fröntend")]
    [InlineData("team/frontend___service")]
    public void Invalid_repository_never_becomes_a_normalized_identity(string repository)
    {
        var exception = Assert.Throws<FormatException>(() => Identity(repository));
        Assert.Equal("Invalid artifact identity.", exception.Message);
    }

    [Fact]
    public void Repository_length_and_null_are_bounded()
    {
        Assert.Equal(255, Identity(new string('a', 255)).Repository.Length);
        Assert.Throws<FormatException>(() => Identity(new string('a', 256)));
        Assert.Throws<FormatException>(() => Identity(null!));
    }

    [Theory]
    [InlineData("linux", "amd64", null)]
    [InlineData("linux", "arm64", null)]
    [InlineData("linux", "arm64", "v8")]
    public void Only_explicit_supported_platforms_construct(string os, string architecture, string? variant)
    {
        var value = ArtifactPlatform.Create(os, architecture, variant);
        Assert.Equal(os, value.Os);
        Assert.Equal(architecture, value.Architecture);
        Assert.Equal(variant, value.Variant);
    }

    [Theory]
    [InlineData("Linux", "arm64", null)]
    [InlineData("windows", "amd64", null)]
    [InlineData("linux", "x86_64", null)]
    [InlineData("linux", "arm64", "")]
    [InlineData("linux", "amd64", "v8")]
    [InlineData("linux", "arm64", "v7")]
    [InlineData("linux", "arm64", "V8")]
    [InlineData(null, "arm64", null)]
    public void Platform_does_not_guess_or_normalize(string? os, string architecture, string? variant) =>
        Assert.Throws<FormatException>(() => ArtifactPlatform.Create(os!, architecture, variant));

    [Fact]
    public void Every_identity_component_is_required_and_immutable()
    {
        Assert.Throws<FormatException>(() => Identity(tenant: Guid.Empty));
        Assert.Throws<FormatException>(() => Identity(project: Guid.Empty));
        Assert.Throws<FormatException>(() => Identity(registry: Guid.Empty));
        Assert.Throws<FormatException>(() => ArtifactIdentity.Create(Tenant, Project, Registry,
            "team/frontend", null!, ArtifactPlatform.Create("linux", "amd64", null)));
        Assert.Throws<FormatException>(() => ArtifactIdentity.Create(Tenant, Project, Registry,
            "team/frontend", OCIImageDigest.Parse("sha256:" + new string('a', 64)), null!));
        foreach (var type in new[] { typeof(ArtifactIdentity), typeof(ArtifactPlatform), typeof(ArtifactSourceClaim) })
        {
            Assert.Empty(type.GetConstructors());
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        }
    }

    [Fact]
    public void Distinct_content_and_owner_tuples_cannot_share_identity()
    {
        var original = Identity();
        var alternatives = new[]
        {
            Identity(tenant: Guid.NewGuid()), Identity(project: Guid.NewGuid()), Identity(registry: Guid.NewGuid()),
            Identity("team/other"), Identity(digest: "sha512:" + new string('a', 128)),
            Identity(platform: ArtifactPlatform.Create("linux", "amd64", null)),
            Identity(platform: ArtifactPlatform.Create("linux", "arm64", "v8"))
        };
        Assert.Equal(original, Identity());
        Assert.All(alternatives, other => Assert.NotEqual(original.ComputeIdentityHash(), other.ComputeIdentityHash()));
    }

    [Theory]
    [InlineData("team/../repo", 40, null)]
    [InlineData("team//repo", 40, null)]
    [InlineData("https://source/repo", 40, null)]
    [InlineData("TEAM/repo", 40, null)]
    [InlineData("team/repo", 39, null)]
    [InlineData("team/repo", 41, null)]
    [InlineData("team/repo", 65, null)]
    [InlineData("team/repo", 40, "")]
    [InlineData("team/repo", 40, "build/token")]
    [InlineData("team/repo", 40, "canary\nsecret")]
    public void Invalid_declaration_never_echoes_untrusted_text(string repository, int commitLength, string? buildRef)
    {
        var exception = Assert.Throws<FormatException>(() =>
            ArtifactSourceClaim.Create(repository, new string('a', commitLength), buildRef));
        Assert.Equal("Invalid artifact source declaration.", exception.Message);
    }

    [Fact]
    public void Declaration_boundaries_are_exact_and_do_not_change_artifact_identity()
    {
        var claim = ArtifactSourceClaim.Create(new string('a', 128), new string('b', 64), new string('A', 128));
        Assert.Equal(64, claim.SourceCommit.Length);
        Assert.Throws<FormatException>(() => ArtifactSourceClaim.Create(new string('a', 129), new string('a', 40), null));
        Assert.Throws<FormatException>(() => ArtifactSourceClaim.Create("repo", new string('A', 40), null));
        Assert.Throws<FormatException>(() => ArtifactSourceClaim.Create("repo", new string('a', 40), new string('A', 129)));
        Assert.Throws<FormatException>(() => ArtifactSourceClaim.Create(null!, new string('a', 40), null));
        Assert.Throws<FormatException>(() => ArtifactSourceClaim.Create("repo", null!, null));
        var identity = Identity();
        var noClaim = new ArtifactRegistrationRequest(identity, null);
        var declared = new ArtifactRegistrationRequest(identity, claim);
        Assert.Equal(noClaim.Identity, declared.Identity);
        Assert.NotEqual(noClaim.ComputeBusinessHash(), declared.ComputeBusinessHash());
    }

    [Fact]
    public void Canonical_vectors_match_independent_compact_UTF8_JSON_reference()
    {
        // Constants computed independently with Python json.dumps/separators + hashlib.sha256.
        // They pin domain separators, key order, lowercase GUIDs, explicit nulls and no whitespace.
        var identity = Identity();
        Assert.Equal("e783d471a7aa73818ddc773738cb1946c35057e782cb83377788f33f70925075", identity.ComputeIdentityHash());
        var request = new ArtifactRegistrationRequest(identity, null);
        Assert.Equal("32aa057e5eed0bb201aae69352bb01b1fb1d242037c8c56e7965966437f450d3", request.ComputeBusinessHash());
        Assert.Equal("815dcf6ee894bc5844025d3bf8e88ca87f7f282b76278f4e1ca493574aabffad",
            request.ComputeIdempotencyKeyHash(Installation, Principal, Key));
        var declared = new ArtifactRegistrationRequest(identity,
            ArtifactSourceClaim.Create("team/frontend", new string('b', 40), null));
        Assert.Equal("4cbbd91b3987e3584a896a0a71f0e722b89906a320a9912d8b854c96f80f1a8b", declared.ComputeBusinessHash());
    }

    [Fact]
    public void Key_scope_is_separate_from_business_conflict_detection()
    {
        var request = new ArtifactRegistrationRequest(Identity(), null);
        var hash = request.ComputeIdempotencyKeyHash(Installation, Principal, Key);
        Assert.NotEqual(hash, request.ComputeIdempotencyKeyHash(Guid.NewGuid(), Principal, Key));
        Assert.NotEqual(hash, request.ComputeIdempotencyKeyHash(Installation, Guid.NewGuid(), Key));
        Assert.NotEqual(hash, request.ComputeIdempotencyKeyHash(Installation, Principal, "request-00000002"));
        foreach (var otherIdentity in new[] { Identity(tenant: Guid.NewGuid()), Identity(project: Guid.NewGuid()) })
        {
            Assert.NotEqual(hash, new ArtifactRegistrationRequest(otherIdentity, null)
                .ComputeIdempotencyKeyHash(Installation, Principal, Key));
        }

        var changedBody = new ArtifactRegistrationRequest(Identity("team/other", registry: Guid.NewGuid()), null);
        Assert.Equal(hash, changedBody.ComputeIdempotencyKeyHash(Installation, Principal, Key));
        Assert.NotEqual(request.ComputeBusinessHash(), changedBody.ComputeBusinessHash());
    }

    [Theory]
    [InlineData(15, 'a')]
    [InlineData(129, 'a')]
    [InlineData(16, '/')]
    [InlineData(16, ' ')]
    [InlineData(16, '\n')]
    [InlineData(16, 'é')]
    public void Invalid_keys_are_rejected_without_echo(int length, char character)
    {
        var request = new ArtifactRegistrationRequest(Identity(), null);
        var exception = Assert.Throws<FormatException>(() =>
            request.ComputeIdempotencyKeyHash(Installation, Principal, new string(character, length)));
        Assert.Equal("Invalid artifact registration key scope.", exception.Message);
    }

    [Fact]
    public async Task No_key_or_canonical_buffer_is_shared_between_calls()
    {
        var request = new ArtifactRegistrationRequest(Identity(), null);
        Assert.Throws<FormatException>(() => request.ComputeIdempotencyKeyHash(Guid.Empty, Principal, Key));
        Assert.Throws<FormatException>(() => request.ComputeIdempotencyKeyHash(Installation, Guid.Empty, Key));
        Assert.Throws<FormatException>(() => request.ComputeIdempotencyKeyHash(Installation, Principal, null!));
        Assert.Throws<ArgumentNullException>(() => new ArtifactRegistrationRequest(null!, null));
        Assert.Equal(64, request.ComputeIdempotencyKeyHash(Installation, Principal, new string('a', 128)).Length);
        var hashes = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(request.ComputeBusinessHash)));
        Assert.All(hashes, hash => Assert.Equal(hashes[0], hash));
    }

    private static readonly DateTimeOffset Created = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static ArtifactRegistrationReceipt Pending() => ArtifactRegistrationReceipt.Pending(
        Guid.NewGuid(), Installation, Guid.NewGuid(), Guid.NewGuid(), Principal,
        new ArtifactRegistrationRequest(Identity(), null), Key, Created);

    [Fact]
    public void Pending_contains_scoped_hashes_and_no_success_facts()
    {
        var pending = Pending();
        Assert.Equal(ArtifactRegistrationState.Pending, pending.State);
        Assert.Equal("815dcf6ee894bc5844025d3bf8e88ca87f7f282b76278f4e1ca493574aabffad", pending.KeyHash);
        Assert.Equal(pending.Request.ComputeBusinessHash(), pending.BusinessHash);
        Assert.Null(pending.ArtifactId);
        Assert.Null(pending.CompletedAtUtc);
        Assert.Null(pending.AttemptVerifiedAtUtc);
        Assert.Null(pending.SafeReason);
        Assert.Empty(typeof(ArtifactRegistrationReceipt).GetConstructors());
        Assert.All(typeof(ArtifactRegistrationReceipt).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void Registered_preserves_attempt_identity_and_actual_capture_time()
    {
        var pending = Pending();
        var artifact = Guid.NewGuid();
        var registered = pending.Register(artifact, Identity(), Created.AddSeconds(1), Created.AddSeconds(2));
        Assert.Equal(ArtifactRegistrationState.Registered, registered.State);
        Assert.Equal(artifact, registered.ArtifactId);
        Assert.Equal(Created.AddSeconds(1), registered.AttemptVerifiedAtUtc);
        Assert.Equal(Created.AddSeconds(2), registered.CompletedAtUtc);
        Assert.Equal(pending.Id, registered.Id);
        Assert.Equal(pending.BootId, registered.BootId);
        Assert.Equal(pending.InvocationId, registered.InvocationId);
        Assert.Equal(pending.PrincipalId, registered.PrincipalId);
        Assert.Equal(pending.InstallationId, registered.InstallationId);
        Assert.Equal(pending.CreatedAtUtc, registered.CreatedAtUtc);
        Assert.Equal(pending.KeyHash, registered.KeyHash);
        Assert.Equal(pending.BusinessHash, registered.BusinessHash);
        // These are immutable candidate values: only the future DB transaction can establish durability.
        Assert.Equal(ArtifactRegistrationState.Pending, pending.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Terminal_values_cannot_reopen_change_or_re_register(int terminal)
    {
        var pending = Pending();
        var value = terminal switch
        {
            0 => pending.Register(Guid.NewGuid(), Identity(), Created, Created.AddSeconds(1)),
            1 => pending.Fail(ArtifactRegistrationFailure.AuthorityLost, Created.AddSeconds(1)),
            _ => pending.Abandon(Created.AddSeconds(1))
        };
        Assert.Throws<InvalidOperationException>(() => value.Register(Guid.NewGuid(), Identity(), Created, Created.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => value.Fail(ArtifactRegistrationFailure.Cancelled, Created.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => value.Abandon(Created.AddSeconds(2)));
    }

    [Fact]
    public void Different_owner_or_content_cannot_complete_a_pending_identity()
    {
        var pending = Pending();
        foreach (var other in new[] { Identity(tenant: Guid.NewGuid()), Identity(project: Guid.NewGuid()), Identity("team/other") })
        {
            Assert.Throws<InvalidOperationException>(() => pending.Register(Guid.NewGuid(), other, Created, Created));
        }
        Assert.Throws<InvalidOperationException>(() => pending.Register(Guid.Empty, Identity(), Created, Created));
        Assert.Throws<InvalidOperationException>(() => pending.Register(Guid.NewGuid(), Identity(), Created.AddSeconds(-1), Created));
        Assert.Throws<InvalidOperationException>(() => pending.Register(Guid.NewGuid(), Identity(), Created.AddSeconds(1), Created));
        Assert.Throws<InvalidOperationException>(() => pending.Fail(ArtifactRegistrationFailure.Cancelled, Created.AddSeconds(-1)));
    }

    [Fact]
    public void Failure_and_abandonment_never_retain_artifact_or_verification_facts()
    {
        foreach (var failure in Enum.GetValues<ArtifactRegistrationFailure>())
        {
            var failed = Pending().Fail(failure, Created.AddSeconds(1));
            Assert.Equal(ArtifactRegistrationState.Failed, failed.State);
            Assert.Matches("^[a-z_]+$", failed.SafeReason!);
            Assert.Null(failed.ArtifactId);
            Assert.Null(failed.AttemptVerifiedAtUtc);
        }
        var abandoned = Pending().Abandon(Created.AddSeconds(1));
        Assert.Equal(ArtifactRegistrationState.Abandoned, abandoned.State);
        Assert.Equal("interrupted_attempt", abandoned.SafeReason);
        Assert.Null(abandoned.ArtifactId);
        Assert.Null(abandoned.AttemptVerifiedAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => Pending().Fail((ArtifactRegistrationFailure)999, Created));
    }

    [Fact]
    public void Receipt_rejects_missing_identity_and_unbounded_or_non_UTC_times()
    {
        var request = new ArtifactRegistrationRequest(Identity(), null);
        var id = Guid.NewGuid(); var boot = Guid.NewGuid(); var invocation = Guid.NewGuid();
        Assert.Throws<FormatException>(() => ArtifactRegistrationReceipt.Pending(Guid.Empty, Installation, boot, invocation, Principal, request, Key, Created));
        Assert.Throws<FormatException>(() => ArtifactRegistrationReceipt.Pending(id, Installation, Guid.Empty, invocation, Principal, request, Key, Created));
        Assert.Throws<FormatException>(() => ArtifactRegistrationReceipt.Pending(id, Installation, boot, Guid.Empty, Principal, request, Key, Created));
        Assert.Throws<FormatException>(() => ArtifactRegistrationReceipt.Pending(id, Installation, boot, invocation, Principal, null!, Key, Created));
        foreach (var invalid in new[] { default(DateTimeOffset), DateTimeOffset.MaxValue, Created.ToOffset(TimeSpan.FromHours(7)) })
        {
            Assert.Throws<FormatException>(() => ArtifactRegistrationReceipt.Pending(id, Installation, boot, invocation, Principal, request, Key, invalid));
            Assert.Throws<FormatException>(() => Pending().Abandon(invalid));
            Assert.Throws<FormatException>(() => Pending().Register(Guid.NewGuid(), Identity(), invalid, Created));
        }
    }
}
