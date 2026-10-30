using Kordra.Api;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class ReleaseRuntimeConfigurationTests
{
    private const string Locator = "Host=127.0.0.1;Port=5432;Database=kordra;Username=kordra_app;Password=synthetic-only";
    [Fact]
    public void BothPrivateLocatorsAreExplicitAndOmissionIsDisabled()
    {
        var absent = ReleaseRuntimeConfiguration.Load(null, null); Assert.Null(absent.ReleaseLocator); Assert.Null(absent.ApprovalLocator);
        var configured = ReleaseRuntimeConfiguration.Load(Locator, Locator); Assert.NotNull(configured.ReleaseLocator); Assert.Equal("ReleaseRuntimeConfiguration [private]", configured.ToString());
        Assert.Throws<InvalidOperationException>(() => ReleaseRuntimeConfiguration.Load(Locator, null));
        Assert.Throws<InvalidOperationException>(() => ReleaseRuntimeConfiguration.Load(null, Locator));
    }
    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=kordra;Username=kordra_app")]
    [InlineData("Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_app")]
    [InlineData("Host=127.0.0.1;Port=5432;Database=kordra;Username=postgres")]
    public void OtherDatabaseHostOrRoleIsRejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() => ReleaseRuntimeConfiguration.Load(value, Locator));
        Assert.Throws<InvalidOperationException>(() => ReleaseRuntimeConfiguration.Load(Locator, value));
    }
}
