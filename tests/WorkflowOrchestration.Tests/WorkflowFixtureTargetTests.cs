using System.Data;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Npgsql;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowFixtureTargetTests
{
    private const string Prefix = "Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC-WFL-PASSWORD";
    [Fact]
    public void ExactTargetProducesOnlyCaseSchemaWithoutOpeningConnection()
    {
        using var connection=new NpgsqlConnection(Prefix);var id=Guid.NewGuid();
        var target=WorkflowFixtureTarget.Validate(connection,id);
        Assert.Equal("t12_"+id.ToString("N"),target.SchemaName);Assert.Equal(id,target.CaseId);
        Assert.Equal(ConnectionState.Closed,connection.State);
        Assert.DoesNotContain("SYNTHETIC",target.ToString());
    }
    [Theory]
    [InlineData("Host=192.0.2.1")][InlineData("Host=localhost")][InlineData("Host=/tmp")]
    [InlineData("Port=5433")][InlineData("Database=kordra")][InlineData("Database=postgres")]
    [InlineData("Username=postgres")][InlineData("Username=kordra_admin")][InlineData("Username=kordra_app")]
    [InlineData("Search Path=public")][InlineData("Options=-c role=postgres")]
    [InlineData("Multiplexing=true")][InlineData("Include Error Detail=true")]
    public void ChangedIdentityAndSessionOverridesFailClosedWithoutIO(string change)
    {
        using var connection=new NpgsqlConnection(Prefix+";"+change);
        var error=Assert.Throws<InvalidOperationException>(()=>WorkflowFixtureTarget.Validate(connection,Guid.NewGuid()));
        Assert.DoesNotContain("SYNTHETIC",error.ToString());Assert.Equal(ConnectionState.Closed,connection.State);
    }
    [Fact]
    public void MissingCaseOrConnectionNeverGrantsFixtureScope()
    {
        Assert.Throws<ArgumentNullException>(()=>WorkflowFixtureTarget.Validate(null!,Guid.NewGuid()));
        using var connection=new NpgsqlConnection(Prefix);
        Assert.Throws<InvalidOperationException>(()=>WorkflowFixtureTarget.Validate(connection,Guid.Empty));
        Assert.Equal(ConnectionState.Closed,connection.State);
    }
}
