using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Wolverine;

namespace Kordra.Modules.Messaging.Infrastructure.Serialization;

/// <summary>
/// Creates a fresh diagnostic envelope, never copying arbitrary native headers, exception, serializer,
/// CLR message or reply metadata. The caller must replace the rejected envelope before any native
/// diagnostic persistence. This helper does not intercept a listener or authorize settlement.
/// Raw bytes must be supplied explicitly: reading Envelope.Data can invoke an attached serializer,
/// which is inappropriate while discarding an untrusted native envelope.
/// </summary>
public static class RejectedEnvelopeDiagnostic
{
    public const string TypeId = "kordra.message.rejected.v1";
    public const string ContentType = "application/vnd.kordra.message-rejection.v1+json";

    /// <summary>
    /// Replaces untrusted diagnostic content on the transport-owned instance without changing its
    /// settlement identity. Does not settle, commit, log, acknowledge or retry the delivery.
    /// Callers must establish a terminal known outcome before using this for handler failures.
    /// </summary>
    public static void ReplaceInPlace(Envelope envelope, ReadOnlySpan<byte> observedWire, Guid installationId,
        Uri trustedDestination, MessageQuarantineReason reason)
    {
        var diagnostic = Create(envelope, observedWire, installationId, trustedDestination, reason);
        NativeMessageIngressRule.ClearUntrustedMetadata(envelope);
        envelope.Data = diagnostic.Data;
        envelope.ContentType = diagnostic.ContentType;
        envelope.MessageType = diagnostic.MessageType;
        envelope.Destination = trustedDestination;
    }

    public static Envelope Create(Envelope rejected, ReadOnlySpan<byte> observedWire, Guid installationId, Uri trustedDestination,
        MessageQuarantineReason reason)
    {
        ArgumentNullException.ThrowIfNull(rejected); ArgumentNullException.ThrowIfNull(trustedDestination);
        if (installationId == Guid.Empty || rejected.Id == Guid.Empty || !Enum.IsDefined(reason) ||
            trustedDestination.Scheme != "postgresql" || !string.IsNullOrEmpty(trustedDestination.UserInfo) ||
            !string.IsNullOrEmpty(trustedDestination.Query) || !string.IsNullOrEmpty(trustedDestination.Fragment))
            throw new ArgumentException("Trusted diagnostic identity and registered destination required.");
        // Oversize bodies are not copied or hashed without a bound. This is a diagnostic digest,
        // not proof of a canonical business binding and never a redrive identity.
        var withinLimit = observedWire.Length is > 0 and <= 32 * 1024;
        var bounded = withinLimit ? observedWire : ReadOnlySpan<byte>.Empty;
        var hash = Convert.ToHexStringLower(SHA256.HashData(bounded));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("installationId", installationId.ToString("D"));
            writer.WriteString("messageId", rejected.Id.ToString("D"));
            writer.WriteString("reason", reason.ToString()); writer.WriteString("observedHash", hash);
            writer.WriteBoolean("bodyWithinLimit", withinLimit);
            writer.WriteEndObject(); writer.Flush();
        }
        return new Envelope
        {
            Id = rejected.Id, Destination = trustedDestination, Data = stream.ToArray(),
            ContentType = ContentType, MessageType = TypeId, Status = EnvelopeStatus.Incoming
        };
    }
}
