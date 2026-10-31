using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class NativeIngressArtifactTests
{
    [Fact]
    public void IngressReviewDdlIsDeterministicAndConfinedToTheUuidFixtureWithoutOpeningDatabase()
    {
        var caseId = Guid.NewGuid(); var schema = "t12_" + caseId.ToString("N");
        var first = NativeIngressDatabaseTests.CreationSql(caseId);
        var second = NativeIngressDatabaseTests.CreationSql(caseId);
        Assert.Equal(first, second);
        Assert.StartsWith("CREATE SCHEMA " + schema + ";", first);
        Assert.Contains("CREATE TABLE " + schema + ".logical_receipts", first);
        Assert.Contains(schema + ".aud_intents", first);
        Assert.Contains(schema + ".wolverine_dead_letters", first);
        Assert.Contains(schema + ".wolverine_queue_nativecompatibility", first);
        Assert.DoesNotContain("CREATE ROLE", first); Assert.DoesNotContain("ALTER ROLE", first);
        Assert.DoesNotContain("CREATE DATABASE", first); Assert.DoesNotContain("app.", first);
        Assert.DoesNotContain("public.", first); Assert.DoesNotContain("wolverine_queues.", first);
        Assert.DoesNotContain("DROP SCHEMA", first);
        Assert.Equal(64, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(first))).Length);
        Assert.Throws<InvalidOperationException>(() => NativeIngressDatabaseTests.CreationSql(Guid.Empty));
    }

    [Fact]
    public void LegacyReviewedQueueOrderPreservesOnlyIndependentNativeObjectOrderAndRejectsDifferentObjects()
    {
        var caseId = Guid.NewGuid();
        string[] originalOrder = ["wolverine_queue_native_result", "wolverine_queue_native_result_scheduled",
            "wolverine_queue_nativecompatibility", "wolverine_queue_nativecompatibility_scheduled"];
        var sql = NativeIngressDatabaseTests.CreationSql(caseId, originalOrder);
        var found = System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE TABLE IF NOT EXISTS [^.]+\.(wolverine_queue_\w+)")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(originalOrder, found);
        Assert.Equal(sql, NativeIngressDatabaseTests.CreationSql(caseId, originalOrder));
        Assert.Throws<InvalidOperationException>(() => NativeIngressDatabaseTests.CreationSql(caseId, ["arbitrary_table"]));
        Assert.Throws<InvalidOperationException>(() => NativeIngressDatabaseTests.CreationSql(caseId,
            [originalOrder[0], originalOrder[0], originalOrder[2], originalOrder[3]]));
    }
}
