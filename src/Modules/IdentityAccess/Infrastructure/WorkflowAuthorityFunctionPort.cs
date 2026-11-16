using Npgsql;
using NpgsqlTypes;

namespace Kordra.Modules.IdentityAccess.Infrastructure;

/// <summary>Exact worker permit claim; every field is only compared against the persisted permit, never trusted.</summary>
public sealed record WorkflowPermitClaim(Guid PermitId, Guid OperationId, Guid InstanceId, long ExpectedVersion,
    string SpecificationHash, Guid BootId, long CustodyEpoch);

public sealed record WorkflowPermitConsumption(Guid TenantId, ulong ConsumedTransactionId, bool Duplicate);

/// <summary>12.3b (D3): generation 1 created by the start root from admitted owner facts. Evidence is canonical bytes.</summary>
public sealed record WorkflowDelegationDraft(Guid DelegationId, Guid TenantId, Guid InstanceId, Guid ActorId, Guid SessionId,
    Guid BootGeneration, long PrincipalRevision, Guid ProjectId, Guid EnvironmentId, Guid ReleaseId, long ReleaseRevision,
    string ReleaseHash, string TemplateId, int TemplateVersion, string SpecificationHash, IReadOnlyList<string> AllowedKinds,
    byte[] Evidence, DateTimeOffset ValidUntilUtc);

public sealed record WorkflowDelegationCreated(Guid DelegationId, long Generation, bool Duplicate);

/// <summary>12.3b (D4): next generation for the same original actor and binding.</summary>
public sealed record WorkflowDelegationReauthorization(Guid TenantId, Guid InstanceId, long ExpectedGeneration, Guid DelegationId,
    Guid ActorId, Guid SessionId, Guid BootGeneration, long PrincipalRevision, string SpecificationHash, byte[] Evidence,
    DateTimeOffset ValidUntilUtc, string ReasonCode);

public sealed record WorkflowDelegationReissued(Guid DelegationId, long Generation, long AuthorityEpoch, bool Duplicate);

public sealed record WorkflowDelegationRevoked(long AuthorityEpoch, bool Duplicate);

public sealed record WorkflowCurrentDelegation(Guid DelegationId, long Generation, Guid ActorId, Guid SessionId, Guid BootGeneration,
    long PrincipalRevision, Guid ProjectId, Guid EnvironmentId, Guid ReleaseId, long ReleaseRevision, string ReleaseHash,
    string TemplateId, int TemplateVersion, string SpecificationHash, IReadOnlyList<string> AllowedKinds, byte[] Evidence,
    DateTimeOffset ValidUntilUtc, bool Revoked);

/// <summary>12.3b (D5): one permit attempt for an authenticated worker service; identity values are compared, never trusted.</summary>
public sealed record WorkflowPermitRequest(Guid PermitId, Guid ServiceId, Guid TenantId, Guid InstanceId, Guid StepId,
    Guid OperationId, long ExpectedVersion, string SpecificationHash, string TemplateHash, string TransitionKind, Guid BootId,
    long CustodyEpoch);

public sealed record WorkflowPermitIssued(Guid PermitId, int Attempt, long AuthorityEpoch, DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc, bool Duplicate);

public sealed record WorkflowServiceBinding(Guid TenantId, Guid EnvironmentId, string TemplateId, int TemplateVersion, string TransitionKind);

/// <summary>12.3b (D6): offline owner manifest (bootstrap/rotate/retire/revoke) applied with its AUD intent on the same root.</summary>
public sealed record WorkflowServiceManifest(string ManifestSha256, string Kind, Guid InstallationId, Guid ServiceId,
    string? ServiceKind, string? LoginName, string? SpkiSha256, DateTimeOffset? PinNotBeforeUtc, DateTimeOffset? PinNotAfterUtc,
    IReadOnlyList<WorkflowServiceBinding> Bindings, Guid AuditIntentId);

public sealed record WorkflowServiceManifestApplied(long Generation, long AuthorityEpoch, bool Duplicate);

/// <summary>
/// T12.3a/12.3b (inactive, no DI registration): calls the IAM owner functions of <c>iam-workflow-authority-v1/v2.sql</c> on the
/// caller-owned root connection/transaction. Never opens, commits or rolls back; identity comes from the session login only.
/// Owner errors surface as <see cref="PostgresException"/> with a Kordra <c>KD…</c> SQLSTATE.
/// </summary>
public sealed class WorkflowAuthorityFunctionPort
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _root;

    public WorkflowAuthorityFunctionPort(NpgsqlConnection connection, NpgsqlTransaction root)
    {
        (_connection, _root) = WorkflowFunctionRoot.Require(connection, root);
    }

    public async Task<long> AcquireFenceAsync(Guid installationId, CancellationToken ct = default)
    {
        await using var command = Command("SELECT iam_workflow.acquire_fence(@installation)");
        command.Parameters.Add(new("installation", NpgsqlDbType.Uuid) { Value = installationId });
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<long> AdvanceEpochAsync(Guid installationId, string reasonCode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reasonCode);
        await using var command = Command("SELECT iam_workflow.advance_epoch(@installation, @reason)");
        command.Parameters.Add(new("installation", NpgsqlDbType.Uuid) { Value = installationId });
        command.Parameters.Add(new("reason", NpgsqlDbType.Text) { Value = reasonCode });
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<WorkflowPermitConsumption> ConsumePermitAsync(WorkflowPermitClaim claim, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using var command = Command("SELECT tenant_id, consumed_xact::text, duplicate FROM iam_workflow.consume_permit(" +
            "@permit, @operation, @instance, @version, @hash, @boot, @custody)");
        command.Parameters.Add(new("permit", NpgsqlDbType.Uuid) { Value = claim.PermitId });
        command.Parameters.Add(new("operation", NpgsqlDbType.Uuid) { Value = claim.OperationId });
        command.Parameters.Add(new("instance", NpgsqlDbType.Uuid) { Value = claim.InstanceId });
        command.Parameters.Add(new("version", NpgsqlDbType.Bigint) { Value = claim.ExpectedVersion });
        command.Parameters.Add(new("hash", NpgsqlDbType.Text) { Value = claim.SpecificationHash ?? (object)DBNull.Value });
        command.Parameters.Add(new("boot", NpgsqlDbType.Uuid) { Value = claim.BootId });
        command.Parameters.Add(new("custody", NpgsqlDbType.Bigint) { Value = claim.CustodyEpoch });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Permit consumption returned no receipt.");
        return new(reader.GetGuid(0), ulong.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture), reader.GetBoolean(2));
    }

    public async Task<DateTimeOffset> AdvanceFloorAsync(Guid installationId, DateTimeOffset admittedUtc, CancellationToken ct = default)
    {
        await using var command = Command("SELECT iam_workflow.advance_floor(@installation, @admitted)");
        command.Parameters.Add(new("installation", NpgsqlDbType.Uuid) { Value = installationId });
        command.Parameters.Add(new("admitted", NpgsqlDbType.TimestampTz) { Value = admittedUtc.ToUniversalTime() });
        var floor = (DateTime)(await command.ExecuteScalarAsync(ct))!;
        return new DateTimeOffset(DateTime.SpecifyKind(floor, DateTimeKind.Utc));
    }

    public async Task<WorkflowDelegationCreated> CreateDelegationAsync(WorkflowDelegationDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var command = Command("SELECT delegation_id, generation, duplicate FROM iam_workflow.create_delegation(" +
            "@id, @tenant, @instance, @actor, @session, @boot, @principal_revision, @project, @environment, @release, " +
            "@release_revision, @release_hash, @template_id, @template_version, @specification_hash, @kinds, @evidence, " +
            "@evidence_hash, @valid_until)");
        Uuid(command, "id", draft.DelegationId); Uuid(command, "tenant", draft.TenantId); Uuid(command, "instance", draft.InstanceId);
        Uuid(command, "actor", draft.ActorId); Uuid(command, "session", draft.SessionId); Uuid(command, "boot", draft.BootGeneration);
        Bigint(command, "principal_revision", draft.PrincipalRevision); Uuid(command, "project", draft.ProjectId);
        Uuid(command, "environment", draft.EnvironmentId); Uuid(command, "release", draft.ReleaseId);
        Bigint(command, "release_revision", draft.ReleaseRevision); Text(command, "release_hash", draft.ReleaseHash);
        Text(command, "template_id", draft.TemplateId);
        command.Parameters.Add(new("template_version", NpgsqlDbType.Integer) { Value = draft.TemplateVersion });
        Text(command, "specification_hash", draft.SpecificationHash);
        command.Parameters.Add(new("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = draft.AllowedKinds?.ToArray() ?? (object)DBNull.Value });
        Bytes(command, "evidence", draft.Evidence); Bytes(command, "evidence_hash", Sha256(draft.Evidence));
        Time(command, "valid_until", draft.ValidUntilUtc);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Delegation creation returned no receipt.");
        return new(reader.GetGuid(0), reader.GetInt64(1), reader.GetBoolean(2));
    }

    public async Task<WorkflowDelegationReissued> ReauthorizeDelegationAsync(WorkflowDelegationReauthorization request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var command = Command("SELECT delegation_id, generation, authority_epoch, duplicate FROM " +
            "iam_workflow.reauthorize_delegation(@tenant, @instance, @expected_generation, @id, @actor, @session, @boot, " +
            "@principal_revision, @specification_hash, @evidence, @evidence_hash, @valid_until, @reason)");
        Uuid(command, "tenant", request.TenantId); Uuid(command, "instance", request.InstanceId);
        Bigint(command, "expected_generation", request.ExpectedGeneration); Uuid(command, "id", request.DelegationId);
        Uuid(command, "actor", request.ActorId); Uuid(command, "session", request.SessionId); Uuid(command, "boot", request.BootGeneration);
        Bigint(command, "principal_revision", request.PrincipalRevision); Text(command, "specification_hash", request.SpecificationHash);
        Bytes(command, "evidence", request.Evidence); Bytes(command, "evidence_hash", Sha256(request.Evidence));
        Time(command, "valid_until", request.ValidUntilUtc); Text(command, "reason", request.ReasonCode);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Delegation reauthorization returned no receipt.");
        return new(reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetBoolean(3));
    }

    public async Task<WorkflowDelegationRevoked> RevokeDelegationAsync(Guid delegationId, string reasonCode, CancellationToken ct = default)
    {
        await using var command = Command("SELECT authority_epoch, duplicate FROM iam_workflow.revoke_delegation(@id, @reason)");
        Uuid(command, "id", delegationId); Text(command, "reason", reasonCode);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Delegation revocation returned no receipt.");
        return new(reader.GetInt64(0), reader.GetBoolean(1));
    }

    public async Task<WorkflowCurrentDelegation?> ReadCurrentDelegationAsync(Guid tenantId, Guid instanceId, CancellationToken ct = default)
    {
        await using var command = Command("SELECT delegation_id, generation, actor_id, session_id, boot_generation, principal_revision, " +
            "project_id, environment_id, release_id, release_revision, release_hash, template_id, template_version, specification_hash, " +
            "allowed_kinds, authority_evidence, valid_until, revoked FROM iam_workflow.read_current_delegation(@tenant, @instance)");
        Uuid(command, "tenant", tenantId); Uuid(command, "instance", instanceId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(reader.GetGuid(0), reader.GetInt64(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4), reader.GetInt64(5),
            reader.GetGuid(6), reader.GetGuid(7), reader.GetGuid(8), reader.GetInt64(9), reader.GetString(10), reader.GetString(11),
            reader.GetInt32(12), reader.GetString(13), reader.GetFieldValue<string[]>(14), reader.GetFieldValue<byte[]>(15),
            Utc(reader.GetFieldValue<DateTime>(16)), reader.GetBoolean(17));
    }

    public async Task<WorkflowPermitIssued> IssuePermitAsync(WorkflowPermitRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var command = Command("SELECT permit_id, attempt, authority_epoch, issued_at, expires_at, duplicate FROM " +
            "iam_workflow.issue_permit(@id, @service, @tenant, @instance, @step, @operation, @version, @hash, @template_hash, @kind, " +
            "@boot, @custody)");
        Uuid(command, "id", request.PermitId); Uuid(command, "service", request.ServiceId); Uuid(command, "tenant", request.TenantId);
        Uuid(command, "instance", request.InstanceId); Uuid(command, "step", request.StepId); Uuid(command, "operation", request.OperationId);
        Bigint(command, "version", request.ExpectedVersion); Text(command, "hash", request.SpecificationHash);
        Text(command, "template_hash", request.TemplateHash); Text(command, "kind", request.TransitionKind);
        Uuid(command, "boot", request.BootId); Bigint(command, "custody", request.CustodyEpoch);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Permit issuance returned no receipt.");
        return new(reader.GetGuid(0), reader.GetInt32(1), reader.GetInt64(2), Utc(reader.GetFieldValue<DateTime>(3)),
            Utc(reader.GetFieldValue<DateTime>(4)), reader.GetBoolean(5));
    }

    public async Task<WorkflowServiceManifestApplied> ApplyServiceManifestAsync(WorkflowServiceManifest manifest, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        await using var command = Command("SELECT generation, authority_epoch, duplicate FROM iam_workflow.apply_service_manifest(" +
            "@sha, @kind, @installation, @service, @service_kind, @login::name, @spki, @not_before, @not_after, @bindings, @audit)");
        Text(command, "sha", manifest.ManifestSha256); Text(command, "kind", manifest.Kind);
        Uuid(command, "installation", manifest.InstallationId); Uuid(command, "service", manifest.ServiceId);
        Text(command, "service_kind", manifest.ServiceKind); Text(command, "login", manifest.LoginName); Text(command, "spki", manifest.SpkiSha256);
        command.Parameters.Add(new("not_before", NpgsqlDbType.TimestampTz) { Value = manifest.PinNotBeforeUtc?.ToUniversalTime() ?? (object)DBNull.Value });
        command.Parameters.Add(new("not_after", NpgsqlDbType.TimestampTz) { Value = manifest.PinNotAfterUtc?.ToUniversalTime() ?? (object)DBNull.Value });
        command.Parameters.Add(new("bindings", NpgsqlDbType.Jsonb) { Value = System.Text.Json.JsonSerializer.Serialize(
            (manifest.Bindings ?? []).Select(b => new Dictionary<string, object> {
                ["tenant_id"] = b.TenantId, ["environment_id"] = b.EnvironmentId, ["template_id"] = b.TemplateId,
                ["template_version"] = b.TemplateVersion, ["transition_kind"] = b.TransitionKind })) });
        Uuid(command, "audit", manifest.AuditIntentId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Service manifest returned no receipt.");
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetBoolean(2));
    }

    private NpgsqlCommand Command(string sql)
    {
        return new NpgsqlCommand(sql, _connection, _root);
    }

    private static void Uuid(NpgsqlCommand c, string name, Guid value) => c.Parameters.Add(new(name, NpgsqlDbType.Uuid) { Value = value });
    private static void Bigint(NpgsqlCommand c, string name, long value) => c.Parameters.Add(new(name, NpgsqlDbType.Bigint) { Value = value });
    private static void Text(NpgsqlCommand c, string name, string? value) =>
        c.Parameters.Add(new(name, NpgsqlDbType.Text) { Value = value ?? (object)DBNull.Value });
    private static void Bytes(NpgsqlCommand c, string name, byte[]? value) =>
        c.Parameters.Add(new(name, NpgsqlDbType.Bytea) { Value = value ?? (object)DBNull.Value });
    private static void Time(NpgsqlCommand c, string name, DateTimeOffset value) =>
        c.Parameters.Add(new(name, NpgsqlDbType.TimestampTz) { Value = value.ToUniversalTime() });
    private static byte[]? Sha256(byte[]? value) => value is null ? null : System.Security.Cryptography.SHA256.HashData(value);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

/// <summary>Shared guard: owner function ports run only on an open caller connection with its own live root.</summary>
internal static class WorkflowFunctionRoot
{
    public static (NpgsqlConnection Connection, NpgsqlTransaction Root) Require(NpgsqlConnection connection, NpgsqlTransaction root)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(root);
        if (!ReferenceEquals(root.Connection, connection) || connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("Owner functions require the caller's open root connection and transaction.");
        return (connection, root);
    }
}
