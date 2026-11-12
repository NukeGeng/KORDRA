using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowStartCommandTests
{
    private readonly Guid installation = Guid.NewGuid(), tenant = Guid.NewGuid(), actor = Guid.NewGuid();
    private readonly Guid project = Guid.NewGuid(), environment = Guid.NewGuid(), request = Guid.NewGuid(), release = Guid.NewGuid();
    private readonly DateTimeOffset deadline = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RetryIsStableAndChangingContentKeepsTheConflictKey()
    {
        var original = Make();
        Assert.Equal(original.ScopedKeyHash, Make().ScopedKeyHash);
        Assert.Equal(original.BusinessHash, Make().BusinessHash);
        foreach (var changed in new[] { Make(revision: 2), Make(requestId: Guid.NewGuid()),
            Make(until: deadline.AddSeconds(1)), Make(environmentId: Guid.NewGuid()) })
        {
            Assert.Equal(original.ScopedKeyHash, changed.ScopedKeyHash);
            Assert.NotEqual(original.BusinessHash, changed.BusinessHash);
        }
    }

    [Fact]
    public void TenantAndActorHaveSeparateKeyNamespaces()
    {
        var original = Make();
        Assert.NotEqual(original.ScopedKeyHash, Make(tenantId: Guid.NewGuid()).ScopedKeyHash);
        Assert.NotEqual(original.ScopedKeyHash, Make(actorId: Guid.NewGuid()).ScopedKeyHash);
        Assert.NotEqual(original.ScopedKeyHash, Make(key: "second-key").ScopedKeyHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("trailing ")]
    [InlineData("line\nbreak")]
    public void RejectsUnboundedOrAmbiguousKeys(string key) =>
        Assert.Throws<ArgumentException>(() => Make(key: key));

    private WorkflowStartCommand Make(Guid? tenantId = null, Guid? actorId = null, Guid? environmentId = null,
        Guid? requestId = null, long revision = 1, DateTimeOffset? until = null, string key = "fixture-key") =>
        new(installation, tenantId ?? tenant, actorId ?? actor, project, environmentId ?? environment, requestId ?? request,
            release, revision, new string('a', 64), WorkflowSpecification.TemplateId, WorkflowSpecification.TemplateVersion,
            until ?? deadline, key);
}
