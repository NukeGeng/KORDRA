using Kordra.Api;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>12.3b D2 validity and the offline guards of the inactive reissuer and permit issuer (no connection opened).</summary>
public sealed class WorkflowDelegationAuthorityTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Project = Guid.NewGuid(), Environment = Guid.NewGuid(),
        Principal = Guid.NewGuid(), Membership = Guid.NewGuid();
    private static readonly DateTimeOffset Deadline = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static WorkflowDelegationEvidence Evidence(DateTimeOffset? startExpiry = null, DateTimeOffset? readExpiry = null) =>
        WorkflowDelegationEvidence.Capture(new(Principal, 2, true, new string('d', 64)),
            new(Principal, Tenant, Membership, 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(), 1, "workflow.start", "environment", Environment, startExpiry),
            new(Principal, Tenant, Membership, 1, 1, Guid.NewGuid(), 1, Guid.NewGuid(), 1, "release.read", "project", Project, readExpiry),
            Tenant, Project, Environment);

    [Fact]
    public void ValidUntilIsTheEarliestOfDeadlineGrantPolicyAndApprovalButNeverTheBrowserSession()
    {
        Assert.Equal(Deadline, WorkflowDelegationAuthority.ValidUntil(Deadline, Evidence(), null, null));
        Assert.Equal(Deadline.AddMinutes(-30), WorkflowDelegationAuthority.ValidUntil(Deadline, Evidence(readExpiry: Deadline.AddMinutes(-30)), null, null));
        Assert.Equal(Deadline.AddMinutes(-20), WorkflowDelegationAuthority.ValidUntil(Deadline, Evidence(), Deadline.AddMinutes(-20), Deadline.AddMinutes(-10)));
        Assert.Equal(Deadline.AddMinutes(-10), WorkflowDelegationAuthority.ValidUntil(Deadline, Evidence(), Deadline.AddHours(1), Deadline.AddMinutes(-10)));
        Assert.Throws<ArgumentException>(() => WorkflowDelegationAuthority.ValidUntil(Deadline, Evidence(), DateTimeOffset.MaxValue, null));
        Assert.Throws<ArgumentException>(() => WorkflowDelegationAuthority.ValidUntil(Deadline.ToOffset(TimeSpan.FromHours(7)), Evidence(), null, null));
        Assert.Equal(["admit", "checkpoint", "timer_due", "cancel"], WorkflowDelegationAuthority.PilotKinds);
    }

    [Fact]
    public async Task ReissuerDeniesWithoutAdmittedFactsAndNeverTouchesTheDatabase()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Timeout=1");
        var reissuer = new IamWorkflowDelegationReissuer(new NoPrincipals());
        var request = new WorkflowDelegationReissueRequest(Guid.NewGuid(), Tenant, Project, Environment, Guid.NewGuid(), Principal,
            Guid.NewGuid(), 1, new string('a', 64), new string('b', 64), Deadline, Guid.NewGuid());
        Assert.Null(await reissuer.StageReissueAsync(connection, null!, request));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task PermitIssuerRejectsAMismatchedRequestBeforeAnyOwnerRead()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Timeout=1");
        var now = new DateTimeOffset(2026, 11, 15, 1, 0, 0, TimeSpan.Zero);
        var installation = Guid.NewGuid();
        var spec = new WorkflowSpecification(installation, Tenant, Guid.NewGuid(), Guid.NewGuid(), Project, Guid.NewGuid(), Environment,
            Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
        var issuer = new WorkflowPermitIssuer(installation, null!, null!, null!, null!, null!, null!);
        var request = new WorkflowPermitRequest(Guid.NewGuid(), Guid.NewGuid(), Tenant, spec.InstanceId, Guid.NewGuid(), Guid.NewGuid(), 1,
            spec.SpecificationHash, WorkflowSpecification.TemplateHash, "admit", Guid.NewGuid(), 0, new string('d', 64));
        foreach (var bad in new[] { request with { TenantId = Guid.NewGuid() }, request with { InstanceId = Guid.NewGuid() },
                     request with { SpecificationHash = new string('f', 64) }, request with { TemplateHash = new string('e', 64) },
                     request with { TransitionKind = "deploy" }, request with { SpkiSha256 = "not-a-pin" },
                     request with { SpkiSha256 = new string('D', 64) } })
            Assert.Equal("request_invalid", (await issuer.IssueUnderFenceAsync(connection, null!, spec, bad)).DenyReason);
        Assert.Equal("request_invalid", (await new WorkflowPermitIssuer(Guid.NewGuid(), null!, null!, null!, null!, null!, null!)
            .IssueUnderFenceAsync(connection, null!, spec, request)).DenyReason);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    private sealed class NoPrincipals : IDurablePrincipalReader
    {
        public Task<DurablePrincipal?> ReadUnderAdmissionAsync(Guid principalId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be reached without admitted facts.");
    }
}
