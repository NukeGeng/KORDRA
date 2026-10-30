using System.Globalization;
using System.Text.Json;
using Kordra.ReleaseManagement.Domain;
using Microsoft.Extensions.Primitives;

namespace Kordra.Api;

public sealed record ParsedReleaseMutation(Guid CorrelationId, ReleaseMutationCommand Command);
public sealed record ParsedReleasePage(int Limit, Guid? AfterId);
public sealed class ReleaseBodyLimitException : Exception;
public sealed class ReleasePreconditionException : Exception;

/// <summary>Closed, bounded wire decoding; no result establishes tenant, permission or admission.</summary>
public static class ReleaseRequestParser
{
    public const int MaximumBodyBytes = 16 * 1024;
    public static ParsedReleaseMutation Parse(string action, string project, string? resource,
        ReadOnlyMemory<byte> body, StringValues ifMatch)
    {
        if (body.Length > MaximumBodyBytes) throw new ReleaseBodyLimitException();
        if (!GuidValue(project, out var projectId)) throw Invalid();
        Guid? resourceId = null;
        if (action != "create-draft")
        {
            if (!GuidValue(resource, out var parsed)) throw Invalid();
            resourceId = parsed;
        }
        else if (resource is not null) throw Invalid();
        using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 3 });
        var root = document.RootElement;
        var fields = action switch
        {
            "create-draft" or "replace-draft" => new[] { "schemaVersion", "correlationId", "label", "applicationId", "environmentId", "targetId", "artifactId" },
            "next-draft" => new[] { "schemaVersion", "correlationId", "baseRevisionId", "label", "applicationId", "environmentId", "targetId", "artifactId" },
            "freeze" => new[] { "schemaVersion", "correlationId", "expectedSelectionHash" },
            "retire" => new[] { "schemaVersion", "correlationId", "reasonCode" },
            _ => throw Invalid()
        };
        Exact(root, fields);
        var schema = root.GetProperty("schemaVersion");
        if (schema.ValueKind != JsonValueKind.Number || schema.GetRawText() != "1" || !GuidValue(Text(root, "correlationId"), out var correlation)) throw Invalid();
        var requiresMatch = action is "replace-draft" or "freeze" or "retire";
        long version = 0; string? etag = null;
        if (requiresMatch)
        {
            if (ifMatch.Count == 0) throw new ReleasePreconditionException();
            if (ifMatch.Count != 1 || string.IsNullOrEmpty(ifMatch[0])) throw Invalid();
            etag = ifMatch[0]!; version = ParseEtag(etag, resourceId!.Value);
        }
        else if (ifMatch.Count != 0) throw Invalid();
        try
        {
            ReleaseMutationCommand command;
            if (action is "create-draft" or "next-draft" or "replace-draft")
            {
                if (!GuidValue(Text(root, "applicationId"), out var app) || !GuidValue(Text(root, "environmentId"), out var environment) ||
                    !GuidValue(Text(root, "targetId"), out var target) || !GuidValue(Text(root, "artifactId"), out var artifact)) throw Invalid();
                var selection = ReleaseSelection.Create(app, environment, target, artifact); var label = Text(root, "label");
                command = action switch
                {
                    "create-draft" => ReleaseMutationCommand.Create(projectId, label, selection),
                    "replace-draft" => ReleaseMutationCommand.Replace(projectId, resourceId!.Value, version, etag!, label, selection),
                    _ => GuidValue(Text(root, "baseRevisionId"), out var baseline)
                        ? ReleaseMutationCommand.Next(projectId, resourceId!.Value, baseline, label, selection) : throw Invalid()
                };
            }
            else command = action == "freeze"
                ? ReleaseMutationCommand.Freeze(projectId, resourceId!.Value, version, etag!, Text(root, "expectedSelectionHash"))
                : ReleaseMutationCommand.Retire(projectId, resourceId!.Value, version, etag!, Text(root, "reasonCode"));
            return new(correlation, command);
        }
        catch (ArgumentException error) { throw new FormatException("Invalid release request.", error); }
    }
    public static ParsedReleasePage ParsePage(IQueryCollection query)
    {
        if (query.Any(x => x.Key is not ("limit" or "afterId") || x.Value.Count != 1)) throw Invalid();
        var limit = 50; Guid? after = null;
        if (query.TryGetValue("limit", out var values))
        {
            var text = values[0];
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 50 ||
                text != limit.ToString(CultureInfo.InvariantCulture)) throw Invalid();
        }
        if (query.TryGetValue("afterId", out var cursors))
        { if (!GuidValue(cursors[0], out var id)) throw Invalid(); after = id; }
        return new(limit, after);
    }
    public static async Task<byte[]> ReadBodyAsync(Stream body, CancellationToken ct = default)
    {
        var buffer = new byte[MaximumBodyBytes + 1]; var length = 0;
        while (length < buffer.Length)
        { var count = await body.ReadAsync(buffer.AsMemory(length), ct); if (count == 0) break; length += count; }
        if (length > MaximumBodyBytes) throw new ReleaseBodyLimitException();
        return buffer[..length];
    }
    public static bool ValidFraming(HttpRequest request) =>
        !(request.Headers.ContainsKey("Content-Length") && request.Headers.ContainsKey("Transfer-Encoding"));
    public static bool GuidValue(string? text, out Guid result) => Guid.TryParseExact(text, "D", out result) &&
        result != Guid.Empty && result.ToString("D") == text;
    private static long ParseEtag(string value, Guid expectedId)
    {
        if (value.Length > 80 || !value.StartsWith("\"rel-v1:", StringComparison.Ordinal) || !value.EndsWith('"')) throw Invalid();
        var parts = value[8..^1].Split(':');
        if (parts.Length != 2 || !GuidValue(parts[0], out var id) ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version <= 0 ||
            parts[1] != version.ToString(CultureInfo.InvariantCulture)) throw Invalid();
        if (id != expectedId) throw new ReleasePreconditionException();
        return version;
    }
    private static void Exact(JsonElement element, string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
        if (seen.Count != fields.Length) throw Invalid();
    }
    private static string Text(JsonElement element, string name) => element.GetProperty(name).ValueKind == JsonValueKind.String
        ? element.GetProperty(name).GetString()! : throw Invalid();
    private static FormatException Invalid() => new("Invalid release request.");
}
