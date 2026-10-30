using System.Text;
using Kordra.ApprovalGovernance.Domain;
using Xunit;

namespace Kordra.ApprovalGovernance.Domain.Tests;

public sealed class ApprovalDeadlinesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Requester = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Approver = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static ReleaseApprovalPolicy Policy(string separation = "independent", string windows = "[]", string expiry = "null") =>
        ReleaseApprovalPolicy.Parse(Encoding.UTF8.GetBytes($$"""
        {"schemaVersion":1,"purpose":"release-intent-v1","status":"active","effectiveFromUtc":"2026-10-27T00:00:00.000000Z","expiresAtUtc":{{expiry}},"rules":[{"classification":"Test","effect":"allow","separationOfDuties":"{{separation}}","allowUnattestedProvenance":true,"pendingLifetimeSeconds":3600,"grantLifetimeSeconds":1800,"blockedWindowsUtc":{{windows}}}]}
        """));

    [Fact]
    public void PendingUsesEarliestOriginalAssignmentOrPolicyExpiry()
    {
        var policy = Policy(expiry: "\"2026-10-29T00:30:00.000000Z\"");
        Assert.Equal(Now.AddMinutes(20), ApprovalDeadlines.Pending(Now, policy, "Test", Now.AddMinutes(20), Now.AddMinutes(25)));
        Assert.Equal(Now.AddMinutes(30), ApprovalDeadlines.Pending(Now, policy, "Test", null, null));
        Assert.Throws<InvalidOperationException>(() => ApprovalDeadlines.Pending(Now, policy, "Test", Now, null));
    }
    [Fact]
    public void GrantCannotExtendOriginalPendingOrAnyAssignment()
    {
        var policy = Policy();
        Assert.Equal(Now.AddMinutes(5), ApprovalDeadlines.Grant(Now, policy, "Test", Requester, Approver, Now.AddMinutes(20),
            Now.AddMinutes(15), null, Now.AddMinutes(5), Now.AddMinutes(10)));
        Assert.Throws<InvalidOperationException>(() => ApprovalDeadlines.Grant(Now, policy, "Test", Requester, Approver, Now, null, null, null, null));
    }
    [Fact]
    public void CurrentWindowBlocksGrantAndFutureWindowCapsItWithoutBlockingRequest()
    {
        const string windows = "[{\"startsAtUtc\":\"2026-10-29T00:10:00.000000Z\",\"endsAtUtc\":\"2026-10-29T00:20:00.000000Z\"}]";
        var policy = Policy(windows: windows);
        Assert.Equal(Now.AddMinutes(10), ApprovalDeadlines.Grant(Now, policy, "Test", Requester, Approver, Now.AddHours(1), null, null, null, null));
        Assert.Equal(Now.AddMinutes(70), ApprovalDeadlines.Pending(Now.AddMinutes(10), policy, "Test", null, null));
        Assert.Throws<InvalidOperationException>(() => ApprovalDeadlines.Grant(Now.AddMinutes(10), policy, "Test", Requester, Approver, Now.AddHours(1), null, null, null, null));
        Assert.Equal(Now.AddMinutes(50), ApprovalDeadlines.Grant(Now.AddMinutes(20), policy, "Test", Requester, Approver, Now.AddHours(1), null, null, null, null));
    }
    [Fact]
    public void SeparationPilotAndUtcAreMandatory()
    {
        var independent = Policy();
        Assert.Throws<InvalidOperationException>(() => ApprovalDeadlines.Grant(Now, independent, "Test", Requester, Requester, Now.AddHours(1), null, null, null, null));
        Assert.Equal(Now.AddMinutes(30), ApprovalDeadlines.Grant(Now, Policy("self_allowed"), "Test", Requester, Requester, Now.AddHours(1), null, null, null, null));
        Assert.Throws<InvalidOperationException>(() => ApprovalDeadlines.Pending(Now, independent, "Production", null, null));
        Assert.Throws<InvalidOperationException>(() => ApprovalDeadlines.Pending(Now.ToOffset(TimeSpan.FromHours(7)), independent, "Test", null, null));
        Assert.Throws<InvalidOperationException>(() => ApprovalDeadlines.Pending(Now, independent, "Test", Now.AddHours(1).ToOffset(TimeSpan.FromHours(7)), null));
    }
}
