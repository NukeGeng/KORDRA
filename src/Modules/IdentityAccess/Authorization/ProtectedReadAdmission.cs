using System.Security.Cryptography;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Contracts;

namespace Kordra.Modules.IdentityAccess.Authorization;

public sealed record TypedReadSelection(Guid InvocationId, Guid TenantId, Guid TargetId, string ScopeKind, Guid ScopeId);

/// <summary>One private bounded copy. No public payload accessor, cache or transport publication.</summary>
public sealed class PrivateReadBuffer
{
    private readonly byte[] _bytes;
    internal ReadOnlyMemory<byte> Bytes => _bytes;
    public int Records { get; }
    public PrivateReadBuffer(ReadOnlySpan<byte> bytes, int records)
    {
        if (bytes.Length > 256 * 1024 || records is < 0 or > 200) throw new InvalidDataException("Protected result exceeds the accepted bound.");
        _bytes = bytes.ToArray(); Records = records;
    }
}
public interface IProtectedTargetRead
{
    // Trusted, nonblocking synchronous admission of this immutable selection; no later retargeting.
    Task<PrivateReadBuffer> Admit(TypedReadSelection selection, CancellationToken ct);
}
public interface ICallerBoundResponseCommitter
{
    Task CommitAsync(TypedReadSelection selection, ReadOnlyMemory<byte> buffer, CancellationToken ct);
    void Abort();
}
public enum ProtectedReadStatus { Completed, Denied, Unavailable, Cancelled, Unknown }

/// <summary>IAM U/D protocol. Never registered with a positive provider by default.</summary>
public sealed class ProtectedReadAdmission(FairAsyncReaderWriterAdmissionGate gate,
    ICurrentSubjectReader subjects, IScopedGrantReader grants, IRoleDefinitionReader definitions,
    ITargetAuthorityReader targets, IResourcePolicyReader policies, IAuthorityCommitter audit,
    IAuthorityClock clock)
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FairAsyncReaderWriterAdmissionGate, SemaphoreSlim> Capacities = new();
    private readonly SemaphoreSlim _capacity = Capacities.GetValue(gate, _ => new(16, 16));
    private sealed record Binding(CurrentSubject Subject, ScopedGrant Grant, TargetAuthority Target,
        ResourcePolicyAuthority Policy, string Digest, DateTimeOffset ValidUntil);

    public async Task<ProtectedReadStatus> ExecuteAsync(SessionSelection subject, Guid targetId,
        IProtectedTargetRead read, ICallerBoundResponseCommitter response, CancellationToken ct = default)
    {
        if (subject is null || targetId == Guid.Empty || read is null || response is null) return ProtectedReadStatus.Denied;
        if (!_capacity.Wait(0)) return ProtectedReadStatus.Unavailable;
        using var invocation = CancellationTokenSource.CreateLinkedTokenSource(ct); invocation.CancelAfter(TimeSpan.FromSeconds(10));
        using var provider = CancellationTokenSource.CreateLinkedTokenSource(invocation.Token); // five-second budget starts at U
        var invocationId = Guid.NewGuid(); Task<PrivateReadBuffer>? pendingRead = null;
        var transported = false;
        try
        {
            Binding? use;
            using (var stage = StageDeadline(invocation.Token))
            await using (var admission = await gate.AcquireReadAsync(stage.Token))
            {
                use = await Bounded(Resolve(subject, targetId, stage.Token), stage.Token, response);
                if (use is null) return ProtectedReadStatus.Denied;
                if (!await Commit(use, invocationId, AuthorityStage.Use, null, stage.Token, response)) return ProtectedReadStatus.Unavailable;
                EnsureValid(use.ValidUntil, stage.Token);
                provider.CancelAfter(TimeSpan.FromSeconds(5));
                var selection = Selection(use, invocationId);
                pendingRead = read.Admit(selection, provider.Token); // U occurs here, under admission, exactly once
                EnsureValid(use.ValidUntil, stage.Token);
            }
            var result = await Bounded(pendingRead!, provider.Token, response, true);
            if (result is null) return ProtectedReadStatus.Unavailable;
            using (var stage = StageDeadline(invocation.Token))
            await using (var admission = await gate.AcquireReadAsync(stage.Token))
            {
                var disclose = await Bounded(Resolve(subject, targetId, stage.Token), stage.Token, response);
                if (disclose is null || disclose.Digest != use!.Digest) return ProtectedReadStatus.Denied;
                var validity = disclose.ValidUntil < use.ValidUntil ? disclose.ValidUntil : use.ValidUntil;
                EnsureValid(validity, stage.Token);
                if (!await Commit(disclose, invocationId, AuthorityStage.Disclosure, result, stage.Token, response)) return ProtectedReadStatus.Unavailable;
                EnsureValid(validity, stage.Token);
                // D is acceptance by this caller-bound committer, not audit commit.
                transported = true;
                await Bounded(WriteResponse(response, Selection(disclose, invocationId), result.Bytes, stage.Token), stage.Token, response, true);
                stage.Token.ThrowIfCancellationRequested();
            }
            return ProtectedReadStatus.Completed;
        }
        catch (OperationCanceledException)
        { response.Abort(); return transported ? ProtectedReadStatus.Unknown : ct.IsCancellationRequested ? ProtectedReadStatus.Cancelled : ProtectedReadStatus.Unavailable; }
        catch
        { response.Abort(); return transported ? ProtectedReadStatus.Unknown : ProtectedReadStatus.Unavailable; }
        finally
        {
            provider.Cancel();
            if (pendingRead is { IsCompleted: false }) { gate.Close(); response.Abort(); Observe(pendingRead); }
            _capacity.Release();
        }
    }

    private async Task<Binding?> Resolve(SessionSelection selection, Guid targetId, CancellationToken ct)
    {
        var now = TrustedUtcNow();
        var subject = await subjects.ReadCurrentUnderAdmissionAsync(selection, ct);
        if (subject is null || subject.PrincipalId != selection.PrincipalId || subject.SessionId == Guid.Empty ||
            subject.BootGeneration == Guid.Empty || subject.BootGeneration != selection.BootGeneration || subject.PrincipalRevision <= 0) return null;
        var target = await targets.ReadTargetUnderAdmissionAsync(targetId, ct);
        if (target is null || target.TargetId != targetId || target.TenantId == Guid.Empty || target.ProjectId == Guid.Empty ||
            target.TargetRevision <= 0 || target.ProjectRevision <= 0 || target.AssociationRevision <= 0 ||
            (target.EnvironmentId is null ? target.EnvironmentRevision is not null || target.Classification is not null :
                target.EnvironmentId == Guid.Empty || target.EnvironmentRevision is not > 0 ||
                target.Classification is not ("Development" or "Test" or "Staging" or "Production"))) return null;
        var scope = target.EnvironmentId is null ? "project" : "environment"; var scopeId = target.EnvironmentId ?? target.ProjectId;
        var grant = await grants.ReadCurrentGrantUnderAdmissionAsync(subject.PrincipalId, target.TenantId, "target.read", scope, scopeId, ct);
        if (grant is null || grant.PrincipalId != subject.PrincipalId || grant.TenantId != target.TenantId || grant.MembershipId == Guid.Empty ||
            grant.TenantRevision <= 0 || grant.MembershipRevision <= 0 || grant.AssignmentId == Guid.Empty || grant.AssignmentRevision <= 0 ||
            grant.DefinitionId == Guid.Empty || grant.DefinitionRevision <= 0 || grant.ScopeKind != scope || grant.ScopeId != scopeId || grant.Permission != "target.read") return null;
        var definition = await definitions.GetCurrentAsync(grant.DefinitionId, grant.DefinitionRevision, ct);
        if (definition is null || definition.DefinitionId != grant.DefinitionId || definition.Revision != grant.DefinitionRevision ||
            !FoundationPermissions.Allows(definition, "target.read", scope)) return null;
        var policy = await policies.ReadPolicyUnderAdmissionAsync(target, "target.read", ct);
        if (policy is null || policy.TenantId != target.TenantId || policy.ProjectId != target.ProjectId || policy.EnvironmentId != target.EnvironmentId ||
            policy.Permission != "target.read" || policy.Decision != ResourceEligibility.Allow || policy.Revision <= 0 || policy.PolicyId == Guid.Empty ||
            policy.BindingId == Guid.Empty || policy.BindingRevision <= 0 || policy.ContentHash is not { Length: 64 } ||
            policy.ContentHash.Any(x => !char.IsAsciiHexDigitLower(x)) || policy.EffectiveFromUtc > now) return null;
        var until = new[] { subject.AbsoluteExpiresAtUtc, subject.IdleExpiresAtUtc, grant.ExpiresAtUtc ?? DateTimeOffset.MaxValue, policy.ExpiresAtUtc ?? DateTimeOffset.MaxValue }.Min();
        if (until <= now || until <= TrustedUtcNow()) return null;
        // Session activity expiry is intentionally not a security revision. Earlier U expiry still caps D.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { subject.PrincipalId, subject.SessionId, subject.BootGeneration,
            subject.PrincipalRevision, subject.AbsoluteExpiresAtUtc, Grant = grant with { ExpiresAtUtc = null }, target,
            Policy = policy with { ExpiresAtUtc = null } });
        return new(subject, grant, target, policy, Convert.ToHexStringLower(SHA256.HashData(bytes)), until);
    }

    private async Task<bool> Commit(Binding binding, Guid invocation, AuthorityStage stage, PrivateReadBuffer? buffer,
        CancellationToken ct, ICallerBoundResponseCommitter response)
    {
        var intent = new AuthorityCommitment(Guid.NewGuid(), invocation, stage, binding.Subject.PrincipalId, binding.Target.TenantId,
            binding.Target.TargetId, binding.Grant.ScopeKind, binding.Grant.ScopeId, binding.Policy.Revision, binding.Grant.AssignmentRevision,
            binding.Digest, TrustedUtcNow(), buffer is null ? null : Convert.ToHexStringLower(SHA256.HashData(buffer.Bytes.Span)), buffer?.Bytes.Length);
        AuthorityCommitStatus status;
        using var auditBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        auditBudget.CancelAfter(TimeSpan.FromSeconds(1));
        try { status = await Bounded(audit.CommitUnderAdmissionAsync(intent, auditBudget.Token), auditBudget.Token, response, true); }
        catch { gate.Close(); throw; } // opaque commit-capable port exceptions are unresolved
        if (status == AuthorityCommitStatus.Unknown) gate.Close();
        return status == AuthorityCommitStatus.Durable;
    }
    private DateTimeOffset TrustedUtcNow()
    { try { return clock.GetTrustedUtcNow(); } catch { gate.Close(); throw; } }
    private void EnsureValid(DateTimeOffset until, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); if (TrustedUtcNow() >= until) throw new OperationCanceledException("Authority expired at commitment."); }
    private static TypedReadSelection Selection(Binding b, Guid invocation) => new(invocation, b.Target.TenantId, b.Target.TargetId, b.Grant.ScopeKind, b.Grant.ScopeId);
    private static CancellationTokenSource StageDeadline(CancellationToken ct)
    { var source = CancellationTokenSource.CreateLinkedTokenSource(ct); source.CancelAfter(TimeSpan.FromSeconds(2)); return source; }
    private async Task<T> Bounded<T>(Task<T> task, CancellationToken ct, ICallerBoundResponseCommitter response, bool fenceIfUnresolved = false)
    {
        try { return await task.WaitAsync(ct); }
        catch (OperationCanceledException) when (!task.IsCompleted)
        { if (fenceIfUnresolved) gate.Close(); response.Abort(); Observe(task); throw; }
    }
    private static async Task<bool> WriteResponse(ICallerBoundResponseCommitter committer, TypedReadSelection selection, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    { await committer.CommitAsync(selection, bytes, ct); return true; }
    private static void Observe(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
