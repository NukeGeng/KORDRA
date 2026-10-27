using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.ArtifactManagement.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Contracts;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public enum ArtifactQueryKind { Detail, List, Registration, Registries, Replay }
public enum ArtifactExecutionStatus { Completed, Denied, Unavailable, Unknown }
public sealed record ArtifactReadCommand(ArtifactQueryKind Kind, Guid CorrelationId, Guid? ResourceId = null,
    int Limit = 50, Guid? AfterArtifactId = null, ArtifactRegistrationReceipt? Replay = null);
public sealed record ArtifactResponseCommitment(Guid InvocationId, Guid ProjectId, string PayloadSha256,
    int StatusCode, DateTimeOffset ValidUntilUtc);
public interface IArtifactResponseCommitter
{
    Task CommitAsync(ArtifactResponseCommitment selection, ReadOnlyMemory<byte> bytes, CancellationToken ct);
    void Abort();
}

/// <summary>Query-specific project U/D; does not reuse an inventory target permit or invoke a provider.</summary>
public sealed class ArtifactReadCoordinator(Guid installation, ArtifactAuthorityResolver authority,
    IDbContextFactory<ArtifactDbContext> contexts, ArtifactOwnerTransaction transactions,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Reply(int Status, byte[] Bytes);
    private sealed class InvocationState { public bool RetainCapacity; }

    public async Task<ArtifactExecutionStatus> ExecuteAsync(SessionSelection subject, Guid project,
        ArtifactReadCommand query, IArtifactResponseCommitter response, CancellationToken ct = default)
    {
        if (subject is null || project == Guid.Empty || query is null || query.CorrelationId == Guid.Empty || response is null ||
            !Enum.IsDefined(query.Kind) || query.Limit is < 1 or > 50 || query.AfterArtifactId == Guid.Empty ||
            query.Kind is ArtifactQueryKind.Detail or ArtifactQueryKind.Registration && (query.ResourceId is null || query.ResourceId == Guid.Empty) ||
            query.Kind == ArtifactQueryKind.Replay && query.Replay is null) return ArtifactExecutionStatus.Denied;
        var capacity = ProtectedInvocationCapacity.TryReserve(gate);
        if (capacity is null) return ArtifactExecutionStatus.Unavailable;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(10));
        var state = new InvocationState();
        var task = Run(subject, project, query, response, state, budget.Token);
        ArtifactExecutionStatus status = ArtifactExecutionStatus.Unknown;
        try
        {
            status = await task.WaitAsync(budget.Token);
            if (status == ArtifactExecutionStatus.Unknown) response.Abort();
            return status;
        }
        catch
        {
            if (!task.IsCompleted)
            { state.RetainCapacity = true; gate.Close(); Observe(task); response.Abort(); status = ArtifactExecutionStatus.Unknown; }
            else status = ArtifactExecutionStatus.Unavailable;
            return status;
        }
        finally
        {
            // An unresolved task/commit retains invocation ownership until process reconciliation.
            if (task.IsCompleted && !state.RetainCapacity) capacity.Dispose();
        }
    }

    private async Task<ArtifactExecutionStatus> Run(SessionSelection subject, Guid project, ArtifactReadCommand query,
        IArtifactResponseCommitter response, InvocationState state, CancellationToken ct)
    {
        var invocation = Guid.NewGuid();
        var permission = query.Kind is ArtifactQueryKind.Detail or ArtifactQueryKind.List ? "artifact.read" : "artifact.register";
        ArtifactAuthorityBinding original;
        var queryDigest = QueryDigest(query);
        using (var stage = Stage(ct))
        await using (var held = await gate.AcquireReadAsync(stage.Token))
        {
            var binding = await authority.ResolveAsync(subject, project, permission, invocation, ct: stage.Token);
            if (binding is null || query.Replay is { } replay && (replay.InstallationId != installation || replay.PrincipalId != binding.Facts.Subject.PrincipalId ||
                replay.Request.Identity.ProjectId != project || replay.Request.Identity.TenantId != binding.Facts.Project.TenantId)) return ArtifactExecutionStatus.Denied;
            var invocationCap = clock.GetTrustedUtcNow().AddSeconds(10);
            var until = binding.ValidUntilUtc < invocationCap ? binding.ValidUntilUtc : invocationCap;
            original = new(binding.Facts, null, null, until, until);
            var use = await transactions.RunAsync(_ => Intent(original, query, queryDigest, AuditIntentStage.Use),
                (_, _) => Task.FromResult<PreparedArtifactRegistration?>(null),
                async token =>
                {
                    var current = await authority.RecheckAsync(subject, original, token);
                    if (current is null || current.Digest != original.Digest) return false;
                    if (current.ValidUntilUtc < original.ValidUntilUtc)
                        original = new(original.Facts, null, null, original.OriginalUntilUtc, current.ValidUntilUtc);
                    return clock.GetTrustedUtcNow() < original.ValidUntilUtc;
                }, stage.Token);
            if (use.Status == "unknown") state.RetainCapacity = true;
            if (use.Status != "durable") return Outcome(use.Status);
            EnsureValid(original.ValidUntilUtc, stage.Token);
        }
        var reply = await ReadReply(original, query, ct);
        if (reply.Bytes.Length > (query.Kind == ArtifactQueryKind.List ? 65536 : 16384)) return ArtifactExecutionStatus.Unavailable;
        using (var stage = Stage(ct))
        await using (var held = await gate.AcquireReadAsync(stage.Token))
        {
            var current = await authority.RecheckAsync(subject, original, stage.Token);
            if (current is null || current.Digest != original.Digest) return ArtifactExecutionStatus.Denied;
            var until = current.ValidUntilUtc < original.ValidUntilUtc ? current.ValidUntilUtc : original.ValidUntilUtc;
            // Do not disclose a changed Pending receipt or a differently selected page/candidate set.
            var fresh = await ReadReply(current, query, stage.Token);
            if (fresh.Status != reply.Status || !fresh.Bytes.AsSpan().SequenceEqual(reply.Bytes)) return ArtifactExecutionStatus.Unavailable;
            var digest = Hash(reply.Bytes);
            var disclosure = await transactions.RunAsync(_ => Intent(current, query, queryDigest, AuditIntentStage.Disclosure, digest, reply.Bytes.Length),
                (_, _) => Task.FromResult<PreparedArtifactRegistration?>(null), async token =>
                {
                    var recheck = await authority.RecheckAsync(subject, original, token);
                    if (recheck is null || recheck.Digest != original.Digest) return false;
                    if (recheck.ValidUntilUtc < until) until = recheck.ValidUntilUtc;
                    return clock.GetTrustedUtcNow() < until;
                }, stage.Token);
            if (disclosure.Status == "unknown") state.RetainCapacity = true;
            if (disclosure.Status != "durable") return Outcome(disclosure.Status);
            EnsureValid(until, stage.Token);
            try
            {
                await response.CommitAsync(new(invocation, project, digest, reply.Status, until), reply.Bytes, stage.Token);
                return ArtifactExecutionStatus.Completed;
            }
            catch { response.Abort(); return ArtifactExecutionStatus.Unknown; }
        }
    }

    private async Task<Reply> ReadReply(ArtifactAuthorityBinding binding, ArtifactReadCommand query, CancellationToken ct)
    {
        var tenant = binding.Facts.Project.TenantId; var project = binding.Facts.Project.ProjectId;
        var correlation = query.CorrelationId;
        if (query.Kind == ArtifactQueryKind.Registries)
        {
            if (authority.Configuration is null) return Error(503, "registry_unconfigured", correlation);
            var items = authority.Configuration.Registries.Where(r => r.TenantId == tenant && r.ProjectId == project)
                .Select(r => new ArtifactRegistrySelectorV1(r.RegistryId, r.DisplayName, r.Repository, r.AuthMode != "anonymous")).ToArray();
            return Encode(200, new { schemaVersion = 1, correlationId = correlation, items });
        }
        await using var db = await contexts.CreateDbContextAsync(ct);
        var store = new ArtifactQueryStore(db);
        if (query.Kind == ArtifactQueryKind.Detail)
        {
            var artifact = await store.ReadDetailAsync(tenant, project, query.ResourceId!.Value, ct);
            return artifact is null ? Error(404, "not_found", correlation) : Encode(200, new { schemaVersion = 1, correlationId = correlation, artifact });
        }
        if (query.Kind == ArtifactQueryKind.List)
        {
            var page = await store.ReadPageAsync(tenant, project, query.Limit, query.AfterArtifactId, ct);
            return page is null ? Error(404, "not_found", correlation) : Encode(200, new
            {
                schemaVersion = 1,
                correlationId = correlation,
                items = page.Items,
                nextAfterArtifactId = page.NextAfterArtifactId
            });
        }
        var id = query.ResourceId;
        if (query.Kind == ArtifactQueryKind.Replay)
        {
            var selected = await store.FindOwnKeyAsync(query.Replay!, ct);
            if (selected is null) return Error(404, "not_found", correlation);
            if (selected.Status == "key_conflict") return Error(409, "idempotency_conflict", correlation);
            id = selected.RegistrationId;
        }
        var registration = await new ArtifactRegistrationStore(db).ReadOwnRegistrationAsync(installation, tenant, project,
            binding.Facts.Subject.PrincipalId, id!.Value, ct);
        if (registration is null) return Error(404, "not_found", correlation);
        if (query.Kind == ArtifactQueryKind.Replay && registration.Status != "Registered")
            return Encode(409, new
            {
                schemaVersion = 1,
                correlationId = correlation,
                code = registration.Status == "Pending" ? "registration_in_progress" : "registration_terminal",
                registration
            });
        return Encode(200, new { schemaVersion = 1, correlationId = correlation, registration });
    }

    private AuditIntent Intent(ArtifactAuthorityBinding binding, ArtifactReadCommand query, string queryDigest,
        AuditIntentStage stage, string? resultDigest = null, long? size = null) =>
        new(1, Guid.NewGuid(), binding.Facts.InvocationId, stage, binding.Facts.Subject.PrincipalId,
            binding.Facts.Project.TenantId, query.ResourceId ?? binding.Facts.Project.ProjectId,
            binding.Facts.Grant.Permission, "project", binding.Facts.Project.ProjectId, null,
            binding.Facts.Grant.AssignmentRevision,
            "artifact-query-" + Hash(Encoding.UTF8.GetBytes(queryDigest + "\n" + binding.Digest)),
            clock.GetTrustedUtcNow(), query.CorrelationId, resultDigest, size);
    private static string QueryDigest(ArtifactReadCommand query) => Hash(Encoding.UTF8.GetBytes("artifact-query-v1\n" +
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            kind = query.Kind.ToString(),
            resourceId = query.ResourceId,
            limit = query.Limit,
            afterArtifactId = query.AfterArtifactId,
            keyHash = query.Replay?.KeyHash,
            businessHash = query.Replay?.BusinessHash
        }, Json)));
    private static Reply Error(int status, string code, Guid correlation) =>
        Encode(status, new { schemaVersion = 1, correlationId = correlation, code, registration = (ArtifactRegistrationV1?)null });
    private static Reply Encode(int status, object value) => new(status, JsonSerializer.SerializeToUtf8Bytes(value, Json));
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static ArtifactExecutionStatus Outcome(string status) => status switch
    { "denied" => ArtifactExecutionStatus.Denied, "unknown" => ArtifactExecutionStatus.Unknown, _ => ArtifactExecutionStatus.Unavailable };
    private void EnsureValid(DateTimeOffset until, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); if (clock.GetTrustedUtcNow() >= until) throw new OperationCanceledException("Artifact authority expired."); }
    private static CancellationTokenSource Stage(CancellationToken ct)
    { var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(2)); return budget; }
    private static void Observe(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
