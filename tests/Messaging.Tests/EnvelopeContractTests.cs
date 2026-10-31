using Kordra.Modules.Messaging.Contracts;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class EnvelopeContractTests
{
    [Fact]
    public void LogicalIdentitySeparatesInstallationTenantOperationStepVersionAndType()
    {
        var installation = Guid.NewGuid(); var tenant = Guid.NewGuid(); var operation = Guid.NewGuid(); var step = Guid.NewGuid();
        var identity = new MessageIdentity(installation, MessageScope.Tenant(tenant), operation, step, 1, "fixture.observe");
        Assert.Equal(identity, new MessageIdentity(installation, MessageScope.Tenant(tenant), operation, step, 1, "fixture.observe"));
        Assert.NotEqual(identity, new MessageIdentity(installation, MessageScope.System(), operation, step, 1, "fixture.observe"));
        Assert.NotEqual(identity, new MessageIdentity(installation, MessageScope.Tenant(Guid.NewGuid()), operation, step, 1, "fixture.observe"));
        Assert.NotEqual(identity, new MessageIdentity(Guid.NewGuid(), MessageScope.Tenant(tenant), operation, step, 1, "fixture.observe"));
        Assert.NotEqual(identity, new MessageIdentity(installation, MessageScope.Tenant(tenant), Guid.NewGuid(), step, 1, "fixture.observe"));
        Assert.NotEqual(identity, new MessageIdentity(installation, MessageScope.Tenant(tenant), operation, Guid.NewGuid(), 1, "fixture.observe"));
        Assert.NotEqual(identity, new MessageIdentity(installation, MessageScope.Tenant(tenant), operation, step, 2, "fixture.observe"));
        Assert.NotEqual(identity, new MessageIdentity(installation, MessageScope.Tenant(tenant), operation, step, 1, "fixture.result"));
        Assert.Throws<ArgumentException>(() => MessageScope.Tenant(Guid.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Kordra.Assembly.Type")]
    [InlineData("fixture..observe")]
    [InlineData("fixture.observe.")]
    [InlineData("fixture/observe")]
    [InlineData("fixture.観測")]
    public void UnregisteredWireTypeNamesAreNotClrTypeSelectors(string typeId) =>
        Assert.Throws<ArgumentException>(() => new MessageTypeRegistration(typeId, 1, MessageScopeKind.Tenant, false));

    [Fact]
    public void RegistryIsClosedExactVersionAndScopeBoundWithNoSystemFallback()
    {
        var source = new List<MessageTypeRegistration> { new("fixture.observe", 1, MessageScopeKind.Tenant, true) };
        var registry = new MessageTypeRegistry(source); source.Clear(); var scope = MessageScope.Tenant(Guid.NewGuid());
        Assert.True(registry.TryResolve("fixture.observe", 1, scope, true, out _));
        Assert.False(registry.TryResolve("fixture.observe", 2, scope, true, out _));
        Assert.False(registry.TryResolve("fixture.observe", 1, MessageScope.System(), true, out _));
        Assert.False(registry.TryResolve("fixture.observe", 1, scope, false, out _));
        Assert.False(registry.TryResolve("fixture.unknown", 1, scope, true, out _));
        Assert.False(MessageTypeRegistry.Empty.TryResolve("fixture.observe", 1, scope, true, out _));
        Assert.Throws<ArgumentException>(() => new MessageTypeRegistry([new("fixture.observe", 1, MessageScopeKind.Tenant, false), new("fixture.observe", 1, MessageScopeKind.Tenant, false)]));
        Assert.DoesNotContain(typeof(MessageIdentity).Assembly.GetReferencedAssemblies(), x => x.Name!.StartsWith("Wolverine", StringComparison.Ordinal) || x.Name.StartsWith("Npgsql", StringComparison.Ordinal) || x.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
