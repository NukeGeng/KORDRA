using System.Text;
using System.Text.Json;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Wolverine;
using Wolverine.Runtime.Serialization;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class RejectedEnvelopeDiagnosticTests
{
    [Fact]
    public void InvalidReplacementRequestLeavesTheOnlyTransportDeliveryUnchanged()
    {
        var bytes = "synthetic-original"u8.ToArray();
        var envelope = new Envelope { Data = bytes, MessageType = "fixture.native", Attempts = 5,
            Destination = new Uri("postgresql://fixture"), WasPersistedInInbox = true };
        envelope.Headers["fixture"] = "preserve";
        var id = envelope.Id;
        Assert.Throws<ArgumentException>(() => RejectedEnvelopeDiagnostic.ReplaceInPlace(envelope, bytes, Guid.Empty,
            new Uri("postgresql://fixture"), MessageQuarantineReason.RetryExhausted));
        Assert.Equal(id, envelope.Id); Assert.Same(bytes, envelope.Data);
        Assert.Equal("fixture.native", envelope.MessageType); Assert.Equal(5, envelope.Attempts);
        Assert.True(envelope.WasPersistedInInbox); Assert.Equal("preserve", envelope.Headers["fixture"]);
    }

    [Fact]
    public void NativeBinaryDiagnosticContainsOnlyClosedMetadataAndNoOriginalBodyOrReplyHeaders()
    {
        const string secret = "synthetic-forbidden-token";
        var source = new Envelope
        {
            Data = Encoding.UTF8.GetBytes("{\"token\":\"" + secret + "\"}"), MessageType = secret,
            ContentType = secret, Source = secret, CorrelationId = secret, ParentId = secret,
            TenantId = secret, SagaId = secret, ReplyUri = new Uri("https://example.invalid/" + secret),
            Destination = new Uri("postgresql://" + secret), Failure = new InvalidOperationException(secret),
            Message = new { Token = secret }
        };
        source.Headers[secret] = secret;
        var installation = Guid.NewGuid(); var destination = new Uri("postgresql://fixture");
        var diagnostic = RejectedEnvelopeDiagnostic.Create(source, source.Data!, installation, destination, MessageQuarantineReason.UnknownType);
        Assert.Equal(source.Id, diagnostic.Id); Assert.Equal(destination, diagnostic.Destination);
        Assert.Null(diagnostic.Message); Assert.Null(diagnostic.Failure); Assert.Null(diagnostic.Serializer);
        Assert.Null(diagnostic.ReplyUri); Assert.Null(diagnostic.TenantId); Assert.Empty(diagnostic.Headers);
        using var json = JsonDocument.Parse(diagnostic.Data!);
        Assert.Equal("UnknownType", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal(installation.ToString("D"), json.RootElement.GetProperty("installationId").GetString());
        Assert.Equal(64, json.RootElement.GetProperty("observedHash").GetString()!.Length);
        var wire = EnvelopeSerializer.Serialize(diagnostic);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(wire));
        var roundTrip = EnvelopeSerializer.Deserialize(wire);
        Assert.Equal(RejectedEnvelopeDiagnostic.TypeId, roundTrip.MessageType);
        Assert.Equal(RejectedEnvelopeDiagnostic.ContentType, roundTrip.ContentType);
        Assert.Equal(diagnostic.Data, roundTrip.Data);
        // No in-place mutation: caller is responsible for substituting this safe object at ingress.
        Assert.Contains(secret, Encoding.UTF8.GetString(source.Data!));
    }

    [Fact]
    public void OversizeOrAbsentBodyProducesBoundedDiagnosticWithoutClaimingBusinessHash()
    {
        var source = new Envelope { Data = new byte[32 * 1024 + 1] };
        var result = RejectedEnvelopeDiagnostic.Create(source, source.Data!, Guid.NewGuid(), new Uri("postgresql://fixture"), MessageQuarantineReason.InvalidEnvelope);
        Assert.InRange(result.Data!.Length, 1, 512);
        using var document = JsonDocument.Parse(result.Data);
        Assert.False(document.RootElement.GetProperty("bodyWithinLimit").GetBoolean());
        source.Data = null;
        var absent = RejectedEnvelopeDiagnostic.Create(source, ReadOnlySpan<byte>.Empty, Guid.NewGuid(), new Uri("postgresql://fixture"), MessageQuarantineReason.InvalidEnvelope);
        using var empty = JsonDocument.Parse(absent.Data!);
        Assert.Equal(document.RootElement.GetProperty("observedHash").GetString(), empty.RootElement.GetProperty("observedHash").GetString());
    }

    [Theory]
    [InlineData("https://example.invalid/")]
    [InlineData("postgresql://user:password@fixture")]
    [InlineData("postgresql://fixture?token=synthetic")]
    public void DiagnosticDestinationCannotComeFromASecretBearingRemoteReply(string destination)
    {
        Assert.Throws<ArgumentException>(() => RejectedEnvelopeDiagnostic.Create(new(), ReadOnlySpan<byte>.Empty, Guid.NewGuid(), new(destination), MessageQuarantineReason.InvalidEnvelope));
    }
}
