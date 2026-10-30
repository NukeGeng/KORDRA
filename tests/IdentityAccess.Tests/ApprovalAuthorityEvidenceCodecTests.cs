using System.Text;
using System.Text.Json.Nodes;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class ApprovalAuthorityEvidenceCodecTests
{
    [Fact]
    public void RequesterEncodingMatchesIndependentVersionOneVector()
    {
        const string json = """{"schemaVersion":1,"purpose":"requester","principal":{"principalId":"11111111-1111-1111-1111-111111111111","principalRevision":1,"active":true,"securityRevisionHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"environmentGrant":{"principalId":"11111111-1111-1111-1111-111111111111","tenantId":"22222222-2222-2222-2222-222222222222","membershipId":"33333333-3333-3333-3333-333333333333","tenantRevision":1,"membershipRevision":1,"assignmentId":"66666666-6666-6666-6666-666666666666","assignmentRevision":1,"definitionId":"88888888-8888-8888-8888-888888888888","definitionRevision":1,"permission":"release.request-approval","scopeKind":"environment","scopeId":"44444444-4444-4444-4444-444444444444","expiresAtUtc":null},"companionGrant":{"principalId":"11111111-1111-1111-1111-111111111111","tenantId":"22222222-2222-2222-2222-222222222222","membershipId":"33333333-3333-3333-3333-333333333333","tenantRevision":1,"membershipRevision":1,"assignmentId":"77777777-7777-7777-7777-777777777777","assignmentRevision":1,"definitionId":"99999999-9999-9999-9999-999999999999","definitionRevision":1,"permission":"release.read","scopeKind":"project","scopeId":"55555555-5555-5555-5555-555555555555","expiresAtUtc":null}}""";
        var bytes = Encoding.UTF8.GetBytes(json);
        var evidence = ApprovalAuthorityEvidenceCodec.Decode(bytes, "requester",
            Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"));
        Assert.Equal(bytes, ApprovalAuthorityEvidenceCodec.Encode(evidence));
        Assert.Equal("a5e3aada3ec094b802d263010664260f47ce3269fdd8f094d3ce1e88b0835e72", ApprovalAuthorityEvidenceCodec.Hash(evidence));
    }
    private readonly Guid _principal = Guid.NewGuid(), _tenant = Guid.NewGuid(), _member = Guid.NewGuid(), _project = Guid.NewGuid(), _environment = Guid.NewGuid();
    private ApprovalAuthorityEvidence Evidence()
    {
        var p = new DurablePrincipal(_principal, 1, true, new string('a', 64));
        ScopedGrant Grant(string permission, string kind, Guid scope) => new(_principal, _tenant, _member, 1, 1,
            Guid.NewGuid(), 1, Guid.NewGuid(), 1, permission, kind, scope, null);
        return ApprovalAuthorityEvidence.CaptureRequester(p, Grant("release.request-approval", "environment", _environment),
            Grant("release.read", "project", _project), _tenant, _project, _environment);
    }
    [Fact]
    public void RoundTripBindsScopeAndDoesNotStoreSessionOrCredential()
    {
        var original = Evidence(); var bytes = ApprovalAuthorityEvidenceCodec.Encode(original);
        var restored = ApprovalAuthorityEvidenceCodec.Decode(bytes, "requester", _tenant, _project, _environment);
        Assert.Equal(original, restored); Assert.Equal(ApprovalAuthorityEvidenceCodec.Hash(original), ApprovalAuthorityEvidenceCodec.Hash(restored));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("session", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("boot", text, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => ApprovalAuthorityEvidenceCodec.Decode(bytes, "requester", Guid.NewGuid(), _project, _environment));
        Assert.Throws<ArgumentException>(() => ApprovalAuthorityEvidenceCodec.Decode(bytes, "approver", _tenant, _project, _environment));
    }
    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("version")]
    [InlineData("whitespace")]
    public void RejectsUnknownDuplicateMissingAndNoncanonicalInput(string change)
    {
        var text = Encoding.UTF8.GetString(ApprovalAuthorityEvidenceCodec.Encode(Evidence()));
        if (change == "unknown") text = text.Insert(1, "\"unknown\":1,");
        if (change == "duplicate") text = text.Insert(1, "\"schemaVersion\":1,");
        if (change == "missing") { var node = JsonNode.Parse(text)!.AsObject(); node.Remove("principal"); text = node.ToJsonString(); }
        if (change == "version") text = text.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal);
        if (change == "whitespace") text += " ";
        Assert.Throws<ArgumentException>(() => ApprovalAuthorityEvidenceCodec.Decode(Encoding.UTF8.GetBytes(text), "requester", _tenant, _project, _environment));
    }
}
