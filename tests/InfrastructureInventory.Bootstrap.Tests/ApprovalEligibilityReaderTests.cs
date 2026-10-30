using Kordra.Api;
using Kordra.ApprovalGovernance.Domain;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ApprovalEligibilityReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static ApprovalRequestRecord Row(string state) => new()
    {
        Id = Guid.NewGuid(), InstallationId = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(),
        EnvironmentId = Guid.NewGuid(), ReleaseRevisionId = Guid.NewGuid(), RequesterPrincipalId = Guid.NewGuid(),
        State = state, Version = 2, CreatedAtUtc = Now.AddMinutes(-5), PendingExpiresAtUtc = Now,
        GrantedAtUtc = state == "Granted" ? Now.AddMinutes(-1) : null,
        ApproverPrincipalId = state == "Granted" ? Guid.NewGuid() : null,
        ApproverEvidence = state == "Granted" ? [1] : null, GrantExpiresAtUtc = state == "Granted" ? Now : null
    };
    [Theory]
    [InlineData("Pending", ApprovalEffectiveStatus.Expired)]
    [InlineData("Granted", ApprovalEffectiveStatus.Expired)]
    [InlineData("Rejected", ApprovalEffectiveStatus.Rejected)]
    [InlineData("Revoked", ApprovalEffectiveStatus.Revoked)]
    public async Task ExpiredAndTerminalHistoryDoNotRequireOldAuthorityOrPolicy(string state, ApprovalEffectiveStatus expected)
    {
        var row = Row(state);
        // Null dependencies deliberately throw if the reader attempts to use old authority for these safe summaries.
        var reader = new ApprovalEligibilityReader(null!, null!, new Clock(Now));
        var result = await reader.ReadUnderAdmissionAsync(row, row.InstallationId, row.TenantId, row.ProjectId, row.EnvironmentId);
        Assert.Equal(expected, result.Effective.Status); Assert.Null(result.ValidUntilUtc);
        Assert.Equal(ApprovalEffectiveStatus.Unavailable, (await reader.ReadUnderAdmissionAsync(row, row.InstallationId, Guid.NewGuid(), row.ProjectId, row.EnvironmentId)).Effective.Status);
    }
    [Fact]
    public async Task ClockBeforeCreationNeverReportsActiveEligibility()
    {
        var row = Row("Pending");
        var result = await new ApprovalEligibilityReader(null!, null!, new Clock(row.CreatedAtUtc.AddTicks(-1)))
            .ReadUnderAdmissionAsync(row, row.InstallationId, row.TenantId, row.ProjectId, row.EnvironmentId);
        Assert.Equal(ApprovalEffectiveStatus.Unavailable, result.Effective.Status);
    }
    private sealed class Clock(DateTimeOffset now) : IAuthorityClock { public DateTimeOffset GetTrustedUtcNow() => now; }
}
