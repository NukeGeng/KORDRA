using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kordra.ArtifactManagement.Domain;
using Kordra.Modules.ArtifactManagement.Contracts;

namespace Kordra.Api;

/// <summary>Closed wire input only. No parsed value establishes tenant or project authority.</summary>
public static class ArtifactRequestParser
{
    public const int MaximumBodyBytes = 16 * 1024;
    private static readonly Regex Repository = new(@"\A[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*(/[a-z0-9]+((\.|_|__|-+)[a-z0-9]+)*)*\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool TryGuid(string? text, out Guid id) => Guid.TryParseExact(text, "D", out id) &&
        id != Guid.Empty && text == id.ToString("D");

    public static bool ValidKey(string? key) => key is { Length: >= 16 and <= 128 } &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    public static ArtifactRegistrationRequestV1 Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumBodyBytes) throw new ArtifactBodyLimitException();
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        Exact(root, "schemaVersion", "correlationId", "registryId", "repository", "digest", "platform", "sourceClaim");
        if (root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number ||
            !root.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1 ||
            !TryGuid(Text(root, "correlationId"), out var correlation) ||
            !TryGuid(Text(root, "registryId"), out var registry)) throw Invalid();
        var repository = Text(root, "repository");
        if (repository.Length is < 1 or > 255 || !Repository.IsMatch(repository)) throw Invalid();
        var digest = OCIImageDigest.Parse(Text(root, "digest"));
        var platform = root.GetProperty("platform"); Exact(platform, "os", "architecture", "variant");
        var selected = ArtifactPlatform.Create(Text(platform, "os"), Text(platform, "architecture"), NullableText(platform, "variant"));
        ArtifactSourceClaimV1? claim = null;
        var source = root.GetProperty("sourceClaim");
        if (source.ValueKind != JsonValueKind.Null)
        {
            Exact(source, "sourceRepository", "sourceCommit", "buildRef");
            var value = ArtifactSourceClaim.Create(Text(source, "sourceRepository"), Text(source, "sourceCommit"), NullableText(source, "buildRef"));
            claim = new(value.SourceRepository, value.SourceCommit, value.BuildRef);
        }
        return new(1, correlation, registry, repository, digest.Value, new(selected.Os, selected.Architecture, selected.Variant), claim);
    }

    public static ArtifactReadCommand ParseQuery(IQueryCollection query, ArtifactQueryKind kind, Guid correlation, Guid? resource = null)
    {
        if (kind != ArtifactQueryKind.List)
        {
            if (query.Count != 0) throw Invalid();
            return new(kind, correlation, resource);
        }
        if (query.Any(p => p.Key is not ("limit" or "afterArtifactId") || p.Value.Count != 1)) throw Invalid();
        var limit = 50; Guid? after = null;
        if (query.TryGetValue("limit", out var limits))
        {
            var text = limits[0];
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 50 ||
                text != limit.ToString(CultureInfo.InvariantCulture)) throw Invalid();
        }
        if (query.TryGetValue("afterArtifactId", out var cursors))
        { if (!TryGuid(cursors[0], out var cursor)) throw Invalid(); after = cursor; }
        return new(kind, correlation, Limit: limit, AfterArtifactId: after);
    }

    public static async Task<byte[]> ReadBodyAsync(Stream body, CancellationToken ct)
    {
        var buffer = new byte[MaximumBodyBytes + 1]; var length = 0;
        while (length < buffer.Length)
        { var read = await body.ReadAsync(buffer.AsMemory(length), ct); if (read == 0) break; length += read; }
        if (length > MaximumBodyBytes) throw new ArtifactBodyLimitException();
        return buffer[..length];
    }

    private static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
        if (seen.Count != names.Length) throw Invalid();
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.String
        ? value.GetProperty(name).GetString()! : throw Invalid();
    private static string? NullableText(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.Null
        ? null : Text(value, name);
    private static FormatException Invalid() => new("Invalid artifact request.");
}

public sealed class ArtifactBodyLimitException : Exception;
