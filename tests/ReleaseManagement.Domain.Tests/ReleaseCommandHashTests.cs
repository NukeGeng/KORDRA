using System.Text;
using Kordra.ReleaseManagement.Domain;
using Xunit;

namespace Kordra.ReleaseManagement.Domain.Tests;

public sealed class ReleaseCommandHashTests
{
    private static readonly Guid Installation = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Actor = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Route = "/api/v1/projects/44444444-4444-4444-4444-444444444444/release-revisions/55555555-5555-5555-5555-555555555555/freeze";
    private const string Etag = "\"rel-v1:55555555-5555-5555-5555-555555555555:2\"";
    private static byte[] Body(string hash) => Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"expectedSelectionHash\":\"" + hash + "\"}");

    [Fact]
    public void HashesMatchIndependentCanonicalVectors()
    {
        Assert.Equal("066d09a01791a70e79e7f474417114dfe04663c8f0e8b549bd3aaaa994fce267",
            ReleaseCommandHash.ScopedKey(Installation, Tenant, Actor, "freeze", Route, "synthetic-key-1234"));
        Assert.Equal("b7c4f791cd712c4d0c648e8b0e1a59584dd61308636d47ebfc0571b75312aec8",
            ReleaseCommandHash.Body("freeze", Body(new string('a', 64)), Etag));
    }
    [Fact]
    public void AuditIntentIdentityIsDeterministicAndBoundToScopedKey()
    {
        var first = ReleaseCommandHash.AuditIntentId(new string('a', 64));
        Assert.Equal("b9354357-71fc-8c08-8b5d-48c0837a6629", first.ToString("D"));
        Assert.Equal(first, ReleaseCommandHash.AuditIntentId(new string('a', 64)));
        Assert.NotEqual(first, ReleaseCommandHash.AuditIntentId(new string('b', 64)));
    }
    [Fact]
    public void AuditReasonBindsKeyAndBusinessHashWithIndependentVector()
    {
        Assert.Equal("release-m-11111111111111111111111111111111-9a892279863b82f5227f46f175713bbd6482d0ce019b72243101095af7748c22", ReleaseCommandHash.AuditReason(Installation, new string('a', 64), new string('b', 64)));
        Assert.NotEqual(ReleaseCommandHash.AuditReason(Installation, new string('a', 64), new string('b', 64)),
            ReleaseCommandHash.AuditReason(Installation, new string('c', 64), new string('b', 64)));
        Assert.Throws<ArgumentException>(() => ReleaseCommandHash.AuditReason(Installation, new string('A', 64), new string('b', 64)));
    }
    [Fact]
    public void ChangesToScopePreconditionOrBusinessContentChangeTheHashes()
    {
        var key = ReleaseCommandHash.ScopedKey(Installation, Tenant, Actor, "freeze", Route, "synthetic-key-1234");
        Assert.NotEqual(key, ReleaseCommandHash.ScopedKey(Installation, Guid.NewGuid(), Actor, "freeze", Route, "synthetic-key-1234"));
        var body = ReleaseCommandHash.Body("freeze", Body(new string('a', 64)), Etag);
        Assert.NotEqual(body, ReleaseCommandHash.Body("freeze", Body(new string('b', 64)), Etag));
        Assert.NotEqual(body, ReleaseCommandHash.Body("freeze", Body(new string('a', 64)), Etag.Replace(":2\"", ":3\"")));
    }
    [Theory]
    [InlineData("W/\"rel-v1:55555555-5555-5555-5555-555555555555:2\"")]
    [InlineData("\"rel-v1:55555555-5555-5555-5555-555555555555:02\"")]
    [InlineData("\"rel-v1:00000000-0000-0000-0000-000000000000:2\"")]
    [InlineData("\"rel-v1:55555555-5555-5555-5555-555555555555:999999999999999999999\"")]
    public void RejectsInvalidStrongEtag(string etag) => Assert.Throws<ArgumentException>(() => ReleaseCommandHash.Body("freeze", Body(new string('a', 64)), etag));
    [Fact]
    public void RejectsUnknownDuplicateFieldsAndNoncanonicalRoutes()
    {
        Assert.Throws<ArgumentException>(() => ReleaseCommandHash.Body("freeze", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1}"), Etag));
        Assert.Throws<ArgumentException>(() => ReleaseCommandHash.Body("freeze", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"correlationId\":\"x\"}"), Etag));
        Assert.Throws<ArgumentException>(() => ReleaseCommandHash.ScopedKey(Installation, Tenant, Actor, "freeze", Route + "?tenant=x", "synthetic-key-1234"));
        Assert.Throws<ArgumentException>(() => ReleaseCommandHash.ScopedKey(Installation, Tenant, Actor, "retire", Route, "synthetic-key-1234"));
        Assert.Throws<ArgumentException>(() => ReleaseCommandHash.ScopedKey(Installation, Tenant, Actor, "freeze", Route + "\n", "synthetic-key-1234"));
        Assert.Throws<ArgumentException>(() => ReleaseCommandHash.Body("freeze", Body(new string('a', 64)), Etag + "\n"));
    }
}
