using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
using System.Globalization;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Npgsql;

namespace Kordra.Api;

/// <summary>Opaque in-process provenance captured from the actual staged caller root, never caller-reported PID/XID.</summary>
public sealed class WorkflowTransactionOrigin
{
    public WorkflowIdentity Identity { get; }
    public Guid OperationId { get; }
    public string DescriptorHash { get; }
    internal Guid CaseId { get; }
    internal string FullTransactionId { get; }
    internal int BackendPid { get; }
    internal DateTime BackendStartedUtc { get; }
    internal DateTime ServerStartedUtc { get; }
    internal DisposableTestBinding? DisposableTestProfile { get; }
    private readonly Guid _auditIntentId;
    private readonly string _auditHash;
    private readonly DeliveryIdentity _publicationDelivery;
    private readonly string _publicationHash;
    private readonly DeliveryIdentity? _consumedDelivery;
    private readonly string? _consumedHash;

    private WorkflowTransactionOrigin(Guid caseId, WorkflowIdentity identity, Guid operationId, string descriptorHash,
        string xid, int pid, DateTime backendStarted, DateTime serverStarted, AuditIntent audit,
        VerifiedMessageEnvelope publication, VerifiedMessageEnvelope? consumption, DisposableTestBinding? disposableTest = null)
    {
        DisposableTestProfile = disposableTest;
        CaseId = caseId; Identity = identity; OperationId = operationId; DescriptorHash = descriptorHash;
        FullTransactionId = xid; BackendPid = pid; BackendStartedUtc = backendStarted; ServerStartedUtc = serverStarted;
        _auditIntentId = audit.IntentId; _auditHash = AuditIntentCanonicalizer.ComputeHash(audit);
        _publicationDelivery = publication.Delivery; _publicationHash = publication.PayloadHash;
        _consumedDelivery = consumption?.Delivery; _consumedHash = consumption?.PayloadHash;
    }

    internal bool MatchesStagedEffects(AuditIntent audit, VerifiedMessageEnvelope publication, VerifiedMessageEnvelope? consumption)
        => audit is not null && publication is not null &&
            _auditIntentId == audit.IntentId && _auditHash == AuditIntentCanonicalizer.ComputeHash(audit) &&
            _publicationDelivery == publication.Delivery && _publicationHash == publication.PayloadHash &&
            _consumedDelivery == consumption?.Delivery && _consumedHash == consumption?.PayloadHash;

    internal static async Task<WorkflowTransactionOrigin> CaptureIsolatedAsync(NpgsqlConnection connection, NpgsqlTransaction root,
        Guid caseId, WorkflowIdentity identity, Guid operationId, WorkflowWakeOperation wake, AuditIntent audit,
        VerifiedMessageEnvelope publication, VerifiedMessageEnvelope? consumption, CancellationToken ct)
    {
        _ = WorkflowFixtureTarget.Validate(connection, caseId);
        if (connection.State != System.Data.ConnectionState.Open || !ReferenceEquals(root.Connection, connection) ||
            identity.InstallationId != wake.InstallationId || identity.TenantId != wake.TenantId || identity.InstanceId != wake.InstanceId ||
            operationId != (consumption is null ? wake.SchedulerOperationId : wake.WakeOperationId))
            throw new InvalidOperationException("Exact staged timer root required for provenance capture.");
        await using var command = new NpgsqlCommand("SELECT pg_current_xact_id()::text, pg_backend_pid(), backend_start, " +
            "pg_postmaster_start_time(), current_database(), session_user FROM pg_stat_activity WHERE pid=pg_backend_pid()", connection, root) { CommandTimeout = 2 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Caller-root provenance unavailable.");
        var xid = reader.GetString(0); var pid = reader.GetInt32(1);
        if (!ulong.TryParse(xid, NumberStyles.None, CultureInfo.InvariantCulture, out var full) || full == 0 ||
            full.ToString(CultureInfo.InvariantCulture) != xid || pid <= 0 || pid != connection.ProcessID || reader.IsDBNull(2) ||
            reader.GetString(4) != "kordra_test" || reader.GetString(5) != "kordra_test")
            throw new InvalidOperationException("Caller-root provenance binding rejected.");
        var backendStarted = DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);
        var serverStarted = DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc);
        if (await reader.ReadAsync(ct)) throw new InvalidOperationException("Ambiguous caller-root provenance.");
        return new(caseId, identity, operationId, wake.DescriptorHash, xid, pid, backendStarted, serverStarted, audit,
            publication, consumption);
    }
    internal static async Task<WorkflowTransactionOrigin> CaptureDisposableTestAsync(NpgsqlConnection connection, NpgsqlTransaction root,
        Guid caseId, WorkflowIdentity identity, Guid operationId, WorkflowWakeOperation wake, AuditIntent audit,
        VerifiedMessageEnvelope publication, VerifiedMessageEnvelope? consumption, DisposableTestBinding profile, CancellationToken ct)
    {
        _ = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, profile);
        if (connection.State != System.Data.ConnectionState.Open || !ReferenceEquals(root.Connection, connection) ||
            identity.InstallationId != wake.InstallationId || identity.TenantId != wake.TenantId || identity.InstanceId != wake.InstanceId ||
            operationId != (consumption is null ? wake.SchedulerOperationId : wake.WakeOperationId))
            throw new InvalidOperationException("Exact staged timer root required for provenance capture.");
        await using var command = new NpgsqlCommand("SELECT pg_current_xact_id()::text, pg_backend_pid(), backend_start, " +
            "pg_postmaster_start_time(), current_database(), session_user FROM pg_stat_activity WHERE pid=pg_backend_pid()", connection, root) { CommandTimeout = 2 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Caller-root provenance unavailable.");
        var xid = reader.GetString(0); var pid = reader.GetInt32(1);
        if (!ulong.TryParse(xid, NumberStyles.None, CultureInfo.InvariantCulture, out var full) || full == 0 ||
            full.ToString(CultureInfo.InvariantCulture) != xid || pid <= 0 || pid != connection.ProcessID || reader.IsDBNull(2) ||
            reader.GetString(4) != "kordra_test_" + profile.RunId.ToString("N") || reader.GetString(5) != "kordra_test")
            throw new InvalidOperationException("Caller-root provenance binding rejected.");
        var backendStarted = DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);
        var serverStarted = DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc);
        if (await reader.ReadAsync(ct)) throw new InvalidOperationException("Ambiguous caller-root provenance.");
        return new(caseId, identity, operationId, wake.DescriptorHash, xid, pid, backendStarted, serverStarted, audit,
            publication, consumption, profile);
    }
    public override string ToString() => "WorkflowTransactionOrigin [private provenance, not authority]";
}

public enum WorkflowTransactionProbeStatus { Committed, Aborted, Unresolved, Unavailable }

/// <summary>Server observation only. Does not attest worker quiescence, authority, owner effects or permission to retry.</summary>
public sealed class WorkflowTransactionObservation
{
    public WorkflowTransactionOrigin Origin { get; }
    public WorkflowTransactionProbeStatus Status { get; }
    public bool? OriginalBackendPresent { get; }
    internal WorkflowTransactionObservation(WorkflowTransactionOrigin origin, WorkflowTransactionProbeStatus status, bool? backendPresent)
    { Origin = origin; Status = status; OriginalBackendPresent = backendPresent; }
    public override string ToString() => "WorkflowTransactionObservation [private, not replay permission]";
}

public static class WorkflowTransactionProbe
{
    // PostgreSQL15 documents NULL for aged-out status and in-progress for prepared transactions:
    // https://www.postgresql.org/docs/15/functions-info.html
    public static WorkflowTransactionProbeStatus Classify(string? transactionStatus, bool preparedPresent, bool sameServerGeneration)
    {
        if (!sameServerGeneration) return WorkflowTransactionProbeStatus.Unavailable;
        if (preparedPresent) return WorkflowTransactionProbeStatus.Unresolved;
        return transactionStatus switch {
            "committed" => WorkflowTransactionProbeStatus.Committed,
            "aborted" => WorkflowTransactionProbeStatus.Aborted,
            "in progress" => WorkflowTransactionProbeStatus.Unresolved,
            _ => WorkflowTransactionProbeStatus.Unavailable
        };
    }

    public static async Task<WorkflowTransactionObservation> InspectIsolatedAsync(WorkflowTransactionOrigin origin,
        NpgsqlConnection independent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origin); ArgumentNullException.ThrowIfNull(independent); ct.ThrowIfCancellationRequested();
        _ = WorkflowFixtureTarget.Validate(independent, origin.CaseId);
        if (independent.State != System.Data.ConnectionState.Open || independent.ProcessID == origin.BackendPid)
            return new(origin, WorkflowTransactionProbeStatus.Unavailable, null);
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_postmaster_start_time(), current_database(), session_user, " +
                "pg_xact_status(CAST(@xid AS xid8)), EXISTS(SELECT 1 FROM pg_prepared_xacts WHERE database=current_database()), " +
                "EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@pid AND backend_start=@started)", independent) { CommandTimeout = 2 };
            command.Parameters.AddWithValue("xid", origin.FullTransactionId); command.Parameters.AddWithValue("pid", origin.BackendPid);
            command.Parameters.AddWithValue("started", origin.BackendStartedUtc);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return new(origin, WorkflowTransactionProbeStatus.Unavailable, null);
            var sameServer = reader.GetDateTime(0) == origin.ServerStartedUtc && reader.GetString(1) == "kordra_test" && reader.GetString(2) == "kordra_test";
            var status = Classify(reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4), sameServer);
            return new(origin, status, sameServer ? reader.GetBoolean(5) : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is System.Data.Common.DbException or InvalidOperationException or InvalidCastException)
        { return new(origin, WorkflowTransactionProbeStatus.Unavailable, null); }
    }
    public static async Task<WorkflowTransactionObservation> InspectDisposableTestAsync(WorkflowTransactionOrigin origin,
        NpgsqlConnection independent, DisposableTestBinding profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origin); ArgumentNullException.ThrowIfNull(independent); ct.ThrowIfCancellationRequested();
        if (origin.DisposableTestProfile is not { } captured || captured != profile)
            return new(origin, WorkflowTransactionProbeStatus.Unavailable, null);
        _ = WorkflowFixtureTarget.ValidateDisposableTest(independent, origin.CaseId, profile);
        if (independent.State != System.Data.ConnectionState.Open || independent.ProcessID == origin.BackendPid)
            return new(origin, WorkflowTransactionProbeStatus.Unavailable, null);
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_postmaster_start_time(), current_database(), session_user, " +
                "pg_xact_status(CAST(@xid AS xid8)), EXISTS(SELECT 1 FROM pg_prepared_xacts WHERE database=current_database()), " +
                "EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@pid AND backend_start=@started)", independent) { CommandTimeout = 2 };
            command.Parameters.AddWithValue("xid", origin.FullTransactionId); command.Parameters.AddWithValue("pid", origin.BackendPid);
            command.Parameters.AddWithValue("started", origin.BackendStartedUtc);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return new(origin, WorkflowTransactionProbeStatus.Unavailable, null);
            var sameServer = reader.GetDateTime(0) == origin.ServerStartedUtc && reader.GetString(1) == "kordra_test_" + profile.RunId.ToString("N") && reader.GetString(2) == "kordra_test";
            var status = Classify(reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4), sameServer);
            return new(origin, status, sameServer ? reader.GetBoolean(5) : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is System.Data.Common.DbException or InvalidOperationException or InvalidCastException)
        { return new(origin, WorkflowTransactionProbeStatus.Unavailable, null); }
    }
}
