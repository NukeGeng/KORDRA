using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Modules.IdentityAccess.Authorization;

/// <summary>
/// Invocation-bound current project facts, not a permit. Registry/SEC eligibility, durable
/// U/C/M/D, original expiry caps and caller-bound disclosure remain the coordinator's duty.
/// </summary>
public sealed class ArtifactProjectAuthoritySnapshot
{
    internal ArtifactProjectAuthoritySnapshot(Guid invocationId, CurrentSubject subject, ScopedGrant grant,
        ArtifactProjectAuthority project, DateTimeOffset validUntilUtc, string digest) =>
        (InvocationId, Subject, Grant, Project, ValidUntilUtc, Digest) =
        (invocationId, subject, grant, project, validUntilUtc, digest);

    public Guid InvocationId { get; }
    public CurrentSubject Subject { get; }
    public ScopedGrant Grant { get; }
    public ArtifactProjectAuthority Project { get; }
    public DateTimeOffset ValidUntilUtc { get; }
    public string Digest { get; }
}

/// <summary>
/// Fixed project eligibility under the caller's held common gate. No target/environment
/// policy reuse, default Allow, organization/platform inheritance, audit or remote work.
/// </summary>
public sealed class ArtifactProjectAuthorityReader(ICurrentSubjectReader subjects, IScopedGrantReader grants,
    IRoleDefinitionReader definitions, IArtifactProjectAuthorityReader projects, IAuthorityClock clock)
{
    public async Task<ArtifactProjectAuthoritySnapshot?> ReadCurrentUnderAdmissionAsync(SessionSelection selection,
        Guid projectId, string permission, Guid invocationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (selection is null || selection.PrincipalId == Guid.Empty || selection.BootGeneration == Guid.Empty ||
            projectId == Guid.Empty || invocationId == Guid.Empty || permission is not ("artifact.read" or "artifact.register"))
            return null;
        try
        {
            var before = clock.GetTrustedUtcNow();
            if (!FiniteUtc(before)) return null;
            var subject = await subjects.ReadCurrentUnderAdmissionAsync(selection, ct);
            if (subject is null || subject.PrincipalId != selection.PrincipalId || subject.SessionId == Guid.Empty ||
                subject.BootGeneration != selection.BootGeneration || subject.PrincipalRevision <= 0 ||
                !FiniteUtc(subject.AbsoluteExpiresAtUtc) || !FiniteUtc(subject.IdleExpiresAtUtc)) return null;
            var project = await projects.ReadCurrentUnderAdmissionAsync(projectId, ct);
            if (project is null || project.ProjectId != projectId || project.TenantId == Guid.Empty || project.Revision <= 0) return null;
            var grant = await grants.ReadCurrentGrantUnderAdmissionAsync(subject.PrincipalId, project.TenantId,
                permission, "project", projectId, ct);
            if (grant is null || grant.PrincipalId != subject.PrincipalId || grant.TenantId != project.TenantId ||
                grant.Permission != permission || grant.ScopeKind != "project" || grant.ScopeId != projectId ||
                grant.MembershipId == Guid.Empty || grant.TenantRevision <= 0 || grant.MembershipRevision <= 0 ||
                grant.AssignmentId == Guid.Empty || grant.AssignmentRevision <= 0 || grant.DefinitionId == Guid.Empty ||
                grant.DefinitionRevision <= 0 || grant.ExpiresAtUtc is { } expiry && !FiniteUtc(expiry)) return null;
            var definition = await definitions.GetCurrentAsync(grant.DefinitionId, grant.DefinitionRevision, ct);
            if (definition is null || definition.DefinitionId != grant.DefinitionId || definition.Revision != grant.DefinitionRevision ||
                !FoundationPermissions.Allows(definition, permission, "project")) return null;
            var until = new[] { subject.AbsoluteExpiresAtUtc, subject.IdleExpiresAtUtc,
                grant.ExpiresAtUtc ?? DateTimeOffset.MaxValue }.Min();
            var after = clock.GetTrustedUtcNow();
            ct.ThrowIfCancellationRequested();
            if (!FiniteUtc(after) || after < before || until <= before || until <= after) return null;
            return new(invocationId, subject, grant, project, until, Digest(invocationId, subject, grant, project));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; } // Missing/unavailable/conflicting authority never becomes a positive fact.
    }

    private static bool FiniteUtc(DateTimeOffset time) => time.Offset == TimeSpan.Zero && time != default && time != DateTimeOffset.MaxValue;

    private static string Digest(Guid invocationId, CurrentSubject subject, ScopedGrant grant, ArtifactProjectAuthority project)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetBytes("artifact-project-authority-v1\n"));
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", 1);
            json.WriteString("predicate", "artifact-registration-v1");
            json.WriteString("invocationId", invocationId);
            json.WriteStartObject("subject");
            json.WriteString("principalId", subject.PrincipalId);
            json.WriteString("sessionId", subject.SessionId);
            json.WriteString("bootGeneration", subject.BootGeneration);
            json.WriteNumber("principalRevision", subject.PrincipalRevision);
            json.WriteString("absoluteExpiresAtUtc", subject.AbsoluteExpiresAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            json.WriteEndObject();
            json.WriteStartObject("project");
            json.WriteString("tenantId", project.TenantId);
            json.WriteString("projectId", project.ProjectId);
            json.WriteNumber("revision", project.Revision);
            json.WriteEndObject();
            json.WriteStartObject("grant");
            json.WriteString("principalId", grant.PrincipalId);
            json.WriteString("tenantId", grant.TenantId);
            json.WriteString("membershipId", grant.MembershipId);
            json.WriteNumber("tenantRevision", grant.TenantRevision);
            json.WriteNumber("membershipRevision", grant.MembershipRevision);
            json.WriteString("assignmentId", grant.AssignmentId);
            json.WriteNumber("assignmentRevision", grant.AssignmentRevision);
            json.WriteString("definitionId", grant.DefinitionId);
            json.WriteNumber("definitionRevision", grant.DefinitionRevision);
            json.WriteString("permission", grant.Permission);
            json.WriteString("scopeKind", grant.ScopeKind);
            json.WriteString("scopeId", grant.ScopeId);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        // Activity/assignment expiry is not a security revision. Every stage still checks it
        // and the coordinator retains the earliest original/current deadline, never extending U.
        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}
