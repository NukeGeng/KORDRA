using System.Text;
using System.Text.Json;
using Kordra.Testing;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class DisposablePostgreSqlProfileTests
{
    private static readonly Guid Run = Guid.Parse("b97edacb-260a-4510-8b9d-caf21ca69a30");
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 8, 0, 0, TimeSpan.Zero);
    private const string Source = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Manifest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Secret = "synthetic-do-not-echo";

    [Fact]
    public void ExplicitProfileAcceptsOnlyExactDisposableLocatorWithoutOpeningConnection()
    {
        var profile = Read(Body());
        using var connection = new NpgsqlConnection(Locator());
        profile.ValidateLocator(connection.ConnectionString, Now);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.Equal("kordra_test_" + Run.ToString("N"), profile.Database);
        Assert.DoesNotContain(profile.Database, profile.ToString());
        Assert.Contains("not operation authority", profile.ToString());
    }

    [Theory]
    [InlineData("host", "localhost")]
    [InlineData("database", "kordra_test")]
    [InlineData("database", "kordra")]
    [InlineData("database", "kordra_test_11111111111111111111111111111111")]
    [InlineData("role", "postgres")]
    [InlineData("role", "kordra_app")]
    [InlineData("schema", "public")]
    [InlineData("kind", "runtime")]
    [InlineData("sourceSha256", Manifest)]
    [InlineData("operationManifestSha256", Source)]
    public void ProfileSubstitutionsFailClosed(string field, string value)
    {
        var input = Fields(); input[field] = value;
        Assert.Throws<InvalidOperationException>(() => Read(JsonSerializer.SerializeToUtf8Bytes(input)));
    }

    [Theory]
    [InlineData("Host=localhost")]
    [InlineData("Port=5433")]
    [InlineData("Database=kordra_test")]
    [InlineData("Database=kordra")]
    [InlineData("Username=postgres")]
    [InlineData("Username=kordra_app")]
    [InlineData("Pooling=true")]
    [InlineData("Multiplexing=true")]
    [InlineData("Include Error Detail=true")]
    [InlineData("Search Path=app")]
    [InlineData("Options=-c search_path=app")]
    [InlineData("Passfile=/tmp/synthetic-do-not-echo")]
    [InlineData("Root Certificate=/tmp/synthetic-do-not-echo")]
    [InlineData("Persist Security Info=true")]
    public void LocatorCannotFallBackToExistingDataOrSessionOverrides(string change)
    {
        var profile = Read(Body()); var parsed = new NpgsqlConnectionStringBuilder(Locator());
        var split = change.Split('=', 2); parsed[split[0]] = split[1];
        var error = Assert.Throws<InvalidOperationException>(() => profile.ValidateLocator(parsed.ConnectionString, Now));
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.DoesNotContain(parsed.ConnectionString, error.ToString());
    }

    [Fact]
    public void ExpiryAndCallerExpectedSourceRunOrManifestAreMandatory()
    {
        var body = Body();
        Assert.Throws<InvalidOperationException>(() => DisposablePostgreSqlProfile.Read(body, Guid.NewGuid(), Source, Manifest, Now));
        Assert.Throws<InvalidOperationException>(() => DisposablePostgreSqlProfile.Read(body, Run, Manifest, Manifest, Now));
        Assert.Throws<InvalidOperationException>(() => DisposablePostgreSqlProfile.Read(body, Run, Source, Source, Now));
        Assert.Throws<InvalidOperationException>(() => Read(body, Now.AddHours(1)));
        var profile = Read(body);
        Assert.Throws<InvalidOperationException>(() => profile.ValidateLocator(Locator(), Now.AddHours(1)));
        Assert.Throws<InvalidOperationException>(() => profile.ValidateLocator(Locator(), Now.ToOffset(TimeSpan.FromHours(7))));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("oversize")]
    [InlineData("malformed")]
    public void UnsupportedOrSecretShapedProfileInputRejectsWithoutEcho(string mode)
    {
        var body = Encoding.UTF8.GetString(Body());
        body = mode switch
        {
            "duplicate" => "{\"host\":\"127.0.0.1\"," + body[1..],
            "unknown" => "{\"password\":\"" + Secret + "\"," + body[1..],
            "missing" => "{}",
            "oversize" => new string('x', 16385) + Secret,
            "malformed" => "{\"password\":\"" + Secret,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var error = Assert.Throws<InvalidOperationException>(() => Read(Encoding.UTF8.GetBytes(body)));
        Assert.DoesNotContain(Secret, error.ToString());
    }

    [Fact]
    public void RunnerProfileRequiresAllExplicitInputsAndNeverFallsBackWhenPartiallyConfigured()
    {
        var reads = 0;
        byte[] ReadFile(string _) { reads++; return Body(); }
        Assert.Null(RegressionPostgreSqlSelection.Read(null, null, null, null, ReadFile, Now));
        Assert.Equal(0, reads);
        Assert.Throws<InvalidOperationException>(() => RegressionPostgreSqlSelection.Read(null, Run.ToString("D"), Source, Manifest, ReadFile, Now));
        Assert.Throws<InvalidOperationException>(() => RegressionPostgreSqlSelection.Read("relative.json", Run.ToString("D"), Source, Manifest, ReadFile, Now));
        Assert.Equal(0, reads);
        var selected = RegressionPostgreSqlSelection.Read("/synthetic/profile.json", Run.ToString("D"), Source, Manifest, ReadFile, Now);
        Assert.NotNull(selected); Assert.Equal(Run, selected.RunId); Assert.Equal(1, reads);
        Assert.Throws<InvalidOperationException>(() => RegressionPostgreSqlSelection.Read("/synthetic/profile.json", Run.ToString("D"), Manifest, Manifest, ReadFile, Now));
    }

    [Fact]
    public void RunnerProfileErrorsOmitFileAndInputValues()
    {
        var error = Assert.Throws<InvalidOperationException>(() => RegressionPostgreSqlSelection.Read(
            "/synthetic/" + Secret, Run.ToString("D"), Source, Manifest,
            _ => throw new IOException(Secret), Now));
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.Contains("no legacy fallback", error.Message);
    }

    private static DisposablePostgreSqlProfile Read(byte[] body, DateTimeOffset? now = null) =>
        DisposablePostgreSqlProfile.Read(body, Run, Source, Manifest, now ?? Now);
    private static byte[] Body() => JsonSerializer.SerializeToUtf8Bytes(Fields());
    private static Dictionary<string, object> Fields() => new()
    {
        ["schemaVersion"] = 1, ["kind"] = "disposable-postgresql-regression", ["runId"] = Run.ToString("D"),
        ["host"] = "127.0.0.1", ["port"] = 5432, ["database"] = "kordra_test_" + Run.ToString("N"),
        ["role"] = "kordra_test", ["schema"] = "app", ["sourceSha256"] = Source,
        ["operationManifestSha256"] = Manifest, ["expiresAtUtc"] = Now.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'")
    };
    private static string Locator() => $"Host=127.0.0.1;Port=5432;Database=kordra_test_{Run:N};Username=kordra_test;Password={Secret};Pooling=false;Multiplexing=false;Include Error Detail=false";
}
