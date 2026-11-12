using Kordra.Api;
using Npgsql;
using Xunit;
using Binding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class DisposableRecoveryReaderTests
{
    private static readonly Guid Run = Guid.NewGuid();
    private static Binding Profile() => (Run, new string('a', 64), new string('b', 64), DateTimeOffset.UtcNow.AddMinutes(10));
    private static string Locator() => $"Host=127.0.0.1;Database=kordra_test_{Run:N};Username=kordra_test;Password=synthetic-do-not-echo;Pooling=false";

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task CompositeReaderConstructionHasNoConnectionEffectAndLegacyDoesNotOptIn(bool cancellation)
    {
        using var independent = new NpgsqlConnection(Locator()); var caseId = Guid.NewGuid(); var profile = Profile();
        await using var reader = WorkflowWakeRecoveryReader.CreateDisposableTestReader(independent, caseId, cancellation, profile);
        Assert.Equal(System.Data.ConnectionState.Closed, independent.State);
        Assert.Throws<InvalidOperationException>(() => WorkflowWakeRecoveryReader.CreateIsolatedTestReader(independent, caseId));
        Assert.Throws<InvalidOperationException>(() => WorkflowWakeRecoveryReader.CreateDisposableTestReader(independent, Guid.Empty, cancellation, profile));
        profile.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.Throws<InvalidOperationException>(() => WorkflowWakeRecoveryReader.CreateDisposableTestReader(independent, caseId, cancellation, profile));
        Assert.Equal(System.Data.ConnectionState.Closed, independent.State);
    }

    [Fact]
    public async Task InvalidCustodySelectionStopsBeforeOpeningDedicatedSession()
    {
        var caseId = Guid.NewGuid(); var key = WorkflowFixtureCustodyLease.LockKey(caseId);
        var profile = Profile(); profile.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowFixtureCustodyLease.TryAcquireDisposableTestAsync(Locator(), caseId, key, profile));
        Assert.DoesNotContain("synthetic-do-not-echo", error.ToString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkflowFixtureCustodyLease.TryAcquireDisposableTestAsync(
            "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Pooling=false", caseId, key, Profile()));
    }
}
