using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>12.3b IAM/WFL owner function v2 artifacts: manifest, lint, definer/search_path and the exact approved grant matrix.</summary>
public sealed class OwnerFunctionV2ArtifactTests
{
    [Fact]
    public void V2ArtifactsMatchManifestsReplaceV1AndAreNonDestructive()
    {
        foreach (var name in OwnerFunctionPoc.V2Artifacts)
        {
            using var manifest = JsonDocument.Parse(OwnerFunctionPoc.Manifest(name));
            var root = manifest.RootElement;
            Assert.Equal(2, root.GetProperty("revision").GetInt32());
            Assert.Equal(name + ".sql", root.GetProperty("artifact").GetString());
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(OwnerFunctionPoc.SqlBytes(name))), root.GetProperty("sha256").GetString());
            Assert.Contains("never deployed", root.GetProperty("replaces").GetString());
            Assert.False(root.GetProperty("destructiveOperations").GetBoolean());
            var sql = Strip(OwnerFunctionPoc.Sql(name));
            foreach (var forbidden in new[] { "DROP ", "TRUNCATE", "DELETE FROM", "current_user", "SET ROLE", "SECURITY INVOKER",
                         "set_config", "SESSION AUTHORIZATION", "CREATE ROLE", "ALTER ROLE", "SUPERUSER", "BYPASSRLS", "format(",
                         "CREATE OR REPLACE" })
                Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(new Regex(@"\bEXECUTE\b(?!\s+ON\s+FUNCTION)", RegexOptions.IgnoreCase), sql);
            var declared = root.GetProperty("functions").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.Equal(declared.Length, Regex.Matches(sql, "CREATE FUNCTION ").Count);
            var owner = name.StartsWith("iam", StringComparison.Ordinal) ? "kordra_iam_owner" : "kordra_wfl_owner";
            foreach (Match body in Regex.Matches(sql, @"CREATE FUNCTION ([a-z_]+\.[a-z_]+)\((.*?)\$fn\$", RegexOptions.Singleline))
            {
                Assert.Contains("SECURITY DEFINER", body.Value);
                Assert.Contains("SET search_path = pg_catalog, pg_temp", body.Value);
                var function = Regex.Escape(body.Groups[1].Value);
                Assert.Matches(new Regex($@"ALTER FUNCTION {function}\([^)]*\) OWNER TO {owner};"), sql);
                Assert.Matches(new Regex($@"REVOKE ALL ON FUNCTION {function}\([^)]*\) FROM PUBLIC;"), sql);
            }
            var statements = string.Join("\n", sql.Split('\n').Where(x => !x.TrimStart().StartsWith("REVOKE ", StringComparison.Ordinal)));
            Assert.DoesNotMatch(new Regex(@"(?<!DISTINCT )\b(FROM|JOIN|INSERT INTO|UPDATE)\s+(?!app\.|wfl\.|iam_workflow\.)[a-z_]+\b(?!\s*\()"), statements);
            foreach (Match grant in Regex.Matches(sql, @"GRANT CREATE ON SCHEMA (\w+) TO (\w+);"))
                Assert.Contains($"REVOKE CREATE ON SCHEMA {grant.Groups[1].Value} FROM {grant.Groups[2].Value};", sql);
        }
    }

    [Fact]
    public void V2GrantMatrixIsExactlyTheApprovedSurface()
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
            ["iam_workflow.issue_permit(uuid, uuid, uuid, uuid, uuid, uuid, bigint, text, text, text, uuid, bigint)"] = ["kordra_api_runtime"],
            ["iam_workflow.consume_permit(uuid, uuid, uuid, bigint, text, uuid, bigint)"] = ["kordra_worker_runtime"],
            ["iam_workflow.require_consumed(uuid, uuid, text)"] = ["kordra_wfl_owner", "kordra_aud_owner"],
            ["wfl.stage_transition(uuid, uuid, uuid, uuid, uuid, uuid, text, bigint, integer)"] = ["kordra_worker_runtime"],
        };
        var all = string.Join("\n", OwnerFunctionPoc.V2Artifacts.Select(x => Strip(OwnerFunctionPoc.Sql(x))));
        var actual = Regex.Matches(all, @"GRANT EXECUTE ON FUNCTION (.+?) TO ([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Split(", "));
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (function, grantees) in expected) Assert.Equal(grantees.Order(), actual[function].Order());
        // apply_service_manifest is owner-only (offline manifest); no runtime login may execute it.
        Assert.DoesNotContain("GRANT EXECUTE ON FUNCTION iam_workflow.apply_service_manifest", all);
        foreach (Match grant in Regex.Matches(all, @"GRANT (.+?) ON (TABLE|SEQUENCE|ALL TABLES|DATABASE) (.+?) TO ([^;]+);"))
        {
            Assert.DoesNotContain("kordra_worker_runtime", grant.Groups[4].Value);
            Assert.DoesNotContain("kordra_api_runtime", grant.Groups[4].Value);
        }
        Assert.Contains("wfl.authority_bindings TO kordra_wfl_owner;", all);
        Assert.Contains("'KD109'", all);
    }

    [Fact]
    public void V2PermitAndDelegationRulesAreEncodedInTheSchema()
    {
        var sql = OwnerFunctionPoc.Sql("iam-workflow-authority-v2");
        Assert.Contains("CONSTRAINT ck_permits_ttl CHECK (expires_at > issued_at AND expires_at - issued_at <= interval '2 seconds')", sql);
        Assert.Contains("CREATE UNIQUE INDEX ux_permits_consumed_operation", sql);
        Assert.Contains("WHERE consumed_xact IS NOT NULL", sql);
        Assert.Contains("CREATE UNIQUE INDEX ux_delegations_current", sql);
        Assert.Contains("LEAST(v_now + interval '2 seconds', v_delegation.valid_until)", sql);
        Assert.Contains("interval '10 minutes'", sql);
        // Rotation, retirement, revocation, reauthorization and revocation of a delegation all advance the epoch.
        Assert.True(Regex.Matches(sql, @"SET epoch = f\.epoch \+ 1").Count >= 4);
    }

    private static string Strip(string sql) => string.Join("\n", sql.Split('\n').Where(x => !x.TrimStart().StartsWith("--", StringComparison.Ordinal)));
}
