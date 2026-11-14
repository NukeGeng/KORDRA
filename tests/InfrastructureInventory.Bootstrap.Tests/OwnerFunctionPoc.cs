using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>T12.3a owner function PoC artifacts and the exact scripts a disposable run executes (no database access).</summary>
internal static partial class OwnerFunctionPoc
{
    internal static readonly string[] Artifacts = ["iam-workflow-authority-v1", "wfl-owner-functions-v1", "aud-bound-append-v1"];
    internal static readonly string[] Roles = ["kordra_iam_owner", "kordra_wfl_owner", "kordra_aud_owner", "kordra_worker_runtime", "kordra_api_runtime"];

    internal static string Directory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "OwnerFunctions");
    internal static string Sql(string artifact) => File.ReadAllText(Path.Combine(Directory, artifact + ".sql"));
    internal static byte[] SqlBytes(string artifact) => File.ReadAllBytes(Path.Combine(Directory, artifact + ".sql"));
    internal static string Manifest(string artifact) => File.ReadAllText(Path.Combine(Directory, artifact + ".manifest.json"));

    [GeneratedRegex("\\b(kordra_(?:iam_owner|wfl_owner|aud_owner|worker_runtime|api_runtime))\\b")]
    private static partial Regex RolePattern();

    [GeneratedRegex("^[0-9a-f]{12}$")]
    private static partial Regex SuffixPattern();

    /// <summary>Run-scoped role names (D4): only the five placeholder identifiers receive the run suffix.</summary>
    internal static string ForRun(string sql, string suffix)
    {
        if (!SuffixPattern().IsMatch(suffix)) throw new ArgumentException("Run suffix must be 12 lowercase hex characters.");
        return RolePattern().Replace(sql, match => match.Value + "_r" + suffix);
    }

    internal static string Role(string placeholder, string suffix) => ForRun(placeholder, suffix);

    internal const string IamFloorTable = "CREATE TABLE app.iam_authority_clock_floors (\n" +
        "    installation_id uuid NOT NULL,\n    admitted_utc timestamp with time zone NOT NULL,\n" +
        "    CONSTRAINT \"PK_iam_authority_clock_floors\" PRIMARY KEY (installation_id)\n);\n";

    /// <summary>Ordered scripts for one run: existing owner baselines, then the reviewed owner artifacts with run role names.</summary>
    internal static IReadOnlyList<(string Name, string Sql)> Scripts(string suffix)
    {
        using var wfl = WorkflowDbContext.CreateOfflineAuthorizationModel();
        using var aud = new AuditDbContext(AuditDbContextFactory.CreateOfflineMigrationOptions(
            "Host=127.0.0.1;Database=kordra;Username=kordra_admin;Timeout=1"));
        return [
            ("baseline-wfl.sql", wfl.Database.GenerateCreateScript()),
            ("baseline-aud.sql", aud.Database.GenerateCreateScript() + "\n" + IamFloorTable),
            .. Artifacts.Select(name => (name + ".sql", ForRun(Sql(name), suffix)))
        ];
    }

    internal static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// Fixture-only permit seed (privileged setup login). Both timestamps derive from ONE clock sample, so the TTL is exactly
    /// the stated interval and never exceeds the 2 s <c>ck_permits_ttl</c> check (PR33-POC-CLOCK-001).
    /// </summary>
    internal static string PermitSeedSql(bool expired) =>
        "INSERT INTO iam_workflow.permits (permit_id, service_id, generation, installation_id, tenant_id, instance_id, operation_id, " +
        "expected_version, specification_hash, transition_kind, authority_epoch, boot_id, custody_epoch, issued_at, expires_at) " +
        "SELECT @id, @s, 1, @i, @t, @n, @o, 1, @h, 'admit', @e, @b, 0, " +
        (expired ? "sample.at - interval '3 seconds', sample.at - interval '2 seconds'"
                 : "sample.at, sample.at + interval '2 seconds'") +
        " FROM (SELECT clock_timestamp() AS at) AS sample";
}
