using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeTimerArtifactTests
{
    [Fact]
    public void TimerDdlUsesSameReviewedNativeFactoryWithoutStartingAHost()
    {
        var rawCase = Environment.GetEnvironmentVariable("KORDRA_MSG_TIMER_DDL_PROBE_CASE_ID");
        var caseId = rawCase is null ? Guid.NewGuid() : Guid.ParseExact(rawCase, "N");
        var rawOrder = Environment.GetEnvironmentVariable("KORDRA_MSG_TIMER_DDL_PROBE_QUEUE_ORDER");
        var order = rawOrder?.Split(',');
        var sql = NativeIngressDatabaseTests.CreationSql(caseId, order);
        Assert.StartsWith("CREATE SCHEMA t12_" + caseId.ToString("N") + ";", sql);
        Assert.Equal(sql, NativeIngressDatabaseTests.CreationSql(caseId, order));
        if (rawCase is not null)
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "Kordra.slnx"))) current = current.Parent;
            var directory = Path.Combine(current?.FullName ?? throw new InvalidOperationException("Product root missing."),
                ".amr-local", "t12-1-design", "schema-review");
            Assert.Null(new DirectoryInfo(directory).LinkTarget);
            var path = Path.Combine(directory, "timer-testhost-" + caseId.ToString("N") + ".sql");
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            var bytes = System.Text.Encoding.UTF8.GetBytes(sql); output.Write(bytes);
        }
    }
}
