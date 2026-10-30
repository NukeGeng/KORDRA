using System.Globalization;
using System.Text.Json;
using Kordra.ReleaseManagement.Domain;
using Microsoft.Extensions.Primitives;

namespace Kordra.Api;

/// <summary>Validated wire input only. The environment's trusted tenant/project and actor are resolved by owners.</summary>
public sealed class ParsedApprovalMutation
{
    internal ParsedApprovalMutation(Guid correlationId, Guid environmentId, Guid? requestId, string action,
        Guid? releaseRevisionId, string? reasonCode, long? expectedVersion, string businessHash) =>
        (CorrelationId, EnvironmentId, RequestId, Action, ReleaseRevisionId, ReasonCode, ExpectedVersion, BusinessHash) =
        (correlationId, environmentId, requestId, action, releaseRevisionId, reasonCode, expectedVersion, businessHash);
    public Guid CorrelationId { get; }
    public Guid EnvironmentId { get; }
    public Guid? RequestId { get; }
    public string Action { get; }
    public Guid? ReleaseRevisionId { get; }
    public string? ReasonCode { get; }
    public long? ExpectedVersion { get; }
    public string BusinessHash { get; }
    public string Permission => Action switch { "request" => "release.request-approval", "grant" or "reject" => "release.approve",
        "revoke" => "release.approval.revoke", _ => throw new InvalidOperationException("Invalid approval action.") };
    public string Resource => "/api/v1/environments/" + EnvironmentId.ToString("D") + "/release-approvals" +
        (RequestId is { } request ? "/" + request.ToString("D") + "/" + Action : "");
}

/// <summary>Closed T11 approval body and exact precondition decoding. No authority or mutation is created.</summary>
public static class ApprovalRequestParser
{
    public static ParsedApprovalMutation Parse(string action, string environment, string? request,
        ReadOnlyMemory<byte> bytes, StringValues ifMatch)
    {
        if (bytes.Length > ReleaseRequestParser.MaximumBodyBytes) throw new ReleaseBodyLimitException();
        if (!ReleaseRequestParser.GuidValue(environment, out var environmentId)) throw Invalid();
        Guid? requestId = null;
        if (action == "request") { if (request is not null || ifMatch.Count != 0) throw Invalid(); }
        else
        {
            if (action is not ("grant" or "reject" or "revoke") || !ReleaseRequestParser.GuidValue(request, out var id)) throw Invalid();
            requestId = id;
        }
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 2 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Invalid();
        string[] fields = action == "request" ? ["schemaVersion", "correlationId", "releaseRevisionId"] : ["schemaVersion", "correlationId", "reasonCode"];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
        if (seen.Count != fields.Length || root.GetProperty("schemaVersion").GetRawText() != "1" ||
            !ReleaseRequestParser.GuidValue(Text(root, "correlationId"), out var correlationId)) throw Invalid();
        Guid? revisionId = null; string? reason = null; long? version = null; string? etag = null;
        if (action == "request")
        {
            if (!ReleaseRequestParser.GuidValue(Text(root, "releaseRevisionId"), out var revision)) throw Invalid();
            revisionId = revision;
        }
        else
        {
            reason = Text(root, "reasonCode");
            if (ifMatch.Count == 0) throw new ReleasePreconditionException();
            if (ifMatch.Count != 1 || ifMatch[0] is not { } text || text.Length > 80 ||
                !text.StartsWith("\"apr-v1:", StringComparison.Ordinal) || !text.EndsWith('"')) throw Invalid();
            var parts = text[8..^1].Split(':');
            if (parts.Length != 2 || !ReleaseRequestParser.GuidValue(parts[0], out var id) ||
                !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVersion) || parsedVersion <= 0 ||
                parts[1] != parsedVersion.ToString(CultureInfo.InvariantCulture)) throw Invalid();
            if (id != requestId) throw new ReleasePreconditionException();
            version = parsedVersion; etag = text;
        }
        var business = action == "request"
            ? JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, releaseRevisionId = revisionId!.Value })
            : JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, reasonCode = reason });
        try { return new(correlationId, environmentId, requestId, action, revisionId, reason, version, ReleaseCommandHash.Body(action, business, etag)); }
        catch (ArgumentException error) { throw new FormatException("Invalid approval request.", error); }
    }
    private static string Text(JsonElement root, string name) => root.GetProperty(name).ValueKind == JsonValueKind.String
        ? root.GetProperty(name).GetString()! : throw Invalid();
    private static FormatException Invalid() => new("Invalid approval request.");
}
