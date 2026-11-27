using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>
/// 12.3d-1 on actual PostgreSQL (operation-gated; ADR-0025 disposable database + run-scoped roles): the offline service CLI
/// (<see cref="OfflineServiceCoordinator"/>) waits while the installation fence is held and advances the epoch once per applied
/// manifest (registry scenario <c>iam.service-cli</c>); its exact replay commits nothing. Permits (IAM v3, D3) require the
/// presented pin of the service's current generation: unknown, not yet valid, expired and revoked-generation pins are
/// refused with KD024. Only the D7 NOLOGIN role may execute <c>apply_service_manifest</c>, and no unpinned issuance remains.
/// </summary>
public sealed class AuthorityFenceServiceCliDatabaseTests
{
    /// <summary>Registry scenarios this class proves; the completeness test pins it to <see cref="AuthorityWriterRegistry"/>.</summary>
    internal static readonly string[] Scenarios = ["iam.service-cli"];
    private const string ManifestVariable = "KORDRA_FENCE_SERVICE_MANIFEST";
    private const string LocatorVariable = "KORDRA_FENCE_SERVICE_CONNECTION_STRING";
    private const string ApplySignature = "iam_workflow.apply_service_manifest(text, text, uuid, uuid, text, name, text, " +
        "timestamp with time zone, timestamp with time zone, jsonb, uuid)";

    [Fact]
    public void ExportFenceSqlWithoutDatabaseAccess()
    {
        var sql = AuthorityFenceIamWriterDatabaseTests.Script("0123456789ab");
        Assert.Contains("GRANT USAGE ON SCHEMA iam_workflow TO kordra_iam_service_admin_r0123456789ab", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_FENCE_SERVICE_EXPORT") is not { Length: > 0 } directory) return;
        var suffix = Environment.GetEnvironmentVariable("KORDRA_FENCE_SERVICE_SUFFIX") ?? throw new InvalidOperationException("Run suffix required.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "iam-workflow-authority-v3.sql"), AuthorityFenceIamWriterDatabaseTests.Script(suffix));
    }

    /// <summary>The manifests this race builds parse offline, so an input defect never costs an approved disposable batch.</summary>
    [Fact]
    public void RaceManifestsParseWithoutDatabaseAccess()
    {
        var installation = Guid.NewGuid(); var now = Seconds(DateTimeOffset.UtcNow);
        var binding = new WorkflowServiceBinding(Guid.NewGuid(), Guid.NewGuid(), WorkflowSpecification.TemplateId,
            WorkflowSpecification.TemplateVersion, "admit");
        foreach (var json in new[] {
                     Manifest("kordra_test_x", installation, "rotate", Guid.NewGuid(), Pin: (Hex(), now.AddMinutes(-1), now.AddHours(1))),
                     Manifest("kordra_test_x", installation, "bootstrap", Guid.NewGuid(), "worker", "fence_worker_two",
                         (Hex(), now.AddHours(1), now.AddHours(2)), [binding]),
                     Manifest("kordra_test_x", installation, "revoke", Guid.NewGuid()) })
            Assert.NotNull(ServiceManifestDocument.Parse(json));
    }

    [AuthorityFenceServiceDatabaseFact]
    public async Task ServiceCliHoldsTheFenceAndPermitsRequireTheCurrentPin()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        var ct = budget.Token;
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable(ManifestVariable)!));
        Assert.Equal("approved_for_execution", approval.RootElement.GetProperty("status").GetString());
        var suffix = approval.RootElement.GetProperty("suffix").GetString()!;
        var script = AuthorityFenceIamWriterDatabaseTests.Script(suffix);
        Assert.Equal(approval.RootElement.GetProperty("sha256").GetProperty("iam-workflow-authority-v3.sql").GetString(), OwnerFunctionPoc.Sha256(script));
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile() ?? throw new InvalidOperationException("Disposable runner profile required.");
        var locator = Environment.GetEnvironmentVariable(LocatorVariable) ?? throw new InvalidOperationException("Approved fixture locator required.");
        var selection = new ProductPostgreSqlFixtureSelection(locator, profile);
        var identity = new PooledDbContextFactory<KordraIdentityDbContext>(selection.Identity());
        var audits = new PooledDbContextFactory<AuditDbContext>(selection.Audit());
        await using (var db = await identity.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using (var db = await audits.CreateDbContextAsync(ct)) await db.Database.MigrateAsync(ct);
        await using var race = await AuthorityFenceRaceHarness.StartAsync(locator, suffix, script, ct);
        var installation = race.InstallationId;
        var lookups = selection.AuditStore(audits);
        var cli = OfflineServiceCoordinator.CreateDisposableTestCoordinator(installation, identity, lookups,
            new FairAsyncReaderWriterAdmissionGate(), new UtcClock(), null, profile.OwnerBinding);
        var lease = new HeldLease();
        var now = Seconds(DateTimeOffset.UtcNow);
        var binding = new WorkflowServiceBinding(race.TenantId, race.EnvironmentId, WorkflowSpecification.TemplateId,
            WorkflowSpecification.TemplateVersion, "admit");
        async Task<ServiceManifestResult> Apply(byte[] json) =>
            await cli.ApplyAsync(lease, json, ServiceManifestDocument.Parse(json)!.Hash(), ct);

        // (a) Registry scenario: rotating the registered worker through the CLI waits on the held fence and advances the epoch
        // once; the exact replay is answered from the manifest receipt and its AUD intent, without waiting or advancing.
        var rotatedPin = Hex();
        var rotate = Manifest(selection.Database, installation, "rotate", race.WorkerServiceId, Pin: (rotatedPin, now.AddMinutes(-1), now.AddHours(1)));
        await race.RaceAsync("iam.service-cli", true, async () => Outcome("success", (await Apply(rotate)).Status));
        var beforeReplay = await race.EpochAsync();
        var replay = await Apply(rotate);
        Assert.True(Outcome("success", replay.Status) && replay.Replayed);
        Assert.Equal(beforeReplay, await race.EpochAsync());
        Assert.Equal(AuditLookupStatus.Found, (await lookups.LookupAsync(ServiceManifestDocument.Parse(rotate)!.ManifestId, ct)).Status);
        // A third valid pin is refused by IAM and commits nothing.
        Assert.True(Outcome("conflict", (await Apply(Manifest(selection.Database, installation, "rotate", race.WorkerServiceId,
            Pin: (Hex(), now.AddMinutes(-1), now.AddHours(1))))).Status));
        Assert.Equal(beforeReplay, await race.EpochAsync());

        // (b) D3: both overlapping pins of the current generation issue; any other pin is refused under the fence.
        Assert.Null(await race.TryIssueAsync(race.WorkerServiceId, race.WorkerPin));
        Assert.Null(await race.TryIssueAsync(race.WorkerServiceId, rotatedPin));
        Assert.Equal("KD024", await race.TryIssueAsync(race.WorkerServiceId, Hex()));

        // A pin that is not valid yet, and a pin after its validity ends.
        var future = Guid.NewGuid(); var futurePin = Hex();
        Assert.True(Outcome("success", (await Apply(Manifest(selection.Database, installation, "bootstrap", future, "worker", "fence_worker_future",
            (futurePin, now.AddHours(1), now.AddHours(2)), [binding]))).Status));
        Assert.Equal("KD024", await race.TryIssueAsync(future, futurePin));
        var shortLived = Guid.NewGuid(); var shortPin = Hex(); var shortUntil = Seconds(DateTimeOffset.UtcNow).AddSeconds(4);
        Assert.True(Outcome("success", (await Apply(Manifest(selection.Database, installation, "bootstrap", shortLived, "worker", "fence_worker_short",
            (shortPin, now.AddMinutes(-1), shortUntil), [binding]))).Status));
        Assert.Null(await race.TryIssueAsync(shortLived, shortPin));
        await Task.Delay(shortUntil - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1), ct);
        Assert.Equal("KD024", await race.TryIssueAsync(shortLived, shortPin));

        // Revocation stops the service; a new bootstrap starts the next generation, whose pin alone issues.
        var cycled = Guid.NewGuid(); var firstPin = Hex(); var secondPin = Hex();
        Assert.True(Outcome("success", (await Apply(Manifest(selection.Database, installation, "bootstrap", cycled, "worker", "fence_worker_cycled",
            (firstPin, now.AddMinutes(-1), now.AddHours(1)), [binding]))).Status));
        Assert.Null(await race.TryIssueAsync(cycled, firstPin));
        Assert.True(Outcome("success", (await Apply(Manifest(selection.Database, installation, "revoke", cycled))).Status));
        Assert.Equal("KD017", await race.TryIssueAsync(cycled, firstPin));
        var regenerated = await Apply(Manifest(selection.Database, installation, "bootstrap", cycled, "worker", "fence_worker_cycled",
            (secondPin, now.AddMinutes(-1), now.AddHours(1)), [binding]));
        Assert.True(Outcome("success", regenerated.Status) && regenerated.Generation == 2);
        Assert.Equal("KD024", await race.TryIssueAsync(cycled, firstPin));
        Assert.Null(await race.TryIssueAsync(cycled, secondPin));

        // (c) D7: only the NOLOGIN service-manifest role executes the function; the unpinned issuance no longer exists.
        Assert.True(await race.QueryScalarAsync<bool>(Privilege("kordra_iam_service_admin", suffix)));
        Assert.False(await race.QueryScalarAsync<bool>(Privilege("kordra_api_runtime", suffix)));
        Assert.False(await race.QueryScalarAsync<bool>(Privilege("kordra_worker_runtime", suffix)));
        Assert.True(await race.QueryScalarAsync<bool>("SELECT to_regprocedure('iam_workflow.issue_permit(uuid, uuid, uuid, uuid, uuid, uuid, " +
            "bigint, text, text, text, uuid, bigint)') IS NULL"));
        Assert.Equal(Scenarios.Order(StringComparer.Ordinal), race.Proven.Order(StringComparer.Ordinal));
    }

    private static string Privilege(string placeholder, string suffix) =>
        $"SELECT has_function_privilege('{OwnerFunctionPoc.Role(placeholder, suffix)}', '{ApplySignature}', 'EXECUTE')";

    /// <summary>One service manifest file as an operator would write it (times at whole seconds, UTC).</summary>
    internal static byte[] Manifest(string database, Guid installation, string kind, Guid service, string? ServiceKind = null,
        string? Login = null, (string Spki, DateTimeOffset From, DateTimeOffset Until)? Pin = null, WorkflowServiceBinding[]? Bindings = null)
    {
        static string Time(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var document = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1, ["manifestId"] = Guid.NewGuid().ToString("D"), ["installationId"] = installation.ToString("D"),
            ["expectedDatabase"] = database, ["expectedRole"] = "kordra_test", ["kind"] = kind, ["serviceId"] = service.ToString("D"),
        };
        if (kind == "bootstrap")
        {
            document["serviceKind"] = ServiceKind; document["loginName"] = Login;
            document["bindings"] = (Bindings ?? []).Select(b => new Dictionary<string, object> {
                ["tenantId"] = b.TenantId.ToString("D"), ["environmentId"] = b.EnvironmentId.ToString("D"), ["templateId"] = b.TemplateId,
                ["templateVersion"] = b.TemplateVersion, ["transitionKind"] = b.TransitionKind }).ToArray();
        }
        if (Pin is { } pin)
            document["pin"] = new Dictionary<string, string> { ["spkiSha256"] = pin.Spki, ["notBeforeUtc"] = Time(pin.From), ["notAfterUtc"] = Time(pin.Until) };
        return JsonSerializer.SerializeToUtf8Bytes(document);
    }

    private static DateTimeOffset Seconds(DateTimeOffset t) => new(t.UtcTicks - t.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    private static string Hex() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>Letter-only Expected/Actual tokens, the only failure text the redacting disposable batch runner keeps.</summary>
    private static bool Outcome(string expected, string? actual)
    {
        if (actual != expected) Assert.Fail($"Unexpected status{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual: {actual ?? "none"}");
        return true;
    }

    private sealed class UtcClock : IAuthorityClock { public DateTimeOffset GetTrustedUtcNow() => DateTimeOffset.UtcNow; }
    private sealed class HeldLease : IOfflineProvisioningLease { public void RequireHeld() { } }
}

public sealed class AuthorityFenceServiceDatabaseFactAttribute : FactAttribute
{
    public AuthorityFenceServiceDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_FENCE_SERVICE_MANIFEST")))
            Skip = "Requires the exact approved 12.3d-1 service fixture manifest, disposable database and run roles.";
    }
}
