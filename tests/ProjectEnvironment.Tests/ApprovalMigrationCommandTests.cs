using Kordra.DbMigrator;
using Xunit;

namespace Kordra.Catalog.Tests;

public sealed class ApprovalMigrationCommandTests
{
    [Fact]
    public async Task Wrong_args_missing_locator_and_unapproved_target_are_rejected_before_connection()
    {
        var key = ApprovalGovernanceMigrationCommand.ConnectionEnvironmentVariable;
        Assert.Equal("KORDRA_APPROVAL_MIGRATION_CONNECTION_STRING", key);
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, null);
            Assert.Equal(2, await ApprovalGovernanceMigrationCommand.RunAsync(["wrong"]));
            Assert.Equal(2, await ApprovalGovernanceMigrationCommand.RunAsync(["approval-governance"]));
            Environment.SetEnvironmentVariable(key, "Host=localhost;Port=5432;Database=kordra;Username=kordra_admin;Password=synthetic");
            Assert.Equal(2, await ApprovalGovernanceMigrationCommand.RunAsync(["approval-governance"]));
            Environment.SetEnvironmentVariable(key, "Host=127.0.0.1;Port=5432;Database=kordra;Username=postgres;Password=synthetic");
            Assert.Equal(2, await ApprovalGovernanceMigrationCommand.RunAsync(["approval-governance"]));
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }
}
