using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Npgsql;
using Xunit;

namespace Kordra.Messaging.Durability.Tests;

public sealed class CommitReplyLossTests
{
    [Fact]
    public async Task LostActualServerCommitReplyRequiresIndependentLookup()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private fixture locator required.");
        var fixture = new FixtureDatabaseScope(locator, Guid.NewGuid()); var created = false;
        await using var owner = fixture.CreateConnection(); await owner.OpenAsync();
        try
        {
            await Execute($"CREATE SCHEMA {fixture.Schema}"); created = true;
            await Execute($"CREATE TABLE {fixture.Schema}.fixture_commit(id uuid PRIMARY KEY)");
            await using var proxy = new CommitReplyProxy();
            // The test proxy binds only a new loopback port and forwards only to the guarded fixture target.
            var proxied = new NpgsqlConnectionStringBuilder(locator)
            { Host = "127.0.0.1", Port = proxy.Port, SslMode = SslMode.Disable, Pooling = false, Timeout = 5, CommandTimeout = 5 };
            await using var client = new NpgsqlConnection(proxied.ConnectionString); await client.OpenAsync();
            await using var transaction = await client.BeginTransactionAsync();
            await using (var insert = new NpgsqlCommand($"INSERT INTO {fixture.Schema}.fixture_commit VALUES (@id)", client, transaction))
            {
                insert.Parameters.AddWithValue("id", fixture.CaseId); await insert.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAnyAsync<Exception>(() => transaction.CommitAsync());
            Assert.True(await proxy.CommitResponseDropped.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            // No replay. A new direct connection reads the durable logical key after a genuine lost reply.
            await using var observer = fixture.CreateConnection(); await observer.OpenAsync();
            await using var lookup = new NpgsqlCommand($"SELECT count(*) FROM {fixture.Schema}.fixture_commit WHERE id=@id", observer);
            lookup.Parameters.AddWithValue("id", fixture.CaseId);
            Assert.Equal(1L, await lookup.ExecuteScalarAsync());
        }
        finally { if (created) await Execute($"DROP SCHEMA {fixture.Schema} CASCADE"); }
        async Task Execute(string sql) { await using var command = new NpgsqlCommand(sql, owner); await command.ExecuteNonQueryAsync(); }
    }

    /// <summary>Drops the actual backend CommandComplete(COMMIT), never fabricates a database result.</summary>
    private sealed class CommitReplyProxy : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(15));
        private readonly Task _forward;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public TaskCompletionSource<bool> CommitResponseDropped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CommitReplyProxy() { _listener.Start(1); _forward = Forward(); }
        private async Task Forward()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var server = new TcpClient(); await server.ConnectAsync(IPAddress.Loopback, 5432, _stop.Token);
                await using var fromClient = client.GetStream(); await using var fromServer = server.GetStream();
                var send = fromClient.CopyToAsync(fromServer, _stop.Token);
                try
                {
                    var header = new byte[5];
                    while (!_stop.IsCancellationRequested)
                    {
                        await fromServer.ReadExactlyAsync(header, _stop.Token);
                        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
                        if (length < 4 || length > 1024 * 1024) throw new IOException("Unexpected fixture protocol frame.");
                        var body = new byte[length - 4]; await fromServer.ReadExactlyAsync(body, _stop.Token);
                        if (header[0] == (byte)'C' && Encoding.UTF8.GetString(body) == "COMMIT\0")
                        {
                            CommitResponseDropped.TrySetResult(true); return;
                        }
                        await fromClient.WriteAsync(header, _stop.Token); await fromClient.WriteAsync(body, _stop.Token);
                    }
                }
                finally
                {
                    client.Close(); server.Close(); _stop.Cancel();
                    try { await send; } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            { CommitResponseDropped.TrySetException(ex); }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel(); _listener.Stop();
            try { await _forward; } finally { _stop.Dispose(); }
        }
    }
}
