using Kordra.Modules.AuditCompliance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class AuditFixtureModelTests
{
    [Fact]
    public void IsolatedModelsKeepOwnSchemaAndLeaveNormalAuditModelUnchanged()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test");
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        using var a = AuditDbContextFactory.CreateIsolatedTestContext(connection, first);
        using var b = AuditDbContextFactory.CreateIsolatedTestContext(connection, second);
        using var normal = new AuditDbContext(AuditDbContextFactory.CreateTestOptions(connection.ConnectionString));
        Assert.Equal("t12_" + first.ToString("N"), a.Model.GetDefaultSchema());
        Assert.Equal("t12_" + second.ToString("N"), b.Model.GetDefaultSchema());
        Assert.Equal("app", normal.Model.GetDefaultSchema());
        Assert.All(a.Model.GetEntityTypes(), e => Assert.Equal("t12_" + first.ToString("N"), e.GetSchema()));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Database=kordra;Username=kordra_app")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=postgres")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Search Path=app")]
    public void TestSeamCannotAcceptProductionPrivilegedOrSessionOverrideTarget(string locator)
    {
        using var connection = new NpgsqlConnection(locator);
        Assert.Throws<InvalidOperationException>(() => AuditDbContextFactory.CreateIsolatedTestContext(connection, Guid.NewGuid()));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }
}
