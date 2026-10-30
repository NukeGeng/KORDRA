using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class ApprovalAuthorityEvidenceTests
{
    private readonly Guid _principal = Guid.NewGuid(), _tenant = Guid.NewGuid(), _member = Guid.NewGuid(), _project = Guid.NewGuid(), _environment = Guid.NewGuid();
    private DurablePrincipal Principal => new(_principal, 2, true, new string('a', 64));
    private ScopedGrant Grant(string permission, string scope, Guid id) => new(_principal, _tenant, _member, 3, 4,
        Guid.NewGuid(), 5, Guid.NewGuid(), 6, permission, scope, id, null);
    [Fact]
    public void CapturesRequesterAndApproverOnlyForTheExactAcceptedScopes()
    {
        var request = Grant("release.request-approval", "environment", _environment);
        var projectRead = Grant("release.read", "project", _project);
        var evidence = ApprovalAuthorityEvidence.CaptureRequester(Principal, request, projectRead, _tenant, _project, _environment);
        Assert.Equal(request, evidence.EnvironmentGrant); Assert.Equal(projectRead, evidence.CompanionGrant);
        var approve = Grant("release.approve", "environment", _environment);
        var approvalRead = Grant("release.approval.read", "environment", _environment);
        Assert.Equal(approvalRead, ApprovalAuthorityEvidence.CaptureApprover(Principal, approve, approvalRead, _tenant, _environment).CompanionGrant);
    }
    [Theory]
    [InlineData("tenant")]
    [InlineData("principal")]
    [InlineData("membership")]
    [InlineData("revision")]
    [InlineData("project")]
    [InlineData("environment")]
    [InlineData("permission")]
    [InlineData("stamp")]
    public void RejectsMismatchedOrMalformedEvidence(string fault)
    {
        var p = Principal; var a = Grant("release.request-approval", "environment", _environment);
        var b = Grant("release.read", "project", _project);
        switch (fault)
        {
            case "tenant": b = b with { TenantId = Guid.NewGuid() }; break;
            case "principal": b = b with { PrincipalId = Guid.NewGuid() }; break;
            case "membership": b = b with { MembershipId = Guid.NewGuid() }; break;
            case "revision": b = b with { MembershipRevision = 5 }; break;
            case "project": b = b with { ScopeId = Guid.NewGuid() }; break;
            case "environment": a = a with { ScopeId = Guid.NewGuid() }; break;
            case "permission": b = b with { Permission = "release.create" }; break;
            case "stamp": p = p with { SecurityRevisionHash = new string('A', 64) }; break;
        }
        Assert.Throws<ArgumentException>(() => ApprovalAuthorityEvidence.CaptureRequester(p, a, b, _tenant, _project, _environment));
    }
}
