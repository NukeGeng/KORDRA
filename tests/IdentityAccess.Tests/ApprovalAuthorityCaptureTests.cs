using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class ApprovalAuthorityCaptureTests
{
    private readonly Owners _o = new();
    private ApprovalAuthorityCapture Capture => new(_o, _o, _o, _o, _o, _o, _o);
    [Fact]
    public async Task ReaderUsesOnlyExactEnvironmentReadGrantAndCannotBecomeDurableApprover()
    {
        var read = await Capture.CaptureReadUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment);
        Assert.NotNull(read); Assert.Equal("release.approval.read", read.Evidence.EnvironmentGrant.Permission);
        Assert.Equal(read.Evidence.EnvironmentGrant, read.Evidence.CompanionGrant);
        Assert.Throws<ArgumentException>(() => ApprovalAuthorityEvidenceCodec.Encode(read.Evidence));
        _o.Fault = "approval-read-deny";
        Assert.Null(await Capture.CaptureReadUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment));
    }
    [Fact]
    public async Task RecaptureBindsSessionAndOwnerRevisionsForCommitComparison()
    {
        var original = await Capture.CaptureRequesterUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment);
        Assert.NotNull(original);
        Assert.True(original.SameAuthorityAs(await Capture.CaptureRequesterUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment)));
        _o.ProjectRevision++;
        Assert.False(original.SameAuthorityAs(await Capture.CaptureRequesterUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment)));
        _o.ProjectRevision--; _o.EnvironmentRevision++;
        Assert.False(original.SameAuthorityAs(await Capture.CaptureRequesterUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment)));
        _o.EnvironmentRevision--; _o.Fault = "session-replaced";
        Assert.False(original.SameAuthorityAs(await Capture.CaptureRequesterUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment)));
        _o.Fault = null;
        Assert.False(original.SameAuthorityAs(await Capture.CaptureApproverUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment)));
        Assert.False(original.SameAuthorityAs(null));
        Assert.Equal("ApprovalAuthorityCaptureResult [private]", original.ToString());
    }
    [Fact]
    public async Task RevokerRequiresExactRevokeAndReadGrantsAndCannotBecomeDurableApprover()
    {
        var result = await Capture.CaptureRevokerUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment);
        Assert.NotNull(result);
        Assert.Equal("release.approval.revoke", result.Evidence.EnvironmentGrant.Permission);
        Assert.Equal("release.approval.read", result.Evidence.CompanionGrant.Permission);
        Assert.Equal(_o.Environment, result.Evidence.CompanionGrant.ScopeId);
        Assert.Throws<ArgumentException>(() => ApprovalAuthorityEvidenceCodec.Encode(result.Evidence));
        _o.Fault = "revoker-deny";
        Assert.Null(await Capture.CaptureRevokerUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment));
    }
    [Theory]
    [InlineData("session")]
    [InlineData("tenant")]
    [InlineData("environment")]
    [InlineData("principal")]
    [InlineData("grant")]
    [InlineData("expired")]
    [InlineData("clock-backwards")]
    [InlineData("approval-read-deny")]
    public async Task InvalidRevokerFactsCannotAuthorizeRevocation(string fault)
    {
        _o.Fault = fault;
        Assert.Null(await Capture.CaptureRevokerUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment));
    }
    [Fact]
    public async Task CurrentSessionAndBothOwnerGrantsProduceBoundEvidence()
    {
        var result = await Capture.CaptureRequesterUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment);
        Assert.NotNull(result); Assert.Equal(_o.Tenant, result.Evidence.EnvironmentGrant.TenantId);
        Assert.Equal(_o.Environment, result.Evidence.EnvironmentGrant.ScopeId);
        Assert.Equal(_o.Project, result.Evidence.CompanionGrant.ScopeId);
        Assert.Equal(_o.Now.AddMinutes(5), result.SessionValidUntilUtc);
    }
    [Fact]
    public async Task ApproverRequiresBothEnvironmentPermissions()
    {
        var result = await Capture.CaptureApproverUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment);
        Assert.NotNull(result);
        Assert.Equal("release.approve", result.Evidence.EnvironmentGrant.Permission);
        Assert.Equal("release.approval.read", result.Evidence.CompanionGrant.Permission);
        Assert.Equal(_o.Environment, result.Evidence.CompanionGrant.ScopeId);
    }
    [Theory]
    [InlineData("session")]
    [InlineData("tenant")]
    [InlineData("environment")]
    [InlineData("principal")]
    [InlineData("grant")]
    [InlineData("role-deny")]
    [InlineData("expired")]
    [InlineData("clock-backwards")]
    public async Task InvalidOrChangedOwnerFactsNeverProduceEvidence(string fault)
    { _o.Fault = fault; Assert.Null(await Capture.CaptureRequesterUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment)); }

    [Theory]
    [InlineData("session")]
    [InlineData("tenant")]
    [InlineData("environment")]
    [InlineData("principal")]
    [InlineData("grant")]
    [InlineData("approver-deny")]
    [InlineData("approval-read-deny")]
    [InlineData("expired")]
    [InlineData("clock-backwards")]
    public async Task InvalidApproverFactsNeverProduceEvidence(string fault)
    { _o.Fault = fault; Assert.Null(await Capture.CaptureApproverUnderAdmissionAsync(_o.Session, _o.Project, _o.Environment)); }

    private sealed class Owners : ICurrentSubjectReader, IDurablePrincipalReader, IScopedGrantReader, IRoleDefinitionReader,
        IReleaseProjectAuthorityReader, IApprovalEnvironmentAuthorityReader, IAuthorityClock
    {
        private readonly Guid _principal = Guid.NewGuid(), _boot = Guid.NewGuid(), _member = Guid.NewGuid(), _session = Guid.NewGuid();
        public readonly Guid Tenant = Guid.NewGuid(), Project = Guid.NewGuid(), Environment = Guid.NewGuid();
        public readonly DateTimeOffset Now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public string? Fault; private int _clockCalls;
        public long ProjectRevision = 1, EnvironmentRevision = 1;
        private readonly Dictionary<string, (Guid Assignment, Guid Definition)> _grantIds = new();
        public SessionSelection Session => new(_principal, new string('a', 43), _boot);
        public DateTimeOffset GetTrustedUtcNow() => Fault == "clock-backwards" && ++_clockCalls > 1 ? Now.AddSeconds(-1) : Now;
        public Task<CurrentSubject?> ReadCurrentUnderAdmissionAsync(SessionSelection selection, CancellationToken ct = default) =>
            Task.FromResult<CurrentSubject?>(Fault == "session" ? null : new(_principal, Fault == "session-replaced" ? Guid.NewGuid() : _session, _boot, 1, Now.AddMinutes(10), Now.AddMinutes(5)));
        public Task<DurablePrincipal?> ReadUnderAdmissionAsync(Guid principalId, CancellationToken ct = default) =>
            Task.FromResult<DurablePrincipal?>(new(_principal, Fault == "principal" ? 2 : 1, true, new string('a', 64)));
        public Task<ReleaseProjectAuthority?> ReadCurrentUnderAdmissionAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult<ReleaseProjectAuthority?>(new(Tenant, Project, ProjectRevision));
        public Task<ApprovalEnvironmentAuthority?> ReadCurrentUnderAdmissionAsync(Guid tenantId, Guid projectId, Guid environmentId, CancellationToken ct = default) =>
            Task.FromResult<ApprovalEnvironmentAuthority?>(new(Fault == "tenant" ? Guid.NewGuid() : Tenant, Project, Fault == "environment" ? Guid.NewGuid() : Environment, EnvironmentRevision));
        public Task<ScopedGrant?> ReadCurrentGrantUnderAdmissionAsync(Guid principal, Guid tenant, string permission, string scopeKind, Guid scopeId, CancellationToken ct = default)
        {
            if (!_grantIds.TryGetValue(permission, out var ids)) _grantIds[permission] = ids = (Guid.NewGuid(), Guid.NewGuid());
            return Task.FromResult<ScopedGrant?>(Fault == "grant" ? null : new(_principal, Tenant, _member, 1, 1, ids.Assignment, 1,
                ids.Definition, 1, permission, scopeKind, scopeId, Fault == "expired" ? Now : null));
        }
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid definitionId, long exactRevision, CancellationToken cancellationToken = default) =>
            Task.FromResult<RoleDefinitionSnapshot?>(new(definitionId, exactRevision, "synthetic", [new("release.request-approval", "environment"),
                new("release.read", "project"), new("release.approve", "environment"), new("release.approval.read", "environment"), new("release.approval.revoke", "environment"),
                .. (Fault == "role-deny" ? new[] { new PermissionRule("release.read", "project", "deny") } : []),
                .. (Fault == "approval-read-deny" ? new[] { new PermissionRule("release.approval.read", "environment", "deny") } : []),
                .. (Fault == "revoker-deny" ? new[] { new PermissionRule("release.approval.revoke", "environment", "deny") } : []),
                .. (Fault == "approver-deny" ? new[] { new PermissionRule("release.approve", "environment", "deny") } : [])]));
        public Task<bool> IsActivePrincipalAsync(Guid principalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
