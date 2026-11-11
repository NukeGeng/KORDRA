namespace Kordra.Testing;

/// <summary>Test-runner configuration only. Product never reads this descriptor or these environment values.
/// Selection grants no operation rights; the separately approved runner verifies its manifest before execution.</summary>
internal static class RegressionPostgreSqlSelection
{
    internal const string PathVariable = "KORDRA_REGRESSION_PROFILE_PATH";
    internal const string RunVariable = "KORDRA_REGRESSION_RUN_ID";
    internal const string SourceVariable = "KORDRA_REGRESSION_SOURCE_SHA256";
    internal const string ManifestVariable = "KORDRA_REGRESSION_MANIFEST_SHA256";

    // No cached/global mutable profile. Each fixture explicitly captures and passes the immutable selection.
    internal static DisposablePostgreSqlProfile? LoadRunnerProfile() => Read(
        Environment.GetEnvironmentVariable(PathVariable), Environment.GetEnvironmentVariable(RunVariable),
        Environment.GetEnvironmentVariable(SourceVariable), Environment.GetEnvironmentVariable(ManifestVariable),
        File.ReadAllBytes, DateTimeOffset.UtcNow);

    internal static DisposablePostgreSqlProfile? Read(string? path, string? run, string? source, string? manifest,
        Func<string, byte[]> readFile, DateTimeOffset now)
    {
        if (path is null && run is null && source is null && manifest is null) return null;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) ||
            !Guid.TryParseExact(run, "D", out var runId) || runId == Guid.Empty || run != runId.ToString("D") ||
            string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(manifest)) throw Invalid();
        try { return DisposablePostgreSqlProfile.Read(readFile(path), runId, source, manifest, now); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new("Explicit regression profile configuration invalid; values omitted; no legacy fallback.");
}
