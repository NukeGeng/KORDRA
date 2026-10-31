using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeRetryArtifactTests
{
    [Fact]
    public void RetryFailureInjectionIsUuidOwnedAndHasNoProductionOrGlobalPrivilegeChanges()
    {
        var caseId = Guid.NewGuid(); var schema = "t12_" + caseId.ToString("N");
        var ddl = NativeRetryDatabaseTests.CreationSql(caseId);
        Assert.Equal(ddl, NativeRetryDatabaseTests.CreationSql(caseId));
        Assert.StartsWith("CREATE SCHEMA " + schema + ";", ddl);
        Assert.Contains("CREATE SEQUENCE " + schema + ".fixture_retry_attempt", ddl);
        Assert.Contains("CREATE FUNCTION " + schema + ".fixture_transient_failure()", ddl);
        Assert.Contains("<= 4", ddl); Assert.Contains("ERRCODE = '40001'", ddl);
        Assert.DoesNotContain("CREATE ROLE", ddl); Assert.DoesNotContain("CREATE DATABASE", ddl);
        Assert.DoesNotContain("SECURITY DEFINER", ddl); Assert.DoesNotContain("app.", ddl);
        Assert.Throws<InvalidOperationException>(() => NativeRetryDatabaseTests.CreationSql(Guid.Empty));
    }
}
