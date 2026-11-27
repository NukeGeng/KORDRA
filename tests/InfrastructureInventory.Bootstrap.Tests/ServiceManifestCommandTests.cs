using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>12.3d-1 offline (no database): the service manifest document, the certificate pin tool and the CLI's early refusals.</summary>
public sealed class ServiceManifestCommandTests
{
    private static readonly Guid Installation = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 6, 0, 0, TimeSpan.Zero);

    private static JsonObject Bootstrap() => JsonNode.Parse(Encoding.UTF8.GetString(AuthorityFenceServiceCliDatabaseTests.Manifest("kordra",
        Installation, "bootstrap", Guid.NewGuid(), "worker", "kordra_worker_runtime", (new string('a', 64), Now, Now.AddDays(30)),
        [new(Guid.NewGuid(), Guid.NewGuid(), WorkflowSpecification.TemplateId, WorkflowSpecification.TemplateVersion, "admit"),
         new(Guid.NewGuid(), Guid.NewGuid(), WorkflowSpecification.TemplateId, WorkflowSpecification.TemplateVersion, "checkpoint")])))!.AsObject();

    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    [Fact]
    public void EveryKindParsesAndMapsToThePortManifest()
    {
        var bootstrap = ServiceManifestDocument.Parse(Bytes(Bootstrap()));
        Assert.NotNull(bootstrap);
        Assert.Equal(("bootstrap", "worker", "kordra_worker_runtime", 2), (bootstrap.Kind, bootstrap.ServiceKind, bootstrap.LoginName, bootstrap.Bindings.Count));
        var port = bootstrap.ToPortManifest();
        Assert.Equal(bootstrap.Hash(), port.ManifestSha256);
        Assert.Equal(bootstrap.ManifestId, port.AuditIntentId);
        Assert.Equal(new string('a', 64), port.SpkiSha256);
        var rotate = ServiceManifestDocument.Parse(AuthorityFenceServiceCliDatabaseTests.Manifest("kordra", Installation, "rotate", Guid.NewGuid(),
            Pin: (new string('b', 64), Now, Now.AddDays(1))));
        Assert.Equal(("rotate", (string?)null, 0), (rotate!.Kind, rotate.ServiceKind, rotate.Bindings.Count));
        foreach (var kind in new[] { "retire", "revoke" })
            Assert.Equal(kind, ServiceManifestDocument.Parse(AuthorityFenceServiceCliDatabaseTests.Manifest("kordra", Installation, kind, Guid.NewGuid()))!.Kind);
    }

    [Fact]
    public void TheHashBindsTheCanonicalContentNotTheFileLayout()
    {
        var node = Bootstrap();
        var original = ServiceManifestDocument.Parse(Bytes(node))!;
        // Same content, other property and binding order: same canonical bytes and hash.
        var reordered = new JsonObject();
        foreach (var (key, value) in node.Reverse()) reordered[key] = value?.DeepClone();
        var bindings = reordered["bindings"]!.AsArray();
        var first = bindings[0]!.DeepClone(); bindings.RemoveAt(0); bindings.Add(first);
        Assert.Equal(original.Hash(), ServiceManifestDocument.Parse(Bytes(reordered))!.Hash());
        // Any change of content changes the hash.
        var changed = Bootstrap(); changed["manifestId"] = original.ManifestId.ToString("D"); changed["serviceId"] = original.ServiceId.ToString("D");
        changed["bindings"] = node["bindings"]!.DeepClone(); changed["pin"]!["notAfterUtc"] = "2026-12-28T06:00:00Z";
        Assert.NotEqual(original.Hash(), ServiceManifestDocument.Parse(Bytes(changed))!.Hash());
        Assert.Equal(64, original.Hash().Length);
    }

    [Fact]
    public void MalformedOrKindInappropriateManifestsAreInvalid()
    {
        var cases = new List<Action<JsonObject>>
        {
            m => m["extra"] = 1,
            m => m["schemaVersion"] = 2,
            m => m["kind"] = "renew",
            m => m["manifestId"] = m["manifestId"]!.GetValue<string>().ToUpperInvariant(),
            m => m["serviceId"] = Guid.Empty.ToString("D"),
            m => m["expectedRole"] = "Kordra Admin",
            m => m["serviceKind"] = "agent",
            m => m["loginName"] = "1bad",
            m => m.Remove("pin"),
            m => m.Remove("bindings"),
            m => m["pin"]!["spkiSha256"] = new string('A', 64),
            m => m["pin"]!["spkiSha256"] = "abc",
            m => m["pin"]!["notBeforeUtc"] = "2026-11-27T06:00:00.000Z",
            m => m["pin"]!["notAfterUtc"] = "2026-11-27T06:00:00Z",
            m => m["pin"]!["extra"] = "x",
            m => m["bindings"]![0]!["transitionKind"] = "deploy",
            m => m["bindings"]![0]!["templateVersion"] = 0,
            m => m["bindings"]!.AsArray().Add(m["bindings"]![0]!.DeepClone()),
            m => { m["kind"] = "rotate"; },              // rotate carries no service kind, login or bindings
            m => { m["kind"] = "revoke"; m.Remove("serviceKind"); m.Remove("loginName"); m.Remove("bindings"); }, // revoke carries no pin
        };
        foreach (var mutate in cases)
        {
            var node = Bootstrap(); mutate(node);
            Assert.Null(ServiceManifestDocument.Parse(Bytes(node)));
        }
        Assert.Null(ServiceManifestDocument.Parse(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1}")));
        Assert.Null(ServiceManifestDocument.Parse(Encoding.UTF8.GetBytes("not json")));
    }

    [Fact]
    public void ThePinToolReadsPublicCertificatesOnly()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=fence-test-worker", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(Now.AddDays(-1), Now.AddDays(1));
        var expected = Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
        var pem = certificate.ExportCertificatePem();
        Assert.Equal(expected, ServiceCertificatePin.FromPem(pem));
        Assert.Equal(expected, ServiceCertificatePin.FromCertificate(certificate));
        Assert.True(ServiceManifestDocument.IsPin(expected));
        Assert.Null(ServiceCertificatePin.FromPem(pem + "\n" + key.ExportPkcs8PrivateKeyPem()));
        Assert.Null(ServiceCertificatePin.FromPem(pem + "\n" + pem));
        Assert.Null(ServiceCertificatePin.FromPem("-----BEGIN CERTIFICATE-----\nnot-a-certificate\n-----END CERTIFICATE-----"));
        Assert.Null(ServiceCertificatePin.FromPem(""));
    }

    [Fact]
    public async Task TheCoordinatorRefusesBeforeAnyDatabaseAccess()
    {
        var coordinator = new OfflineServiceCoordinator(Installation, null!, null!, new FairAsyncReaderWriterAdmissionGate(), new FixedClock());
        var json = Bytes(Bootstrap());
        var hash = ServiceManifestDocument.Parse(json)!.Hash();
        var lease = new HeldLease();
        Assert.Equal("denied", (await coordinator.ApplyAsync(lease, json, new string('0', 64))).Status);
        Assert.Equal("invalid", (await coordinator.ApplyAsync(lease, Encoding.UTF8.GetBytes("{}"), hash)).Status);
        var other = new OfflineServiceCoordinator(Guid.NewGuid(), null!, null!, new FairAsyncReaderWriterAdmissionGate(), new FixedClock());
        Assert.Equal("invalid", (await other.ApplyAsync(lease, json, hash)).Status);
        Assert.Null(await other.InspectAsync(lease, json));
        Assert.Throws<InvalidOperationException>(() => coordinator.ApplyAsync(new ReleasedLease(), json, hash).GetAwaiter().GetResult());
    }

    private sealed class FixedClock : IAuthorityClock { public DateTimeOffset GetTrustedUtcNow() => Now; }
    private sealed class HeldLease : IOfflineProvisioningLease { public void RequireHeld() { } }
    private sealed class ReleasedLease : IOfflineProvisioningLease
    {
        public void RequireHeld() => throw new InvalidOperationException("Offline custody is not held.");
    }
}
