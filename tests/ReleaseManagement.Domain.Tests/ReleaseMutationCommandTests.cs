using Kordra.ReleaseManagement.Domain;
using Xunit;

namespace Kordra.ReleaseManagement.Domain.Tests;

public sealed class ReleaseMutationCommandTests
{
    private readonly Guid _project = Guid.NewGuid(), _revision = Guid.NewGuid();
    private static ReleaseSelection Selection() => ReleaseSelection.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    [Fact]
    public void ExactRequestIdentityBindsProjectActorActionSelectionAndVersion()
    {
        var selection = Selection(); var tenant = Guid.NewGuid(); var installation = Guid.NewGuid(); var actor = Guid.NewGuid();
        var first = ReleaseMutationCommand.Create(_project, "Frontend", selection);
        var repeat = ReleaseMutationCommand.Create(_project, "Frontend", selection);
        Assert.Equal(first.BusinessHash, repeat.BusinessHash);
        Assert.Equal(first.ScopedKeyHash(installation, tenant, actor, "synthetic-key-1234"), repeat.ScopedKeyHash(installation, tenant, actor, "synthetic-key-1234"));
        Assert.NotEqual(first.BusinessHash, ReleaseMutationCommand.Create(_project, "Changed", selection).BusinessHash);
        Assert.NotEqual(first.ScopedKeyHash(installation, tenant, actor, "synthetic-key-1234"), first.ScopedKeyHash(installation, tenant, Guid.NewGuid(), "synthetic-key-1234"));
        var freeze = ReleaseMutationCommand.Freeze(_project, _revision, 1, $"\"rel-v1:{_revision:D}:1\"", new string('a', 64));
        Assert.Equal("release.create", freeze.Permission);
        Assert.Equal("release.retire", ReleaseMutationCommand.Retire(_project, _revision, 1, $"\"rel-v1:{_revision:D}:1\"", "obsolete").Permission);
    }
    [Fact]
    public void WeakMismatchedOrUnrelatedEtagsCannotCreateCommands()
    {
        Assert.Throws<ArgumentException>(() => ReleaseMutationCommand.Freeze(_project, _revision, 1, $"W/\"rel-v1:{_revision:D}:1\"", new string('a', 64)));
        Assert.Throws<ArgumentException>(() => ReleaseMutationCommand.Retire(_project, _revision, 1, $"\"rel-v1:{Guid.NewGuid():D}:1\"", "obsolete"));
        Assert.Throws<ArgumentException>(() => ReleaseMutationCommand.Replace(_project, _revision, 2, $"\"rel-v1:{_revision:D}:1\"", "x", Selection()));
        Assert.Throws<ArgumentException>(() => ReleaseMutationCommand.Retire(_project, _revision, 1, $"\"rel-v1:{_revision:D}:1\"", "free text"));
    }
}
