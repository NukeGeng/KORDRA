using System.Diagnostics;
using System.Text.Json;
using Kordra.Api;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowCustodyProcessTests
{
    public static readonly Guid CaseId = Guid.Parse("a31bdfe1-1057-42c6-84eb-8ee28721a47d");
    private const long Key = 3001163439027280936;
    private const string Ready = "custody-held";
    private const string ManifestVariable = "KORDRA_WFL_CUSTODY_FIXTURE_MANIFEST";
    private const string LocatorVariable = "KORDRA_WFL_CUSTODY_CONNECTION_STRING";

    [Fact]
    public async Task ManifestKeyAndTargetMustMatchBeforeConnection()
    {
        Assert.Equal(Key, WorkflowFixtureCustodyLease.LockKey(CaseId));
        const string forbiddenTarget = "Host=127.0.0.1;Database=kordra;Username=kordra_test;Password=SYNTHETIC";
        var keyError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowFixtureCustodyLease.TryAcquireIsolatedAsync(forbiddenTarget, CaseId, Key + 1));
        Assert.Equal("Custody key differs from the fixture installation.", keyError.Message);
        var targetError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowFixtureCustodyLease.TryAcquireIsolatedAsync(forbiddenTarget, CaseId, Key));
        Assert.Equal("Exact isolated WFL test target required; locator omitted.", targetError.Message);
    }

    [WorkflowCustodyProcessFact]
    public async Task IndependentSuccessorCannotAcquireUntilOwnedPredecessorExits()
    {
        var locator = ApprovedLocator();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = budget.Token;
        var start = new ProcessStartInfo("dotnet") {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add(typeof(WorkflowFixtureProgram).Assembly.Location);
        start.ArgumentList.Add("custody-fixture");
        start.ArgumentList.Add("hold");
        using var predecessor = Process.Start(start) ?? throw new InvalidOperationException("Owned custody child failed to start.");
        try
        {
            Assert.Equal(Ready, await predecessor.StandardOutput.ReadLineAsync(ct));
            Assert.False(predecessor.HasExited);
            await using (var blocked = await IsolatedWorkflowFixture.Custody(locator, CaseId, Key, ct))
                Assert.Null(blocked);

            predecessor.Kill();
            await predecessor.WaitForExitAsync(ct);
            Assert.NotEqual(0, predecessor.ExitCode);
            await using var successor = await AcquireAfterExitAsync(locator, ct);
            await using (await successor.EnterAsync(ct)) { }
            await successor.DisposeAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var denied = await successor.EnterAsync(ct); });
        }
        finally
        {
            if (!predecessor.HasExited) predecessor.Kill();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await predecessor.WaitForExitAsync(cleanup.Token);
            // Probe the exact key again so cleanup also verifies the successor's session released it.
            await using var cleanupProbe = await AcquireAfterExitAsync(locator, cleanup.Token);
        }
    }

    private static async Task<WorkflowFixtureCustodyLease> AcquireAfterExitAsync(string locator, CancellationToken ct)
    {
        while (true)
        {
            var lease = await IsolatedWorkflowFixture.Custody(locator, CaseId, Key, ct);
            if (lease is not null) return lease;
            await Task.Delay(25, ct);
        }
    }

    internal static async Task<int> HoldInChildAsync()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = budget.Token;
        try
        {
            await using var lease = await IsolatedWorkflowFixture.Custody(ApprovedLocator(), CaseId, Key, ct);
            if (lease is null) return 2;
            await using (await lease.EnterAsync(ct)) { }
            await Console.Out.WriteLineAsync(Ready);
            await Console.Out.FlushAsync(ct);
            await Console.In.ReadLineAsync(ct);
            return 0;
        }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException or
            IOException or Npgsql.NpgsqlException or JsonException or ArgumentException)
        {
            await Console.Error.WriteLineAsync("Custody fixture child could not hold the approved session.");
            return 3;
        }
    }

    private static string ApprovedLocator()
    {
        var manifest = Environment.GetEnvironmentVariable(ManifestVariable)
            ?? throw new InvalidOperationException("Custody operation approval required.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
        var root = document.RootElement;
        if (root.GetProperty("status").GetString() != "approved_for_execution" ||
            root.GetProperty("caseId").GetGuid() != CaseId ||
            root.GetProperty("advisoryKey").GetInt64() != Key)
            throw new InvalidOperationException("Custody operation approval differs from the fixed fixture.");
        return Environment.GetEnvironmentVariable(LocatorVariable)
            ?? throw new InvalidOperationException("Approved private custody locator required.");
    }

    public sealed class WorkflowCustodyProcessFactAttribute : FactAttribute
    {
        public WorkflowCustodyProcessFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ManifestVariable)))
                Skip = "Requires exact operation approval for PostgreSQL advisory custody and owned child termination.";
        }
    }
}
