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
    public DateTimeOffset? DisclosureValidUntilUtc { get; }
    public PrivateReadBuffer(ReadOnlySpan<byte> bytes, int records) : this(bytes, records, null) { }
    public PrivateReadBuffer(ReadOnlySpan<byte> bytes, int records, DateTimeOffset? disclosureValidUntilUtc)
    {
        if (bytes.Length > 256 * 1024 || records is < 0 or > 200) throw new InvalidDataException("Protected result exceeds the accepted bound.");
        if (disclosureValidUntilUtc is { } cap && (cap.Offset != TimeSpan.Zero || cap == default || cap == DateTimeOffset.MaxValue))
            throw new InvalidDataException("Protected result requires a finite UTC disclosure deadline.");
        DisclosureValidUntilUtc = disclosureValidUntilUtc;
        _bytes = bytes.ToArray(); Records = records;
    }
}
public interface IProtectedTargetRead
{
    // Trusted, nonblocking synchronous admission of this immutable selection; no later retargeting.
    Task<PrivateReadBuffer> Admit(TypedReadSelection selection, CancellationToken ct);
}
/// <summary>One invocation-bound C commitment; no reusable credential permit.</summary>
public interface IProtectedUseBoundary
{
    Task<PrivateReadBuffer> RunAsync(Func<CancellationToken, Task<PrivateReadBuffer>> operation, CancellationToken ct);
}
public interface IProtectedCredentialRead
{
    ProtectedReadPlan Plan { get; }
    IProtectedCredentialReadReservation? TryReserve();
}
public interface IProtectedCredentialReadReservation : IDisposable
{
    Task<PrivateReadBuffer> Admit(TypedReadSelection selection, ReadDependencySnapshot dependency, IProtectedUseBoundary use, CancellationToken ct);
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
    IAuthorityClock clock, IProtectedReadDependencyReader? dependencies = null)
{
    private readonly FairAsyncReaderWriterAdmissionGate _gate = gate;
    private sealed record Binding(CurrentSubject Subject, ScopedGrant Grant, TargetAuthority Target,
        ResourcePolicyAuthority Policy, string Digest, DateTimeOffset ValidUntil, ReadDependencySnapshot? Dependency);

    public Task<ProtectedReadStatus> ExecuteAsync(SessionSelection subject, Guid targetId,
        IProtectedTargetRead read, ICallerBoundResponseCommitter response, CancellationToken ct = default) =>
        ExecuteCoreAsync(subject, targetId, read, null, ProtectedReadPlan.Metadata, response, ct);
    public Task<ProtectedReadStatus> ExecuteCredentialAsync(SessionSelection subject, Guid targetId,
        IProtectedCredentialRead read, ICallerBoundResponseCommitter response, CancellationToken ct = default) =>
        read is null ? Task.FromResult(ProtectedReadStatus.Denied) : ExecuteCoreAsync(subject, targetId, null, read, read.Plan, response, ct);
    private async Task<ProtectedReadStatus> ExecuteCoreAsync(SessionSelection subject, Guid targetId,
        IProtectedTargetRead? read, IProtectedCredentialRead? credentialRead, ProtectedReadPlan plan,
        ICallerBoundResponseCommitter response, CancellationToken ct)
    {
        if (subject is null || targetId == Guid.Empty || read is null && credentialRead is null || response is null || !plan.IsValid ||
            credentialRead is not null && !plan.IsCredential) return ProtectedReadStatus.Denied;
        using var capacity = ProtectedInvocationCapacity.TryReserve(_gate);
        if (capacity is null) return ProtectedReadStatus.Unavailable;
        using var invocation = CancellationTokenSource.CreateLinkedTokenSource(ct); invocation.CancelAfter(TimeSpan.FromSeconds(10));
        using var provider = CancellationTokenSource.CreateLinkedTokenSource(invocation.Token); // five-second budget starts at U
        var invocationId = Guid.NewGuid(); Task<PrivateReadBuffer>? pendingRead = null;
        var transported = false; UseBoundary? boundary = null;
        IProtectedCredentialReadReservation? reservation = null;
        try
        {
            reservation = credentialRead?.TryReserve();
            if (credentialRead is not null && reservation is null) return ProtectedReadStatus.Unavailable;
            Binding? use;
            using (var stage = StageDeadline(invocation.Token))
            await using (var admission = await _gate.AcquireReadAsync(stage.Token))
            {
                use = await Bounded(Resolve(subject, targetId, plan, stage.Token), stage.Token, response);
                if (use is null) return ProtectedReadStatus.Denied;
                if (!await Commit(use, invocationId, AuthorityStage.Use, null, stage.Token, response)) return ProtectedReadStatus.Unavailable;
                EnsureValid(use.ValidUntil, stage.Token);
                provider.CancelAfter(TimeSpan.FromSeconds(5));
                var selection = Selection(use, invocationId);
                if (credentialRead is null) pendingRead = read!.Admit(selection, provider.Token);
                else
                {
                    boundary = new UseBoundary(this, use, subject, targetId, plan, invocationId, response, provider.Token);
                    pendingRead = reservation!.Admit(selection, use.Dependency!, boundary, provider.Token);
                } // U occurs here, under admission, exactly once
                EnsureValid(use.ValidUntil, stage.Token);
            }
            var result = await Bounded(pendingRead!, provider.Token, response, true);
            if (result is null || credentialRead is not null && boundary?.Completed != true) return ProtectedReadStatus.Unavailable;
            using (var stage = StageDeadline(invocation.Token))
            await using (var admission = await _gate.AcquireReadAsync(stage.Token))
            {
                var disclose = await Bounded(Resolve(subject, targetId, plan, stage.Token), stage.Token, response);
                if (disclose is null || disclose.Digest != use!.Digest) return ProtectedReadStatus.Denied;
                var validity = disclose.ValidUntil < use.ValidUntil ? disclose.ValidUntil : use.ValidUntil;
                if (result.DisclosureValidUntilUtc is { } cap && cap < validity) validity = cap;
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
            if (pendingRead is { IsCompleted: false }) { _gate.Close(); response.Abort(); Observe(pendingRead); }
            boundary?.Stop();
            reservation?.Dispose();
        }
    }

    private async Task<Binding?> Resolve(SessionSelection selection, Guid targetId, ProtectedReadPlan plan, CancellationToken ct)
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
        ReadDependencySnapshot? dependency = null;
        if (plan.IsCredential)
        {
            if (dependencies is null) return null;
            var requested = new ReadDependencySelection(plan, target.TenantId, target.TargetId,
                plan.ConsumerConfigurationDigest is null ? null : scope,
                plan.ConsumerConfigurationDigest is null ? null : scopeId);
            dependency = await dependencies.ReadCurrentUnderAdmissionAsync(requested, ct);
            if (dependency is null || dependency.Kind != "credential-binding" || dependency.Selection != requested ||
                dependency.BindingId == Guid.Empty || dependency.Revision <= 0 || dependency.Digest.Length != 64 ||
                dependency.Digest.Any(x => !char.IsAsciiHexDigitLower(x)) || dependency.NotBeforeUtc > now ||
                dependency.ExpiresAtUtc <= now || dependency.NotBeforeUtc.Offset != TimeSpan.Zero || dependency.ExpiresAtUtc.Offset != TimeSpan.Zero) return null;
            if (dependency.ExpiresAtUtc < until) until = dependency.ExpiresAtUtc;
        }
        if (until <= now || until <= TrustedUtcNow()) return null;
        // Session activity expiry is intentionally not a security revision. Earlier U expiry still caps D.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { subject.PrincipalId, subject.SessionId, subject.BootGeneration,
            subject.PrincipalRevision, subject.AbsoluteExpiresAtUtc, Grant = grant with { ExpiresAtUtc = null }, target,
            Policy = policy with { ExpiresAtUtc = null } });
        if (dependency is not null)
        {
            // Pin historical v2 field order/shape explicitly: additive record properties must not change old hashes.
            var legacyPlan = new { plan.Kind, plan.InstallationId, plan.ConsumerId, plan.IsCredential, plan.IsValid };
            var legacyDependency = new { dependency.Kind,
                Selection = new { Plan = legacyPlan, dependency.Selection.TenantId, dependency.Selection.TargetId },
                dependency.BindingId, dependency.Revision, dependency.Digest, dependency.NotBeforeUtc, dependency.ExpiresAtUtc };
            var authorityDigest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            bytes = plan.ConsumerConfigurationDigest is null
                ? JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 2, authorityDigest, plan = legacyPlan, dependency = legacyDependency })
                : JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 3, authorityDigest, plan = legacyPlan,
                    plan.ConsumerConfigurationDigest, dependency = legacyDependency, dependency.Selection.ScopeKind, dependency.Selection.ScopeId });
        }
        return new(subject, grant, target, policy, Convert.ToHexStringLower(SHA256.HashData(bytes)), until, dependency);
    }

    private async Task<bool> Commit(Binding binding, Guid invocation, AuthorityStage stage, PrivateReadBuffer? buffer,
        CancellationToken ct, ICallerBoundResponseCommitter response, AuthorityCommitPurpose purpose = AuthorityCommitPurpose.ReadAdmission)
    {
        var intent = new AuthorityCommitment(Guid.NewGuid(), invocation, stage, binding.Subject.PrincipalId, binding.Target.TenantId,
            binding.Target.TargetId, binding.Grant.ScopeKind, binding.Grant.ScopeId, binding.Policy.Revision, binding.Grant.AssignmentRevision,
            binding.Digest, TrustedUtcNow(), buffer is null ? null : Convert.ToHexStringLower(SHA256.HashData(buffer.Bytes.Span)), buffer?.Bytes.Length, purpose);
        AuthorityCommitStatus status;
        using var auditBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        auditBudget.CancelAfter(TimeSpan.FromSeconds(1));
        try { status = await Bounded(audit.CommitUnderAdmissionAsync(intent, auditBudget.Token), auditBudget.Token, response, true); }
        catch { _gate.Close(); throw; } // opaque commit-capable port exceptions are unresolved
        if (status == AuthorityCommitStatus.Unknown) _gate.Close();
        return status == AuthorityCommitStatus.Durable;
    }
    private sealed class UseBoundary(ProtectedReadAdmission owner, Binding original, SessionSelection subject, Guid target,
        ProtectedReadPlan plan, Guid invocation, ICallerBoundResponseCommitter response, CancellationToken provider) : IProtectedUseBoundary
    {
        private int _attempted, _stopped;
        private Task<PrivateReadBuffer>? _pending;
        public bool Completed { get; private set; }
        public async Task<PrivateReadBuffer> RunAsync(Func<CancellationToken, Task<PrivateReadBuffer>> operation, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _attempted, 1) != 0 || Volatile.Read(ref _stopped) != 0) throw new InvalidOperationException("Credential use is unavailable.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(provider, ct);
            using (var stage = StageDeadline(linked.Token))
            await using (var held = await owner._gate.AcquireReadAsync(stage.Token))
            {
                var current = await owner.Bounded(owner.Resolve(subject, target, plan, stage.Token), stage.Token, response);
                if (current is null || current.Digest != original.Digest) throw new InvalidOperationException("Credential use is unavailable.");
                var until = current.ValidUntil < original.ValidUntil ? current.ValidUntil : original.ValidUntil;
                owner.EnsureValid(until, stage.Token);
                if (!await owner.Commit(current, invocation, AuthorityStage.Use, null, stage.Token, response, AuthorityCommitPurpose.CredentialConsumption))
                    throw new InvalidOperationException("Credential use is unavailable.");
                owner.EnsureValid(until, stage.Token);
                if (Volatile.Read(ref _stopped) != 0) throw new InvalidOperationException("Credential use is unavailable.");
                _pending = operation(linked.Token); // C: exact consumer admission, under the common fence.
                owner.EnsureValid(until, stage.Token);
            }
            var result = await owner.Bounded(_pending!, linked.Token, response, true);
            Completed = true; return result;
        }
        public void Stop()
        {
            Interlocked.Exchange(ref _stopped, 1);
            if (_pending is { IsCompleted: false }) { owner._gate.Close(); response.Abort(); Observe(_pending); }
        }
    }
    private DateTimeOffset TrustedUtcNow()
    { try { return clock.GetTrustedUtcNow(); } catch { _gate.Close(); throw; } }
    private void EnsureValid(DateTimeOffset until, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); if (TrustedUtcNow() >= until) throw new OperationCanceledException("Authority expired at commitment."); }
    private static TypedReadSelection Selection(Binding b, Guid invocation) => new(invocation, b.Target.TenantId, b.Target.TargetId, b.Grant.ScopeKind, b.Grant.ScopeId);
    private static CancellationTokenSource StageDeadline(CancellationToken ct)
    { var source = CancellationTokenSource.CreateLinkedTokenSource(ct); source.CancelAfter(TimeSpan.FromSeconds(2)); return source; }
    private async Task<T> Bounded<T>(Task<T> task, CancellationToken ct, ICallerBoundResponseCommitter response, bool fenceIfUnresolved = false)
    {
        try { return await task.WaitAsync(ct); }
        catch (OperationCanceledException) when (!task.IsCompleted)
        { if (fenceIfUnresolved) _gate.Close(); response.Abort(); Observe(task); throw; }
    }
    private static async Task<bool> WriteResponse(ICallerBoundResponseCommitter committer, TypedReadSelection selection, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    { await committer.CommitAsync(selection, bytes, ct); return true; }
    private static void Observe(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
