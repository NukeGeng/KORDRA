using System.Security.Cryptography;
using System.Text;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

/// <summary>12.3b: session-free delegation evidence (original actor + workflow.start/release.read grants). Offline only.</summary>
public sealed class WorkflowDelegationEvidenceTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Project = Guid.NewGuid(), Environment = Guid.NewGuid(),
        Principal = Guid.NewGuid(), Membership = Guid.NewGuid();

    internal static DurablePrincipal ActivePrincipal() => new(Principal, 3, true, new string('c', 64));
    internal static ScopedGrant Grant(string permission, string scopeKind, Guid scope, DateTimeOffset? expires = null) =>
        new(Principal, Tenant, Membership, 2, 4, Guid.NewGuid(), 5, Guid.NewGuid(), 6, permission, scopeKind, scope, expires);

    [Fact]
    public void CaptureRequiresTheExactWorkflowStartAndReleaseReadPairForTheSamePrincipalAndMembership()
    {
        var start = Grant("workflow.start", "environment", Environment);
        var read = Grant("release.read", "project", Project);
        var evidence = WorkflowDelegationEvidence.Capture(ActivePrincipal(), start, read, Tenant, Project, Environment);
        Assert.Equal(start, evidence.StartGrant); Assert.Equal(read, evidence.ReadGrant);
        foreach (var (s, r) in new[] {
            (start with { Permission = "release.approve" }, read), (start, read with { Permission = "release.approval.read" }),
            (start with { ScopeId = Guid.NewGuid() }, read), (start, read with { ScopeId = Guid.NewGuid() }),
            (start with { MembershipId = Guid.NewGuid() }, read), (start with { PrincipalId = Guid.NewGuid() }, read),
            (start with { TenantId = Guid.NewGuid() }, read), (start with { ExpiresAtUtc = DateTimeOffset.Now.AddHours(1) }, read) })
            Assert.Throws<ArgumentException>(() => WorkflowDelegationEvidence.Capture(ActivePrincipal(), s, r, Tenant, Project, Environment));
        Assert.Throws<ArgumentException>(() => WorkflowDelegationEvidence.Capture(ActivePrincipal() with { Active = false }, start, read,
            Tenant, Project, Environment));
    }

    [Fact]
    public void CodecRoundTripsCanonicalBytesOnlyAndHashIsSha256OfThoseBytes()
    {
        var evidence = WorkflowDelegationEvidence.Capture(ActivePrincipal(), Grant("workflow.start", "environment", Environment),
            Grant("release.read", "project", Project), Tenant, Project, Environment);
        var bytes = WorkflowDelegationEvidenceCodec.Encode(evidence);
        Assert.Equal(evidence, WorkflowDelegationEvidenceCodec.Decode(bytes, Tenant, Project, Environment));
        Assert.Equal(SHA256.HashData(bytes), WorkflowDelegationEvidenceCodec.Hash(bytes));
        Assert.Contains("\"purpose\":\"workflow-delegate\"", Encoding.UTF8.GetString(bytes));
        // Non-canonical spacing, a foreign scope or another purpose never decode.
        var spaced = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(",", ", "));
        Assert.Throws<ArgumentException>(() => WorkflowDelegationEvidenceCodec.Decode(spaced, Tenant, Project, Environment));
        Assert.Throws<ArgumentException>(() => WorkflowDelegationEvidenceCodec.Decode(bytes, Tenant, Guid.NewGuid(), Environment));
        var other = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("workflow-delegate", "requester"));
        Assert.Throws<ArgumentException>(() => WorkflowDelegationEvidenceCodec.Decode(other, Tenant, Project, Environment));
    }

    [Fact]
    public void EarliestGrantExpiryIsTheMinimumOfExpiringGrantsOrNull()
    {
        var soon = new DateTimeOffset(2026, 11, 15, 1, 0, 0, TimeSpan.Zero);
        var later = soon.AddHours(2);
        Assert.Null(WorkflowDelegationEvidence.Capture(ActivePrincipal(), Grant("workflow.start", "environment", Environment),
            Grant("release.read", "project", Project), Tenant, Project, Environment).EarliestGrantExpiryUtc);
        Assert.Equal(soon, WorkflowDelegationEvidence.Capture(ActivePrincipal(), Grant("workflow.start", "environment", Environment, later),
            Grant("release.read", "project", Project, soon), Tenant, Project, Environment).EarliestGrantExpiryUtc);
        Assert.Equal(later, WorkflowDelegationEvidence.Capture(ActivePrincipal(), Grant("workflow.start", "environment", Environment, later),
            Grant("release.read", "project", Project), Tenant, Project, Environment).EarliestGrantExpiryUtc);
    }
}
