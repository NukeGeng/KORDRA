using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;
using Wolverine.Runtime.Serialization;

namespace Kordra.Messaging.Durability.Tests;

/// <summary>Only this test's UUID schema and owned child PIDs may be changed or terminated.</summary>
public sealed class TwoProcessDeliveryTests
{
    [Theory]
    [InlineData("unknown-type", 0)]
    [InlineData("wrong-case", 1)]
    [InlineData("wrong-tenant", 1)]
    [InlineData("unsupported-schema", 1)]
    [InlineData("expired", 1)]
    [InlineData("expires-in-queue", 1)]
    [InlineData("transient", 5)]
    public async Task InvalidOrExhaustedMessagesHaveNoEffectAndReachDeadLetter(string scenario, int attempts)
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private fixture locator required.");
        var fixture = new FixtureDatabaseScope(locator, Guid.NewGuid()); var logicalId = Guid.NewGuid(); var created = false;
        var children = new List<Child>();
        await using var setup = fixture.CreateConnection(); await setup.OpenAsync();
        try
        {
            await Execute($"CREATE SCHEMA {fixture.Schema}"); created = true;
            await Execute($"CREATE TABLE {fixture.Schema}.fixture_owner(id uuid PRIMARY KEY, value text NOT NULL); " +
                $"CREATE TABLE {fixture.Schema}.fixture_effect(id uuid PRIMARY KEY); " +
                $"CREATE TABLE {fixture.Schema}.fixture_attempt(at timestamptz NOT NULL)");
            using (var migrator = Program.Builder(locator, fixture.CaseId, true, true, false).Build())
            { await migrator.StartAsync(); await migrator.StopAsync(); }
            var producer = new Child("producer", fixture.CaseId, logicalId, scenario); children.Add(producer);
            await producer.WaitForExit(); Assert.Equal(0, producer.Process.ExitCode);
            if (scenario == "unknown-type")
            {
                // Routing uses the serialized envelope header, not the queue's diagnostic message_type column.
                await using var read = new NpgsqlCommand($"SELECT body FROM {fixture.Schema}.wolverine_queue_t12_fixture", setup);
                var envelope = EnvelopeSerializer.Deserialize((byte[])(await read.ExecuteScalarAsync())!);
                envelope.MessageType = "Kordra.T12.UnsupportedSyntheticType";
                await using var replace = new NpgsqlCommand($"UPDATE {fixture.Schema}.wolverine_queue_t12_fixture SET body=@body, message_type=@type", setup);
                replace.Parameters.AddWithValue("body", EnvelopeSerializer.Serialize(envelope));
                replace.Parameters.AddWithValue("type", envelope.MessageType);
                Assert.Equal(1, await replace.ExecuteNonQueryAsync());
            }
            if (scenario == "expires-in-queue") await Task.Delay(TimeSpan.FromSeconds(2));
            var consumer = new Child("consumer", fixture.CaseId, logicalId, scenario); children.Add(consumer);
            await consumer.WaitFor("FIXTURE_READY");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(scenario == "transient" ? 100 : 15));
            while (await Count("wolverine_dead_letters") != 1) await Task.Delay(200, deadline.Token);
            Assert.Equal(attempts, await Count("fixture_attempt"));
            Assert.Equal(0, await Count("fixture_effect"));
            if (scenario == "transient")
            {
                await using var command = new NpgsqlCommand($"SELECT at FROM {fixture.Schema}.fixture_attempt ORDER BY at", setup);
                await using var reader = await command.ExecuteReaderAsync(); var times = new List<DateTime>();
                while (await reader.ReadAsync()) times.Add(reader.GetDateTime(0));
                var expected = new[] { 1, 5, 15, 60 };
                for (var i = 0; i < 4; i++) Assert.True((times[i + 1] - times[i]).TotalSeconds >= expected[i] - 0.1);
            }
        }
        finally
        {
            try { foreach (var child in children) await child.DisposeAsync(); }
            finally { if (created) await Execute($"DROP SCHEMA {fixture.Schema} CASCADE"); }
        }
        async Task Execute(string sql) { await using var command = new NpgsqlCommand(sql, setup); await command.ExecuteNonQueryAsync(); }
        async Task<long> Count(string table)
        {
            await using var query = new NpgsqlCommand($"SELECT count(*) FROM {fixture.Schema}.{table}", setup);
            return (long)(await query.ExecuteScalarAsync())!;
        }
    }

    [Theory]
    [InlineData("producer", 1, "consumer")]
    [InlineData("producer", 1, "consumer-retention")]
    [InlineData("producer", 1, "consumer-before-commit")]
    [InlineData("producer", 1, "consumer-before-effect")]
    [InlineData("producer", 1, "consumer-after-effect")]
    [InlineData("producer-before-commit", 0, "consumer")]
    [InlineData("producer-after-commit", 1, "consumer")]
    public async Task SeparateProcessesRespectProducerCommitAndLogicalDeduplication(string producerMode, long expected, string consumerMode)
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private fixture locator required.");
        var fixture = new FixtureDatabaseScope(locator, Guid.NewGuid()); var logicalId = Guid.NewGuid();
        var children = new List<Child>(); var created = false;
        await using var setup = fixture.CreateConnection(); await setup.OpenAsync();
        try
        {
            await Execute($"CREATE SCHEMA {fixture.Schema}"); created = true;
            await Execute($"CREATE TABLE {fixture.Schema}.fixture_owner(id uuid PRIMARY KEY, value text NOT NULL); " +
                $"CREATE TABLE {fixture.Schema}.fixture_effect(id uuid PRIMARY KEY); " +
                $"CREATE TABLE {fixture.Schema}.fixture_attempt(at timestamptz NOT NULL)");
            using (var migrator = Program.Builder(locator, fixture.CaseId, true, true, false).Build())
            {
                await migrator.StartAsync(); await migrator.StopAsync();
            }
            var producer = Start(producerMode); await producer.WaitForExit();
            Assert.Equal(producerMode == "producer" ? 0 : producerMode == "producer-before-commit" ? 71 : 72, producer.Process.ExitCode);
            Assert.Equal(expected, await Count("fixture_owner"));
            var consumer = Start(consumerMode); await consumer.WaitFor("FIXTURE_READY");
            if (consumerMode is "consumer-before-effect" or "consumer-before-commit" or "consumer-after-effect")
            {
                await consumer.WaitForExit();
                Assert.Equal(consumerMode == "consumer-before-effect" ? 74 : consumerMode == "consumer-before-commit" ? 75 : 73, consumer.Process.ExitCode);
                Assert.Equal(consumerMode == "consumer-after-effect" ? 1 : 0, await Count("fixture_effect"));
                Assert.Equal(consumerMode == "consumer-after-effect" ? 1 : 0, await Count("wolverine_outgoing_envelopes"));
                await using (var status = new NpgsqlCommand($"SELECT count(*) FROM {fixture.Schema}.wolverine_incoming_envelopes WHERE status='Handled'", setup))
                    Assert.Equal(consumerMode == "consumer-after-effect" ? 1L : 0L, await status.ExecuteScalarAsync());
                // The durable inbox survives both crash windows; restart must consume it again.
                Assert.Equal(1, await Count("wolverine_incoming_envelopes"));
                consumer = Start("consumer"); await consumer.WaitFor("FIXTURE_READY");
                if (consumerMode != "consumer-after-effect") await consumer.WaitFor("FIXTURE_EFFECT_COMMITTED");
            }
            if (expected == 1)
            {
                await AwaitEffect();
                // Different transport envelope ID, identical logical operation: still one effect.
                var duplicate = Start("producer"); await duplicate.WaitForExit(); Assert.Equal(0, duplicate.Process.ExitCode);
                await consumer.WaitForOccurrences("FIXTURE_EFFECT_COMMITTED", consumerMode == "consumer-after-effect" ? 1 : 2);
                Assert.Equal(1, await Count("fixture_effect"));
                using (var resultDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    while (await Count("wolverine_queue_t12_results") != 1) await Task.Delay(100, resultDeadline.Token);
                }
                Assert.Equal(1, await Count("wolverine_queue_t12_results"));
                if (consumerMode == "consumer-retention")
                {
                    Assert.True(await Count("wolverine_incoming_envelopes") > 0);
                    using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    while (await Count("wolverine_incoming_envelopes") != 0) await Task.Delay(100, cleanupDeadline.Token);
                    Assert.Equal(1, await Count("fixture_effect"));
                }
            }
            else
            {
                // There is no envelope or owner record to recover when the transaction never committed.
                Assert.Equal(0, await Count("wolverine_incoming_envelopes"));
                Assert.Equal(0, await Count("wolverine_outgoing_envelopes"));
                Assert.Equal(0, await Count("fixture_effect"));
            }
        }
        finally
        {
            try { foreach (var child in children) await child.DisposeAsync(); }
            finally { if (created) await Execute($"DROP SCHEMA {fixture.Schema} CASCADE"); }
        }

        Child Start(string mode)
        {
            var child = new Child(mode, fixture.CaseId, logicalId); children.Add(child); return child;
        }
        async Task Execute(string sql) { await using var command = new NpgsqlCommand(sql, setup); await command.ExecuteNonQueryAsync(); }
        async Task<long> Count(string table)
        {
            await using var observer = fixture.CreateConnection(); await observer.OpenAsync();
            await using var query = new NpgsqlCommand($"SELECT count(*) FROM {fixture.Schema}.{table}", observer);
            return (long)(await query.ExecuteScalarAsync())!;
        }
        async Task AwaitEffect()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (await Count("fixture_effect") != 1) await Task.Delay(100, deadline.Token);
        }
    }

    private sealed class Child : IAsyncDisposable
    {
        public Process Process { get; }
        private readonly List<string> _lines = [];
        private readonly Task _stdout;
        private readonly Task _stderr;
        private readonly string _caseName;
        public Child(string mode, Guid caseId, Guid logicalId, string scenario = "normal")
        {
            _caseName = caseId.ToString("N") + "-" + mode;
            var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { typeof(Program).Assembly.Location, "fixture", mode, caseId.ToString("N"), logicalId.ToString("N"), scenario }) info.ArgumentList.Add(arg);
            Process = Process.Start(info) ?? throw new InvalidOperationException("Fixture child did not start.");
            _stdout = Drain(Process.StandardOutput); _stderr = Drain(Process.StandardError);
        }
        private async Task Drain(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line) { lock (_lines) _lines.Add(line); }
        }
        public Task WaitFor(string marker) => WaitForOccurrences(marker, 1);
        public async Task WaitForOccurrences(string marker, int count)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (true)
            {
                lock (_lines) if (_lines.Count(x => x == marker) >= count) return;
                if (Process.HasExited) throw new InvalidOperationException($"Fixture child exited unexpectedly ({Process.ExitCode}).");
                await Task.Delay(100, deadline.Token);
            }
        }
        public async Task WaitForExit()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await Process.WaitForExitAsync(deadline.Token); await Task.WhenAll(_stdout, _stderr);
        }
        public async ValueTask DisposeAsync()
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
            await Process.WaitForExitAsync(); await Task.WhenAll(_stdout, _stderr);
            if (Environment.GetEnvironmentVariable("KORDRA_T12_PRIVATE_LOG_DIRECTORY") is { } directory)
            {
                var path = Path.Combine(directory, _caseName + "-" + Process.Id + ".log");
                await File.WriteAllLinesAsync(path, _lines);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            Process.Dispose();
        }
    }
}
