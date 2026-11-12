using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Npgsql;

namespace Kordra.Api;

/// <summary>Isolated installation coordination on a dedicated PostgreSQL session.</summary>
public sealed class WorkflowFixtureCustodyLease : IAsyncDisposable
{
    private readonly NpgsqlConnection _session;
    private readonly int _backend;
    private int _disposed;
    private readonly DisposableTestBinding? _disposableTest;
    internal bool MatchesTestTarget(DisposableTestBinding? expected) => Nullable.Equals(_disposableTest, expected);
    internal Guid InstallationId { get; }
    internal bool IsClosed => Admission.IsClosed;

    private WorkflowFixtureCustodyLease(NpgsqlConnection session, Guid installationId, DisposableTestBinding? disposableTest = null)
    {
        InstallationId = installationId; _disposableTest = disposableTest;
        _session = session;
        _backend = session.ProcessID;
        _session.StateChange += SessionChanged;
    }

    private FairAsyncReaderWriterAdmissionGate Admission { get; } = new();

    public static long LockKey(Guid installationId)
    {
        if (installationId == Guid.Empty)
            throw new ArgumentException("Explicit fixture installation required.", nameof(installationId));
        return BinaryPrimitives.ReadInt64BigEndian(
            SHA256.HashData(Encoding.UTF8.GetBytes("kordra.wfl.fixture.custody.v1:" + installationId.ToString("N"))));
    }

    public static async Task<WorkflowFixtureCustodyLease?> TryAcquireIsolatedAsync(
        string locator, Guid caseId, long manifestKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (manifestKey != LockKey(caseId))
            throw new InvalidOperationException("Custody key differs from the fixture installation.");

        var settings = new NpgsqlConnectionStringBuilder(locator) {
            Pooling = false, Enlist = false, Timeout = 2, CommandTimeout = 2
        };
        var session = new NpgsqlConnection(settings.ConnectionString);
        var transferred = false;
        try
        {
            _ = WorkflowFixtureTarget.Validate(session, caseId);
            await session.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT CASE WHEN current_database()='kordra_test' AND session_user='kordra_test' " +
                "THEN pg_try_advisory_lock(@key) ELSE false END", session);
            command.Parameters.AddWithValue("key", manifestKey);
            if (await command.ExecuteScalarAsync(ct) is not true) return null;

            var lease = new WorkflowFixtureCustodyLease(session, caseId);
            transferred = true;
            return lease;
        }
        finally
        {
            if (!transferred) await session.DisposeAsync();
        }
    }

    public static async Task<WorkflowFixtureCustodyLease?> TryAcquireDisposableTestAsync(
        string locator, Guid caseId, long manifestKey, DisposableTestBinding profile, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (manifestKey != LockKey(caseId))
            throw new InvalidOperationException("Custody key differs from the fixture installation.");

        _ = WorkflowFixtureTarget.ValidateDisposableTestLocator(locator, caseId, profile);
        var settings = new NpgsqlConnectionStringBuilder(locator) {
            Pooling = false, Enlist = false, Timeout = 2, CommandTimeout = 2
        };
        var session = new NpgsqlConnection(settings.ConnectionString);
        var transferred = false;
        try
        {
            _ = WorkflowFixtureTarget.ValidateDisposableTest(session, caseId, profile);
            await session.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT CASE WHEN current_database()=@database AND session_user='kordra_test' " +
                "THEN pg_try_advisory_lock(@key) ELSE false END", session);
            command.Parameters.AddWithValue("database", "kordra_test_" + profile.RunId.ToString("N"));
            command.Parameters.AddWithValue("key", manifestKey);
            if (await command.ExecuteScalarAsync(ct) is not true) return null;

            var lease = new WorkflowFixtureCustodyLease(session, caseId, profile);
            transferred = true;
            return lease;
        }
        finally
        {
            if (!transferred) await session.DisposeAsync();
        }
    }

    public async ValueTask<FairAsyncReaderWriterAdmissionGate.AdmissionLease> EnterAsync(CancellationToken ct = default)
    {
        var permit = await Admission.AcquireReadAsync(ct);
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || _session.State != ConnectionState.Open)
                throw new InvalidOperationException("Dedicated custody session is unavailable.");
            // The dedicated session issues no unlock or reconnect. Check identity before each admission.
            await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", _session);
            if (await command.ExecuteScalarAsync(ct) is not int backend || backend != _backend ||
                Volatile.Read(ref _disposed) != 0)
                throw new InvalidOperationException("Dedicated custody session changed.");
            return permit;
        }
        catch
        {
            Admission.Close();
            await permit.DisposeAsync();
            throw;
        }
    }

    private void SessionChanged(object? sender, StateChangeEventArgs args)
    {
        if (args.CurrentState is ConnectionState.Closed or ConnectionState.Broken)
            Admission.Close();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Admission.Close();
        _session.StateChange -= SessionChanged;
        await _session.DisposeAsync();
    }
}
