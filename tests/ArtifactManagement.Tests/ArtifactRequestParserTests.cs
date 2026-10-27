using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kordra.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Kordra.Artifacts.Tests;

public sealed class ArtifactRequestParserTests
{
    private static readonly Guid Correlation = Guid.NewGuid(), Registry = Guid.NewGuid();
    private static string Body => $$"""{"schemaVersion":1,"correlationId":"{{Correlation:D}}","registryId":"{{Registry:D}}","repository":"team/frontend","digest":"sha256:{{new string('a',64)}}","platform":{"os":"linux","architecture":"amd64","variant":null},"sourceClaim":null}""";
    [Fact]
    public void Exact_input_retains_explicit_nulls_and_correlation()
    {
        var request = ArtifactRequestParser.Parse(Encoding.UTF8.GetBytes(Body));
        Assert.Equal(Correlation, request.CorrelationId); Assert.Equal(Registry, request.RegistryId);
        Assert.Null(request.Platform.Variant); Assert.Null(request.SourceClaim);
    }

    [Theory]
    [InlineData("tenantId", "\"bad\"")]
    [InlineData("schemaVersion", "1")]
    [InlineData("platform", "null")]
    [InlineData("credential", "\"never-authority\"")]
    public void Unknown_or_duplicate_top_level_fields_reject(string key, string value) =>
        Assert.Throws<FormatException>(() => ArtifactRequestParser.Parse(Encoding.UTF8.GetBytes(Body[..^1] + $",\"{key}\":{value}}}")));

    [Theory]
    [InlineData("schemaVersion", "null")]
    [InlineData("schemaVersion", "\"1\"")]
    [InlineData("schemaVersion", "2")]
    [InlineData("schemaVersion", "1.5")]
    [InlineData("correlationId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("registryId", "null")]
    [InlineData("repository", "\"Team/frontend\"")]
    [InlineData("repository", "\"team/../frontend\"")]
    [InlineData("repository", "\"https://private.invalid/image\"")]
    [InlineData("digest", "\"latest\"")]
    [InlineData("platform", "{\"os\":\"linux\",\"architecture\":\"amd64\"}")]
    [InlineData("platform", "{\"os\":\"linux\",\"architecture\":\"amd64\",\"variant\":null,\"variant\":null}")]
    [InlineData("platform", "{\"os\":\"linux\",\"architecture\":\"amd64\",\"variant\":\"v8\"}")]
    [InlineData("sourceClaim", "{\"sourceRepository\":\"team/frontend\",\"sourceCommit\":\"short\",\"buildRef\":null}")]
    [InlineData("sourceClaim", "{}")]
    public void Invalid_types_values_and_nested_shape_reject(string key, string replacement)
    {
        var node = JsonNode.Parse(Body)!; node[key] = JsonNode.Parse(replacement);
        Assert.Throws<FormatException>(() => ArtifactRequestParser.Parse(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Fact]
    public void Missing_and_escaped_duplicate_fields_reject()
    {
        var node = JsonNode.Parse(Body)!.AsObject(); node.Remove("sourceClaim");
        Assert.Throws<FormatException>(() => ArtifactRequestParser.Parse(Encoding.UTF8.GetBytes(node.ToJsonString())));
        Assert.Throws<FormatException>(() => ArtifactRequestParser.Parse(Encoding.UTF8.GetBytes(Body[..^1] + ",\"schema\\u0056ersion\":1}")));
        Assert.Throws<FormatException>(() => ArtifactRequestParser.Parse(Encoding.UTF8.GetBytes(Body.Replace(Correlation.ToString("D"), Correlation.ToString("D").ToUpperInvariant()))));
    }

    [Theory]
    [InlineData("1234567890123456", true)]
    [InlineData("123456789012345_", true)]
    [InlineData("123456789012345~", false)]
    [InlineData("123456789012345 ", false)]
    [InlineData("123456789012345é", false)]
    [InlineData("123456789012345", false)]
    public void Key_has_exact_ASCII_grammar(string value, bool valid) => Assert.Equal(valid, ArtifactRequestParser.ValidKey(value));

    [Fact]
    public async Task Streaming_limit_reads_at_most_one_extra_byte_and_accepts_exact_bound()
    {
        using var exact = new MemoryStream(new byte[ArtifactRequestParser.MaximumBodyBytes]);
        Assert.Equal(ArtifactRequestParser.MaximumBodyBytes, (await ArtifactRequestParser.ReadBodyAsync(exact, default)).Length);
        using var tooLarge = new MemoryStream(new byte[ArtifactRequestParser.MaximumBodyBytes + 100]);
        await Assert.ThrowsAsync<ArtifactBodyLimitException>(() => ArtifactRequestParser.ReadBodyAsync(tooLarge, default));
        Assert.Equal(ArtifactRequestParser.MaximumBodyBytes + 1, tooLarge.Position);
    }

    [Theory]
    [InlineData("limit", "0")]
    [InlineData("limit", "51")]
    [InlineData("limit", "01")]
    [InlineData("limit", "+1")]
    [InlineData("limit", " 1")]
    [InlineData("afterArtifactId", "")]
    [InlineData("tenantId", "anything")]
    public void Invalid_pagination_rejects(string key, string value)
    {
        var query = new QueryCollection(new Dictionary<string, StringValues> { [key] = value });
        Assert.Throws<FormatException>(() => ArtifactRequestParser.ParseQuery(query, ArtifactQueryKind.List, Correlation));
    }

    [Fact]
    public void Pagination_is_bounded_unique_and_only_for_list()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues> { ["limit"] = "1", ["afterArtifactId"] = Registry.ToString("D") });
        var parsed = ArtifactRequestParser.ParseQuery(query, ArtifactQueryKind.List, Correlation);
        Assert.Equal(1, parsed.Limit); Assert.Equal(Registry, parsed.AfterArtifactId);
        Assert.Throws<FormatException>(() => ArtifactRequestParser.ParseQuery(query, ArtifactQueryKind.Detail, Correlation, Registry));
        var duplicate = new QueryCollection(new Dictionary<string, StringValues> { ["limit"] = new(new[] { "1", "2" }) });
        Assert.Throws<FormatException>(() => ArtifactRequestParser.ParseQuery(duplicate, ArtifactQueryKind.List, Correlation));
    }

    [Fact]
    public void Activation_is_opt_in_and_validates_locators_without_connecting()
    {
        var installation = Guid.NewGuid();
        Assert.Null(ArtifactRuntimeConfiguration.Load(installation, null, null, null).ConnectionString);
        var locator = "Host=127.0.0.1;Port=5432;Database=kordra;Username=kordra_app;Timeout=1";
        Assert.Equal(locator, ArtifactRuntimeConfiguration.Load(installation, locator, null, null).ConnectionString);
        Assert.Throws<InvalidOperationException>(() => ArtifactRuntimeConfiguration.Load(installation, locator.Replace("kordra_app", "postgres"), null, null));
        Assert.Throws<InvalidOperationException>(() => ArtifactRuntimeConfiguration.Load(installation, locator, "file", null));
        Assert.Throws<InvalidOperationException>(() => ArtifactRuntimeConfiguration.Load(installation, null, "file", new string('a',64)));
        Assert.Throws<InvalidOperationException>(() => ArtifactRuntimeConfiguration.Load(installation, "", null, null));
        Assert.DoesNotContain(locator, ArtifactRuntimeConfiguration.Load(installation, locator, null, null).ToString());
    }

    [Fact]
    public void Published_schema_rejects_platform_and_identifier_shapes_that_the_API_rejects()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kordra.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var api = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root!.FullName, "docs/contracts/openapi/artifact-registration-v1.openapi.json")));
        var schemas = api.RootElement.GetProperty("components").GetProperty("schemas");
        var branches = schemas.GetProperty("Platform").GetProperty("oneOf").EnumerateArray().ToArray();
        bool Allows(string arch, string? variant) => branches.Count(branch =>
        {
            var properties = branch.GetProperty("properties");
            if (properties.GetProperty("architecture").GetProperty("const").GetString() != arch) return false;
            var choice = properties.GetProperty("variant");
            if (choice.TryGetProperty("type", out var type)) return type.GetString() == "null" && variant is null;
            return choice.GetProperty("enum").EnumerateArray().Any(value =>
                value.ValueKind == JsonValueKind.Null ? variant is null : value.GetString() == variant);
        }) == 1;
        Assert.True(Allows("amd64", null)); Assert.True(Allows("arm64", null)); Assert.True(Allows("arm64", "v8"));
        Assert.False(Allows("amd64", "v8")); Assert.False(Allows("arm64", "v7")); Assert.False(Allows("x86", null));
        var request = schemas.GetProperty("RegistrationRequest").GetProperty("properties");
        var guid = new Regex(request.GetProperty("registryId").GetProperty("pattern").GetString()!, RegexOptions.CultureInvariant);
        Assert.Matches(guid, Registry.ToString("D")); Assert.DoesNotMatch(guid, Guid.Empty.ToString("D"));
        Assert.DoesNotMatch(guid, "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        var repository = new Regex(request.GetProperty("repository").GetProperty("pattern").GetString()!, RegexOptions.CultureInvariant);
        Assert.Matches(repository, "team/frontend"); Assert.DoesNotMatch(repository, "team/../frontend");
        Assert.DoesNotMatch(repository, "Team/frontend");
        var source = new Regex(schemas.GetProperty("SourceClaim").GetProperty("properties").GetProperty("sourceRepository").GetProperty("pattern").GetString()!, RegexOptions.CultureInvariant);
        Assert.Matches(source, "team/frontend"); Assert.DoesNotMatch(source, "/team");
        Assert.DoesNotMatch(source, "team/"); Assert.DoesNotMatch(source, "team/../frontend");
    }
}
