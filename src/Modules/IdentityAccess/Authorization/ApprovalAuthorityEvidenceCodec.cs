using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Modules.IdentityAccess.Authorization;

/// <summary>Private, versioned persistence format for session-free approval authority facts.
/// Parsing validates the expected owner scope and canonical bytes; it never authorizes an action.</summary>
public static class ApprovalAuthorityEvidenceCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };
    private sealed record Document(int SchemaVersion, string Purpose, DurablePrincipal Principal,
        ScopedGrant EnvironmentGrant, ScopedGrant CompanionGrant);

    public static byte[] Encode(ApprovalAuthorityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var purpose = evidence.EnvironmentGrant.Permission switch
        {
            "release.request-approval" => "requester",
            "release.approve" => "approver",
            _ => throw new ArgumentException("Unknown approval evidence purpose.", nameof(evidence))
        };
        return JsonSerializer.SerializeToUtf8Bytes(new Document(1, purpose, evidence.Principal,
            evidence.EnvironmentGrant, evidence.CompanionGrant), Options);
    }

    public static ApprovalAuthorityEvidence Decode(ReadOnlyMemory<byte> bytes, string purpose,
        Guid tenantId, Guid projectId, Guid environmentId)
    {
        if (bytes.Length is <= 0 or > 65536 || purpose is not ("requester" or "approver") ||
            tenantId == Guid.Empty || projectId == Guid.Empty || environmentId == Guid.Empty)
            throw new ArgumentException("Invalid approval evidence input.");
        try
        {
            var document = JsonSerializer.Deserialize<Document>(bytes.Span, Options) ?? throw new FormatException();
            if (document.SchemaVersion != 1 || document.Purpose != purpose) throw new FormatException();
            var result = purpose == "requester"
                ? ApprovalAuthorityEvidence.CaptureRequester(document.Principal, document.EnvironmentGrant, document.CompanionGrant, tenantId, projectId, environmentId)
                : ApprovalAuthorityEvidence.CaptureApprover(document.Principal, document.EnvironmentGrant, document.CompanionGrant, tenantId, environmentId);
            var canonical = Encode(result);
            if (!bytes.Span.SequenceEqual(canonical)) throw new FormatException();
            return result;
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException or InvalidOperationException or NullReferenceException)
        { throw new ArgumentException("Invalid approval evidence encoding.", nameof(bytes), error); }
    }

    public static string Hash(ApprovalAuthorityEvidence evidence)
    {
        var prefix = Encoding.UTF8.GetBytes("approval-authority-evidence-v1\n");
        var bytes = Encode(evidence);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prefix); hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
