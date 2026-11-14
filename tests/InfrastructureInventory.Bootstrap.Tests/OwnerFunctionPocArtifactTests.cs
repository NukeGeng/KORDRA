using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>T12.3a owner function/ACL PoC — offline artifact, grant-matrix, parity and adapter-guard checks (no connection).</summary>
public sealed class OwnerFunctionPocArtifactTests
{
    [Fact]
    public void ArtifactsMatchTheirManifestsAndAreNonDestructive()
    {
        foreach (var name in OwnerFunctionPoc.Artifacts)
        {
            using var manifest = JsonDocument.Parse(OwnerFunctionPoc.Manifest(name));
            var root = manifest.RootElement;
            Assert.Equal(1, root.GetProperty("revision").GetInt32());
            Assert.Equal(name + ".sql", root.GetProperty("artifact").GetString());
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(OwnerFunctionPoc.SqlBytes(name))), root.GetProperty("sha256").GetString());
            Assert.False(root.GetProperty("destructiveOperations").GetBoolean());
            Assert.Equal(OwnerFunctionPoc.Roles, root.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
            var sql = Strip(OwnerFunctionPoc.Sql(name));
            foreach (var forbidden in new[] { "DROP ", "TRUNCATE", "DELETE FROM", "current_user", "SET ROLE", "SECURITY INVOKER",
                         "set_config", "SESSION AUTHORIZATION", "CREATE ROLE", "ALTER ROLE", "SUPERUSER", "BYPASSRLS", "format(" })
                Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
            // No dynamic SQL: the only EXECUTE keyword is GRANT EXECUTE ON FUNCTION.
            Assert.DoesNotMatch(new Regex(@"\bEXECUTE\b(?!\s+ON\s+FUNCTION)", RegexOptions.IgnoreCase), sql);
            Assert.DoesNotContain("CREATE OR REPLACE", sql, StringComparison.OrdinalIgnoreCase);
            // Temporary CREATE for an ownership transfer is always revoked in the same artifact.
            foreach (Match grant in Regex.Matches(sql, @"GRANT CREATE ON SCHEMA (\w+) TO (\w+);"))
                Assert.Contains($"REVOKE CREATE ON SCHEMA {grant.Groups[1].Value} FROM {grant.Groups[2].Value};", sql);
            var declared = root.GetProperty("functions").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.Equal(declared.Length, Regex.Matches(sql, @"CREATE FUNCTION ").Count);
        }
    }

    [Fact]
    public void EveryFunctionIsOwnerDefinerWithFixedSearchPathAndNoPublicExecute()
    {
        foreach (var name in OwnerFunctionPoc.Artifacts)
        {
            var sql = Strip(OwnerFunctionPoc.Sql(name));
            var owner = name switch { "iam-workflow-authority-v1" => "kordra_iam_owner", "wfl-owner-functions-v1" => "kordra_wfl_owner", _ => "kordra_aud_owner" };
            var bodies = Regex.Matches(sql, @"CREATE FUNCTION ([a-z_]+\.[a-z_]+)\((.*?)\$fn\$", RegexOptions.Singleline);
            Assert.NotEmpty(bodies);
            foreach (Match body in bodies)
            {
                var header = body.Value;
                Assert.Contains("SECURITY DEFINER", header);
                Assert.Contains("SET search_path = pg_catalog, pg_temp", header);
                var function = body.Groups[1].Value;
                Assert.Matches(new Regex($@"ALTER FUNCTION {Regex.Escape(function)}\([^)]*\) OWNER TO {owner};"), sql);
                Assert.Matches(new Regex($@"REVOKE ALL ON FUNCTION {Regex.Escape(function)}\([^)]*\) FROM PUBLIC;"), sql);
            }
            // Every unqualified table/function reference would be a search_path hijack risk; all owner objects are qualified.
            var statements = string.Join("\n", sql.Split('\n').Where(x => !x.TrimStart().StartsWith("REVOKE ", StringComparison.Ordinal)));
            Assert.DoesNotMatch(new Regex(@"(?<!DISTINCT )\b(FROM|JOIN|INSERT INTO|UPDATE)\s+(?!app\.|wfl\.|iam_workflow\.)[a-z_]+\b(?!\s*\()"), statements);
        }
    }

    [Fact]
    public void GrantMatrixIsExactlyTheApprovedSurface()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["iam_workflow.current_service()"] = ["kordra_worker_runtime", "kordra_api_runtime", "kordra_wfl_owner", "kordra_aud_owner"],
            ["iam_workflow.acquire_fence(uuid)"] = ["kordra_worker_runtime", "kordra_api_runtime"],
            ["iam_workflow.advance_epoch(uuid, text)"] = ["kordra_api_runtime"],
            ["iam_workflow.consume_permit(uuid, uuid, uuid, bigint, text, uuid, bigint)"] = ["kordra_worker_runtime"],
            ["iam_workflow.require_consumed(uuid, uuid, text)"] = ["kordra_wfl_owner", "kordra_aud_owner"],
            ["iam_workflow.advance_floor(uuid, timestamp with time zone)"] = ["kordra_worker_runtime", "kordra_api_runtime"],
            ["wfl.stage_transition(uuid, uuid, uuid, uuid, uuid, uuid, text, bigint, integer)"] = ["kordra_worker_runtime"],
            ["app.aud_append_bound_intent(uuid, uuid, text, text, bytea)"] = ["kordra_worker_runtime"],
        };
        var all = string.Join("\n", OwnerFunctionPoc.Artifacts.Select(x => Strip(OwnerFunctionPoc.Sql(x))));
        var actual = Regex.Matches(all, @"GRANT EXECUTE ON FUNCTION (.+?) TO ([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Split(", "));
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (function, grantees) in expected) Assert.Equal(grantees.Order(), actual[function].Order());

        // Login roles receive only schema USAGE and function EXECUTE: never a table, sequence or database object privilege.
        foreach (Match grant in Regex.Matches(all, @"GRANT (.+?) ON (TABLE|SEQUENCE|ALL TABLES|DATABASE) (.+?) TO ([^;]+);"))
        {
            Assert.DoesNotContain("kordra_worker_runtime", grant.Groups[4].Value);
            Assert.DoesNotContain("kordra_api_runtime", grant.Groups[4].Value);
        }
        // AUD owner may read and append only; WFL owner may update only version/state.
        Assert.Contains("GRANT SELECT, INSERT ON TABLE app.aud_intents TO kordra_aud_owner;", all);
        Assert.DoesNotMatch(new Regex(@"GRANT [^;]*(UPDATE|DELETE)[^;]* ON TABLE app\.aud_intents"), all);
        Assert.Contains("GRANT UPDATE (version, state) ON TABLE wfl.instances TO kordra_wfl_owner;", all);
        Assert.DoesNotMatch(new Regex(@"GRANT [^;]*DELETE"), all);
    }

    [Fact]
    public void StateEdgesMatchWorkflowStateRulesAndOnlyAdmitIsWorkerOwned()
    {
        var sql = OwnerFunctionPoc.Sql("wfl-owner-functions-v1");
        var values = sql[sql.IndexOf("INSERT INTO wfl.state_edges", StringComparison.Ordinal)..];
        values = values[..values.IndexOf(';')];
        var rows = Regex.Matches(values, @"\((\d+), (\d+), (NULL|'admit')\)")
            .Select(m => (From: int.Parse(m.Groups[1].Value), To: int.Parse(m.Groups[2].Value), Kind: m.Groups[3].Value)).ToArray();
        var expected = Enum.GetValues<WorkflowState>().SelectMany(from => Enum.GetValues<WorkflowState>()
            .Where(to => WorkflowStateRules.CanChangeState(from, to)).Select(to => ((int)from, (int)to))).Order().ToArray();
        Assert.Equal(expected, rows.Select(x => (x.From, x.To)).Order().ToArray());
        Assert.Equal([(1, 2)], rows.Where(x => x.Kind == "'admit'").Select(x => (x.From, x.To)));
    }

    [Fact]
    public void AuditFunctionKeySetAndTimestampShapeMatchTheCanonicalizer()
    {
        var at = new DateTimeOffset(2026, 11, 14, 3, 4, 5, TimeSpan.Zero).AddTicks(1234567);
        var intent = new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "workflow.start", "environment", Guid.NewGuid(), 3, 4, "worker.admit", at, Guid.NewGuid());
        var (json, hash) = AuditBoundAppendFunctionPort.CanonicalFor(intent);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(json)), hash);
        Assert.Equal(AuditIntentCanonicalizer.ComputeHash(intent), Convert.ToHexStringLower(hash));
        using var document = JsonDocument.Parse(json);
        var keys = document.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
        var sql = OwnerFunctionPoc.Sql("aud-bound-append-v1");
        var literal = Regex.Match(sql, @"<> ARRAY\[(.*?)\]::text\[\]", RegexOptions.Singleline).Groups[1].Value;
        var declared = Regex.Matches(literal, "'([A-Za-z]+)'").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(keys.Order(StringComparer.Ordinal), declared);
        Assert.Contains($"<> {keys.Length}", sql);
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$"), document.RootElement.GetProperty("occurredAtUtc").GetString()!);
    }

    [Fact]
    public void RunSuffixOnlyRenamesTheFivePlaceholderRoles()
    {
        foreach (var name in OwnerFunctionPoc.Artifacts)
        {
            var sql = OwnerFunctionPoc.Sql(name);
            var run = OwnerFunctionPoc.ForRun(sql, "0123456789ab");
            Assert.Equal(sql, Regex.Replace(run, "(kordra_[a-z_]+?)_r0123456789ab", "$1"));
            Assert.DoesNotContain("kordra_msg_runtime_r", run);
            Assert.All(OwnerFunctionPoc.Roles, role => Assert.DoesNotMatch(new Regex($@"\b{role}\b(?!_r0123456789ab)"), run));
        }
        Assert.Throws<ArgumentException>(() => OwnerFunctionPoc.ForRun("x", "Robert'); --"));
    }

    [Fact]
    public void PermitSeedSamplesTheClockOnceAndStaysWithinTheTtlCheck()
    {
        // PR33-POC-CLOCK-001: two clock_timestamp() samples could make expires_at - issued_at exceed ck_permits_ttl.
        var check = Regex.Match(OwnerFunctionPoc.Sql("iam-workflow-authority-v1"), @"expires_at - issued_at <= interval '(\d+) seconds'");
        Assert.True(check.Success);
        var limit = TimeSpan.FromSeconds(int.Parse(check.Groups[1].Value));
        foreach (var expired in new[] { false, true })
        {
            var sql = OwnerFunctionPoc.PermitSeedSql(expired);
            Assert.Single(Regex.Matches(sql, @"clock_timestamp\(\)"));
            var offsets = Regex.Matches(sql, @"sample\.at(?: ([+-]) interval '(\d+) seconds')?")
                .Select(m => m.Groups[1].Success ? (m.Groups[1].Value == "-" ? -1 : 1) * int.Parse(m.Groups[2].Value) : 0).ToArray();
            Assert.Equal(2, offsets.Length);
            Assert.InRange(TimeSpan.FromSeconds(offsets[1] - offsets[0]), TimeSpan.FromTicks(1), limit);
        }
    }

    [Fact]
    public void AdaptersRequireTheCallerRootAndNeverOpenAConnection()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Timeout=1");
        Assert.Throws<ArgumentNullException>(() => new WorkflowAuthorityFunctionPort(connection, null!));
        Assert.Throws<ArgumentNullException>(() => new WorkflowTransitionFunctionPort(connection, null!));
        Assert.Throws<ArgumentNullException>(() => new AuditBoundAppendFunctionPort(connection, null!));
        Assert.Throws<ArgumentNullException>(() => new WorkflowAuthorityFunctionPort(null!, null!));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void PocAdaptersAreNotComposedIntoTheRuntimeHost()
    {
        var host = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Host"));
        Assert.True(Directory.Exists(host));
        foreach (var file in Directory.EnumerateFiles(host, "*.cs", SearchOption.AllDirectories)
                     .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                 !x.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(nameof(WorkflowAuthorityFunctionPort), text);
            Assert.DoesNotContain(nameof(WorkflowTransitionFunctionPort), text);
            Assert.DoesNotContain(nameof(AuditBoundAppendFunctionPort), text);
        }
    }

    private static string Strip(string sql) => string.Join("\n", sql.Split('\n').Where(x => !x.TrimStart().StartsWith("--", StringComparison.Ordinal)));
}
