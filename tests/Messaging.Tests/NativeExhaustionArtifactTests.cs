using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeExhaustionArtifactTests
{
    [Fact]
    public void TerminalFailureDdlIsOfflineDeterministicAndConfinedToNewUuidSchema()
    {
        var caseId = Guid.NewGuid(); var schema = "t12_" + caseId.ToString("N");
        var sql = NativeExhaustionDatabaseTests.CreationSql(caseId);
        Assert.Equal(sql, NativeExhaustionDatabaseTests.CreationSql(caseId));
        Assert.StartsWith("CREATE SCHEMA " + schema + ";", sql);
        Assert.Contains("CREATE SEQUENCE " + schema + ".fixture_terminal_attempt;", sql);
        Assert.Contains("CREATE FUNCTION " + schema + ".fixture_terminal_failure()", sql);
        Assert.Contains("ERRCODE = '40001'", sql);
        foreach (var excluded in new[] { "CREATE ROLE", "CREATE DATABASE", "SECURITY DEFINER", "app.", "public.", "wolverine_queues.", "DROP SCHEMA" })
            Assert.DoesNotContain(excluded, sql);
        Assert.Throws<InvalidOperationException>(() => NativeExhaustionDatabaseTests.CreationSql(Guid.Empty));
    }
}
