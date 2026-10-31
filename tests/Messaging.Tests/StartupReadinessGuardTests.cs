using Npgsql;
using Xunit;
namespace Kordra.Messaging.Tests;
public sealed class StartupReadinessGuardTests
{
    [Theory]
    [InlineData("host")]
    [InlineData("shared-port")]
    [InlineData("database")]
    [InlineData("owner")]
    [InlineData("runtime")]
    [InlineData("options")]
    [InlineData("search-path")]
    [InlineData("multiplexing")]
    [InlineData("empty-case")]
    public void RejectsSharedRemoteOrUnboundTargetBeforeAnyConnection(string scenario)
    {
        var caseId = Guid.NewGuid();
        var owner = new NpgsqlConnectionStringBuilder { Host="127.0.0.1", Port=49123, Database="kordra_test", Username="kordra_startup_"+caseId.ToString("N") };
        var runtime = new NpgsqlConnectionStringBuilder(owner.ConnectionString) { Username="kordra_msg_runtime" };
        if(scenario=="host") runtime.Host="example.invalid";
        if(scenario=="shared-port") owner.Port=runtime.Port=5432;
        if(scenario=="database") runtime.Database="kordra";
        if(scenario=="owner") owner.Username="postgres";
        if(scenario=="runtime") runtime.Username="kordra_app";
        if(scenario=="options") runtime.Options="-c search_path=app";
        if(scenario=="search-path") runtime.SearchPath="app";
        if(scenario=="multiplexing") runtime.Multiplexing=true;
        Assert.Throws<InvalidOperationException>(()=>StartupReadinessFixture.ValidateTargets(owner.ConnectionString,runtime.ConnectionString,
            scenario=="empty-case" ? Guid.Empty : caseId,scenario=="shared-port" ? 5432 : 49123));
    }
}
