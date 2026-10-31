using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class LifecycleBoundaryArtifactTests
{
    [Fact]
    public async Task ReviewOnlyExecutableEmitsExactNativeSqlWithoutRuntimeLogPreamble()
    {
        var caseId = Guid.NewGuid();
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            ArgumentList = { typeof(CompatibilityProgram).Assembly.Location, "lifecycle-cleanup-sql", caseId.ToString("N") },
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Offline fixture review process failed to start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        Assert.Equal(0, process.ExitCode); Assert.Empty(await stderr);
        Assert.Equal(LifecycleBoundaryDatabaseTests.CleanupSql(caseId), await stdout);
    }

    [Fact]
    public void ReviewedNativeCleanupIsBoundedAndTargetsOnlyExpiredHandledRowsInTheExactFixture()
    {
        var caseId = Guid.NewGuid(); var schema = "t12_" + caseId.ToString("N");
        var sql = LifecycleBoundaryDatabaseTests.CleanupSql(caseId);
        Assert.Equal(sql, LifecycleBoundaryDatabaseTests.CleanupSql(caseId));
        Assert.Equal($"delete from \"{schema}\".wolverine_incoming_envelopes where status = 'Handled' and ctid in " +
            $"(select ctid from \"{schema}\".wolverine_incoming_envelopes where status = 'Handled' and keep_until <= :now limit 100);", sql);
        Assert.Throws<InvalidOperationException>(() => LifecycleBoundaryDatabaseTests.CleanupSql(Guid.Empty));
    }
}
