using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class DurableApprovalAuthorityReaderTests
{
    private readonly Fixture _fixture = new();
    private DurableApprovalAuthorityReader Reader => new(_fixture, _fixture, _fixture, _fixture);
    private Task<DurableApprovalAuthorityResult> Check() => Reader.CheckUnderAdmissionAsync(_fixture.Principal, _fixture.Request, _fixture.Read);

    [Fact]
    public async Task StoredEvidenceRequiresExactOwnerAndNeverReusesStaleAuthority()
    {
        var f = _fixture;
        var evidence = ApprovalAuthorityEvidence.CaptureRequester(f.Principal, f.Request, f.Read,
            f.Request.TenantId, f.Read.ScopeId, f.Request.ScopeId);
        var encoded = ApprovalAuthorityEvidenceCodec.Encode(evidence);
        var reader = new StoredApprovalAuthorityReader(Reader);
        Task<DurableApprovalAuthorityStatus> Check(Guid tenant, Guid principal) => reader.CheckUnderAdmissionAsync(encoded,
            "requester", tenant, f.Read.ScopeId, f.Request.ScopeId, principal);
        Assert.Equal(DurableApprovalAuthorityStatus.Current, await Check(f.Request.TenantId, f.Principal.PrincipalId));
        Assert.Equal(DurableApprovalAuthorityStatus.Unavailable, await Check(Guid.NewGuid(), f.Principal.PrincipalId));
        Assert.Equal(DurableApprovalAuthorityStatus.Unavailable, await Check(f.Request.TenantId, Guid.NewGuid()));
        f.CurrentPrincipal = f.Principal with { PrincipalRevision = 2 };
        Assert.Equal(DurableApprovalAuthorityStatus.Invalidated, await Check(f.Request.TenantId, f.Principal.PrincipalId));
        f.CurrentPrincipal = f.Principal; f.MissingGrant = true;
        Assert.Equal(DurableApprovalAuthorityStatus.Unavailable, await Check(f.Request.TenantId, f.Principal.PrincipalId));
        Assert.Equal(DurableApprovalAuthorityStatus.Unavailable, await reader.CheckUnderAdmissionAsync(new byte[] { 1, 2 }, "requester",
            f.Request.TenantId, f.Read.ScopeId, f.Request.ScopeId, f.Principal.PrincipalId));
    }
    [Fact]
    public async Task ChecksBothExactOwnerGrantsWithoutSession()
    {
        Assert.Equal(DurableApprovalAuthorityStatus.Current, (await Check()).Status);
        Assert.Equal(2, _fixture.GrantReads);
    }
    [Theory]
    [InlineData("principal")]
    [InlineData("membership")]
    [InlineData("assignment")]
    [InlineData("definition")]
    [InlineData("expiry")]
    public async Task ChangedPersistedEvidenceCannotResurrectAnApproval(string change)
    {
        if (change == "principal") _fixture.CurrentPrincipal = _fixture.Principal with { PrincipalRevision = 2 };
        else _fixture.Override = change switch
        {
            "membership" => _fixture.Request with { MembershipRevision = 2 },
            "assignment" => _fixture.Request with { AssignmentRevision = 2 },
            "definition" => _fixture.Request with { DefinitionRevision = 2 },
            _ => _fixture.Request with { ExpiresAtUtc = _fixture.Now.AddHours(2) }
        };
        Assert.Equal(DurableApprovalAuthorityStatus.Invalidated, (await Check()).Status);
    }
    [Fact]
    public async Task AmbiguousOrUnavailableOwnerNeverProducesCurrent()
    {
        _fixture.MissingGrant = true;
        Assert.Equal(DurableApprovalAuthorityStatus.Unavailable, (await Check()).Status);
        _fixture.MissingGrant = false; _fixture.Throw = true;
        Assert.Equal(DurableApprovalAuthorityStatus.Unavailable, (await Check()).Status);
    }
    [Fact]
    public async Task OriginalExpiryAndWrongScopeFailClosed()
    {
        var expired = _fixture.Request with { ExpiresAtUtc = _fixture.Now };
        Assert.Equal(DurableApprovalAuthorityStatus.Expired, (await Reader.CheckUnderAdmissionAsync(_fixture.Principal, expired, _fixture.Read)).Status);
        var wrong = _fixture.Request with { ScopeKind = "project" };
        Assert.Equal(DurableApprovalAuthorityStatus.Unavailable, (await Reader.CheckUnderAdmissionAsync(_fixture.Principal, wrong, _fixture.Read)).Status);
    }
    private sealed class Fixture : IDurablePrincipalReader, IDurableGrantEvidenceReader, IRoleDefinitionReader, IDurableRoleEvidenceReader, IAuthorityClock
    {
        private readonly Guid _principal = Guid.NewGuid(), _tenant = Guid.NewGuid(), _member = Guid.NewGuid();
        public DateTimeOffset Now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public DurablePrincipal Principal { get; }
        public DurablePrincipal CurrentPrincipal;
        public ScopedGrant Request { get; }
        public ScopedGrant Read { get; }
        public ScopedGrant? Override;
        public bool MissingGrant, Throw;
        public int GrantReads;
        public Fixture()
        {
            Principal = new(_principal, 1, true, new string('a', 64)); CurrentPrincipal = Principal;
            Request = new(_principal, _tenant, _member, 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(), 1, "release.request-approval", "environment", Guid.NewGuid(), Now.AddHours(1));
            Read = new(_principal, _tenant, _member, 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(), 1, "release.read", "project", Guid.NewGuid(), null);
        }
        public DateTimeOffset GetTrustedUtcNow() => Now;
        public Task<DurablePrincipal?> ReadUnderAdmissionAsync(Guid principalId, CancellationToken cancellationToken = default)
        { if (Throw) throw new InvalidOperationException(); return Task.FromResult<DurablePrincipal?>(CurrentPrincipal); }
        public Task<DurableGrantEvidenceStatus> CompareUnderAdmissionAsync(ScopedGrant expected, CancellationToken cancellationToken = default)
        {
            GrantReads++;
            var current = expected.Permission == Request.Permission ? Override ?? Request : Read;
            return Task.FromResult(MissingGrant ? DurableGrantEvidenceStatus.Unavailable :
                current == expected ? DurableGrantEvidenceStatus.Current : DurableGrantEvidenceStatus.Invalidated);
        }
        public Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid definitionId, long exactRevision, CancellationToken cancellationToken = default)
        {
            var grant = definitionId == Request.DefinitionId ? Request : Read;
            return Task.FromResult<RoleDefinitionSnapshot?>(new(definitionId, exactRevision, "synthetic", [new(grant.Permission, grant.ScopeKind)]));
        }
        public async Task<DurableRoleEvidence> ReadExactUnderAdmissionAsync(Guid definitionId, long revision, CancellationToken cancellationToken = default)
            => new(DurableRoleEvidenceStatus.Current, await GetCurrentAsync(definitionId, revision, cancellationToken));
        public Task<bool> IsActivePrincipalAsync(Guid principalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
