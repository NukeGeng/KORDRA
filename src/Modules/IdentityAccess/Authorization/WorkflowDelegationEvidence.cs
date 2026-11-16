using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Modules.IdentityAccess.Authorization;

/// <summary>
/// 12.3b: session-free authority facts captured when a workflow delegation is created (original actor, current
/// <c>workflow.start</c> environment grant and <c>release.read</c> project grant). Stored with the delegation and revalidated
/// before every permit. Never an action permit by itself.
/// </summary>
public sealed record WorkflowDelegationEvidence
{
    private WorkflowDelegationEvidence(DurablePrincipal principal, ScopedGrant startGrant, ScopedGrant readGrant)
        => (Principal, StartGrant, ReadGrant) = (principal, startGrant, readGrant);
    public DurablePrincipal Principal { get; }
    public ScopedGrant StartGrant { get; }
    public ScopedGrant ReadGrant { get; }

    public static WorkflowDelegationEvidence Capture(DurablePrincipal principal, ScopedGrant startGrant, ScopedGrant readGrant,
        Guid tenantId, Guid projectId, Guid environmentId)
    {
        if (principal is null || startGrant is null || readGrant is null || !principal.Active || principal.PrincipalId == Guid.Empty ||
            principal.PrincipalRevision <= 0 || principal.SecurityRevisionHash is not { Length: 64 } ||
            !principal.SecurityRevisionHash.All(char.IsAsciiHexDigitLower) || tenantId == Guid.Empty || projectId == Guid.Empty ||
            environmentId == Guid.Empty || startGrant.PrincipalId != principal.PrincipalId || readGrant.PrincipalId != principal.PrincipalId ||
            startGrant.TenantId != tenantId || readGrant.TenantId != tenantId || startGrant.MembershipId == Guid.Empty ||
            startGrant.MembershipId != readGrant.MembershipId || startGrant.TenantRevision <= 0 ||
            startGrant.TenantRevision != readGrant.TenantRevision || startGrant.MembershipRevision <= 0 ||
            startGrant.MembershipRevision != readGrant.MembershipRevision ||
            startGrant.Permission != "workflow.start" || startGrant.ScopeKind != "environment" || startGrant.ScopeId != environmentId ||
            readGrant.Permission != "release.read" || readGrant.ScopeKind != "project" || readGrant.ScopeId != projectId ||
            !Valid(startGrant) || !Valid(readGrant))
            throw new ArgumentException("Invalid workflow delegation evidence.");
        return new(principal, startGrant, readGrant);
    }

    /// <summary>D2: the earliest grant expiry, or null when neither grant expires.</summary>
    public DateTimeOffset? EarliestGrantExpiryUtc => new[] { StartGrant.ExpiresAtUtc, ReadGrant.ExpiresAtUtc }
        .Where(x => x is not null).Select(x => x!.Value).DefaultIfEmpty().Min() is var min && min != default ? min : null;

    private static bool Valid(ScopedGrant grant) => grant.AssignmentId != Guid.Empty && grant.AssignmentRevision > 0 &&
        grant.DefinitionId != Guid.Empty && grant.DefinitionRevision > 0 &&
        (grant.ExpiresAtUtc is null || grant.ExpiresAtUtc.Value.Offset == TimeSpan.Zero &&
            grant.ExpiresAtUtc.Value != default && grant.ExpiresAtUtc.Value != DateTimeOffset.MaxValue);
}

/// <summary>Private, versioned persistence format for <see cref="WorkflowDelegationEvidence"/>; canonical bytes only.</summary>
public static class WorkflowDelegationEvidenceCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };
    private sealed record Document(int SchemaVersion, string Purpose, DurablePrincipal Principal, ScopedGrant StartGrant, ScopedGrant ReadGrant);

    public static byte[] Encode(WorkflowDelegationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(1, "workflow-delegate", evidence.Principal,
            evidence.StartGrant, evidence.ReadGrant), Options);
        if (bytes.Length is < 2 or > 8192) throw new ArgumentException("Workflow delegation evidence exceeds its bound.", nameof(evidence));
        return bytes;
    }

    /// <summary>SHA-256 of the exact stored bytes (verified again by <c>iam_workflow.create_delegation</c>).</summary>
    public static byte[] Hash(ReadOnlySpan<byte> encoded) => SHA256.HashData(encoded);

    public static WorkflowDelegationEvidence Decode(ReadOnlyMemory<byte> bytes, Guid tenantId, Guid projectId, Guid environmentId)
    {
        if (bytes.Length is < 2 or > 8192 || tenantId == Guid.Empty || projectId == Guid.Empty || environmentId == Guid.Empty)
            throw new ArgumentException("Invalid workflow delegation evidence input.");
        try
        {
            var document = JsonSerializer.Deserialize<Document>(bytes.Span, Options) ?? throw new FormatException();
            if (document.SchemaVersion != 1 || document.Purpose != "workflow-delegate") throw new FormatException();
            var result = WorkflowDelegationEvidence.Capture(document.Principal, document.StartGrant, document.ReadGrant,
                tenantId, projectId, environmentId);
            if (!bytes.Span.SequenceEqual(Encode(result))) throw new FormatException();
            return result;
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException or InvalidOperationException or NullReferenceException)
        { throw new ArgumentException("Invalid workflow delegation evidence encoding.", nameof(bytes), error); }
    }
}

public enum WorkflowDelegationAuthorityStatus { Current, Invalidated, Expired, Unavailable }

/// <summary>
/// Revalidates captured delegation evidence under caller-held admission without a browser session: the principal is still
/// active with the same revision/security hash and both exact grants and their role definitions are still current.
/// An eligibility input for the permit issuer only; never a sessionless action permit.
/// </summary>
public sealed class DurableWorkflowDelegationAuthorityReader(IDurablePrincipalReader principals, IDurableGrantEvidenceReader grants,
    IRoleDefinitionReader definitions, IAuthorityClock clock)
{
    public async Task<WorkflowDelegationAuthorityStatus> CheckUnderAdmissionAsync(WorkflowDelegationEvidence evidence,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (evidence is null) return WorkflowDelegationAuthorityStatus.Unavailable;
        try
        {
            var before = clock.GetTrustedUtcNow();
            if (!ValidTime(before)) return WorkflowDelegationAuthorityStatus.Unavailable;
            if (Expired(evidence.StartGrant, before) || Expired(evidence.ReadGrant, before)) return WorkflowDelegationAuthorityStatus.Expired;
            var principal = await principals.ReadUnderAdmissionAsync(evidence.Principal.PrincipalId, ct);
            if (principal is null || !principal.Active || principal != evidence.Principal) return WorkflowDelegationAuthorityStatus.Invalidated;
            foreach (var expected in new[] { evidence.StartGrant, evidence.ReadGrant })
            {
                var current = await grants.CompareUnderAdmissionAsync(expected, ct);
                if (current == DurableGrantEvidenceStatus.Invalidated) return WorkflowDelegationAuthorityStatus.Invalidated;
                if (current != DurableGrantEvidenceStatus.Current) return WorkflowDelegationAuthorityStatus.Unavailable;
                if (definitions is not IDurableRoleEvidenceReader durableRoles) return WorkflowDelegationAuthorityStatus.Unavailable;
                var role = await durableRoles.ReadExactUnderAdmissionAsync(expected.DefinitionId, expected.DefinitionRevision, ct);
                if (role.Status == DurableRoleEvidenceStatus.Invalidated) return WorkflowDelegationAuthorityStatus.Invalidated;
                if (role.Status != DurableRoleEvidenceStatus.Current || role.Definition is null) return WorkflowDelegationAuthorityStatus.Unavailable;
                var definition = role.Definition;
                if (definition.DefinitionId != expected.DefinitionId || definition.Revision != expected.DefinitionRevision ||
                    !FoundationPermissions.Allows(definition, expected.Permission, expected.ScopeKind))
                    return WorkflowDelegationAuthorityStatus.Invalidated;
            }
            var after = clock.GetTrustedUtcNow();
            if (!ValidTime(after) || after < before) return WorkflowDelegationAuthorityStatus.Unavailable;
            return Expired(evidence.StartGrant, after) || Expired(evidence.ReadGrant, after)
                ? WorkflowDelegationAuthorityStatus.Expired : WorkflowDelegationAuthorityStatus.Current;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return WorkflowDelegationAuthorityStatus.Unavailable; }
    }

    private static bool ValidTime(DateTimeOffset t) => t.Offset == TimeSpan.Zero && t != default && t != DateTimeOffset.MaxValue;
    private static bool Expired(ScopedGrant grant, DateTimeOffset now) => grant.ExpiresAtUtc is { } expiry && expiry <= now;
}
