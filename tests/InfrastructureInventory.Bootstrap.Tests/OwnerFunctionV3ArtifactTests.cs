using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>
/// 12.3d IAM owner function v3 artifact (ENG-T12-3D-INTERNAL-PERMIT-SCOPE D3/D7): manifest, lint, definer/search_path, the
/// pinned permit rule read under the fence and the exact grant matrix of the current baselines (IAM v3 with WFL v2).
/// </summary>
public sealed class OwnerFunctionV3ArtifactTests
{
    private const string Iam = "iam-workflow-authority-v3";

    [Fact]
    public void V3ArtifactMatchesItsManifestReplacesV2AndIsNonDestructive()
    {
        using var manifest = JsonDocument.Parse(OwnerFunctionPoc.Manifest(Iam));
        var root = manifest.RootElement;
        Assert.Equal(3, root.GetProperty("revision").GetInt32());
        Assert.Equal(Iam + ".sql", root.GetProperty("artifact").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(OwnerFunctionPoc.SqlBytes(Iam))), root.GetProperty("sha256").GetString());
        Assert.StartsWith("iam-workflow-authority-v2.sql", root.GetProperty("replaces").GetString());
        Assert.Contains("never deployed", root.GetProperty("replaces").GetString());
        Assert.False(root.GetProperty("destructiveOperations").GetBoolean());
        Assert.Equal(OwnerFunctionPoc.CurrentRoles, root.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToArray());
        var sql = Strip(OwnerFunctionPoc.Sql(Iam));
        foreach (var forbidden in new[] { "DROP ", "TRUNCATE", "DELETE FROM", "current_user", "SET ROLE", "SECURITY INVOKER",
                     "set_config", "SESSION AUTHORIZATION", "CREATE ROLE", "ALTER ROLE", "SUPERUSER", "BYPASSRLS", "format(",
                     "CREATE OR REPLACE" })
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"\bEXECUTE\b(?!\s+ON\s+FUNCTION)", RegexOptions.IgnoreCase), sql);
        var declared = root.GetProperty("functions").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Equal(declared.Length, Regex.Matches(sql, "CREATE FUNCTION ").Count);
        foreach (Match body in Regex.Matches(sql, @"CREATE FUNCTION ([a-z_]+\.[a-z_]+)\((.*?)\$fn\$", RegexOptions.Singleline))
        {
            Assert.Contains("SECURITY DEFINER", body.Value);
            Assert.Contains("SET search_path = pg_catalog, pg_temp", body.Value);
            var function = Regex.Escape(body.Groups[1].Value);
            Assert.Matches(new Regex($@"ALTER FUNCTION {function}\([^)]*\) OWNER TO kordra_iam_owner;"), sql);
            Assert.Matches(new Regex($@"REVOKE ALL ON FUNCTION {function}\([^)]*\) FROM PUBLIC;"), sql);
        }
        var statements = string.Join("\n", sql.Split('\n').Where(x => !x.TrimStart().StartsWith("REVOKE ", StringComparison.Ordinal)));
        Assert.DoesNotMatch(new Regex(@"(?<!DISTINCT )\b(FROM|JOIN|INSERT INTO|UPDATE)\s+(?!app\.|wfl\.|iam_workflow\.)[a-z_]+\b(?!\s*\()"), statements);
    }

    [Fact]
    public void CurrentGrantMatrixIsExactlyTheApprovedSurface()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["iam_workflow.current_service()"] = ["kordra_worker_runtime", "kordra_api_runtime", "kordra_wfl_owner", "kordra_aud_owner"],
            ["iam_workflow.acquire_fence(uuid)"] = ["kordra_worker_runtime", "kordra_api_runtime"],
            ["iam_workflow.advance_epoch(uuid, text)"] = ["kordra_api_runtime"],
            ["iam_workflow.advance_floor(uuid, timestamp with time zone)"] = ["kordra_worker_runtime", "kordra_api_runtime"],
            ["iam_workflow.create_delegation(uuid, uuid, uuid, uuid, uuid, uuid, bigint, uuid, uuid, uuid, bigint, text, text, integer, text, text[], bytea, bytea, timestamp with time zone)"] = ["kordra_api_runtime"],
            ["iam_workflow.reauthorize_delegation(uuid, uuid, bigint, uuid, uuid, uuid, uuid, bigint, text, bytea, bytea, timestamp with time zone, text)"] = ["kordra_api_runtime"],
            ["iam_workflow.revoke_delegation(uuid, text)"] = ["kordra_api_runtime"],
            ["iam_workflow.read_current_delegation(uuid, uuid)"] = ["kordra_api_runtime"],
            // D3: only the pinned issuance exists; no unpinned variant remains.
            ["iam_workflow.issue_permit(uuid, uuid, uuid, uuid, uuid, uuid, bigint, text, text, text, uuid, bigint, text)"] = ["kordra_api_runtime"],
            ["iam_workflow.consume_permit(uuid, uuid, uuid, bigint, text, uuid, bigint)"] = ["kordra_worker_runtime"],
            ["iam_workflow.require_consumed(uuid, uuid, text)"] = ["kordra_wfl_owner", "kordra_aud_owner"],
            // D7: offline service manifests only through the NOLOGIN role; no runtime login may execute them.
            ["iam_workflow.apply_service_manifest(text, text, uuid, uuid, text, name, text, timestamp with time zone, timestamp with time zone, jsonb, uuid)"] = ["kordra_iam_service_admin"],
            ["wfl.stage_transition(uuid, uuid, uuid, uuid, uuid, uuid, text, bigint, integer)"] = ["kordra_worker_runtime"],
        };
        var all = string.Join("\n", OwnerFunctionPoc.CurrentArtifacts.Select(x => Strip(OwnerFunctionPoc.Sql(x))));
        var actual = Regex.Matches(all, @"GRANT EXECUTE ON FUNCTION (.+?) TO ([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Split(", "));
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (function, grantees) in expected) Assert.Equal(grantees.Order(), actual[function].Order());
        // The service-admin role holds exactly schema usage plus the manifest function, nothing on tables or other functions.
        var adminGrants = Regex.Matches(all, @"GRANT [^;]+ TO ([^;]+);").Where(m => m.Groups[1].Value.Contains("kordra_iam_service_admin"))
            .Select(m => m.Value).ToArray();
        Assert.Equal(2, adminGrants.Length);
        Assert.Contains("GRANT USAGE ON SCHEMA iam_workflow TO kordra_iam_service_admin;", adminGrants);
        foreach (Match grant in Regex.Matches(all, @"GRANT (.+?) ON (TABLE|SEQUENCE|ALL TABLES|DATABASE) (.+?) TO ([^;]+);"))
        {
            Assert.DoesNotContain("kordra_worker_runtime", grant.Groups[4].Value);
            Assert.DoesNotContain("kordra_api_runtime", grant.Groups[4].Value);
            Assert.DoesNotContain("kordra_iam_service_admin", grant.Groups[4].Value);
        }
    }

    [Fact]
    public void PinnedIssuanceIsReadUnderTheFenceForTheCurrentGeneration()
    {
        var sql = OwnerFunctionPoc.Sql(Iam);
        Assert.Contains("spki_sha256 character(64) NOT NULL CHECK (spki_sha256 ~ '^[0-9a-f]{64}$')", sql);
        var issue = Regex.Match(sql, @"CREATE FUNCTION iam_workflow\.issue_permit\(.*?\$fn\$;", RegexOptions.Singleline).Value;
        Assert.NotEmpty(issue);
        var fence = issue.IndexOf("FOR UPDATE", StringComparison.Ordinal);
        var pin = issue.IndexOf("'KD024'", StringComparison.Ordinal);
        Assert.True(fence > 0 && pin > fence, "The pin must be checked after the fence row is locked.");
        Assert.Contains("p.generation = v_target.generation AND p.spki_sha256 = p_spki_sha256", issue);
        Assert.Contains("p.not_before <= v_now AND v_now < p.not_after AND (p.retired_at IS NULL OR p.retired_at > v_now)", issue);
        Assert.Contains("v_now := clock_timestamp();", issue);
        // An exact replay must present the same pin; a different pin is a conflict, never a second permit.
        Assert.Contains("AND v_prior.spki_sha256 = p_spki_sha256 THEN", issue);
        Assert.Contains("p_custody_epoch, p_spki_sha256, v_now,", issue);
        // Everything else of the 12.3b permit and delegation rules is unchanged.
        Assert.Contains("CONSTRAINT ck_permits_ttl CHECK (expires_at > issued_at AND expires_at - issued_at <= interval '2 seconds')", sql);
        Assert.Contains("LEAST(v_now + interval '2 seconds', v_delegation.valid_until)", sql);
        Assert.True(Regex.Matches(sql, @"SET epoch = f\.epoch \+ 1").Count >= 4);
    }

    [Fact]
    public void WorkflowFunctionsNowRequireTheV3Baseline()
    {
        using var manifest = JsonDocument.Parse(OwnerFunctionPoc.Manifest("wfl-owner-functions-v2"));
        var requires = manifest.RootElement.GetProperty("requires").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("iam-workflow-authority-v3.sql", requires);
        Assert.DoesNotContain("iam-workflow-authority-v2.sql", requires);
    }

    private static string Strip(string sql) => string.Join("\n", sql.Split('\n').Where(x => !x.TrimStart().StartsWith("--", StringComparison.Ordinal)));
}
