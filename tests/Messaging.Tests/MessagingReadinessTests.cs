using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Xunit;

namespace Kordra.Messaging.Tests;

public sealed class MessagingReadinessTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-revision")]
    [InlineData("wrong-hash")]
    [InlineData("ambiguous")]
    [InlineData("null")]
    [InlineData("matching")]
    public async Task SchemaEvidenceMustBeUniqueExactRevisionAndHash(string scenario)
    {
        var expected = new string('a', 64);
        var rows = new DataTable(); rows.Columns.Add("revision", typeof(int)); rows.Columns.Add("manifest_sha256", typeof(string));
        switch (scenario)
        {
            case "wrong-revision": rows.Rows.Add(2, expected); break;
            case "wrong-hash": rows.Rows.Add(1, new string('b', 64)); break;
            case "ambiguous": rows.Rows.Add(1, expected); rows.Rows.Add(1, expected); break;
            case "null": rows.Rows.Add(1, DBNull.Value); break;
            case "matching": rows.Rows.Add(1, expected); break;
        }
        using var connection = new EvidenceConnection(rows);
        Assert.Equal(scenario == "matching", await MessagingSchemaReadiness.MatchesAsync(connection, expected, CancellationToken.None));
        Assert.Equal("SELECT revision, manifest_sha256 FROM msg.schema_manifest ORDER BY revision LIMIT 2", connection.LastCommand);
        Assert.Equal(0, connection.OpenCalls);
        connection.Close();
        Assert.False(await MessagingSchemaReadiness.MatchesAsync(connection, expected, CancellationToken.None));
        Assert.False(await MessagingSchemaReadiness.MatchesAsync(connection, "bad", CancellationToken.None));
        Assert.Equal(0, connection.OpenCalls);
    }

    // In-memory query evidence only. It proves read/mismatch behavior, not actual PostgreSQL/ACL correctness.
    private sealed class EvidenceConnection(DataTable rows) : DbConnection
    {
        private ConnectionState _state = ConnectionState.Open;
        public string? LastCommand { get; set; }
        public int OpenCalls { get; private set; }
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "synthetic";
        public override string DataSource => "in-memory";
        public override string ServerVersion => "fixture";
        public override ConnectionState State => _state;
        public override void Open() { OpenCalls++; throw new InvalidOperationException("No connection opening allowed."); }
        public override void Close() => _state = ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new EvidenceCommand(this, rows);
    }

    private sealed class EvidenceCommand(EvidenceConnection connection, DataTable rows) : DbCommand
    {
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        public override void Cancel() { }
        public override void Prepare() => throw new NotSupportedException();
        public override int ExecuteNonQuery() => throw new InvalidOperationException("Writes prohibited.");
        public override object? ExecuteScalar() => throw new NotSupportedException();
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        { connection.LastCommand = CommandText; return rows.CreateDataReader(); }
    }
}
