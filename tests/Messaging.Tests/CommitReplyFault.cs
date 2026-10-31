using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Npgsql;

namespace Kordra.Messaging.Tests;

/// <summary>
/// Test instrumentation only: drops the actual backend COMMIT response on an owned unpooled
/// connection. Does not alter target guards, return a fabricated commit outcome or enter runtime code.
/// </summary>
internal sealed class CommitReplyFault : Stream
{
    private readonly Stream _inner;
    private byte[] _pending = [];
    private int _position;
    public bool Dropped { get; private set; }

    private CommitReplyFault(Stream inner) => _inner = inner;

    public static CommitReplyFault Install(NpgsqlConnection connection)
    {
        var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (settings.Host != "127.0.0.1" || settings.Port != 5432 || settings.Database != "kordra_test" ||
            settings.Username != "kordra_test" || settings.Pooling || settings.Multiplexing)
            throw new InvalidOperationException("Fault injection requires the exact unpooled owned test connection.");
        if (typeof(NpgsqlConnection).Assembly.GetName().Version != new Version(10, 0, 3, 0))
            throw new InvalidOperationException("Fault instrumentation must be revalidated for this Npgsql version.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var connector = typeof(NpgsqlConnection).GetProperty("Connector", flags)?.GetValue(connection)
            ?? throw new InvalidOperationException("Owned connector unavailable.");
        var readBuffer = connector.GetType().GetProperty("ReadBuffer", flags)?.GetValue(connector)
            ?? throw new InvalidOperationException("Owned read buffer unavailable.");
        var left = readBuffer.GetType().GetProperty("ReadBytesLeft", flags)?.GetValue(readBuffer);
        if (left is not int count || count != 0) throw new InvalidOperationException("Fault boundary requires a drained read buffer.");
        var property = readBuffer.GetType().GetProperty("Underlying", flags)
            ?? throw new InvalidOperationException("Owned stream boundary unavailable.");
        var fault = new CommitReplyFault((Stream)property.GetValue(readBuffer)!);
        property.SetValue(readBuffer, fault);
        return fault;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0 || Dropped) return 0;
        if (_position == _pending.Length)
        {
            var header = new byte[5]; await _inner.ReadExactlyAsync(header, cancellationToken);
            var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
            if (length < 4 || length > 1024 * 1024) throw new IOException("Unexpected fixture response frame.");
            var body = new byte[length - 4]; await _inner.ReadExactlyAsync(body, cancellationToken);
            if (header[0] == (byte)'C' && Encoding.UTF8.GetString(body) == "COMMIT\0")
            {
                Dropped = true; return 0;
            }
            _pending = new byte[header.Length + body.Length]; header.CopyTo(_pending, 0); body.CopyTo(_pending, header.Length); _position = 0;
        }
        var count = Math.Min(buffer.Length, _pending.Length - _position);
        _pending.AsMemory(_position, count).CopyTo(buffer); _position += count; return count;
    }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
