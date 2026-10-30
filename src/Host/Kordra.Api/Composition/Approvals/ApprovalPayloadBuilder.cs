using System.Globalization;
using System.Text.Json;
using Kordra.ApprovalGovernance.Domain;
using Kordra.Modules.ApprovalGovernance;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.ReleaseManagement;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public sealed record ApprovalReadQuery(Guid CorrelationId, Guid? RequestId = null, int Limit = 50, Guid? AfterId = null);

internal sealed record ApprovalPayload(int Status, byte[] Bytes, string? Etag, DateTimeOffset? ValidUntilUtc);

/// <summary>Closed response projection only. Caller authorizes and audits the exact bytes before disclosure.</summary>
internal sealed class ApprovalPayloadBuilder(IDbContextFactory<ApprovalDbContext> approvals,
    IDbContextFactory<ReleaseDbContext> releases, ApprovalEligibilityReader eligibility)
{
    internal async Task<ApprovalPayload> DetailAsync(Guid installation, Guid tenant, Guid project, Guid environment,
        Guid requestId, Guid correlation, CancellationToken ct)
    {
        await using var db = await approvals.CreateDbContextAsync(ct);
        var owner = await new ApprovalRequestQueryStore(db).ReadUnderAdmissionAsync(installation, tenant, project, environment, requestId, ct);
        if (owner is null) return new(404, JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = correlation, code = "approval_not_found" }), null, null);
        var row = owner.Request;
        await using var rel = await releases.CreateDbContextAsync(ct);
        var revision = await new ReleaseQueryStore(rel).ReadRevisionUnderAdmissionAsync(installation, tenant, project, row.ReleaseRevisionId, ct);
        if (revision is null || revision.EnvironmentId != environment || row.SpecHash is not { Length: 32 } ||
            revision.SpecHash != Convert.ToHexStringLower(row.SpecHash) || revision.GetCanonicalSubject() is not { } bytes)
            throw new InvalidDataException("Approval frozen subject integrity mismatch.");
        var current = await eligibility.ReadUnderAdmissionAsync(row, installation, tenant, project, environment, ct);
        if (current.Effective.Status == ApprovalEffectiveStatus.Unavailable) return new(503, [], null, null);
        using var doc = JsonDocument.Parse(bytes);
        var subject = doc.RootElement;
        var decisions = owner.Decisions.Select(x => new { decisionId = x.Id, action = x.Action, actorPrincipalId = x.ActorPrincipalId,
            occurredAtUtc = Time(x.OccurredAtUtc), reasonCode = x.ReasonCode }).ToArray();
        var response = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, correlationId = correlation,
            approval = new
            {
                requestId = row.Id, version = row.Version, purpose = "release-intent-v1", releaseId = revision.ReleaseId,
                releaseRevisionId = row.ReleaseRevisionId, projectId = project, environmentId = environment, targetId = revision.TargetId,
                specHash = revision.SpecHash, requesterPrincipalId = row.RequesterPrincipalId, approverPrincipalId = row.ApproverPrincipalId,
                recordedState = row.State, effectiveState = current.Effective.Status.ToString(), createdAtUtc = Time(row.CreatedAtUtc),
                pendingExpiresAtUtc = Time(row.PendingExpiresAtUtc), grantedAtUtc = Optional(row.GrantedAtUtc), grantExpiresAtUtc = Optional(row.GrantExpiresAtUtc),
                policy = subject.GetProperty("approvalPolicy"), subject, decisions,
                eligibilityReasonCode = current.Effective.ReasonCode, executionAuthorized = false
            }
        });
        if (response.Length > 96 * 1024) return new(503, [], null, null);
        return new(200, response, "\"apr-v1:" + row.Id.ToString("D") + ":" + row.Version.ToString(CultureInfo.InvariantCulture) + "\"", current.ValidUntilUtc);
    }
    internal Task<ApprovalPayload> QueryAsync(Guid installation, Guid tenant, Guid project, Guid environment,
        ApprovalReadQuery query, CancellationToken ct) => query.RequestId is { } id
        ? DetailAsync(installation, tenant, project, environment, id, query.CorrelationId, ct)
        : ListAsync(installation, tenant, project, environment, query, ct);

    private async Task<ApprovalPayload> ListAsync(Guid installation, Guid tenant, Guid project, Guid environment,
        ApprovalReadQuery query, CancellationToken ct)
    {
        await using var db = await approvals.CreateDbContextAsync(ct);
        var page = await new ApprovalRequestQueryStore(db).ReadPageUnderAdmissionAsync(installation, tenant, project, environment,
            query.Limit, query.AfterId, ct);
        if (page is null) return new(404, JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = query.CorrelationId, code = "approval_not_found" }), null, null);
        var items = new List<Dictionary<string, JsonElement>>(); DateTimeOffset? until = null;
        string[] fields = ["requestId", "version", "releaseRevisionId", "projectId", "environmentId", "targetId", "specHash", "requesterPrincipalId",
            "approverPrincipalId", "recordedState", "effectiveState", "createdAtUtc", "pendingExpiresAtUtc", "grantExpiresAtUtc", "eligibilityReasonCode", "executionAuthorized"];
        foreach (var row in page.Requests)
        {
            var detail = await DetailAsync(installation, tenant, project, environment, row.Id, query.CorrelationId, ct);
            if (detail.Status != 200) return new(503, [], null, null);
            using var document = JsonDocument.Parse(detail.Bytes);
            var approval = document.RootElement.GetProperty("approval");
            items.Add(fields.ToDictionary(x => x, x => approval.GetProperty(x).Clone(), StringComparer.Ordinal));
            if (detail.ValidUntilUtc is { } boundary && (until is null || boundary < until)) until = boundary;
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = query.CorrelationId, items, nextAfterId = page.NextAfterId });
        return bytes.Length <= 64 * 1024 ? new(200, bytes, null, until) : new(503, [], null, null);
    }

    private static string Time(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0) throw new InvalidDataException("Approval timestamp precision mismatch.");
        return value.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    }
    private static string? Optional(DateTimeOffset? value) => value is { } date ? Time(date) : null;
}
