using System.Security.Cryptography;
using System.Text;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

/// <summary>12.2f pure reauthorization command and structural rules; no IO, no authority.</summary>
public sealed class WorkflowReauthorizationTests
{
    private static readonly Guid Installation = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Instance = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Request = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Actor = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateTimeOffset Created = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    private static WorkflowReauthorizationCommand Command(Guid? actor = null) =>
        new(Installation, Tenant, Instance, Request, actor ?? Actor, 5, new string('a', 64));
    private static WorkflowReauthorizationSnapshot Snapshot(WorkflowState state = WorkflowState.BlockedAuthority,
        bool cancellation = false, bool custody = false, bool unknown = false, bool timer = false,
        long generation = 1, Guid? actor = null) =>
        new(state, Created, Created.AddHours(24), cancellation, custody, unknown, timer, generation, actor ?? Actor);

    [Fact]
    public void CommandHashMatchesIndependentClosedVectorAndDoesNotRevealAuthority()
    {
        var command = Command();
        const string vector = "{\"schemaVersion\":1,\"kind\":\"workflow.reauthorize\",\"installationId\":\"11111111-1111-4111-8111-111111111111\",\"tenantId\":\"22222222-2222-4222-8222-222222222222\",\"instanceId\":\"33333333-3333-4333-8333-333333333333\",\"requestId\":\"44444444-4444-4444-8444-444444444444\",\"actorId\":\"55555555-5555-4555-8555-555555555555\",\"expectedVersion\":5,\"specificationHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}";
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(vector))), command.CommandHash);
        Assert.Equal(command.CommandHash, Command().CommandHash);
        Assert.DoesNotContain(Actor.ToString(), command.ToString());
        Assert.NotEqual(new WorkflowCancellationIntent(Installation, Tenant, Instance, Request, Actor, 5, new string('a', 64)).CommandHash,
            command.CommandHash);
    }

    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)]
    public void CommandIdentityCannotBeReboundAcrossScopeActorVersionOrSpecification(int field)
    {
        var altered = new WorkflowReauthorizationCommand(field == 0 ? Guid.NewGuid() : Installation, field == 1 ? Guid.NewGuid() : Tenant,
            field == 2 ? Guid.NewGuid() : Instance, field == 3 ? Guid.NewGuid() : Request, field == 4 ? Guid.NewGuid() : Actor,
            field == 5 ? 6 : 5, new string(field == 6 ? 'b' : 'a', 64));
        Assert.NotEqual(Command().CommandHash, altered.CommandHash);
    }

    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)]
    public void EmptyOwnerActorOrRequestIdentityIsRejected(int empty)
    {
        var ids = new[] { Installation, Tenant, Instance, Request, Actor }; ids[empty] = Guid.Empty;
        Assert.Throws<ArgumentException>(() => new WorkflowReauthorizationCommand(ids[0], ids[1], ids[2], ids[3], ids[4], 5, new string('a', 64)));
    }

    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(long.MaxValue)]
    public void VersionExhaustionIsRejected(long version) =>
        Assert.Throws<ArgumentException>(() => new WorkflowReauthorizationCommand(Installation, Tenant, Instance, Request, Actor, version, new string('a', 64)));

    [Theory]
    [InlineData(null)][InlineData("")][InlineData("a")][InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void NonCanonicalSpecificationHashIsRejected(string? hash) =>
        Assert.Throws<ArgumentException>(() => new WorkflowReauthorizationCommand(Installation, Tenant, Instance, Request, Actor, 5, hash!));

    [Fact]
    public void EligibleBlockedWorkflowGetsTheNextGenerationOnly()
    {
        Assert.Equal(2, WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(), Created.AddHours(1)));
        Assert.Equal(8, WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(generation: 7), Created));
    }

    [Theory]
    [InlineData(WorkflowState.Pending)][InlineData(WorkflowState.Running)][InlineData(WorkflowState.WaitingTimer)]
    [InlineData(WorkflowState.CancelRequested)][InlineData(WorkflowState.Reconciling)]
    [InlineData(WorkflowState.Succeeded)][InlineData(WorkflowState.Failed)][InlineData(WorkflowState.Cancelled)][InlineData(WorkflowState.Expired)]
    public void OnlyBlockedAuthorityIsEligible(WorkflowState state) =>
        Assert.Throws<InvalidOperationException>(() => WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(state), Created.AddHours(1)));

    [Theory]
    [InlineData("cancellation")][InlineData("custody")][InlineData("unknown")][InlineData("timer")]
    public void CancellationOrUnresolvedWorkBlocksReauthorization(string fault) =>
        Assert.Throws<InvalidOperationException>(() => WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(
            cancellation: fault == "cancellation", custody: fault == "custody", unknown: fault == "unknown", timer: fault == "timer"),
            Created.AddHours(1)));

    [Fact]
    public void OriginalWindowIsHalfOpenAndNeverExtended()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(), Created.AddTicks(-1)));
        Assert.Throws<InvalidOperationException>(() => WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(), Created.AddHours(24)));
        Assert.Throws<InvalidOperationException>(() => WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(), Created.AddDays(90)));
        Assert.Equal(2, WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(), Created.AddHours(24).AddTicks(-1)));
    }

    [Fact]
    public void OnlyTheOriginalActorWithAnExistingBindingIsEligible()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowReauthorizationRules.RequireEligible(Command(Guid.NewGuid()), Snapshot(), Created));
        Assert.Throws<InvalidOperationException>(() => WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(generation: 0), Created));
        Assert.Throws<InvalidOperationException>(() => WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(actor: Guid.Empty), Created));
        Assert.Throws<InvalidOperationException>(() => WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(generation: long.MaxValue), Created));
    }

    [Fact]
    public void MissingClockOrInputsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => WorkflowReauthorizationRules.RequireEligible(Command(), Snapshot(), default));
        Assert.Throws<ArgumentNullException>(() => WorkflowReauthorizationRules.RequireEligible(null!, Snapshot(), Created));
        Assert.Throws<ArgumentNullException>(() => WorkflowReauthorizationRules.RequireEligible(Command(), null!, Created));
    }
}
