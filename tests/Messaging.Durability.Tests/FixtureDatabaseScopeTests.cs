using System.Text.Json;
using Kordra.Testing;
using Xunit;
using Wolverine;
using Wolverine.Postgresql;

namespace Kordra.Messaging.Durability.Tests;

public sealed class FixtureDatabaseScopeTests
{
    private const string Locator = "Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_test;Password=synthetic-unused";
    [Fact]
    public void NativeQueueSchemaIsExplicitlyBoundToTheSameCaseAsPersistence()
    {
        var schema = "t12_" + Guid.NewGuid().ToString("N"); var options = new WolverineOptions();
        Program.ConfigureStorage(options, Locator, schema);
        options.PublishMessage<FixtureMessage>().ToPostgresqlQueue("t12_fixture");
        Assert.Equal(schema, options.Transports.GetOrCreate<Wolverine.Postgresql.Transport.PostgresqlTransport>().TransportSchemaName);
    }
    [Fact]
    public void RestrictedIdentityMustMatchItsOwnCaseAndCannotUseOwnerOrAnotherCase()
    {
        var id = Guid.NewGuid();
        var locator = Locator.Replace("Username=kordra_test", "Username=kordra_t12_" + id.ToString("N"));
        var fixture = FixtureDatabaseScope.RestrictedRuntime(locator, id);
        using var connection = fixture.CreateConnection(); Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.Throws<InvalidOperationException>(() => FixtureDatabaseScope.RestrictedRuntime(locator, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => FixtureDatabaseScope.RestrictedRuntime(Locator, id));
        Assert.Throws<InvalidOperationException>(() => new FixtureDatabaseScope(locator, id));
        Assert.Throws<InvalidOperationException>(() => FixtureDatabaseScope.RestrictedRuntime(locator + ";Search Path=app", id));
    }
    [Fact]
    public void FixtureIdentityNamesOnlyItsOwnFreshSchemaWithoutOpeningConnection()
    {
        var id = Guid.NewGuid(); var fixture = new FixtureDatabaseScope(Locator, id);
        Assert.Equal("t12_" + id.ToString("N"), fixture.Schema);
        Assert.Equal("FixtureDatabaseScope [private]", fixture.ToString());
        using var connection = fixture.CreateConnection();
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.Equal("kordra_test", connection.Database);
        Assert.NotEqual(fixture.Schema, new FixtureDatabaseScope(Locator, Guid.NewGuid()).Schema);
        Assert.Throws<InvalidOperationException>(() => new FixtureDatabaseScope(Locator, Guid.Empty));
    }
    [Theory]
    [InlineData("Host=localhost;Database=kordra_test;Username=kordra_test")]
    [InlineData("Host=127.0.0.1;Port=5433;Database=kordra_test;Username=kordra_test")]
    [InlineData("Host=127.0.0.1;Database=kordra;Username=kordra_app")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=postgres")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Search Path=app")]
    [InlineData("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Options=-c search_path=app")]
    [InlineData("not a connection locator")]
    public void OtherTargetsAndSessionOverridesAreRejectedWithoutEchoingLocator(string locator)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new FixtureDatabaseScope(locator, Guid.NewGuid()));
        Assert.DoesNotContain(locator, error.Message); Assert.Null(error.InnerException);
    }
    [Fact]
    public void DisposableOwnerAndRestrictedRuntimeRemainDistinctAndRequireExactRoleCase()
    {
        var run = Guid.NewGuid(); var caseId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var source = new string('a', 64); var manifest = new string('b', 64);
        var database = "kordra_test_" + run.ToString("N");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new {
            schemaVersion = 1, kind = "disposable-postgresql-regression", runId = run.ToString("D"), host = "127.0.0.1", port = 5432,
            database, role = "kordra_test", schema = "app", sourceSha256 = source, operationManifestSha256 = manifest,
            expiresAtUtc = now.AddMinutes(10).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'") });
        var profile = DisposablePostgreSqlProfile.Read(bytes, run, source, manifest, now);
        var owner = $"Host=127.0.0.1;Database={database};Username=kordra_test;Pooling=false";
        var runtime = owner.Replace("Username=kordra_test", "Username=kordra_t12_" + caseId.ToString("N"));
        using var ownerConnection = FixtureDatabaseScope.ForProfile(owner, caseId, false, profile).CreateConnection();
        using var runtimeConnection = FixtureDatabaseScope.ForProfile(runtime, caseId, true, profile).CreateConnection();
        Assert.Equal(database, ownerConnection.Database); Assert.Equal(database, runtimeConnection.Database);
        Assert.Equal(System.Data.ConnectionState.Closed, ownerConnection.State); Assert.Equal(System.Data.ConnectionState.Closed, runtimeConnection.State);
        Assert.Throws<ArgumentNullException>(() => FixtureDatabaseScope.ForProfile(owner, caseId, false, null!));
        Assert.Throws<InvalidOperationException>(() => new FixtureDatabaseScope(owner, caseId));
        Assert.Throws<InvalidOperationException>(() => FixtureDatabaseScope.RestrictedRuntime(runtime, caseId));
        Assert.Throws<InvalidOperationException>(() => FixtureDatabaseScope.ForProfile(runtime, caseId, false, profile));
        Assert.Throws<InvalidOperationException>(() => FixtureDatabaseScope.ForProfile(owner, caseId, true, profile));
        Assert.Throws<InvalidOperationException>(() => FixtureDatabaseScope.ForProfile(runtime, Guid.NewGuid(), true, profile));
        Assert.Throws<InvalidOperationException>(() => FixtureDatabaseScope.ForProfile(runtime.Replace(database, "kordra_test"), caseId, true, profile));
        Assert.Throws<InvalidOperationException>(() => FixtureDatabaseScope.ForProfile(runtime + ";Options=-c search_path=app", caseId, true, profile));
        Assert.Throws<InvalidOperationException>(() => profile.ValidateRestrictedFixtureLocator(runtime, caseId, now.AddHours(1)));
    }
}
