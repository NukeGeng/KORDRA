using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests.IdentityAccess;

// FEAT-IAM-001: candidate proof only. All authority/provenance is assumed by the
// synthetic fixture; none is established through authentication or owner lookup.
public sealed class AuthorizationPolicyProofTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("project", "project-a")]
    [InlineData("environment", "environment-a")]
    public void SameTenant_WithExactCatalogScopeAndAllAuthority_Permits(string kind, string id)
    {
        var facts = ValidFacts();
        facts = facts with
        {
            Request = facts.Request! with { ScopeKind = kind, ScopeId = id },
            Grant = facts.Grant! with { ScopeKind = kind, ScopeId = id }
        };

        Assert.True(Evaluate(facts, Now));
    }

    [Fact]
    public void ProjectScope_DoesNotRequireAnEnvironment()
    {
        var facts = ValidFacts();
        Assert.True(Evaluate(facts with { Owner = facts.Owner! with { EnvironmentId = null } }, Now));
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("grant")]
    [InlineData("request")]
    [InlineData("owner")]
    public void WrongTenant_InEachIndependentAuthorityOrSelection_Denies(string changed)
    {
        AssertDeniedChange(facts => changed switch
        {
            "membership" => facts with { Membership = facts.Membership! with { OrganizationId = "tenant-b" } },
            "grant" => facts with { Grant = facts.Grant! with { OrganizationId = "tenant-b" } },
            "request" => facts with { Request = facts.Request! with { OrganizationId = "tenant-b" } },
            "owner" => facts with { Owner = facts.Owner! with { OrganizationId = "tenant-b" } },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        });
    }

    [Fact]
    public void CallerForgedTenantAndScope_CannotReplaceTrustedOwnerAndMembership()
    {
        AssertDeniedChange(facts => facts with
        {
            Request = facts.Request! with { OrganizationId = "tenant-b", ScopeId = "project-b" }
        });
    }

    [Theory]
    [InlineData("session")]
    [InlineData("membership")]
    [InlineData("grant")]
    public void MismatchedSubject_Denies(string changed)
    {
        AssertDeniedChange(facts => changed switch
        {
            "session" => facts with { Session = facts.Session! with { PrincipalId = "principal-b" } },
            "membership" => facts with { Membership = facts.Membership! with { PrincipalId = "principal-b" } },
            "grant" => facts with { Grant = facts.Grant! with { PrincipalId = "principal-b" } },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        });
    }

    [Theory]
    [InlineData("PRINCIPAL-A")]
    [InlineData("principal-a-extra")]
    [InlineData(" principal-a ")]
    public void SubjectIdentifiers_AreOpaqueOrdinalValues(string principal)
    {
        AssertDeniedChange(facts => facts with { Grant = facts.Grant! with { PrincipalId = principal } });
    }

    [Fact]
    public void RequestedTarget_MustMatchAuthoritativeOwner()
    {
        AssertDeniedChange(facts => facts with { Request = facts.Request! with { TargetId = "target-b" } });
    }

    [Theory]
    [InlineData("project", "project-a")]
    [InlineData("environment", "environment-a")]
    public void ScopeMismatch_InRequestGrantOrTrustedOwner_Denies(string kind, string id)
    {
        var facts = ValidFacts();
        facts = facts with
        {
            Request = facts.Request! with { ScopeKind = kind, ScopeId = id },
            Grant = facts.Grant! with { ScopeKind = kind, ScopeId = id }
        };
        Assert.True(Evaluate(facts, Now));

        Assert.False(Evaluate(facts with { Request = facts.Request! with { ScopeId = "other-scope" } }, Now));
        Assert.False(Evaluate(facts with { Grant = facts.Grant! with { ScopeId = "other-scope" } }, Now));
        // Even agreeing caller/grant scope cannot override the trusted ownership chain.
        Assert.False(Evaluate(facts with
        {
            Request = facts.Request! with { ScopeId = "other-scope" },
            Grant = facts.Grant! with { ScopeId = "other-scope" }
        }, Now));
    }

    [Theory]
    [InlineData("project", "project-a", "environment", "environment-a")]
    [InlineData("environment", "environment-a", "project", "project-a")]
    public void ProjectAndEnvironmentScopes_DoNotConvertImplicitly(
        string requestKind, string requestId, string grantKind, string grantId)
    {
        AssertDeniedChange(facts => facts with
        {
            Request = facts.Request! with { ScopeKind = requestKind, ScopeId = requestId },
            Grant = facts.Grant! with { ScopeKind = grantKind, ScopeId = grantId }
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void EnvironmentScope_WithoutAnOwnerEnvironment_Denies(string? environment)
    {
        var facts = ValidFacts();
        facts = facts with
        {
            Request = facts.Request! with { ScopeKind = "environment", ScopeId = "environment-a" },
            Grant = facts.Grant! with { ScopeKind = "environment", ScopeId = "environment-a" }
        };
        Assert.True(Evaluate(facts, Now));
        Assert.False(Evaluate(facts with { Owner = facts.Owner! with { EnvironmentId = environment } }, Now));
        Assert.False(Evaluate(facts with { Owner = facts.Owner! with { ProjectId = null } }, Now));
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("organization")]
    [InlineData("target")]
    [InlineData("unknown")]
    [InlineData("PROJECT")]
    public void PlatformOrUnsupportedScope_EvenWithTargetRead_Denies(string kind)
    {
        AssertDeniedChange(facts => facts with
        {
            Request = facts.Request! with { ScopeKind = kind },
            Grant = facts.Grant! with { ScopeKind = kind }
        });
    }

    [Fact]
    public void PlatformOnlyAuthority_DoesNotGrantTenantAccess()
    {
        AssertDeniedChange(facts => facts with
        {
            Grant = facts.Grant! with
            {
                OrganizationId = null,
                Permission = "platform.system.manage",
                ScopeKind = "platform",
                ScopeId = "platform"
            }
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("target.manage")]
    [InlineData("platform.system.manage")]
    [InlineData("TARGET.READ")]
    public void RequestAndGrant_EachRequireExactTargetReadPermission(string? permission)
    {
        AssertDeniedChange(facts => facts with { Request = facts.Request! with { Permission = permission } });
        AssertDeniedChange(facts => facts with { Grant = facts.Grant! with { Permission = permission } });
        AssertDeniedChange(facts => facts with
        {
            Request = facts.Request! with { Permission = permission },
            Grant = facts.Grant! with { Permission = permission }
        });
    }

    [Theory]
    [InlineData("request")]
    [InlineData("identity/session")]
    [InlineData("membership")]
    [InlineData("grant")]
    [InlineData("owner")]
    [InlineData("policy")]
    public void MissingAuthorityOrRequest_Denies(string missing)
    {
        AssertDeniedChange(facts => missing switch
        {
            "request" => facts with { Request = null },
            "identity/session" => facts with { Session = null },
            "membership" => facts with { Membership = null },
            "grant" => facts with { Grant = null },
            "owner" => facts with { Owner = null },
            "policy" => facts with { Policy = null },
            _ => throw new ArgumentOutOfRangeException(nameof(missing))
        });
    }

    [Fact]
    public void UnvalidatedIdentityOrInactiveMembership_Denies()
    {
        AssertDeniedChange(facts => facts with { Session = facts.Session! with { Validated = false } });
        AssertDeniedChange(facts => facts with { Membership = facts.Membership! with { Active = false } });
    }

    [Theory]
    [InlineData("session")]
    [InlineData("membership")]
    [InlineData("grant")]
    public void RevokedAuthority_IndependentlyDenies(string authority)
    {
        AssertDeniedChange(facts => authority switch
        {
            "session" => facts with { Session = facts.Session! with { Revoked = true } },
            "membership" => facts with { Membership = facts.Membership! with { Revoked = true } },
            "grant" => facts with { Grant = facts.Grant! with { Revoked = true } },
            _ => throw new ArgumentOutOfRangeException(nameof(authority))
        });
    }

    [Theory]
    [InlineData("session")]
    [InlineData("membership")]
    [InlineData("grant")]
    public void Expiry_IsExclusiveForEachAuthority(string authority)
    {
        var facts = ValidFacts();
        facts = authority switch
        {
            "session" => facts with { Session = facts.Session! with { ExpiresAtUtc = Now } },
            "membership" => facts with { Membership = facts.Membership! with { ExpiresAtUtc = Now } },
            "grant" => facts with { Grant = facts.Grant! with { ExpiresAtUtc = Now } },
            _ => throw new ArgumentOutOfRangeException(nameof(authority))
        };
        Assert.True(Evaluate(facts, Now.AddTicks(-1)));
        Assert.False(Evaluate(facts, Now));
        Assert.False(Evaluate(facts, Now.AddTicks(1)));
    }

    [Fact]
    public void UnavailableOrDenyingPolicy_OverridesPositiveGrant()
    {
        AssertDeniedChange(facts => facts with { Policy = facts.Policy! with { Available = false } });
        AssertDeniedChange(facts => facts with { Policy = facts.Policy! with { ExplicitDeny = true } });
    }

    [Theory]
    [InlineData("request-target")]
    [InlineData("request-tenant")]
    [InlineData("request-permission")]
    [InlineData("request-scope-kind")]
    [InlineData("request-scope-id")]
    [InlineData("session-principal")]
    [InlineData("membership-principal")]
    [InlineData("membership-tenant")]
    [InlineData("owner-target")]
    [InlineData("owner-tenant")]
    [InlineData("owner-project")]
    [InlineData("grant-principal")]
    [InlineData("grant-tenant")]
    [InlineData("grant-permission")]
    [InlineData("grant-scope-kind")]
    [InlineData("grant-scope-id")]
    [InlineData("policy-revision")]
    public void MissingOrBlankRequiredValues_Deny(string field)
    {
        foreach (var value in new string?[] { null, "", " \t" })
        {
            AssertDeniedChange(facts => field switch
            {
                "request-target" => facts with { Request = facts.Request! with { TargetId = value } },
                "request-tenant" => facts with { Request = facts.Request! with { OrganizationId = value } },
                "request-permission" => facts with { Request = facts.Request! with { Permission = value } },
                "request-scope-kind" => facts with { Request = facts.Request! with { ScopeKind = value } },
                "request-scope-id" => facts with { Request = facts.Request! with { ScopeId = value } },
                "session-principal" => facts with { Session = facts.Session! with { PrincipalId = value } },
                "membership-principal" => facts with { Membership = facts.Membership! with { PrincipalId = value } },
                "membership-tenant" => facts with { Membership = facts.Membership! with { OrganizationId = value } },
                "owner-target" => facts with { Owner = facts.Owner! with { TargetId = value } },
                "owner-tenant" => facts with { Owner = facts.Owner! with { OrganizationId = value } },
                "owner-project" => facts with { Owner = facts.Owner! with { ProjectId = value } },
                "grant-principal" => facts with { Grant = facts.Grant! with { PrincipalId = value } },
                "grant-tenant" => facts with { Grant = facts.Grant! with { OrganizationId = value } },
                "grant-permission" => facts with { Grant = facts.Grant! with { Permission = value } },
                "grant-scope-kind" => facts with { Grant = facts.Grant! with { ScopeKind = value } },
                "grant-scope-id" => facts with { Grant = facts.Grant! with { ScopeId = value } },
                "policy-revision" => facts with { Policy = facts.Policy! with { Revision = value } },
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            });
        }
    }

    [Fact]
    public void Evaluation_IsRepeatableAndDoesNotMutateInputsOrCacheAPermit()
    {
        var facts = ValidFacts();
        var before = ValidFacts();
        var revoked = facts with { Grant = facts.Grant! with { Revoked = true } };
        var changedPolicy = facts with { Policy = facts.Policy! with { ExplicitDeny = true } };
        var changedOwner = facts with { Owner = facts.Owner! with { OrganizationId = "tenant-b" } };

        for (var i = 0; i < 3; i++)
        {
            Assert.True(Evaluate(facts, Now));
            Assert.False(Evaluate(revoked, Now));
            Assert.False(Evaluate(changedPolicy, Now));
            Assert.False(Evaluate(changedOwner, Now));
            Assert.False(Evaluate(facts, Now.AddHours(1)));
        }

        Assert.Equal(before, facts);
        Assert.True(revoked.Grant!.Revoked);
        Assert.True(changedPolicy.Policy!.ExplicitDeny);
        Assert.Equal("tenant-b", changedOwner.Owner!.OrganizationId);
    }

    private static void AssertDeniedChange(Func<SyntheticFacts, SyntheticFacts> change)
    {
        var facts = ValidFacts();
        Assert.True(Evaluate(facts, Now));
        Assert.False(Evaluate(change(facts), Now));
    }

    private static SyntheticFacts ValidFacts() => new(
        new("target-a", "tenant-a", "target.read", "project", "project-a"),
        new("principal-a", true, Now.AddHours(1), false),
        new("principal-a", "tenant-a", true, Now.AddHours(1), false),
        new("target-a", "tenant-a", "project-a", "environment-a"),
        new("principal-a", "tenant-a", "target.read", "project", "project-a", Now.AddHours(1), false),
        new(true, "synthetic-policy-v1", false));

    // Pure candidate predicate. Null facts stand for missing/unavailable authority.
    // The request is untrusted selection; only the synthetic owner binds resource scope.
    private static bool Evaluate(SyntheticFacts facts, DateTimeOffset nowUtc)
    {
        var (request, session, membership, owner, grant, policy) = facts;
        if (request is null || session is null || membership is null || owner is null ||
            grant is null || policy is null)
            return false;

        string?[] required =
        [
            request.TargetId, request.OrganizationId, request.Permission, request.ScopeKind, request.ScopeId,
            session.PrincipalId, membership.PrincipalId, membership.OrganizationId,
            owner.TargetId, owner.OrganizationId, owner.ProjectId,
            grant.PrincipalId, grant.OrganizationId, grant.Permission, grant.ScopeKind, grant.ScopeId,
            policy.Revision
        ];
        if (required.Any(string.IsNullOrWhiteSpace))
            return false;

        if (!session.Validated || session.Revoked || nowUtc >= session.ExpiresAtUtc ||
            !membership.Active || membership.Revoked || nowUtc >= membership.ExpiresAtUtc ||
            grant.Revoked || nowUtc >= grant.ExpiresAtUtc || !policy.Available || policy.ExplicitDeny)
            return false;

        if (!Matches(membership.PrincipalId, session.PrincipalId) ||
            !Matches(grant.PrincipalId, session.PrincipalId) ||
            !Matches(membership.OrganizationId, owner.OrganizationId) ||
            !Matches(grant.OrganizationId, owner.OrganizationId) ||
            !Matches(request.OrganizationId, owner.OrganizationId) ||
            !Matches(request.TargetId, owner.TargetId) ||
            !Matches(request.Permission, "target.read") || !Matches(grant.Permission, "target.read") ||
            !Matches(request.ScopeKind, grant.ScopeKind) || !Matches(request.ScopeId, grant.ScopeId))
            return false;

        return grant.ScopeKind switch
        {
            "project" => Matches(grant.ScopeId, owner.ProjectId),
            "environment" => !string.IsNullOrWhiteSpace(owner.EnvironmentId) &&
                Matches(grant.ScopeId, owner.EnvironmentId),
            _ => false
        };
    }

    private static bool Matches(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    // Immutable test-private values; not API, domain or persistence contracts.
    private sealed record SyntheticFacts(Request? Request, Session? Session, Membership? Membership,
        TargetOwner? Owner, Grant? Grant, PolicyAuthority? Policy);
    private sealed record Request(string? TargetId, string? OrganizationId, string? Permission,
        string? ScopeKind, string? ScopeId);
    private sealed record Session(string? PrincipalId, bool Validated, DateTimeOffset ExpiresAtUtc, bool Revoked);
    private sealed record Membership(string? PrincipalId, string? OrganizationId, bool Active,
        DateTimeOffset ExpiresAtUtc, bool Revoked);
    private sealed record TargetOwner(string? TargetId, string? OrganizationId, string? ProjectId,
        string? EnvironmentId);
    private sealed record Grant(string? PrincipalId, string? OrganizationId, string? Permission,
        string? ScopeKind, string? ScopeId, DateTimeOffset ExpiresAtUtc, bool Revoked);
    private sealed record PolicyAuthority(bool Available, string? Revision, bool ExplicitDeny);
}
