using System.Text.Json;
using Kordra.ArtifactManagement.Domain;
using Kordra.Modules.ArtifactManagement.Contracts;
using Kordra.Modules.ArtifactManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ArtifactManagement;

/// <summary>Staged owner result, never a durable commit or an authorization/disclosure permit.</summary>
public sealed record PreparedArtifactRegistration(string Status, Guid RegistrationId, Guid? ArtifactId = null);

/// <summary>
/// Host must supply current authority, fair gate and shared AUD/floor transaction. This owner
/// stages data only: no SaveChanges, commit, provider calls, secret resolution or retries.
/// </summary>
public sealed class ArtifactRegistrationStore(ArtifactDbContext db)
{
    public async Task<PreparedArtifactRegistration> PreparePendingAsync(ArtifactRegistrationReceipt pending,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        if (pending.State != ArtifactRegistrationState.Pending) throw new InvalidOperationException("A pending attempt is required.");
        var identity = pending.Request.Identity;
        var existing = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(row =>
            row.InstallationId == pending.InstallationId && row.TenantId == identity.TenantId &&
            row.ProjectId == identity.ProjectId && row.PrincipalId == pending.PrincipalId && row.KeyHash == pending.KeyHash,
            cancellationToken);
        if (existing is not null)
            return new(existing.BusinessHash != pending.BusinessHash ? "key_conflict" :
                existing.State == "Pending" ? "in_progress" : "terminal_replay", existing.Id,
                existing.BusinessHash == pending.BusinessHash ? existing.ArtifactId : null);
        db.Registrations.Add(new()
        {
            Id = pending.Id,
            InstallationId = pending.InstallationId,
            TenantId = identity.TenantId,
            ProjectId = identity.ProjectId,
            PrincipalId = pending.PrincipalId,
            BootId = pending.BootId,
            InvocationId = pending.InvocationId,
            KeyHash = pending.KeyHash,
            BusinessHash = pending.BusinessHash,
            IdentityHash = identity.ComputeIdentityHash(),
            RequestJson = ArtifactStorageJson.Request(pending.Request),
            CreatedAtUtc = pending.CreatedAtUtc
        });
        return new("staged_pending", pending.Id);
    }

    public async Task<PreparedArtifactRegistration> PrepareRegisteredAsync(ArtifactRegistrationReceipt pending,
        ArtifactVerificationFacts facts, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        // Pure value transition validates exact identity/time, not permission or durable state.
        _ = pending.Register(Guid.NewGuid(), facts.Identity, facts.CapturedAtUtc, completedAtUtc);
        var row = await CurrentPending(pending, cancellationToken);
        if (row is null) return new("attempt_conflict", pending.Id);
        var identity = facts.Identity;
        var platform = ArtifactStorageJson.Platform(identity.Platform);
        var artifact = await db.Artifacts.SingleOrDefaultAsync(a => a.TenantId == identity.TenantId &&
            a.ProjectId == identity.ProjectId && a.RegistryId == identity.RegistryId && a.Repository == identity.Repository &&
            a.RootDigest == identity.RootDigest.Value && a.PlatformJson == platform, cancellationToken);
        if (artifact is not null && !facts.Matches(artifact)) return new("integrity_conflict", pending.Id);
        if (artifact is null)
        {
            artifact = facts.NewRecord(Guid.NewGuid(), completedAtUtc);
            db.Artifacts.Add(artifact);
        }
        row.State = "Registered";
        row.ArtifactId = artifact.Id;
        row.AttemptVerifiedAtUtc = facts.CapturedAtUtc;
        row.CompletedAtUtc = completedAtUtc;
        if (pending.Request.SourceClaim is { } claim)
        {
            db.ProvenanceClaims.Add(new()
            {
                Id = Guid.NewGuid(),
                RegistrationId = row.Id,
                TenantId = row.TenantId,
                ProjectId = row.ProjectId,
                ArtifactId = artifact.Id,
                CreatedAtUtc = completedAtUtc,
                DeclarationJson = JsonSerializer.Serialize(ArtifactStorageJson.Claim(claim), ArtifactStorageJson.Options)
            });
        }
        return new("staged_registered", row.Id, artifact.Id);
    }

    public async Task<PreparedArtifactRegistration> PrepareFailedAsync(ArtifactRegistrationReceipt pending,
        ArtifactRegistrationFailure failure, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        var failed = pending.Fail(failure, completedAtUtc);
        var row = await CurrentPending(pending, cancellationToken);
        if (row is null) return new("attempt_conflict", pending.Id);
        row.State = "Failed";
        row.SafeReason = failed.SafeReason;
        row.CompletedAtUtc = failed.CompletedAtUtc;
        return new("staged_failed", row.Id);
    }

    /// <summary>Offline coordinator must separately hold custody and prove old-process quiescence/current receipts.</summary>
    public async Task<PreparedArtifactRegistration> PrepareAbandonedAsync(Guid installationId, Guid registrationId,
        Guid expectedBootId, Guid expectedInvocationId, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
    {
        RequireTransaction(offline: true);
        if (installationId == Guid.Empty || registrationId == Guid.Empty || expectedBootId == Guid.Empty || expectedInvocationId == Guid.Empty ||
            completedAtUtc.Offset != TimeSpan.Zero || completedAtUtc == default || completedAtUtc == DateTimeOffset.MaxValue)
            throw new InvalidOperationException("An exact bounded recovery selection is required.");
        var row = await db.Registrations.SingleOrDefaultAsync(r => r.InstallationId == installationId && r.Id == registrationId &&
            r.BootId == expectedBootId && r.InvocationId == expectedInvocationId && r.State == "Pending", cancellationToken);
        if (row is null || completedAtUtc < row.CreatedAtUtc) return new("attempt_conflict", registrationId);
        row.State = "Abandoned"; row.SafeReason = "interrupted_attempt"; row.CompletedAtUtc = completedAtUtc;
        return new("staged_abandoned", row.Id);
    }

    /// <summary>Neutral owner read; caller must perform current U/D before exposing the projection.</summary>
    public async Task<ArtifactRegistrationV1?> ReadOwnRegistrationAsync(Guid installationId, Guid tenantId,
        Guid projectId, Guid principalId, Guid registrationId, CancellationToken cancellationToken = default)
    {
        var row = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(r => r.InstallationId == installationId &&
            r.TenantId == tenantId && r.ProjectId == projectId && r.PrincipalId == principalId && r.Id == registrationId, cancellationToken);
        if (row is null) return null;
        var request = ArtifactStorageJson.Request(row);
        var artifact = row.ArtifactId is { } id
            ? await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId && a.ProjectId == projectId, cancellationToken)
            : null;
        if (row.State == "Registered" && artifact is null) throw new InvalidOperationException("Registered artifact identity is unavailable.");
        return new(row.Id, row.State, row.CreatedAtUtc, row.CompletedAtUtc, row.AttemptVerifiedAtUtc, row.SafeReason,
            artifact?.ToProjection(), ArtifactStorageJson.Claim(request.SourceClaim), request.SourceClaim is null ? "not_supplied" : "declared_unverified");
    }

    private Task<ArtifactRegistrationRecord?> CurrentPending(ArtifactRegistrationReceipt pending, CancellationToken cancellationToken) =>
        db.Registrations.SingleOrDefaultAsync(row => row.Id == pending.Id && row.InstallationId == pending.InstallationId &&
            row.TenantId == pending.Request.Identity.TenantId && row.ProjectId == pending.Request.Identity.ProjectId &&
            row.PrincipalId == pending.PrincipalId && row.BootId == pending.BootId && row.InvocationId == pending.InvocationId &&
            row.KeyHash == pending.KeyHash && row.BusinessHash == pending.BusinessHash && row.IdentityHash == pending.Request.Identity.ComputeIdentityHash() &&
            row.CreatedAtUtc == pending.CreatedAtUtc && row.State == "Pending", cancellationToken);

    private void RequireTransaction(bool offline = false)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("ART mutation requires the caller's shared transaction.");
        ArtifactDbContextFactory.ValidateEnlistedTarget(db.Database.GetDbConnection(), offline);
    }
}
