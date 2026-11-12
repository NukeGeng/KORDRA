using System.Globalization;
using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.Extensions.Primitives;

namespace Kordra.Api;

public sealed record ParsedWorkflowStart(Guid EnvironmentId, Guid ReleaseId, long ReleaseRevision, string ReleaseHash,
    string TemplateId, int TemplateVersion, DateTimeOffset OriginalDeadlineUtc, Guid RequestId, string IdempotencyKey)
{
    public override string ToString() => "ParsedWorkflowStart [redacted]";
}

/// <summary>Decodes the approved wire shape. No caller-supplied actor, tenant, policy or instance facts are accepted.</summary>
public static class WorkflowStartRequestParser
{
    private const string DeadlineFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private static readonly string[] Fields =
        ["schemaVersion", "releaseId", "releaseRevision", "releaseHash", "templateId", "templateVersion", "originalDeadline", "requestId"];

    public static ParsedWorkflowStart Parse(string environment, ReadOnlyMemory<byte> body, StringValues keys)
    {
        if (body.Length > ReleaseRequestParser.MaximumBodyBytes ||
            !ReleaseRequestParser.GuidValue(environment, out var environmentId) ||
            keys.Count != 1 || keys[0] is not { Length: >= 1 and <= 128 } key ||
            key.Any(c => c is < '!' or > '~' or ','))
            throw Invalid();

        using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 2 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!Fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
        if (seen.Count != Fields.Length || root.GetProperty("schemaVersion").GetRawText() != "1" ||
            !ReleaseRequestParser.GuidValue(Text(root, "releaseId"), out var releaseId) ||
            !ReleaseRequestParser.GuidValue(Text(root, "requestId"), out var requestId))
            throw Invalid();

        var revisionField = root.GetProperty("releaseRevision");
        var hash = Text(root, "releaseHash");
        var templateId = Text(root, "templateId");
        if (revisionField.ValueKind != JsonValueKind.Number || !revisionField.TryGetInt64(out var revision) ||
            revision <= 0 || hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            templateId != WorkflowSpecification.TemplateId ||
            root.GetProperty("templateVersion").GetRawText() != WorkflowSpecification.TemplateVersion.ToString(CultureInfo.InvariantCulture) ||
            !DateTimeOffset.TryParseExact(Text(root, "originalDeadline"), DeadlineFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var deadline) ||
            deadline == default || deadline == DateTimeOffset.MaxValue)
            throw Invalid();

        return new(environmentId, releaseId, revision, hash, templateId, WorkflowSpecification.TemplateVersion, deadline, requestId, key);
    }

    private static string Text(JsonElement root, string name) =>
        root.GetProperty(name).ValueKind == JsonValueKind.String ? root.GetProperty(name).GetString()! : throw Invalid();

    private static FormatException Invalid() => new("Invalid workflow start request.");
}
