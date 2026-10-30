using Kordra.ReleaseManagement.Domain;
using Xunit;

namespace Kordra.ReleaseManagement.Domain.Tests;

public sealed class ReleaseDraftTests
{
    private static ReleaseSelection Selection() => ReleaseSelection.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static ReleaseDraft Draft(Guid creator, string label = "Frontend") =>
        ReleaseDraft.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, creator, label, Selection());

    [Fact]
    public void DraftReplacementRequiresCreatorAndExactVersionAndPreservesIdentity()
    {
        var creator = Guid.NewGuid(); var draft = Draft(creator); var replacement = Selection();
        Assert.Throws<InvalidOperationException>(() => draft.Replace(Guid.NewGuid(), 1, "New", replacement));
        Assert.Throws<InvalidOperationException>(() => draft.Replace(creator, 2, "New", replacement));
        var updated = draft.Replace(creator, 1, "New", replacement);
        Assert.Equal(2, updated.Version); Assert.Equal(draft.RevisionId, updated.RevisionId);
        Assert.Equal(draft.ReleaseId, updated.ReleaseId); Assert.Equal(draft.Selection, SelectionCopy(draft.Selection));
        Assert.Equal("Frontend", draft.Label); Assert.Equal(replacement, updated.Selection);
        Assert.Throws<InvalidOperationException>(() => updated.Replace(creator, 1, "Old", replacement));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello\nworld")]
    [InlineData("e\u0301")]
    [InlineData("\u0000")]
    public void DraftRejectsInvalidLabels(string value) => Assert.Throws<ArgumentException>(() => Draft(Guid.NewGuid(), value));

    [Fact]
    public void LabelBoundCountsUnicodeScalarsAndRejectsUnpairedSurrogate()
    {
        var emoji = string.Concat(Enumerable.Repeat("🐋", 128));
        Assert.Equal(emoji, Draft(Guid.NewGuid(), emoji).Label);
        Assert.Throws<ArgumentException>(() => Draft(Guid.NewGuid(), emoji + "a"));
        Assert.Throws<ArgumentException>(() => Draft(Guid.NewGuid(), "\ud800"));
        Assert.Equal("é", Draft(Guid.NewGuid(), "é").Label);
    }

    [Fact]
    public void EmptyIdentifiersAreNeverAccepted()
    {
        Assert.Throws<ArgumentException>(() => ReleaseSelection.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => ReleaseDraft.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "a", Selection()));
        Assert.Throws<ArgumentException>(() => ReleaseDraft.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), "a", Selection()));
    }

    [Fact]
    public void PreviewMatchesIndependentContractVectorAndBindsVersionAndSpec()
    {
        var revision = Guid.Parse("11111111-1111-1111-1111-111111111111"); var spec = new string('a', 64);
        const string expected = "e0d6394d960da4ef63d5e2db5f8b940c47617a65d4be0101c57511e937aa1a28";
        Assert.Equal(expected, ReleasePreviewHash.Compute(revision, 1, spec));
        Assert.True(ReleasePreviewHash.Matches(revision, 1, spec, expected));
        Assert.False(ReleasePreviewHash.Matches(revision, 2, spec, expected));
        Assert.False(ReleasePreviewHash.Matches(revision, 1, new string('b', 64), expected));
        Assert.False(ReleasePreviewHash.Matches(revision, 1, spec, expected.ToUpperInvariant()));
        Assert.Throws<ArgumentException>(() => ReleasePreviewHash.Compute(revision, 1, "sha256:" + spec));
    }

    private static ReleaseSelection SelectionCopy(ReleaseSelection value) =>
        ReleaseSelection.Create(value.ApplicationId, value.EnvironmentId, value.TargetId, value.ArtifactId);
}
