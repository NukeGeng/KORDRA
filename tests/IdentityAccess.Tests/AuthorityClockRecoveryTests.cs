using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class AuthorityClockRecoveryTests
{
    [Fact]
    public async Task Persisted_floor_blocks_clock_rollback_across_restart_for_the_same_installation()
    {
        var clock = new LocalAuthenticationTestHost.ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await LocalAuthenticationTestHost.StartAsync(timeProvider: clock);
        var user = host.NewUserName(); Assert.Equal(LocalAuthenticationStatus.Success, (await host.EnrollAsync(user)).Status);
        await using (var db = await host.CreateIdentityContextAsync())
        {
            Assert.True(await db.Set<KordraAuthorityClockFloor>().AnyAsync(x => x.AdmittedUtc > clock.GetUtcNow().AddSeconds(-1)));
        }
        clock.StepUtcWithoutAdvancingMonotonicTime(TimeSpan.FromSeconds(-5));
        var error = await Record.ExceptionAsync(() => host.RestartAsync());
        Assert.NotNull(error); Assert.Equal("ClockTrustException", error.GetType().Name);
    }

    [Fact]
    public async Task Backward_clock_in_one_process_closes_further_authority_instead_of_reusing_a_session()
    {
        var clock = new LocalAuthenticationTestHost.ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await LocalAuthenticationTestHost.StartAsync(timeProvider: clock);
        clock.StepUtcWithoutAdvancingMonotonicTime(TimeSpan.FromSeconds(-5));
        var error = Record.Exception(() => host.Authentication.GetTrustedUtcNow());
        Assert.NotNull(error); Assert.Equal("ClockTrustException", error.GetType().Name);
        clock.StepUtcWithoutAdvancingMonotonicTime(TimeSpan.FromSeconds(5));
        var user = host.NewUserName();
        Assert.Equal(LocalAuthenticationStatus.AuthorityUnavailable, (await host.EnrollAsync(user)).Status);
    }
}
