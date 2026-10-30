using System.Globalization;
using System.Text.Json;
using Kordra.Modules.ReleaseManagement;
using Kordra.Modules.ReleaseManagement.Infrastructure;
using Kordra.ReleaseManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

internal sealed record ReleasePayload(int Status, byte[] Bytes, string? Etag, DateTimeOffset? SelectionUntilUtc);
internal sealed class ReleaseDisclosureDeniedException : Exception;
public enum ReleaseReadKind { Lineages, Revisions, Detail }
public sealed record ReleaseReadQuery(ReleaseReadKind Kind, Guid CorrelationId, Guid? ResourceId = null, int Limit = 50, Guid? AfterId = null);

/// <summary>Exact bounded owner projections, with no authority or response handoff.</summary>
internal sealed class ReleasePayloadBuilder(IDbContextFactory<ReleaseDbContext> contexts, ReleaseSelectionReader selections)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal async Task<ReleasePayload> DetailAsync(Guid installation, Guid tenant, Guid project, Guid revision,
        Guid correlation, DateTimeOffset now, bool requireCurrentFrozen, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var row = await new ReleaseQueryStore(db).ReadRevisionUnderAdmissionAsync(installation, tenant, project, revision, ct);
        if (row is null) return Error(correlation);
        if (requireCurrentFrozen && row.State != "Frozen") throw new ReleaseDisclosureDeniedException();
        string status; string reason; JsonElement? selection = null; string? selectionHash = null; JsonElement? spec = null;
        DateTimeOffset? boundary = null;
        if (row.State == "Draft")
        {
            var facts = await selections.ReadUnderAdmissionAsync(installation, tenant, project,
                ReleaseSelection.Create(row.ApplicationId, row.EnvironmentId, row.TargetId, row.ArtifactId), now, ct);
            status = facts.Status == "eligible" ? "eligible" : "ineligible";
            reason = facts.Status switch { "eligible" => "eligible", "policy_missing" => "policy_missing", "policy_denied" => "policy_denied",
                "qualification_required" => "qualification_required", "owner_missing" or "owner_mismatch" => "selection_changed", _ => throw new InvalidDataException("Unknown selection state.") };
            boundary = facts.EligibilityExpiresAtUtc;
            if (facts.Intent is not null && status == "eligible")
            {
                using var document = JsonDocument.Parse(facts.Intent.GetCanonicalBytes()); selection = document.RootElement.Clone();
                selectionHash = ReleasePreviewHash.Compute(row.RevisionId, row.Version, facts.Intent.SpecHash);
            }
        }
        else
        {
            status = "not_applicable"; reason = row.State == "Retired" ? "release_retired" : "eligible";
            if (requireCurrentFrozen && row.State == "Frozen")
            {
                var current = await selections.ReadUnderAdmissionAsync(installation, tenant, project,
                    ReleaseSelection.Create(row.ApplicationId, row.EnvironmentId, row.TargetId, row.ArtifactId), now, ct);
                if (current.Status != "eligible" || current.Intent?.SpecHash != row.SpecHash) throw new ReleaseDisclosureDeniedException();
                boundary = current.EligibilityExpiresAtUtc;
            }
            var bytes = row.GetCanonicalSubject() ?? throw new InvalidDataException("Frozen subject is missing.");
            using var document = JsonDocument.Parse(bytes); spec = document.RootElement.Clone();
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, correlationId = correlation.ToString("D"), revision = new
            {
                releaseId = row.ReleaseId.ToString("D"), releaseRevisionId = row.RevisionId.ToString("D"), row.RevisionNumber, row.Version,
                recordedState = row.State, creatorPrincipalId = row.CreatorPrincipalId.ToString("D"), row.Label,
                createdAtUtc = Format(row.CreatedAtUtc), updatedAtUtc = Format(row.UpdatedAtUtc),
                frozenAtUtc = FormatNullable(row.FrozenAtUtc), retiredAtUtc = FormatNullable(row.RetiredAtUtc), row.RetiredReasonCode,
                selectionStatus = status, eligibilityReasonCode = reason, selection, selectionHash, spec, row.SpecHash, executionAuthorized = false
            }
        }, Json);
        return new(200, payload, $"\"rel-v1:{row.RevisionId:D}:{row.Version}\"", boundary);
    }
    internal async Task<ReleasePayload> QueryAsync(Guid installation, Guid tenant, Guid project, ReleaseReadQuery query,
        DateTimeOffset now, CancellationToken ct)
    {
        if (query.Kind == ReleaseReadKind.Detail)
            return await DetailAsync(installation, tenant, project, query.ResourceId!.Value, query.CorrelationId, now, false, ct);
        await using var db = await contexts.CreateDbContextAsync(ct); var store = new ReleaseQueryStore(db);
        if (query.Kind == ReleaseReadKind.Lineages)
        {
            var page = await store.ReadLineagesUnderAdmissionAsync(installation, tenant, project, query.Limit, query.AfterId, ct);
            if (page is null) return Error(query.CorrelationId);
            var items = page.Items.Select(x => new { releaseId = x.ReleaseId.ToString("D"), projectId = x.ProjectId.ToString("D"), createdAtUtc = Format(x.CreatedAtUtc) }).ToArray();
            return new(200, JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = query.CorrelationId.ToString("D"), items, nextAfterId = page.NextAfterId?.ToString("D") }, Json), null, null);
        }
        var revisions = await store.ReadRevisionsUnderAdmissionAsync(installation, tenant, project, query.ResourceId!.Value, query.Limit, query.AfterId, ct);
        if (revisions is null) return Error(query.CorrelationId);
        var summaries = revisions.Items.Select(x => new { releaseId = x.ReleaseId.ToString("D"), releaseRevisionId = x.RevisionId.ToString("D"),
            x.RevisionNumber, x.Version, recordedState = x.State, x.Label, applicationId = x.ApplicationId.ToString("D"),
            environmentId = x.EnvironmentId.ToString("D"), targetId = x.TargetId.ToString("D"), artifactId = x.ArtifactId.ToString("D"),
            createdAtUtc = Format(x.CreatedAtUtc), frozenAtUtc = FormatNullable(x.FrozenAtUtc), x.SpecHash, executionAuthorized = false }).ToArray();
        return new(200, JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = query.CorrelationId.ToString("D"), items = summaries,
            nextAfterId = revisions.NextAfterId?.ToString("D") }, Json), null, null);
    }
    private static ReleasePayload Error(Guid correlation) => new(404, JsonSerializer.SerializeToUtf8Bytes(new
    { schemaVersion = 1, correlationId = correlation.ToString("D"), code = "not_found" }, Json), null, null);
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    private static string? FormatNullable(DateTimeOffset? value) => value is null ? null : Format(value.Value);
}
