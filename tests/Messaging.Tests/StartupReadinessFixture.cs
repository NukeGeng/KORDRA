using System.Security.Cryptography;
using System.Text.Json;
using Kordra.Modules.Messaging.Infrastructure.Diagnostics;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.RDBMS;
using Xunit;

namespace Kordra.Messaging.Tests;

/// <summary>Explicit operational fixture entry point, never an automatic service or production migrator.</summary>
public static class StartupReadinessFixture
{
    public static void ValidateTargets(string ownerLocator, string runtimeLocator, Guid caseId, int port)
    {
        if (caseId == Guid.Empty || port is <= 1024 or > 65535 || port == 5432)
            throw new InvalidOperationException("Fresh isolated startup target required; shared Product port excluded.");
        var owner = new NpgsqlConnectionStringBuilder(ownerLocator); var runtime = new NpgsqlConnectionStringBuilder(runtimeLocator);
        foreach (var target in new[] { owner, runtime })
            if (target.Host != "127.0.0.1" || target.Port != port || target.Database != "kordra_test" || target.Multiplexing ||
                !string.IsNullOrEmpty(target.SearchPath) || !string.IsNullOrEmpty(target.Options))
                throw new InvalidOperationException("Startup fixture rejects nonlocal/shared/overridden target.");
        if (owner.Username != "kordra_startup_" + caseId.ToString("N") || runtime.Username != "kordra_msg_runtime")
            throw new InvalidOperationException("Exact new-instance owner/runtime identities required.");
    }

    public static async Task<int> RunAsync(string manifestPath)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var plan = document.RootElement;
        if (plan.GetProperty("status").GetString() != "approved_for_execution" || plan.GetProperty("purpose").GetString() != "T12.1f-startup")
            throw new InvalidOperationException("Exact isolated startup operation approval required.");
        var caseId = plan.GetProperty("caseId").GetGuid(); var port = plan.GetProperty("port").GetInt32();
        var ownerLocator = Environment.GetEnvironmentVariable("KORDRA_MSG_STARTUP_OWNER_LOCATOR") ?? throw new InvalidOperationException("Private new-instance owner locator required.");
        var runtimeLocator = Environment.GetEnvironmentVariable("KORDRA_MSG_STARTUP_RUNTIME_LOCATOR") ?? throw new InvalidOperationException("Private new-instance runtime locator required.");
        ValidateTargets(ownerLocator, runtimeLocator, caseId, port);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kordra.slnx"))) root = root.Parent;
        var expectedDirectory = Path.Combine(root?.FullName ?? throw new InvalidOperationException("Product root required."),
            ".amr-local", "t12-1-design", "startup-" + caseId.ToString("N"), "pgdata");
        Assert.Equal(expectedDirectory, plan.GetProperty("dataDirectory").GetString());
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var ct = budget.Token;
        await using var owner = new NpgsqlConnection(ownerLocator); await owner.OpenAsync(ct);
        await using var runtime = new NpgsqlConnection(runtimeLocator); await runtime.OpenAsync(ct);
        using (var target = new NpgsqlCommand("SHOW data_directory", owner))
            Assert.Equal(expectedDirectory, (string)(await target.ExecuteScalarAsync(ct))!);
        var bundle = Path.Combine(root!.FullName, ".amr-local", "t12-1-design", "schema-review", "current-profile");
        foreach (var entry in plan.GetProperty("artifactSha256").EnumerateObject())
        {
            if (entry.Name is not ("creation.sql" or "owned-acl.sql" or "native-acl-review.sql" or "manifest.json"))
                throw new InvalidOperationException("Unexpected reviewed artifact.");
            Assert.Equal(entry.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(bundle, entry.Name), ct))));
        }
        var hash = plan.GetProperty("reviewManifestSha256").GetString()!;
        Assert.True(await MessagingSchemaReadiness.MatchesAsync(runtime, hash, ct));
        using (var roles = new NpgsqlCommand("SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls OR rolinherit FROM pg_roles WHERE rolname=current_user", runtime))
            Assert.False((bool)(await roles.ExecuteScalarAsync(ct))!);
        foreach (var forbidden in new[]
        {
            "CREATE SCHEMA forbidden_startup", "CREATE TABLE msg.forbidden_startup(id integer)",
            "ALTER TABLE msg.logical_receipts ADD COLUMN forbidden integer", "DELETE FROM msg.logical_receipts WHERE false",
            "TRUNCATE msg.logical_receipts", "DELETE FROM msg.schema_manifest WHERE false",
            "SELECT * FROM startup_foreign.probe LIMIT 0"
        })
        {
            using var command = new NpgsqlCommand(forbidden, runtime) { CommandTimeout = 5 };
            var denial = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(ct));
            Assert.Equal("42501", denial.SqlState);
        }
        using (var host = Host.CreateDefaultBuilder().ConfigureLogging(logging => logging.ClearProviders()
            .AddProvider(new MessageDiagnosticLoggingProvider(new FixtureDiagnosticConsole())))
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery();
                MessagingSchemaProfile.Configure(options, runtimeLocator);
                options.Durability.FirstNodeReassignmentExecution = TimeSpan.FromMinutes(5);
                options.Durability.ScheduledJobFirstExecution = TimeSpan.FromMinutes(5);
            }).Build())
        {
            try
            {
                await host.StartAsync(ct); // Exact current profile; role cannot provision or alter native tables.
                var store = Assert.IsAssignableFrom<IMessageDatabase>(host.Services.GetRequiredService<IMessageStore>());
                var id = Guid.NewGuid();
                await using var transaction = await runtime.BeginTransactionAsync(ct);
                await store.StoreIncomingAsync(transaction, [Seed("fixture-a"), Seed("fixture-b")]);
                using var rows = new NpgsqlCommand("SELECT count(*) FROM msg.wolverine_incoming_envelopes WHERE id=@id", runtime, transaction);
                rows.Parameters.AddWithValue("id", id); Assert.Equal(2L, await rows.ExecuteScalarAsync(ct));
                await transaction.RollbackAsync(ct);
                Envelope Seed(string destination) => new()
                {
                    Id = id, Destination = new Uri("local://" + destination), Status = EnvelopeStatus.Incoming,
                    Data = "{}"u8.ToArray(), MessageType = "fixture.startup", ContentType = "application/json"
                };
            }
            finally { await host.StopAsync(ct); }
        }
        // Owner mutates only synthetic fixture manifest state; production reader is unchanged.
        await Sql("UPDATE msg.schema_manifest SET manifest_sha256='" + new string('b', 64) + "'");
        Assert.False(await MessagingSchemaReadiness.MatchesAsync(runtime, hash, ct));
        // Simulate an incompatible future manifest only in the entirely disposable instance.
        // The actual production revision=1 constraint is restored before subsequent probes.
        await Sql("ALTER TABLE msg.schema_manifest DROP CONSTRAINT schema_manifest_revision_check");
        await Sql("UPDATE msg.schema_manifest SET manifest_sha256='" + hash + "',revision=2");
        Assert.False(await MessagingSchemaReadiness.MatchesAsync(runtime, hash, ct));
        await Sql("DELETE FROM msg.schema_manifest");
        Assert.False(await MessagingSchemaReadiness.MatchesAsync(runtime, hash, ct));
        await Sql("INSERT INTO msg.schema_manifest(revision,manifest_sha256) VALUES(1,'" + hash + "')");
        await Sql("ALTER TABLE msg.schema_manifest ADD CONSTRAINT schema_manifest_revision_check CHECK(revision=1)");
        Assert.True(await MessagingSchemaReadiness.MatchesAsync(runtime, hash, ct));
        await Sql("ALTER TABLE msg.schema_manifest RENAME TO fixture_hidden_manifest");
        try { Assert.False(await MessagingSchemaReadiness.MatchesAsync(runtime, hash, ct)); }
        finally { await Sql("ALTER TABLE msg.fixture_hidden_manifest RENAME TO schema_manifest"); }
        await runtime.CloseAsync(); Assert.False(await MessagingSchemaReadiness.MatchesAsync(runtime, hash, ct));
        Console.WriteLine("STARTUP_FIXTURE_PASSED"); return 0;
        async Task Sql(string sql) { using var command = new NpgsqlCommand(sql, owner) { CommandTimeout = 5 }; await command.ExecuteNonQueryAsync(ct); }
    }
}
