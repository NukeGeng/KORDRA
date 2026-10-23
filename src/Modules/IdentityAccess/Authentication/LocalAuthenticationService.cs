using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kordra.Modules.IdentityAccess.Authentication;

/// <summary>IAM-owned local authentication and self-session behavior for the operator pilot.</summary>
public sealed partial class LocalAuthenticationService : Kordra.Modules.IdentityAccess.Contracts.IAuthorityClock, Kordra.Modules.IdentityAccess.Contracts.ICurrentSubjectReader, Kordra.Modules.IdentityAccess.Contracts.IAuthorityCommitter
{
    private static readonly TimeSpan RequestDeadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionWindow = TimeSpan.FromMinutes(1);
    private const int FailedAttemptsBeforeLockout = 5;

    private readonly IDbContextFactory<KordraIdentityDbContext> _contexts;
    private readonly IIdentityAuditTransactionFactory _auditTransactions;
    private readonly IPasswordHasher<KordraIdentityUser> _passwordHasher;
    private readonly LocalPasswordPolicy? _passwordPolicy;
    private readonly LocalAuthenticationConfiguration _configuration;
    private readonly FairAsyncReaderWriterAdmissionGate _admission;
    private readonly TimeProvider _timeProvider;
    private readonly LoginRateLimiter _rateLimiter;
    private readonly SemaphoreSlim _hashSlots = new(2, 2);
    private readonly KordraIdentityUser _dummyUser = new() { UserName = "kordra-dummy" };
    private readonly string _dummyPasswordHash;
    private readonly TrustedUtcClock _clock;

    public LocalAuthenticationService(
        IDbContextFactory<KordraIdentityDbContext> contexts,
        IIdentityAuditTransactionFactory auditTransactions,
        IPasswordHasher<KordraIdentityUser> passwordHasher,
        LocalAuthenticationConfiguration configuration,
        FairAsyncReaderWriterAdmissionGate admission,
        TimeProvider? timeProvider = null,
        LocalPasswordPolicy? passwordPolicy = null)
    {
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _auditTransactions = auditTransactions ?? throw new ArgumentNullException(nameof(auditTransactions));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        if (configuration.InstallationId == Guid.Empty || configuration.BootGeneration == Guid.Empty ||
            configuration.PasswordHashIterations < 220_000 || configuration.MaximumActiveSessions != 5 ||
            configuration.AbsoluteLifetime != TimeSpan.FromHours(8) ||
            configuration.IdleLifetime != TimeSpan.FromMinutes(30))
        {
            throw new ArgumentException("Local authentication configuration must match the accepted v1 bounds.", nameof(configuration));
        }

        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _passwordPolicy = passwordPolicy;
        _rateLimiter = new LoginRateLimiter(_timeProvider);
        _clock = new TrustedUtcClock(_timeProvider);
        _dummyPasswordHash = _passwordHasher.HashPassword(
            _dummyUser,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    /// <summary>Must complete before the explicit local-pilot HTTPS listener starts accepting requests.</summary>
    public async Task InitializeBootAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await _admission.AcquireWriteAsync(cancellationToken);
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken);
        var persistedFloor = await db.AuthorityClockFloors.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == _configuration.InstallationId, cancellationToken);
        if (persistedFloor is not null)
        {
            try { _clock.AdoptPersistedFloor(persistedFloor.AdmittedUtc); }
            catch { _admission.Close(); throw; }
        }
        var now = GetTrustedUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var intent = CreateIntent(
            AuditIntentStage.Mutation,
            subjectId: null,
            scopeKind: "instance",
            scopeId: _configuration.InstallationId,
            permission: "iam.startup",
            safeReason: "startup-session-invalidation",
            authorityRevision: null,
            now);

        var outcome = await CommitWithAuditAsync(db, transaction, intent, async token =>
        {
            await db.Sessions
                .Where(session => !session.IsRevoked)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(session => session.IsRevoked, true)
                    .SetProperty(session => session.RevokedAtUtc, now)
                    .SetProperty(session => session.RevocationReason, "api-restart"), token);
            await db.SaveChangesAsync(token);
        }, cancellationToken);

        if (outcome.Status != AuditAppendStatus.Durable)
        {
            throw new InvalidOperationException("Local-pilot startup could not durably invalidate prior sessions and audit the new boot generation.");
        }
    }

    public async Task<LocalLoginResult> LoginAsync(
        string? userName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeUserName(userName, out var normalizedUserName) || !IsPasswordShapeValid(password))
        {
            return new LocalLoginResult(LocalAuthenticationStatus.InvalidRequest);
        }

        if (!_rateLimiter.TryConsume(normalizedUserName))
        {
            return new LocalLoginResult(LocalAuthenticationStatus.RateLimited);
        }

        if (!_hashSlots.Wait(0))
        {
            return new LocalLoginResult(LocalAuthenticationStatus.RateLimited);
        }

        try
        {
            using var requestDeadline = CreateRequestDeadline(cancellationToken);
            var token = requestDeadline.Token;
            var now = GetTrustedUtcNow();
            await using var readDb = await _contexts.CreateDbContextAsync(token);
            var snapshot = await readDb.Principals.AsNoTracking()
                .SingleOrDefaultAsync(user => user.NormalizedUserName == normalizedUserName, token);
            var candidate = snapshot ?? _dummyUser;
            var encodedHash = snapshot?.PasswordHash ?? _dummyPasswordHash;
            var verification = _passwordHasher.VerifyHashedPassword(candidate, encodedHash, password!);
            var locked = snapshot?.LockoutEnd is { } lockoutEnd && lockoutEnd > now;
            var active = snapshot is not null && string.Equals(snapshot.AccountStatus, "active", StringComparison.Ordinal);

            if (snapshot is null || snapshot.PasswordHash is null ||
                verification == PasswordVerificationResult.Failed || !active || locked)
            {
                var requiresFailureMutation = snapshot is not null && active && !locked && verification == PasswordVerificationResult.Failed;
                var failure = requiresFailureMutation
                    ? await RecordFailedLoginAsync(snapshot!, now, token)
                    : await RecordCredentialDenialAsync(snapshot?.Id, now, token);
                return !requiresFailureMutation || failure == AuditAppendStatus.Durable
                    ? new LocalLoginResult(LocalAuthenticationStatus.InvalidCredentials)
                    : new LocalLoginResult(LocalAuthenticationStatus.AuthorityUnavailable);
            }

            var replacementHash = verification == PasswordVerificationResult.SuccessRehashNeeded
                ? _passwordHasher.HashPassword(snapshot, password!)
                : null;
            return await CommitLoginAsync(snapshot, replacementHash, now, token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new LocalLoginResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        catch (ClockTrustException)
        {
            return new LocalLoginResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        catch
        {
            return new LocalLoginResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        finally
        {
            _hashSlots.Release();
        }
    }

    public async Task<LocalIdentityResult> ResolveCurrentSessionAsync(
        Guid principalId,
        string? sessionReference,
        Guid bootGeneration,
        CancellationToken cancellationToken = default)
    {
        await using var admission = await AdmitCurrentSessionAsync(
            principalId, sessionReference, bootGeneration, cancellationToken);
        return admission.Result;
    }

    /// <summary>
    /// Resolves and refreshes the current session under the shared admission gate. The caller
    /// may retain the returned lease through completion of a protected self-response.
    /// </summary>
    public async Task<LocalSessionAdmission> AdmitCurrentSessionAsync(
        Guid principalId,
        string? sessionReference,
        Guid bootGeneration,
        CancellationToken cancellationToken = default)
    {
        if (principalId == Guid.Empty || bootGeneration != _configuration.BootGeneration ||
            !TryDecodeSessionReference(sessionReference, out var reference))
        {
            return new LocalSessionAdmission(new LocalIdentityResult(LocalAuthenticationStatus.SessionInvalid));
        }

        FairAsyncReaderWriterAdmissionGate.AdmissionLease? lease = null;
        byte[]? sessionHash = null;
        try
        {
            using var requestDeadline = CreateRequestDeadline(cancellationToken);
            lease = await _admission.AcquireReadAsync(requestDeadline.Token);
            var now = GetTrustedUtcNow();
            sessionHash = SHA256.HashData(reference);
            await using var db = await _contexts.CreateDbContextAsync(requestDeadline.Token);
            var session = await db.Sessions.AsNoTracking()
                .Where(value => value.PrincipalId == principalId && value.BootGeneration == bootGeneration)
                .Select(value => new
                {
                    value.SessionId,
                    value.SessionReferenceHash,
                    value.PrincipalId,
                    value.PrincipalRevision,
                    value.BootGeneration,
                    value.LastActivityAtUtc,
                    value.AbsoluteExpiresAtUtc,
                    value.IsRevoked,
                    CurrentPrincipalRevision = value.Principal!.Revision,
                    PrincipalStatus = value.Principal.AccountStatus,
                    PrincipalName = value.Principal.UserName
                })
                .SingleOrDefaultAsync(value => value.SessionReferenceHash == sessionHash, requestDeadline.Token);

            if (session is null || session.IsRevoked || session.PrincipalStatus != "active" ||
                session.CurrentPrincipalRevision != session.PrincipalRevision ||
                session.AbsoluteExpiresAtUtc <= now || session.LastActivityAtUtc + _configuration.IdleLifetime <= now ||
                !CryptographicOperations.FixedTimeEquals(session.SessionReferenceHash, sessionHash))
            {
                await lease.DisposeAsync();
                lease = null;
                return new LocalSessionAdmission(new LocalIdentityResult(LocalAuthenticationStatus.SessionInvalid));
            }

            var changed = await db.Sessions
                .Where(value => value.SessionId == session.SessionId && !value.IsRevoked &&
                    value.PrincipalRevision == session.PrincipalRevision && value.BootGeneration == bootGeneration &&
                    value.AbsoluteExpiresAtUtc > now && value.LastActivityAtUtc + _configuration.IdleLifetime > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    value => value.LastActivityAtUtc,
                    value => value.LastActivityAtUtc < now ? now : value.LastActivityAtUtc), requestDeadline.Token);
            if (changed != 1)
            {
                await lease.DisposeAsync();
                lease = null;
                return new LocalSessionAdmission(new LocalIdentityResult(LocalAuthenticationStatus.SessionInvalid));
            }

            var result = new LocalIdentityResult(
                LocalAuthenticationStatus.Success, session.PrincipalId, session.PrincipalName, session.AbsoluteExpiresAtUtc);
            var retainedLease = lease;
            lease = null;
            return new LocalSessionAdmission(result, retainedLease);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ClockTrustException)
        {
            return new LocalSessionAdmission(new LocalIdentityResult(LocalAuthenticationStatus.AuthorityUnavailable));
        }
        catch
        {
            return new LocalSessionAdmission(new LocalIdentityResult(LocalAuthenticationStatus.AuthorityUnavailable));
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
            }

            CryptographicOperations.ZeroMemory(reference);
            if (sessionHash is not null)
            {
                CryptographicOperations.ZeroMemory(sessionHash);
            }
        }
    }

    public async Task<LocalCommandResult> LogoutAsync(
        Guid principalId,
        string? sessionReference,
        Guid bootGeneration,
        CancellationToken cancellationToken = default)
    {
        if (principalId == Guid.Empty || bootGeneration != _configuration.BootGeneration ||
            !TryDecodeSessionReference(sessionReference, out var reference))
        {
            return new LocalCommandResult(LocalAuthenticationStatus.SessionInvalid);
        }

        byte[]? sessionHash = null;
        try
        {
            using var requestDeadline = CreateRequestDeadline(cancellationToken);
            await using var lease = await _admission.AcquireWriteAsync(requestDeadline.Token);
            var now = GetTrustedUtcNow();
            sessionHash = SHA256.HashData(reference);
            await using var db = await _contexts.CreateDbContextAsync(requestDeadline.Token);
            await using var transaction = await db.Database.BeginTransactionAsync(requestDeadline.Token);
            var session = await db.Sessions.Include(value => value.Principal)
                .SingleOrDefaultAsync(value => value.PrincipalId == principalId &&
                    value.BootGeneration == bootGeneration && value.SessionReferenceHash == sessionHash,
                    requestDeadline.Token);
            if (!IsCurrentSession(session, principalId, bootGeneration, now))
            {
                return new LocalCommandResult(LocalAuthenticationStatus.SessionInvalid);
            }

            session!.IsRevoked = true;
            session.RevokedAtUtc = now;
            session.RevocationReason = "logout";
            var intent = CreateIntent(AuditIntentStage.Mutation, principalId, "principal", principalId,
                "iam.logout", "logout", session.PrincipalRevision, now);
            var outcome = await CommitWithAuditAsync(db, transaction, intent,
                token => db.SaveChangesAsync(token), requestDeadline.Token);
            return outcome.Status == AuditAppendStatus.Durable
                ? new LocalCommandResult(LocalAuthenticationStatus.Success, principalId, intent.CorrelationId)
                : new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(reference);
            if (sessionHash is not null)
            {
                CryptographicOperations.ZeroMemory(sessionHash);
            }
        }
    }

    public async Task<LocalCommandResult> LogoutAllAsync(
        Guid principalId,
        string? sessionReference,
        Guid bootGeneration,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (principalId == Guid.Empty || bootGeneration != _configuration.BootGeneration ||
            !TryHashIdempotencyKey(idempotencyKey, out var idempotencyHash))
        {
            return new LocalCommandResult(LocalAuthenticationStatus.InvalidRequest);
        }

        if (!TryDecodeSessionReference(sessionReference, out var reference))
        {
            CryptographicOperations.ZeroMemory(idempotencyHash);
            return new LocalCommandResult(LocalAuthenticationStatus.InvalidRequest);
        }

        byte[]? sessionHash = null;
        try
        {
            using var requestDeadline = CreateRequestDeadline(cancellationToken);
            await using var lease = await _admission.AcquireWriteAsync(requestDeadline.Token);
            var now = GetTrustedUtcNow();
            sessionHash = SHA256.HashData(reference);
            await using var db = await _contexts.CreateDbContextAsync(requestDeadline.Token);
            await using var transaction = await db.Database.BeginTransactionAsync(requestDeadline.Token);
            var current = await db.Sessions.Include(value => value.Principal)
                .SingleOrDefaultAsync(value => value.PrincipalId == principalId &&
                    value.BootGeneration == bootGeneration && value.SessionReferenceHash == sessionHash,
                    requestDeadline.Token);
            if (!IsCurrentSession(current, principalId, bootGeneration, now))
            {
                return new LocalCommandResult(LocalAuthenticationStatus.SessionInvalid);
            }

            var previous = await FindCommandReplayAsync(db, idempotencyHash, principalId, "logout-all", requestDeadline.Token);
            if (previous is not null)
            {
                return previous;
            }

            db.AuthenticationCommands.Add(new KordraAuthenticationCommand
            {
                IdempotencyKeyHash = idempotencyHash,
                PrincipalId = principalId,
                CommandKind = "logout-all",
                CreatedAtUtc = now
            });
            await RevokePrincipalSessionsAsync(db, principalId, now, "logout-all", requestDeadline.Token);

            var intent = CreateIntent(AuditIntentStage.Mutation, principalId, "principal", principalId,
                "iam.logout-all", "logout-all", current!.PrincipalRevision, now);
            var outcome = await CommitWithAuditAsync(db, transaction, intent,
                token => db.SaveChangesAsync(token), requestDeadline.Token);
            return outcome.Status == AuditAppendStatus.Durable
                ? new LocalCommandResult(LocalAuthenticationStatus.Success, principalId, intent.CorrelationId)
                : new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        catch (DbUpdateException)
        {
            return new LocalCommandResult(LocalAuthenticationStatus.Conflict);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(reference);
            CryptographicOperations.ZeroMemory(sessionHash ?? []);
            CryptographicOperations.ZeroMemory(idempotencyHash);
        }
    }

    public async Task<LocalCommandResult> ChangePasswordAsync(
        Guid principalId,
        string? sessionReference,
        Guid bootGeneration,
        string? currentPassword,
        string? newPassword,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (principalId == Guid.Empty || bootGeneration != _configuration.BootGeneration ||
            !IsPasswordShapeValid(currentPassword) || !IsPasswordAcceptable(newPassword) ||
            !TryHashIdempotencyKey(idempotencyKey, out var idempotencyHash))
        {
            return new LocalCommandResult(LocalAuthenticationStatus.InvalidRequest);
        }

        if (!TryDecodeSessionReference(sessionReference, out var reference))
        {
            CryptographicOperations.ZeroMemory(idempotencyHash);
            return new LocalCommandResult(LocalAuthenticationStatus.InvalidRequest);
        }

        if (!_hashSlots.Wait(0))
        {
            CryptographicOperations.ZeroMemory(reference);
            CryptographicOperations.ZeroMemory(idempotencyHash);
            return new LocalCommandResult(LocalAuthenticationStatus.RateLimited);
        }

        byte[]? sessionHash = null;
        try
        {
            using var requestDeadline = CreateRequestDeadline(cancellationToken);
            var token = requestDeadline.Token;
            sessionHash = SHA256.HashData(reference);
            // Recovery still requires a fresh valid self session, but not the superseded password.
            await using (var recoveryLease = await _admission.AcquireReadAsync(token))
            await using (var recoveryDb = await _contexts.CreateDbContextAsync(token))
            {
                var recoverySession = await recoveryDb.Sessions.Include(value => value.Principal)
                    .SingleOrDefaultAsync(value => value.PrincipalId == principalId &&
                        value.BootGeneration == bootGeneration && value.SessionReferenceHash == sessionHash, token);
                if (!IsCurrentSession(recoverySession, principalId, bootGeneration, GetTrustedUtcNow()))
                {
                    return new LocalCommandResult(LocalAuthenticationStatus.SessionInvalid);
                }
                var replay = await FindCommandReplayAsync(recoveryDb, idempotencyHash, principalId, "password-change", token);
                if (replay is not null)
                {
                    return replay;
                }
            }
            await using var initialDb = await _contexts.CreateDbContextAsync(token);
            var snapshot = await initialDb.Principals.AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == principalId, token);
            if (snapshot is null || snapshot.PasswordHash is null || snapshot.AccountStatus != "active" ||
                _passwordHasher.VerifyHashedPassword(snapshot, snapshot.PasswordHash, currentPassword!) == PasswordVerificationResult.Failed)
            {
                return new LocalCommandResult(LocalAuthenticationStatus.InvalidCredentials);
            }

            var replacementHash = _passwordHasher.HashPassword(snapshot, newPassword!);
            await using var lease = await _admission.AcquireWriteAsync(token);
            var now = GetTrustedUtcNow();
            await using var db = await _contexts.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var user = await db.Principals.SingleOrDefaultAsync(value => value.Id == principalId, token);
            var session = await db.Sessions.Include(value => value.Principal)
                .SingleOrDefaultAsync(value => value.PrincipalId == principalId &&
                    value.BootGeneration == bootGeneration && value.SessionReferenceHash == sessionHash, token);
            if (user is null || user.AccountStatus != "active" || user.Revision != snapshot.Revision ||
                user.SecurityStamp != snapshot.SecurityStamp || user.PasswordHash != snapshot.PasswordHash ||
                !IsCurrentSession(session, principalId, bootGeneration, now))
            {
                return new LocalCommandResult(LocalAuthenticationStatus.Conflict);
            }

            var previous = await FindCommandReplayAsync(db, idempotencyHash, principalId, "password-change", token);
            if (previous is not null)
            {
                return previous;
            }

            user.PasswordHash = replacementHash;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            user.Revision++;
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            db.AuthenticationCommands.Add(new KordraAuthenticationCommand
            {
                IdempotencyKeyHash = idempotencyHash,
                PrincipalId = principalId,
                CommandKind = "password-change",
                CreatedAtUtc = now
            });
            await RevokePrincipalSessionsAsync(db, principalId, now, "password-change", token);

            var intent = CreateIntent(AuditIntentStage.Mutation, principalId, "principal", principalId,
                "iam.password-change", "password-change", user.Revision, now);
            var outcome = await CommitWithAuditAsync(db, transaction, intent,
                ct => db.SaveChangesAsync(ct), token);
            return outcome.Status == AuditAppendStatus.Durable
                ? new LocalCommandResult(LocalAuthenticationStatus.Success, principalId, intent.CorrelationId)
                : new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        catch (DbUpdateException)
        {
            return new LocalCommandResult(LocalAuthenticationStatus.Conflict);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        finally
        {
            _hashSlots.Release();
            CryptographicOperations.ZeroMemory(reference);
            CryptographicOperations.ZeroMemory(idempotencyHash);
            if (sessionHash is not null)
            {
                CryptographicOperations.ZeroMemory(sessionHash);
            }
        }
    }

    public async Task<LocalCommandResult> EnrollOfflineAsync(
        string? userName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeUserName(userName, out var normalizedUserName) || !IsPasswordAcceptable(password))
        {
            return new LocalCommandResult(LocalAuthenticationStatus.InvalidRequest);
        }

        if (!_hashSlots.Wait(0))
        {
            return new LocalCommandResult(LocalAuthenticationStatus.RateLimited);
        }

        try
        {
            using var requestDeadline = CreateRequestDeadline(cancellationToken);
            var now = GetTrustedUtcNow();
            var principal = new KordraIdentityUser
            {
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                AccountStatus = "active",
                Revision = 1,
                LockoutEnabled = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N")
            };
            principal.PasswordHash = _passwordHasher.HashPassword(principal, password!);

            await using var lease = await _admission.AcquireWriteAsync(requestDeadline.Token);
            await using var db = await _contexts.CreateDbContextAsync(requestDeadline.Token);
            await using var transaction = await db.Database.BeginTransactionAsync(requestDeadline.Token);
            if (await db.Principals.AnyAsync(user => user.NormalizedUserName == normalizedUserName, requestDeadline.Token))
            {
                return new LocalCommandResult(LocalAuthenticationStatus.Conflict);
            }

            db.Principals.Add(principal);
            var intent = CreateIntent(AuditIntentStage.Mutation, principal.Id, "principal", principal.Id,
                "iam.enroll-user", "offline-enrollment", principal.Revision, now);
            var outcome = await CommitWithAuditAsync(db, transaction, intent,
                token => db.SaveChangesAsync(token), requestDeadline.Token);
            return outcome.Status == AuditAppendStatus.Durable
                ? new LocalCommandResult(LocalAuthenticationStatus.Success, principal.Id, intent.CorrelationId)
                : new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        catch (DbUpdateException)
        {
            return new LocalCommandResult(LocalAuthenticationStatus.Conflict);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        finally
        {
            _hashSlots.Release();
        }
    }

    public async Task<LocalCommandResult> RecoverOfflineAsync(
        Guid principalId,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (principalId == Guid.Empty || !IsPasswordAcceptable(password))
        {
            return new LocalCommandResult(LocalAuthenticationStatus.InvalidRequest);
        }

        if (!_hashSlots.Wait(0))
        {
            return new LocalCommandResult(LocalAuthenticationStatus.RateLimited);
        }

        try
        {
            using var requestDeadline = CreateRequestDeadline(cancellationToken);
            await using var initialDb = await _contexts.CreateDbContextAsync(requestDeadline.Token);
            var snapshot = await initialDb.Principals.AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == principalId, requestDeadline.Token);
            if (snapshot is null)
            {
                return new LocalCommandResult(LocalAuthenticationStatus.InvalidRequest);
            }

            var replacementHash = _passwordHasher.HashPassword(snapshot, password!);
            await using var lease = await _admission.AcquireWriteAsync(requestDeadline.Token);
            var now = GetTrustedUtcNow();
            await using var db = await _contexts.CreateDbContextAsync(requestDeadline.Token);
            await using var transaction = await db.Database.BeginTransactionAsync(requestDeadline.Token);
            var user = await db.Principals.SingleOrDefaultAsync(value => value.Id == principalId, requestDeadline.Token);
            if (user is null || user.Revision != snapshot.Revision || user.SecurityStamp != snapshot.SecurityStamp)
            {
                return new LocalCommandResult(LocalAuthenticationStatus.Conflict);
            }

            user.PasswordHash = replacementHash;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            user.Revision++;
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            await RevokePrincipalSessionsAsync(db, principalId, now, "offline-recovery", requestDeadline.Token);
            var intent = CreateIntent(AuditIntentStage.Mutation, principalId, "principal", principalId,
                "iam.recover-user", "offline-recovery", user.Revision, now);
            var outcome = await CommitWithAuditAsync(db, transaction, intent,
                token => db.SaveChangesAsync(token), requestDeadline.Token);
            return outcome.Status == AuditAppendStatus.Durable
                ? new LocalCommandResult(LocalAuthenticationStatus.Success, principalId, intent.CorrelationId)
                : new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        catch (DbUpdateException)
        {
            return new LocalCommandResult(LocalAuthenticationStatus.Conflict);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        finally
        {
            _hashSlots.Release();
        }
    }

    public async Task<LocalCommandResult> DisableOfflineAsync(
        Guid principalId,
        CancellationToken cancellationToken = default)
    {
        if (principalId == Guid.Empty)
        {
            return new LocalCommandResult(LocalAuthenticationStatus.InvalidRequest);
        }

        try
        {
            using var requestDeadline = CreateRequestDeadline(cancellationToken);
            await using var lease = await _admission.AcquireWriteAsync(requestDeadline.Token);
            var now = GetTrustedUtcNow();
            await using var db = await _contexts.CreateDbContextAsync(requestDeadline.Token);
            await using var transaction = await db.Database.BeginTransactionAsync(requestDeadline.Token);
            var user = await db.Principals.SingleOrDefaultAsync(value => value.Id == principalId, requestDeadline.Token);
            if (user is null)
            {
                return new LocalCommandResult(LocalAuthenticationStatus.InvalidRequest);
            }

            if (user.AccountStatus != "disabled")
            {
                user.AccountStatus = "disabled";
                user.SecurityStamp = Guid.NewGuid().ToString("N");
                user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                user.Revision++;
            }

            await RevokePrincipalSessionsAsync(db, principalId, now, "offline-disable", requestDeadline.Token);
            var intent = CreateIntent(AuditIntentStage.Mutation, principalId, "principal", principalId,
                "iam.disable-user", "offline-disable", user.Revision, now);
            var outcome = await CommitWithAuditAsync(db, transaction, intent,
                token => db.SaveChangesAsync(token), requestDeadline.Token);
            return outcome.Status == AuditAppendStatus.Durable
                ? new LocalCommandResult(LocalAuthenticationStatus.Success, principalId, intent.CorrelationId)
                : new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new LocalCommandResult(LocalAuthenticationStatus.AuthorityUnavailable);
        }
    }

    private async Task<LocalLoginResult> CommitLoginAsync(
        KordraIdentityUser snapshot,
        string? replacementHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var lease = await _admission.AcquireWriteAsync(cancellationToken);
        now = GetTrustedUtcNow();
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await db.Principals.SingleOrDefaultAsync(value => value.Id == snapshot.Id, cancellationToken);
        if (user is null || user.AccountStatus != "active" ||
            user.Revision != snapshot.Revision || user.SecurityStamp != snapshot.SecurityStamp ||
            user.ConcurrencyStamp != snapshot.ConcurrencyStamp || user.PasswordHash != snapshot.PasswordHash ||
            user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
        {
            return new LocalLoginResult(LocalAuthenticationStatus.InvalidCredentials);
        }

        var activeSessions = await db.Sessions.CountAsync(session => session.PrincipalId == user.Id &&
            !session.IsRevoked && session.BootGeneration == _configuration.BootGeneration &&
            session.AbsoluteExpiresAtUtc > now && session.LastActivityAtUtc + _configuration.IdleLifetime > now,
            cancellationToken);
        if (activeSessions >= _configuration.MaximumActiveSessions)
        {
            return new LocalLoginResult(LocalAuthenticationStatus.SessionLimitReached);
        }

        var rawReference = RandomNumberGenerator.GetBytes(32);
        try
        {
            var session = new KordraSession
            {
                SessionReferenceHash = SHA256.HashData(rawReference),
                PrincipalId = user.Id,
                PrincipalRevision = user.Revision,
                BootGeneration = _configuration.BootGeneration,
                CreatedAtUtc = now,
                LastActivityAtUtc = now,
                AbsoluteExpiresAtUtc = now + _configuration.AbsoluteLifetime
            };
            db.Sessions.Add(session);
            if (replacementHash is not null)
            {
                user.PasswordHash = replacementHash;
            }

            if (user.AccessFailedCount != 0 || user.LockoutEnd is not null || replacementHash is not null)
            {
                user.AccessFailedCount = 0;
                user.LockoutEnd = null;
                user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            }

            var intent = CreateIntent(AuditIntentStage.Mutation, user.Id, "principal", user.Id,
                "iam.authenticate", "login-succeeded", user.Revision, now);
            var outcome = await CommitWithAuditAsync(db, transaction, intent,
                token => db.SaveChangesAsync(token), cancellationToken);
            if (outcome.Status != AuditAppendStatus.Durable)
            {
                return new LocalLoginResult(LocalAuthenticationStatus.AuthorityUnavailable);
            }

            var encodedReference = Convert.ToBase64String(rawReference).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return new LocalLoginResult(
                LocalAuthenticationStatus.Success,
                user.Id,
                user.UserName,
                encodedReference,
                _configuration.BootGeneration,
                session.AbsoluteExpiresAtUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawReference);
        }
    }

    private async Task<AuditAppendStatus> RecordFailedLoginAsync(
        KordraIdentityUser snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var lease = await _admission.AcquireWriteAsync(cancellationToken);
        now = GetTrustedUtcNow();
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await db.Principals.SingleOrDefaultAsync(value => value.Id == snapshot.Id, cancellationToken);
        if (user is null || user.Revision != snapshot.Revision || user.SecurityStamp != snapshot.SecurityStamp ||
            user.AccountStatus != "active")
        {
            return AuditAppendStatus.Durable;
        }

        if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
        {
            return AuditAppendStatus.Durable;
        }

        if (++user.AccessFailedCount >= FailedAttemptsBeforeLockout)
        {
            user.AccessFailedCount = 0;
            user.LockoutEnd = now + LockoutDuration;
        }

        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        var intent = CreateIntent(AuditIntentStage.Outcome, user.Id, "principal", user.Id,
            "iam.authenticate", "login-denied", user.Revision, now, AuditActionOutcome.Denied);
        var outcome = await CommitWithAuditAsync(db, transaction, intent,
            token => db.SaveChangesAsync(token), cancellationToken);
        return outcome.Status;
    }

    private async Task<AuditAppendStatus> RecordCredentialDenialAsync(
        Guid? principalId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMilliseconds(100));
        cancellationToken = budget.Token;
        try
        {
            await using var lease = await _admission.AcquireWriteAsync(cancellationToken);
            await using var db = await _contexts.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var intent = CreateIntent(AuditIntentStage.Outcome, principalId, "instance", _configuration.InstallationId,
                "iam.authenticate", "login-denied", authorityRevision: null, now, AuditActionOutcome.Denied);
            var outcome = await CommitWithAuditAsync(db, transaction, intent, _ => Task.CompletedTask, cancellationToken);
            return outcome.Status;
        }
        catch
        {
            return AuditAppendStatus.Unavailable;
        }
    }

    private static async Task<LocalCommandResult?> FindCommandReplayAsync(
        KordraIdentityDbContext db, byte[] keyHash, Guid principalId, string commandKind,
        CancellationToken cancellationToken)
    {
        var previous = await db.AuthenticationCommands.AsNoTracking()
            .SingleOrDefaultAsync(command => command.IdempotencyKeyHash == keyHash, cancellationToken);
        if (previous is null) return null;
        return previous.PrincipalId == principalId && previous.CommandKind == commandKind
            ? new LocalCommandResult(LocalAuthenticationStatus.Success, principalId, previous.CommandId)
            : new LocalCommandResult(LocalAuthenticationStatus.Conflict);
    }

    private static async Task RevokePrincipalSessionsAsync(
        KordraIdentityDbContext db,
        Guid principalId,
        DateTimeOffset now,
        string reason,
        CancellationToken cancellationToken)
    {
        await db.Sessions.Where(session => session.PrincipalId == principalId && !session.IsRevoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.IsRevoked, true)
                .SetProperty(session => session.RevokedAtUtc, now)
                .SetProperty(session => session.RevocationReason, reason), cancellationToken);
    }

    private async Task<AuditAppendOutcome> CommitWithAuditAsync(
        KordraIdentityDbContext db,
        IDbContextTransaction transaction,
        AuditIntent intent,
        Func<CancellationToken, Task> saveOwnerMutationAsync,
        CancellationToken cancellationToken, Action? protocolStarted = null)
    {
        // Enlistment only prepares the audit context: no owner mutation or commit can
        // have started if setup fails, including cancellation of best-effort denial audit.
        await using var audit = await _auditTransactions.EnlistAsync(
            db.Database.GetDbConnection(), transaction.GetDbTransaction(), cancellationToken);
        protocolStarted?.Invoke();
        try
        {
            var outcome = await audit.CommitWithOwnerMutationAsync(intent, async token =>
            {
                await saveOwnerMutationAsync(token);
                await PersistClockFloorAsync(db, GetTrustedUtcNow(), token);
            }, cancellationToken);
            if (outcome.Status == AuditAppendStatus.Unknown)
            {
                _admission.Close();
            }
            return outcome;
        }
        catch
        {
            // After protocol handoff an exception cannot establish the commit outcome.
            _admission.Close();
            throw;
        }
    }

    private AuditIntent CreateIntent(
        AuditIntentStage stage,
        Guid? subjectId,
        string scopeKind,
        Guid scopeId,
        string permission,
        string safeReason,
        long? authorityRevision,
        DateTimeOffset now,
        AuditActionOutcome? actionOutcome = null)
    {
        var correlationId = Guid.NewGuid();
        return new AuditIntent(
            SchemaVersion: 1,
            IntentId: Guid.NewGuid(),
            InvocationId: correlationId,
            Stage: stage,
            SubjectRef: subjectId,
            TenantRef: null,
            ResourceRef: null,
            Permission: permission,
            ScopeKind: scopeKind,
            ScopeId: scopeId,
            PolicyRevision: null,
            AuthorityRevision: authorityRevision,
            SafeReason: safeReason,
            OccurredAtUtc: now,
            CorrelationId: correlationId,
            ActionOutcome: actionOutcome);
    }

    public DateTimeOffset GetTrustedUtcNow()
    {
        try { return _clock.GetUtcNow(); }
        catch { _admission.Close(); throw; }
    }

    private static CancellationTokenSource CreateRequestDeadline(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestDeadline);
        return deadline;
    }

    private bool IsPasswordAcceptable(string? password) => _passwordPolicy?.IsAcceptable(password) == true;

    private static bool IsPasswordShapeValid(string? password)
    {
        if (password is null || Encoding.UTF8.GetByteCount(password) > 512)
        {
            return false;
        }

        var remaining = password.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out _, out var consumed);
            if (status != System.Buffers.OperationStatus.Done)
            {
                return false;
            }

            count++;
            remaining = remaining[consumed..];
        }

        return count is >= 15 and <= 128;
    }

    public static bool TryNormalizeUserName(string? userName, out string normalized)
    {
        normalized = string.Empty;
        if (userName is null || userName.Length is < 3 or > 64 ||
            userName.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            return false;
        }

        normalized = userName.ToUpperInvariant();
        return true;
    }

    public static bool TryHashIdempotencyKey(string? value, out byte[] hash)
    {
        hash = [];
        if (value is null || value.Length is < 16 or > 128 ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '~' or '-')))
        {
            return false;
        }

        hash = SHA256.HashData(Encoding.ASCII.GetBytes(value));
        return true;
    }

    private static bool TryDecodeSessionReference(string? value, out byte[] reference)
    {
        reference = [];
        if (value is null || value.Length != 43 || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            return false;
        }

        try
        {
            var encoded = value.Replace('-', '+').Replace('_', '/') + "=";
            reference = Convert.FromBase64String(encoded);
            if (reference.Length == 32)
            {
                return true;
            }

            CryptographicOperations.ZeroMemory(reference);
            reference = [];
            return false;
        }
        catch (FormatException)
        {
            reference = [];
            return false;
        }
    }

    private static bool IsCurrentSession(
        KordraSession? session,
        Guid principalId,
        Guid bootGeneration,
        DateTimeOffset now) =>
        session is not null && !session.IsRevoked && session.Principal is { AccountStatus: "active" } principal &&
        principal.Id == principalId && principal.Revision == session.PrincipalRevision &&
        session.BootGeneration == bootGeneration && session.AbsoluteExpiresAtUtc > now &&
        session.LastActivityAtUtc + TimeSpan.FromMinutes(30) > now;

    private sealed class LoginRateLimiter(TimeProvider timeProvider)
    {
        private const int MaximumBuckets = 1024;
        private readonly object _sync = new();
        private readonly Dictionary<string, Window> _byUser = new(StringComparer.Ordinal);
        private long _globalStart;
        private int _globalCount;

        public bool TryConsume(string normalizedUserName)
        {
            lock (_sync)
            {
                var timestamp = timeProvider.GetTimestamp();
                if (_globalCount == 0 || timeProvider.GetElapsedTime(_globalStart, timestamp) >= SessionWindow)
                {
                    _globalStart = timestamp;
                    _globalCount = 0;
                }

                foreach (var expired in _byUser.Where(pair =>
                             timeProvider.GetElapsedTime(pair.Value.Start, timestamp) >= SessionWindow)
                         .Select(pair => pair.Key).ToArray())
                {
                    _byUser.Remove(expired);
                }

                if (!_byUser.TryGetValue(normalizedUserName, out var window))
                {
                    if (_byUser.Count >= MaximumBuckets)
                    {
                        return false;
                    }

                    window = new Window(timestamp, 0);
                    _byUser.Add(normalizedUserName, window);
                }

                if (_globalCount >= 20 || window.Count >= 5)
                {
                    return false;
                }

                _globalCount++;
                _byUser[normalizedUserName] = window with { Count = window.Count + 1 };
                return true;
            }
        }

        private sealed record Window(long Start, int Count);
    }

    private sealed class TrustedUtcClock(TimeProvider timeProvider)
    {
        private readonly object _sync = new();
        private DateTimeOffset? _lastUtc;
        private long _lastTimestamp;

        public void AdoptPersistedFloor(DateTimeOffset floor)
        {
            var current = GetUtcNow();
            if (floor > current + TimeSpan.FromSeconds(1)) throw new ClockTrustException();
            lock (_sync) { if (floor > _lastUtc) _lastUtc = floor; }
        }

        public DateTimeOffset GetUtcNow()
        {
            var observed = timeProvider.GetUtcNow().ToUniversalTime();
            var timestamp = timeProvider.GetTimestamp();
            lock (_sync)
            {
                if (_lastUtc is { } previous)
                {
                    var monotonicElapsed = timeProvider.GetElapsedTime(_lastTimestamp, timestamp);
                    var wallElapsed = observed - previous;
                    if ((wallElapsed - monotonicElapsed) < TimeSpan.FromSeconds(-1) ||
                        (wallElapsed - monotonicElapsed) > TimeSpan.FromSeconds(1))
                    {
                        throw new ClockTrustException();
                    }

                    if (observed < previous)
                    {
                        observed = previous;
                    }
                }

                _lastUtc = observed;
                _lastTimestamp = timestamp;
                return observed;
            }
        }
    }

    private sealed class ClockTrustException : Exception
    {
    }
}

public static class LocalAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddLocalAuthenticationServices(
        this IServiceCollection services,
        string runtimeConnectionString,
        LocalAuthenticationConfiguration configuration,
        LocalPasswordPolicy passwordPolicy)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(passwordPolicy);
        IdentityAccessDbContextFactory.ValidateRuntimeTarget(runtimeConnectionString);

        services.AddDbContextFactory<KordraIdentityDbContext>(options =>
            options.UseNpgsql(runtimeConnectionString, postgres => postgres
                .MigrationsHistoryTable(
                    KordraIdentityDbContext.MigrationHistoryTableName,
                    KordraIdentityDbContext.SchemaName)
                .CommandTimeout(2)));
        services.AddSingleton(configuration);
        services.AddSingleton(passwordPolicy);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<FairAsyncReaderWriterAdmissionGate>();
        services.AddSingleton<IPasswordHasher<KordraIdentityUser>>(_ =>
            new PasswordHasher<KordraIdentityUser>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                IterationCount = configuration.PasswordHashIterations
            })));
        services.AddSingleton<LocalAuthenticationService>();
        return services;
    }
}
