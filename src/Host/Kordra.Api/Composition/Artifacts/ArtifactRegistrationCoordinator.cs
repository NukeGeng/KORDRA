using System.Security.Cryptography;
using System.Text.Json;
using Kordra.ArtifactManagement.Domain;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.ArtifactManagement;
using Kordra.Modules.ArtifactManagement.Contracts;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Kordra.Modules.ArtifactManagement.Infrastructure.Registry;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.ConnectionManagement;
using Kordra.Modules.ConnectionManagement.Contracts;
using Kordra.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

/// <summary>One explicitly keyed attempt. No automatic replay, background takeover or provider call under a DB transaction.</summary>
public sealed class ArtifactRegistrationCoordinator(Guid installation, ArtifactAuthorityResolver authority,
    IDbContextFactory<ArtifactDbContext> contexts, ArtifactWriteReadiness readiness, ArtifactOwnerTransaction transactions,
    ArtifactReadCoordinator reads, FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock, CredentialBroker? broker)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class Attempt
    {
        public ArtifactAuthorityBinding? Binding;
        public ArtifactRegistrationReceipt? Receipt;
        public CredentialBroker.Reservation? Helper;
        public bool PendingDurable, MutationDurable, UnknownCommit, RetainCapacity, Denied;
        public int Consumption;
        public ArtifactRegistrationFailure Failure = ArtifactRegistrationFailure.RegistryUnavailable;
    }
    private sealed record Result(ArtifactExecutionStatus Status, ArtifactRegistrationReceipt? Replay = null);

    public async Task<ArtifactExecutionStatus> ExecuteAsync(SessionSelection subject, Guid project,
        ArtifactRegistrationRequestV1 command, string key, IArtifactResponseCommitter response, CancellationToken ct = default)
    {
        if (subject is null || project == Guid.Empty || command is null || command.SchemaVersion != 1 || command.CorrelationId == Guid.Empty ||
            command.Platform is null || response is null || key is not { Length: >= 16 and <= 128 } ||
            key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))) return ArtifactExecutionStatus.Denied;
        var capacity = ProtectedInvocationCapacity.TryReserve(gate);
        if (capacity is null) return ArtifactExecutionStatus.Unavailable;
        using var invocation = CancellationTokenSource.CreateLinkedTokenSource(ct); invocation.CancelAfter(TimeSpan.FromSeconds(10));
        var attempt = new Attempt();
        var work = Run(subject, project, command, key, response, attempt, invocation.Token);
        try
        {
            Result result;
            try { result = await work.WaitAsync(invocation.Token); }
            catch (OperationCanceledException) when (!work.IsCompleted)
            {
                // Give cancelled provider/helper and exact failure bookkeeping a bounded cleanup window.
                try { result = await work.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch { attempt.RetainCapacity = true; gate.Close(); Observe(work); response.Abort(); return ArtifactExecutionStatus.Unknown; }
            }
            if (invocation.IsCancellationRequested) response.Abort();
            if (attempt.UnknownCommit) { response.Abort(); return ArtifactExecutionStatus.Unknown; }
            if (result.Status == ArtifactExecutionStatus.Unavailable && attempt.PendingDurable && !attempt.MutationDurable &&
                !attempt.RetainCapacity && !invocation.IsCancellationRequested)
            {
                var disclose = DiscloseFailure(subject, command, attempt, response, invocation.Token);
                try { return await disclose.WaitAsync(invocation.Token); }
                catch
                {
                    if (!disclose.IsCompleted) { attempt.RetainCapacity = true; gate.Close(); Observe(disclose); }
                    response.Abort(); return ArtifactExecutionStatus.Unknown;
                }
            }
            if (result.Replay is { } replay)
            {
                capacity.Dispose(); // Replay has a fresh query U/D and never reserves a second concurrent invocation slot.
                return await reads.ExecuteAsync(subject, project, new(ArtifactQueryKind.Replay, command.CorrelationId, Replay: replay), response, invocation.Token);
            }
            if (result.Status == ArtifactExecutionStatus.Unknown) response.Abort();
            return result.Status;
        }
        catch { response.Abort(); return ArtifactExecutionStatus.Unavailable; }
        finally { if (work.IsCompleted && !attempt.RetainCapacity) capacity.Dispose(); }
    }

    private async Task<Result> Run(SessionSelection subject, Guid project, ArtifactRegistrationRequestV1 command,
        string key, IArtifactResponseCommitter response, Attempt attempt, CancellationToken ct)
    {
        try
        {
            using (var stage = Stage(ct))
            await using (var held = await gate.AcquireWriteAsync(stage.Token))
            {
                var invocation = Guid.NewGuid();
                var facts = await authority.ResolveAsync(subject, project, "artifact.register", invocation, ct: stage.Token);
                if (facts is null) return new(ArtifactExecutionStatus.Denied);
                var identity = ArtifactIdentity.Create(facts.Facts.Project.TenantId, project, command.RegistryId, command.Repository,
                    OCIImageDigest.Parse(command.Digest), ArtifactPlatform.Create(command.Platform.Os, command.Platform.Architecture, command.Platform.Variant));
                var claim = command.SourceClaim is null ? null : ArtifactSourceClaim.Create(command.SourceClaim.SourceRepository,
                    command.SourceClaim.SourceCommit, command.SourceClaim.BuildRef);
                attempt.Receipt = ArtifactRegistrationReceipt.Pending(Guid.NewGuid(), installation, subject.BootGeneration,
                    invocation, facts.Facts.Subject.PrincipalId, new(identity, claim), key, Now());
                await using (var db = await contexts.CreateDbContextAsync(stage.Token))
                    if (await new ArtifactQueryStore(db).FindOwnKeyAsync(attempt.Receipt, stage.Token) is not null)
                        return new(ArtifactExecutionStatus.Completed, attempt.Receipt);
                if (authority.Configuration is null) return new(ArtifactExecutionStatus.Unavailable);
                if (!await readiness.CheckUnderAdmissionAsync(stage.Token)) return new(ArtifactExecutionStatus.Unavailable);
                var bound = await authority.ResolveAsync(subject, project, "artifact.register", invocation, identity.RegistryId,
                    identity.Repository, ct: stage.Token);
                if (bound is null || bound.Facts.Digest != facts.Facts.Digest) return new(ArtifactExecutionStatus.Denied);
                var cap = Now().AddSeconds(5); if (bound.ValidUntilUtc < cap) cap = bound.ValidUntilUtc;
                attempt.Binding = new(bound.Facts, bound.Registry, bound.Credential, cap, cap);
                if (bound.Credential is not null)
                {
                    attempt.Helper = broker?.TryReserve();
                    if (attempt.Helper is null) return new(ArtifactExecutionStatus.Unavailable);
                }
                var use = await transactions.RunAsync(_ => Intent(attempt, command, AuditIntentStage.Use, "u"),
                    async (store, token) => await store.PreparePendingAsync(attempt.Receipt, token),
                    token => Current(subject, attempt, token), stage.Token);
                if (!Durable(use, attempt)) return new(Status(use.Status));
                attempt.PendingDurable = true;
            }

            using var provider = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var remaining = attempt.Binding!.ValidUntilUtc - Now();
            if (remaining <= TimeSpan.Zero) { attempt.Failure = ArtifactRegistrationFailure.AttemptExpired; return new(ArtifactExecutionStatus.Unavailable); }
            provider.CancelAfter(remaining);
            ArtifactVerificationFacts proof;
            if (attempt.Binding.Credential is { } credential)
            {
                var context = new RegistryCredentialUseContextV1(attempt.Receipt!.InvocationId, credential.Selection,
                    attempt.Binding.Registry!.ConfigurationDigest);
                proof = await attempt.Helper!.ReadRegistryAndUseAsync(context, credential.Digest(),
                    (operation, token) => Consume(subject, command, attempt, operation, token),
                    new RegistryConsumer(this, attempt), provider.Token);
            }
            else proof = await Consume(subject, command, attempt,
                token => Verify(attempt, ReadOnlyMemory<byte>.Empty, token), provider.Token);

            using (var stage = Stage(ct))
            await using (var held = await gate.AcquireWriteAsync(stage.Token))
            {
                if (!await Current(subject, attempt, stage.Token) || !Fresh(proof, attempt))
                { attempt.Denied = true; attempt.Failure = ArtifactRegistrationFailure.AuthorityLost; return new(ArtifactExecutionStatus.Denied); }
                var mutation = await transactions.RunAsync(prepared => Intent(attempt, command, AuditIntentStage.Mutation, "m", prepared?.ArtifactId, proof),
                    async (store, token) => await store.PrepareRegisteredAsync(attempt.Receipt!, proof, Now(), token),
                    async token => await Current(subject, attempt, token) && Fresh(proof, attempt), stage.Token);
                if (!Durable(mutation, attempt))
                {
                    if (mutation.Status == "denied") { attempt.Denied = true; attempt.Failure = ArtifactRegistrationFailure.AuthorityLost; }
                    if (mutation.Prepared?.Status == "integrity_conflict") attempt.Failure = ArtifactRegistrationFailure.IntegrityConflict;
                    return new(Status(mutation.Status));
                }
                attempt.MutationDurable = true;
            }
            return new(await Disclose(subject, command, attempt, response, ct));
        }
        catch (OperationCanceledException) { attempt.Failure = ArtifactRegistrationFailure.Cancelled; return new(ArtifactExecutionStatus.Unavailable); }
        catch
        {
            if (attempt.UnknownCommit) { response.Abort(); return new(ArtifactExecutionStatus.Unknown); }
            if (attempt.Helper?.HasUnresolvedHelper == true)
            { attempt.RetainCapacity = true; response.Abort(); return new(ArtifactExecutionStatus.Unknown); }
            return new(attempt.Denied ? ArtifactExecutionStatus.Denied : ArtifactExecutionStatus.Unavailable);
        }
        finally
        {
            attempt.Helper?.Dispose();
            if (attempt.PendingDurable && !attempt.MutationDurable && !attempt.UnknownCommit && !attempt.RetainCapacity)
                await FinishFailure(attempt, command);
        }
    }

    private async Task<ArtifactVerificationFacts> Consume(SessionSelection subject, ArtifactRegistrationRequestV1 command,
        Attempt attempt, Func<CancellationToken, Task<ArtifactVerificationFacts>> operation, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref attempt.Consumption, 1) != 0) throw new InvalidOperationException("Artifact consumption unavailable.");
        using (var stage = Stage(ct))
        await using (var held = await gate.AcquireReadAsync(stage.Token))
        {
            if (!await Current(subject, attempt, stage.Token))
            { attempt.Denied = true; attempt.Failure = ArtifactRegistrationFailure.AuthorityLost; throw new InvalidOperationException("Artifact authority changed."); }
            var consumption = await transactions.RunAsync(_ => Intent(attempt, command, AuditIntentStage.Use, "c"),
                (_, _) => Task.FromResult<PreparedArtifactRegistration?>(null), token => Current(subject, attempt, token), stage.Token);
            if (!Durable(consumption, attempt))
            {
                if (consumption.Status == "denied") { attempt.Denied = true; attempt.Failure = ArtifactRegistrationFailure.AuthorityLost; }
                throw new InvalidOperationException("Artifact consumption unavailable.");
            }
            EnsureValid(attempt, stage.Token);
        }
        // No provider I/O while holding admission or an owner transaction.
        ct.ThrowIfCancellationRequested();
        return await operation(ct);
    }

    private async Task<ArtifactVerificationFacts> Verify(Attempt attempt, ReadOnlyMemory<byte> credential, CancellationToken ct)
    {
        try { return await new ArtifactRegistryVerifier(attempt.Binding!.Registry!, Now).VerifyAsync(attempt.Receipt!.Request.Identity, credential, ct); }
        catch (ArtifactRegistryUnavailableException) { attempt.Failure = ArtifactRegistrationFailure.RegistryUnavailable; throw; }
        catch (InvalidDataException) { attempt.Failure = ArtifactRegistrationFailure.UnsupportedImage; throw; }
    }
    private sealed class RegistryConsumer(ArtifactRegistrationCoordinator owner, Attempt attempt) : RegistryCredentialConsumer<ArtifactVerificationFacts>
    {
        public override string Id => "artifact.registry.read.v1";
        public override Task<ArtifactVerificationFacts> ReadAsync(ReadOnlyMemory<byte> bytes, RegistryCredentialUseContextV1 context, CancellationToken ct)
        {
            if (context.InvocationId != attempt.Receipt!.InvocationId || context.Selection != attempt.Binding!.Credential!.Selection ||
                context.ConfigurationDigest != attempt.Binding.Registry!.ConfigurationDigest) throw new InvalidOperationException("Artifact consumer context unavailable.");
            return owner.Verify(attempt, bytes, ct);
        }
    }

    private async Task<ArtifactExecutionStatus> Disclose(SessionSelection subject, ArtifactRegistrationRequestV1 command,
        Attempt attempt, IArtifactResponseCommitter response, CancellationToken ct)
    {
        using var stage = Stage(ct);
        await using var held = await gate.AcquireReadAsync(stage.Token);
        if (!await Current(subject, attempt, stage.Token)) return ArtifactExecutionStatus.Denied;
        await using var db = await contexts.CreateDbContextAsync(stage.Token);
        var receipt = attempt.Receipt!; var identity = receipt.Request.Identity;
        var registration = await new ArtifactRegistrationStore(db).ReadOwnRegistrationAsync(installation, identity.TenantId,
            identity.ProjectId, receipt.PrincipalId, receipt.Id, stage.Token);
        if (registration is null || registration.Status != "Registered") return ArtifactExecutionStatus.Unavailable;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, correlationId = command.CorrelationId, registration }, Json);
        if (bytes.Length > 16384) return ArtifactExecutionStatus.Unavailable;
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var disclosure = await transactions.RunAsync(_ => Intent(attempt, command, AuditIntentStage.Disclosure, "d",
                registration.Artifact?.ArtifactId) with
        { ResultDigest = hash, ResultSizeBytes = bytes.Length },
            (_, _) => Task.FromResult<PreparedArtifactRegistration?>(null), token => Current(subject, attempt, token), stage.Token);
        if (!Durable(disclosure, attempt)) return Status(disclosure.Status);
        EnsureValid(attempt, stage.Token);
        try
        {
            await response.CommitAsync(new(receipt.InvocationId, identity.ProjectId, hash, 201, attempt.Binding!.ValidUntilUtc), bytes, stage.Token);
            return ArtifactExecutionStatus.Completed;
        }
        catch { response.Abort(); return ArtifactExecutionStatus.Unknown; }
    }

    private async Task<ArtifactExecutionStatus> DiscloseFailure(SessionSelection subject, ArtifactRegistrationRequestV1 command,
        Attempt attempt, IArtifactResponseCommitter response, CancellationToken ct)
    {
        using var stage = Stage(ct); await using var held = await gate.AcquireReadAsync(stage.Token);
        if (!await Current(subject, attempt, stage.Token)) return ArtifactExecutionStatus.Denied;
        var unsupported = attempt.Failure == ArtifactRegistrationFailure.UnsupportedImage;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            correlationId = command.CorrelationId,
            code = unsupported ? "unsupported_image" : "registry_unavailable",
            registration = (ArtifactRegistrationV1?)null
        }, Json);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var disclosure = await transactions.RunAsync(_ => Intent(attempt, command, AuditIntentStage.Disclosure, "failure-d") with
        { ResultDigest = hash, ResultSizeBytes = bytes.Length }, (_, _) => Task.FromResult<PreparedArtifactRegistration?>(null),
            token => Current(subject, attempt, token), stage.Token);
        if (!Durable(disclosure, attempt))
        { if (attempt.UnknownCommit) response.Abort(); return Status(disclosure.Status); }
        EnsureValid(attempt, stage.Token);
        try
        {
            await response.CommitAsync(new(attempt.Receipt!.InvocationId, attempt.Receipt.Request.Identity.ProjectId,
                hash, unsupported ? 422 : 503, attempt.Binding!.ValidUntilUtc), bytes, stage.Token);
            return ArtifactExecutionStatus.Completed;
        }
        catch { response.Abort(); return ArtifactExecutionStatus.Unknown; }
    }

    private async Task FinishFailure(Attempt attempt, ArtifactRegistrationRequestV1 command)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await using var held = await gate.AcquireWriteAsync(cleanup.Token);
            var outcome = await transactions.RunAsync(_ => Intent(attempt, command, AuditIntentStage.Outcome, "failed") with
            { ActionOutcome = attempt.Failure == ArtifactRegistrationFailure.Cancelled ? AuditActionOutcome.Cancelled : AuditActionOutcome.Denied },
                async (store, token) => await store.PrepareFailedAsync(attempt.Receipt!, attempt.Failure, Now(), token),
                token => Task.FromResult(!token.IsCancellationRequested), cleanup.Token);
            _ = Durable(outcome, attempt); // Failure to terminalize leaves Pending; never assert a guessed terminal result.
        }
        catch { } // Exact authoritative history is reconciled only offline; no retry/takeover/provider replay.
    }
    private async Task<bool> Current(SessionSelection subject, Attempt attempt, CancellationToken ct)
    {
        var old = attempt.Binding!; var current = await authority.RecheckAsync(subject, old, ct);
        if (current is null || current.Digest != old.Digest) return false;
        if (current.ValidUntilUtc < old.ValidUntilUtc)
            attempt.Binding = new(old.Facts, old.Registry, old.Credential, old.OriginalUntilUtc, current.ValidUntilUtc);
        return Now() < attempt.Binding!.ValidUntilUtc;
    }
    private bool Fresh(ArtifactVerificationFacts proof, Attempt attempt) => proof.Identity == attempt.Receipt!.Request.Identity &&
        proof.CapturedAtUtc >= attempt.Receipt.CreatedAtUtc && proof.CapturedAtUtc <= Now() && Now() < proof.CapturedAtUtc.AddSeconds(15);
    private static bool Durable(ArtifactOwnerCommit result, Attempt attempt)
    {
        if (result.Status == "unknown") { attempt.UnknownCommit = true; attempt.RetainCapacity = true; }
        return result.Status == "durable";
    }
    private AuditIntent Intent(Attempt attempt, ArtifactRegistrationRequestV1 command, AuditIntentStage stage, string phase,
        Guid? artifact = null, ArtifactVerificationFacts? proof = null)
    {
        var receipt = attempt.Receipt!; var binding = attempt.Binding!;
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            phase,
            registrationId = receipt.Id,
            artifactId = artifact,
            receipt.BusinessHash,
            authorityDigest = binding.Digest,
            selectedDigest = proof?.SelectedManifestDigest.Value,
            configDigest = proof?.ConfigDigest.Value,
            attemptVerifiedAtUtc = proof?.CapturedAtUtc,
            failure = phase == "failed" ? attempt.Failure.ToString() : null
        }, Json)));
        return new(1, Guid.NewGuid(), receipt.InvocationId, stage, receipt.PrincipalId, receipt.Request.Identity.TenantId,
            artifact ?? receipt.Id, "artifact.register", "project", receipt.Request.Identity.ProjectId, null,
            binding.Facts.Grant.AssignmentRevision, "artifact-" + phase + "-" + digest, Now(), command.CorrelationId);
    }
    private void EnsureValid(Attempt attempt, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); if (Now() >= attempt.Binding!.ValidUntilUtc) throw new OperationCanceledException("Artifact authority expired."); }
    private DateTimeOffset Now()
    { var now = clock.GetTrustedUtcNow(); if (now.Offset != TimeSpan.Zero || now == default || now == DateTimeOffset.MaxValue) throw new InvalidOperationException("Trusted artifact clock unavailable."); return now; }
    private static ArtifactExecutionStatus Status(string status) => status switch
    { "denied" => ArtifactExecutionStatus.Denied, "unknown" => ArtifactExecutionStatus.Unknown, _ => ArtifactExecutionStatus.Unavailable };
    private static CancellationTokenSource Stage(CancellationToken ct)
    { var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(2)); return budget; }
    private static void Observe(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
