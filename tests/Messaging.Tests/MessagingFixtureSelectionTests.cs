using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class MessagingFixtureSelectionTests
{
    [Fact]
    public void RunnerSelectionKeepsAuditAndMessageInspectionOnExactUnopenedTarget()
    {
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile();
        var database = profile?.Database ?? "kordra_test";
        var locator = $"Host=127.0.0.1;Database={database};Username=kordra_test;Pooling=false";
        MessagingFixtureSelection.ValidateTestTarget(locator);
        using var connection = new NpgsqlConnection(locator); var caseId = Guid.NewGuid();
        using var audit = MessagingFixtureSelection.AuditContext(connection, caseId);
        Assert.Same(connection, audit.Database.GetDbConnection());
        Assert.Equal("t12_" + caseId.ToString("N"), audit.Model.GetDefaultSchema());
        Assert.NotNull(MessagingFixtureSelection.MessageReceipt(connection, caseId));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        var wrong = new NpgsqlConnectionStringBuilder(locator) { Database = profile is null ? "kordra" : "kordra_test" };
        Assert.Throws<InvalidOperationException>(() => MessagingFixtureSelection.ValidateTestTarget(wrong.ConnectionString));
        if (profile is not null)
        {
            Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateIsolatedTestContext(connection, caseId));
            Assert.Throws<InvalidOperationException>(() => MessageReceiptInspector.CreateIsolatedTestInspector(connection, caseId));
        }
    }
}
