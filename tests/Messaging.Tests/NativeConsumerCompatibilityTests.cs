using System.Diagnostics;
using System.Text.Json;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;
using Kordra.Modules.Messaging.Infrastructure.Persistence;

namespace Kordra.Messaging.Tests;

public sealed class NativeConsumerCompatibilityTests
{
    [Theory]
    [InlineData("consumer", 4)]
    [InlineData("before-commit", 5)]
    [InlineData("after-commit", 6)]
    [InlineData("commit-reply-loss", 6)]
    public async Task NativeConsumerEnlistsPublicAdapterAndRecoversWithoutDuplicateEffect(string mode, int slot)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("KORDRA_MSG_COMPAT_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Explicit approved fixture manifest required.")));
        Assert.Equal("approved_for_execution", manifest.RootElement.GetProperty("status").GetString());
        var schema = manifest.RootElement.GetProperty("schemas")[slot].GetString()!;
        Assert.Matches("^t12_[0-9a-f]{32}$", schema); var caseId = Guid.ParseExact(schema[4..], "N");
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Private fixture locator required.");
        await using var setup = new NpgsqlConnection(locator);
        await using var model = AuditDbContextFactory.CreateIsolatedTestContext(setup, caseId);
        await setup.OpenAsync(); var created = false; var children = new List<Child>(); var logicalId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        try
        {
            await Execute($"CREATE SCHEMA {schema}"); created = true;
            await Execute(model.Database.GenerateCreateScript());
            await Execute($"CREATE TABLE {schema}.fixture_effect(id uuid PRIMARY KEY)");
            await Execute(OwnedMessagingSchema.TablesSql.Replace("msg.", schema + ".", StringComparison.Ordinal));
            using (var migrator = CompatibilityProgram.Builder(locator, caseId, "setup", false, true).Build())
            { await migrator.StartAsync(); await migrator.StopAsync(); }
            var producer = Start("producer"); await producer.Exit(); Assert.Equal(0, producer.Process.ExitCode);
            var consumer = Start(mode);
            if (mode != "consumer")
            {
                await consumer.Exit(); Assert.Equal(mode == "before-commit" ? 81 : mode == "after-commit" ? 82 : 83, consumer.Process.ExitCode);
                var committed = mode is "after-commit" or "commit-reply-loss";
                if (mode == "commit-reply-loss") Assert.Contains("NATIVE_FIXTURE_UNKNOWN_COMMIT", await consumer.Output());
                Assert.Equal(committed ? 1 : 0, await Count("fixture_effect"));
                Assert.Equal(committed ? 1 : 0, await Count("aud_intents"));
                Assert.Equal(committed ? 1 : 0, await Count("logical_receipts"));
                if (committed)
                {
                    await using var lookup = new NpgsqlCommand($"SELECT binding_hash FROM {schema}.logical_receipts WHERE installation_id=@case " +
                        "AND scope_kind='tenant' AND tenant_key=@case AND operation_id=@logical AND step_id=@case AND step_version=1 AND type_id=@type", setup);
                    lookup.Parameters.AddWithValue("case", caseId); lookup.Parameters.AddWithValue("logical", logicalId);
                    lookup.Parameters.AddWithValue("type", NativeWireFixture.TypeId);
                    Assert.Equal(NativeWireFixture.Create(caseId, logicalId, occurredAt).PayloadHash, await lookup.ExecuteScalarAsync());
                }
                Assert.Equal(committed ? 1 : 0, await Count("wolverine_incoming_envelopes", "WHERE status='Handled'"));
                Assert.Equal(committed ? 1 : 0, await OutputCount());
                consumer = Start("consumer");
            }
            await Until(async () => await OutputCount() == 1 && await Count("fixture_effect") == 1);
            producer = Start("producer"); await producer.Exit(); Assert.Equal(0, producer.Process.ExitCode);
            await Until(async () => await Count("aud_intents") == 2);
            Assert.Equal(1, await Count("fixture_effect"));
            Assert.Equal(1, await Count("logical_receipts"));
            Assert.Equal(1, await OutputCount());
            Assert.Equal(2, await Count("wolverine_incoming_envelopes", "WHERE status='Handled'"));
            producer = Start("producer-conflict"); await producer.Exit(); Assert.Equal(0, producer.Process.ExitCode);
            await Until(async () => await Count("quarantine_journal") == 1 && await Count("aud_intents") == 3);
            Assert.Equal(1, await Count("fixture_effect")); Assert.Equal(1, await Count("logical_receipts"));
            Assert.Equal(1, await OutputCount());
            Assert.Equal(1, await Count("quarantine_journal", "WHERE reason='HashConflict'"));
            Assert.Equal(3, await Count("wolverine_incoming_envelopes", "WHERE status='Handled'"));
        }
        finally
        {
            try { foreach (var child in children) await child.DisposeAsync(); }
            finally { if (created) await Execute($"DROP SCHEMA {schema} CASCADE"); }
        }
        Child Start(string command) { var child = new Child(command, slot, logicalId, occurredAt); children.Add(child); return child; }
        async Task Execute(string sql) { await using var c = new NpgsqlCommand(sql, setup) { CommandTimeout = 10 }; await c.ExecuteNonQueryAsync(); }
        async Task<long> Count(string table, string predicate = "") => await Query($"SELECT count(*) FROM {schema}.{table} {predicate}");
        async Task<long> OutputCount() => await Query($"SELECT (SELECT count(*) FROM {schema}.wolverine_outgoing_envelopes) + (SELECT count(*) FROM {schema}.wolverine_queue_native_result)");
        async Task<long> Query(string sql) { await using var c = new NpgsqlCommand(sql, setup) { CommandTimeout = 5 }; return (long)(await c.ExecuteScalarAsync())!; }
        static async Task Until(Func<Task<bool>> predicate)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (!await predicate()) await Task.Delay(100, budget.Token);
        }
    }

    private sealed class Child : IAsyncDisposable
    {
        public Process Process { get; }
        private readonly Task<string> _stdout;
        private readonly Task<string> _stderr;
        public Child(string mode, int slot, Guid logicalId, DateTimeOffset occurredAt)
        {
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            info.Environment["KORDRA_MSG_COMPAT_OCCURRED_AT_UTC"] = occurredAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var arg in new[] { typeof(CompatibilityProgram).Assembly.Location, "fixture", mode, slot.ToString(System.Globalization.CultureInfo.InvariantCulture), logicalId.ToString("N") }) info.ArgumentList.Add(arg);
            Process = Process.Start(info) ?? throw new InvalidOperationException("Owned fixture process did not start.");
            _stdout = Process.StandardOutput.ReadToEndAsync(); _stderr = Process.StandardError.ReadToEndAsync();
        }
        public async Task Exit()
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await Process.WaitForExitAsync(budget.Token); await Task.WhenAll(_stdout, _stderr);
        }
        public async Task<string> Output() => await _stdout + await _stderr;
        public async ValueTask DisposeAsync()
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
            await Process.WaitForExitAsync(); await Task.WhenAll(_stdout, _stderr);
            if (Environment.GetEnvironmentVariable("KORDRA_MSG_COMPAT_PROCESS_LOG_ROOT") is { } directory)
            {
                var path = Path.Combine(directory, "native-child-" + Process.Id + ".log");
                await File.WriteAllTextAsync(path, await _stdout + await _stderr);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            Process.Dispose();
        }
    }
}
