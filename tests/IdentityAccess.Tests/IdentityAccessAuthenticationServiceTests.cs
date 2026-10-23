using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Net;
using System.Text.Json;
using Kordra.Api;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

[CollectionDefinition("IdentityAccess PostgreSQL and local HTTPS", DisableParallelization = true)]
public sealed class IdentityAccessPostgresCollection { }

[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class IdentityAccessAuthenticationServiceTests : IAsyncLifetime
{
    private LocalAuthenticationTestHost _host = null!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Denial_audit_setup_failure_does_not_close_existing_or_future_admissions(bool cancellation)
    {
        var factory = new FaultAuditFactory();
        await _host.DisposeAsync();
        _host = await LocalAuthenticationTestHost.StartAsync(factory);
        var name = _host.NewUserName();
        await _host.EnrollAsync(name);
        using var authenticated = _host.CreateClient();
        using var login = await _host.LoginAsync(authenticated, name, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var previousCommits = factory.ProtocolCalls;
        factory.FailNextSetup = cancellation ? "cancel" : "exception";
        using var anonymous = _host.CreateClient();
        using var denied = await _host.LoginAsync(anonymous, _host.NewUserName(), LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal("AUTHENTICATION_FAILED", await ReadErrorCodeAsync(denied));
        Assert.Equal(previousCommits, factory.ProtocolCalls);
        using var me = await authenticated.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var nextLogin = await _host.LoginAsync(anonymous, name, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, nextLogin.StatusCode);
    }

    [Fact]
    public async Task Concurrent_session_refresh_cannot_replace_newer_activity_with_an_older_timestamp()
    {
        var barrier = new ActivityUpdateBarrier();
        await _host.DisposeAsync();
        _host = await LocalAuthenticationTestHost.StartAsync(commandInterceptor: barrier);
        var name = _host.NewUserName();
        await _host.EnrollAsync(name);
        var login = await _host.Authentication.LoginAsync(name, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(LocalAuthenticationStatus.Success, login.Status);
        barrier.Armed = true;
        var older = _host.Authentication.ResolveCurrentSessionAsync(login.PrincipalId!.Value, login.SessionReference, login.BootGeneration!.Value);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            _host.Clock.Advance(TimeSpan.FromMilliseconds(500));
            var newer = await _host.Authentication.ResolveCurrentSessionAsync(login.PrincipalId.Value, login.SessionReference, login.BootGeneration.Value);
            Assert.Equal(LocalAuthenticationStatus.Success, newer.Status);
            await using var db = await _host.CreateIdentityContextAsync();
            var activity = await db.Sessions.AsNoTracking().Where(x => x.PrincipalId == login.PrincipalId).Select(x => x.LastActivityAtUtc).SingleAsync();
            barrier.Release.TrySetResult();
            Assert.Equal(LocalAuthenticationStatus.Success, (await older).Status);
            Assert.Equal(activity, await db.Sessions.AsNoTracking().Where(x => x.PrincipalId == login.PrincipalId).Select(x => x.LastActivityAtUtc).SingleAsync());
        }
        finally { barrier.Release.TrySetResult(); }
    }

    [Theory]
    [InlineData("logout-all")]
    [InlineData("password-change")]
    [InlineData("logout")]
    [InlineData("offline-disable")]
    [InlineData("throw-after-commit")]
    public async Task Committed_mutation_with_lost_ack_closes_all_new_authority_until_restart(string kind)
    {
        var factory = new FaultAuditFactory();
        await _host.DisposeAsync();
        _host = await LocalAuthenticationTestHost.StartAsync(factory);
        var name = _host.NewUserName();
        await _host.EnrollAsync(name);
        var login = await _host.Authentication.LoginAsync(name, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(LocalAuthenticationStatus.Success, login.Status);
        factory.LoseNextMutationAck = true;
        factory.ThrowAfterCommit = kind == "throw-after-commit";
        var principal = login.PrincipalId!.Value;
        var boot = login.BootGeneration!.Value;
        var result = kind switch
        {
            "logout-all" => await _host.Authentication.LogoutAllAsync(principal, login.SessionReference, boot, "ack-loss-key-0123456789"),
            "password-change" => await _host.Authentication.ChangePasswordAsync(principal, login.SessionReference, boot, LocalAuthenticationTestHost.ValidPassword, "Replacement-Ackloss-Passphrase-123!", "ack-loss-key-0123456789"),
            "logout" => await _host.Authentication.LogoutAsync(principal, login.SessionReference, boot),
            _ => await _host.Authentication.DisableOfflineAsync(principal)
        };
        Assert.Equal(LocalAuthenticationStatus.AuthorityUnavailable, result.Status);
        await using (var db = await _host.CreateIdentityContextAsync())
            Assert.False(await db.Sessions.AnyAsync(x => x.PrincipalId == principal && !x.IsRevoked));
        Assert.Equal(LocalAuthenticationStatus.AuthorityUnavailable,
            (await _host.Authentication.ResolveCurrentSessionAsync(principal, login.SessionReference, boot)).Status);
        Assert.Equal(LocalAuthenticationStatus.AuthorityUnavailable, (await _host.Authentication.EnrollOfflineAsync(_host.NewUserName(), LocalAuthenticationTestHost.ValidPassword)).Status);
        await _host.RestartAsync();
        Assert.Equal(LocalAuthenticationStatus.Success, (await _host.EnrollAsync(_host.NewUserName())).Status);
    }

    [Fact]
    public async Task Denial_audit_timeout_is_best_effort_and_does_not_hold_the_writer_for_the_request_deadline()
    {
        var factory = new FaultAuditFactory { SlowDenial = true };
        await _host.DisposeAsync();
        _host = await LocalAuthenticationTestHost.StartAsync(factory);
        var result = await _host.Authentication.LoginAsync(_host.NewUserName(), LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(LocalAuthenticationStatus.InvalidCredentials, result.Status);
        Assert.InRange(factory.DenialWait.TotalMilliseconds, 50, 500);
        var name = _host.NewUserName();
        Assert.Equal(LocalAuthenticationStatus.Success, (await _host.EnrollAsync(name)).Status);
        Assert.Equal(LocalAuthenticationStatus.Success, (await _host.Authentication.LoginAsync(name, LocalAuthenticationTestHost.ValidPassword)).Status);
    }

    [Fact]
    public async Task Command_key_cannot_be_rebound_to_another_subject_or_action()
    {
        const string key = "same-key-conflict-0123456789";
        var firstName = _host.NewUserName();
        var otherName = _host.NewUserName();
        await _host.EnrollAsync(firstName);
        await _host.EnrollAsync(otherName);
        using var first = _host.CreateClient();
        using var other = _host.CreateClient();
        using var login = await _host.LoginAsync(first, firstName, LocalAuthenticationTestHost.ValidPassword);
        using var command = await _host.PostEmptyCsrfAsync(first, "/api/v1/auth/logout-all", key);
        Assert.Equal(HttpStatusCode.NoContent, command.StatusCode);
        using var otherLogin = await _host.LoginAsync(other, otherName, LocalAuthenticationTestHost.ValidPassword);
        using var foreignReplay = await _host.PostEmptyCsrfAsync(other, "/api/v1/auth/logout-all", key);
        Assert.Equal(HttpStatusCode.Conflict, foreignReplay.StatusCode);
        using var relogin = await _host.LoginAsync(first, firstName, LocalAuthenticationTestHost.ValidPassword);
        using var actionReplay = await _host.ChangePasswordAsync(first, LocalAuthenticationTestHost.ValidPassword, "Different-Action-Passphrase-123!", key);
        Assert.Equal(HttpStatusCode.Conflict, actionReplay.StatusCode);
        using var stillValid = await first.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Successful_command_replay_after_reauthentication_does_not_mutate_again(bool passwordChange, bool restart)
    {
        var name = _host.NewUserName();
        var enrolled = await _host.EnrollAsync(name);
        using var client = _host.CreateClient();
        using var login = await _host.LoginAsync(client, name, LocalAuthenticationTestHost.ValidPassword);
        const string key = "replay-command-0123456789";
        const string replacement = "Synthetic-Replacement-Passphrase-123!";
        using var first = passwordChange
            ? await _host.ChangePasswordAsync(client, LocalAuthenticationTestHost.ValidPassword, replacement, key)
            : await _host.PostEmptyCsrfAsync(client, "/api/v1/auth/logout-all", key);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using var unauthenticatedReplay = passwordChange
            ? await _host.ChangePasswordAsync(client, LocalAuthenticationTestHost.ValidPassword, replacement, key)
            : await _host.PostEmptyCsrfAsync(client, "/api/v1/auth/logout-all", key);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedReplay.StatusCode);
        if (restart) await _host.RestartAsync();
        using var relogin = await _host.LoginAsync(client, name, passwordChange ? replacement : LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        await using var db = await _host.CreateIdentityContextAsync();
        var revision = await db.Principals.AsNoTracking().Where(x => x.Id == enrolled.PrincipalId).Select(x => x.Revision).SingleAsync();
        using var replay = passwordChange
            ? await _host.ChangePasswordAsync(client, LocalAuthenticationTestHost.ValidPassword, replacement, key)
            : await _host.PostEmptyCsrfAsync(client, "/api/v1/auth/logout-all", key);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal(revision, await db.Principals.AsNoTracking().Where(x => x.Id == enrolled.PrincipalId).Select(x => x.Revision).SingleAsync());
        Assert.Equal(1, await db.AuthenticationCommands.CountAsync(x => x.PrincipalId == enrolled.PrincipalId));
        Assert.Equal(1, await db.Sessions.CountAsync(x => x.PrincipalId == enrolled.PrincipalId && !x.IsRevoked));
        await using var audit = await _host.CreateAuditContextAsync();
        Assert.Equal(1, await audit.Entries.CountAsync(x => x.SubjectRef == enrolled.PrincipalId &&
            x.Permission == (passwordChange ? "iam.password-change" : "iam.logout-all")));
    }

    [Fact]
    public async Task Offline_enrollment_is_case_insensitive_and_commits_the_audit_record_with_iam_state()
    {
        var userName = _host.NewUserName();
        var enrolled = await _host.EnrollAsync(userName);
        var duplicate = await _host.EnrollAsync(userName.ToUpperInvariant());

        Assert.Equal(LocalAuthenticationStatus.Success, enrolled.Status);
        Assert.NotEqual(Guid.Empty, enrolled.PrincipalId);
        Assert.Equal(LocalAuthenticationStatus.Conflict, duplicate.Status);

        await using var audit = await _host.CreateAuditContextAsync();
        var entry = await audit.Entries.AsNoTracking().SingleAsync(value => value.CorrelationId == enrolled.CorrelationId);
        Assert.Equal("mutation", entry.Stage);
        Assert.Equal(enrolled.PrincipalId, entry.SubjectRef);
        Assert.Equal("iam.enroll-user", entry.Permission);
        Assert.Equal("offline-enrollment", entry.SafeReason);
    }

    [Fact]
    public async Task Login_issues_a_protected_cookie_and_me_returns_only_current_self_fields()
    {
        var userName = _host.NewUserName();
        var enrolled = await _host.EnrollAsync(userName);
        using var client = _host.CreateClient();

        using var login = await _host.LoginAsync(client, userName, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(
            login.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-kordra.session=", StringComparison.Ordinal));
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        var loginBody = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sessionReference", loginBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", loginBody, StringComparison.OrdinalIgnoreCase);
        using var loginPayload = JsonDocument.Parse(loginBody);
        Assert.Equal(enrolled.PrincipalId, loginPayload.RootElement.GetProperty("principalId").GetGuid());

        using var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var mePayload = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        var propertyNames = mePayload.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
        Assert.Equal(
            new[] { "correlationId", "principalId", "schemaVersion", "sessionExpiresAtUtc", "username" }.Order(StringComparer.Ordinal),
            propertyNames);
        Assert.Equal(userName, mePayload.RootElement.GetProperty("username").GetString());

        using var inventory = await client.GetAsync("/api/inventory");
        Assert.Equal(HttpStatusCode.Forbidden, inventory.StatusCode);
    }

    [Fact]
    public async Task Unknown_wrong_disabled_and_locked_credentials_have_the_same_public_failure()
    {
        var wrongUser = _host.NewUserName();
        var unknownUser = _host.NewUserName();
        var disabledUser = _host.NewUserName();
        var lockedUser = _host.NewUserName();
        await _host.EnrollAsync(wrongUser);
        var disabled = await _host.EnrollAsync(disabledUser);
        var locked = await _host.EnrollAsync(lockedUser);
        var disableResult = await _host.Authentication.DisableOfflineAsync(disabled.PrincipalId!.Value);
        Assert.Equal(LocalAuthenticationStatus.Success, disableResult.Status);

        using var client = _host.CreateClient();
        using var wrong = await _host.LoginAsync(client, wrongUser, "Wrong-Operator-Passphrase-123!");
        using var unknown = await _host.LoginAsync(client, unknownUser, LocalAuthenticationTestHost.ValidPassword);
        using var disabledResponse = await _host.LoginAsync(client, disabledUser, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, disabledResponse.StatusCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await _host.LoginAsync(client, lockedUser, "Wrong-Operator-Passphrase-123!");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        _host.Clock.Advance(TimeSpan.FromSeconds(61));
        using var lockedResponse = await _host.LoginAsync(client, lockedUser, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, lockedResponse.StatusCode);

        var codes = new[]
        {
            await ReadErrorCodeAsync(wrong),
            await ReadErrorCodeAsync(unknown),
            await ReadErrorCodeAsync(disabledResponse),
            await ReadErrorCodeAsync(lockedResponse)
        };
        Assert.All(codes, code => Assert.Equal("AUTHENTICATION_FAILED", code));

        await using var db = await _host.CreateIdentityContextAsync();
        var lockedPrincipal = await db.Principals.AsNoTracking().SingleAsync(value => value.Id == locked.PrincipalId);
        Assert.Equal(0, lockedPrincipal.AccessFailedCount);
        Assert.True(lockedPrincipal.LockoutEnd > _host.Clock.GetUtcNow());
        Assert.Null(await db.Principals.AsNoTracking().SingleOrDefaultAsync(value => value.NormalizedUserName == unknownUser.ToUpperInvariant()));
    }

    [Fact]
    public async Task Five_active_sessions_are_not_evicted_and_the_sixth_login_is_rejected()
    {
        var userName = _host.NewUserName();
        var enrolled = await _host.EnrollAsync(userName);
        var clients = Enumerable.Range(0, 6).Select(_ => _host.CreateClient()).ToArray();
        try
        {
            for (var index = 0; index < 5; index++)
            {
                using var login = await _host.LoginAsync(clients[index], userName, LocalAuthenticationTestHost.ValidPassword);
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            }

            _host.Clock.Advance(TimeSpan.FromSeconds(61));
            using var sixth = await _host.LoginAsync(clients[5], userName, LocalAuthenticationTestHost.ValidPassword);
            Assert.Equal(HttpStatusCode.Conflict, sixth.StatusCode);
            Assert.Equal("SESSION_LIMIT_REACHED", await ReadErrorCodeAsync(sixth));

            await using var db = await _host.CreateIdentityContextAsync();
            Assert.Equal(5, await db.Sessions.CountAsync(session => session.PrincipalId == enrolled.PrincipalId && !session.IsRevoked));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task Logout_all_revokes_every_current_session_and_clears_the_caller_cookie()
    {
        var userName = _host.NewUserName();
        var enrolled = await _host.EnrollAsync(userName);
        using var first = _host.CreateClient();
        using var second = _host.CreateClient();
        using var firstLogin = await _host.LoginAsync(first, userName, LocalAuthenticationTestHost.ValidPassword);
        using var secondLogin = await _host.LoginAsync(second, userName, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);

        using var logout = await _host.PostEmptyCsrfAsync(first, "/api/v1/auth/logout-all", "logout-all-request-0123456789");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-kordra.session=", StringComparison.Ordinal));

        using var otherSession = await second.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, otherSession.StatusCode);
        await using var db = await _host.CreateIdentityContextAsync();
        Assert.Equal(2, await db.Sessions.CountAsync(session => session.PrincipalId == enrolled.PrincipalId && session.IsRevoked));
    }

    [Fact]
    public async Task Password_change_revokes_old_sessions_and_requires_fresh_login_with_the_new_password()
    {
        var userName = _host.NewUserName();
        var enrolled = await _host.EnrollAsync(userName);
        using var client = _host.CreateClient();
        using var login = await _host.LoginAsync(client, userName, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        const string newPassword = "Synthetic-New-Operator-Passphrase-456!";
        using var changed = await _host.ChangePasswordAsync(
            client,
            LocalAuthenticationTestHost.ValidPassword,
            newPassword,
            "password-change-request-0123456789");
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        using var oldLogin = await _host.LoginAsync(client, userName, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using var newLogin = await _host.LoginAsync(client, userName, newPassword);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        await using var db = await _host.CreateIdentityContextAsync();
        var oldSessions = await db.Sessions.AsNoTracking()
            .Where(session => session.PrincipalId == enrolled.PrincipalId && session.RevocationReason == "password-change")
            .ToArrayAsync();
        Assert.NotEmpty(oldSessions);
        Assert.All(oldSessions, session => Assert.True(session.IsRevoked));
    }

    [Fact]
    public async Task Recovery_does_not_reenable_a_disabled_principal_and_revokes_prior_sessions()
    {
        var userName = _host.NewUserName();
        var enrolled = await _host.EnrollAsync(userName);
        using var client = _host.CreateClient();
        using var login = await _host.LoginAsync(client, userName, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var disabled = await _host.Authentication.DisableOfflineAsync(enrolled.PrincipalId!.Value);
        var recovered = await _host.Authentication.RecoverOfflineAsync(
            enrolled.PrincipalId.Value, "Synthetic-Recovered-Operator-Passphrase-789!");
        Assert.Equal(LocalAuthenticationStatus.Success, disabled.Status);
        Assert.Equal(LocalAuthenticationStatus.Success, recovered.Status);

        using var attempt = await _host.LoginAsync(client, userName, "Synthetic-Recovered-Operator-Passphrase-789!");
        Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        Assert.Equal("AUTHENTICATION_FAILED", await ReadErrorCodeAsync(attempt));

        await using var db = await _host.CreateIdentityContextAsync();
        var principal = await db.Principals.AsNoTracking().SingleAsync(value => value.Id == enrolled.PrincipalId);
        Assert.Equal("disabled", principal.AccountStatus);
        Assert.All(await db.Sessions.AsNoTracking().Where(session => session.PrincipalId == principal.Id).ToArrayAsync(),
            session => Assert.True(session.IsRevoked));
    }

    [Fact]
    public async Task Startup_invalidates_prior_sessions_and_audit_records_the_new_boot()
    {
        var userName = _host.NewUserName();
        var enrolled = await _host.EnrollAsync(userName);
        using var client = _host.CreateClient();
        using var login = await _host.LoginAsync(client, userName, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        await _host.RestartAsync();
        using var priorCookie = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, priorCookie.StatusCode);

        await using var db = await _host.CreateIdentityContextAsync();
        var sessions = await db.Sessions.AsNoTracking().Where(session => session.PrincipalId == enrolled.PrincipalId).ToArrayAsync();
        Assert.NotEmpty(sessions);
        Assert.All(sessions, session => Assert.True(session.IsRevoked));
        Assert.All(sessions, session => Assert.Equal("api-restart", session.RevocationReason));
    }

    [Fact]
    public async Task Unknown_audit_commit_outcome_never_issues_a_session_cookie_or_persists_a_session()
    {
        var userName = _host.NewUserName();
        await _host.DisposeAsync();
        _host = await LocalAuthenticationTestHost.StartAsync(new UnknownAuditFactory(), initializeBoot: false);
        var principal = await _host.SeedPrincipalAsync(userName);
        using var client = _host.CreateClient();
        using var login = await _host.LoginAsync(client, userName, LocalAuthenticationTestHost.ValidPassword);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
        Assert.Equal("AUTHORITY_UNAVAILABLE", await ReadErrorCodeAsync(login));
        if (login.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            Assert.DoesNotContain(cookies, value => value.StartsWith("__Host-kordra.session=", StringComparison.Ordinal));
        }

        await using var db = await _host.CreateIdentityContextAsync();
        Assert.Equal(0, await db.Sessions.CountAsync(session => session.PrincipalId == principal.Id));
    }

    [Fact]
    public async Task Revocation_waits_for_an_admitted_me_response_to_finish()
    {
        var meEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logoutReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedAudit = new LogoutObservingAuditFactory(new IdentityAccessAuditTransactionAdapter());
        var firstMe = 0;
        await _host.DisposeAsync();
        _host = await LocalAuthenticationTestHost.StartAsync(
            observedAudit,
            afterAuthentication: async (context, next) =>
            {
                if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/api/v1/auth/me" &&
                    Interlocked.Exchange(ref firstMe, 1) == 0)
                {
                    meEntered.TrySetResult();
                    await releaseMe.Task.WaitAsync(context.RequestAborted);
                }

                if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path == "/api/v1/auth/logout")
                {
                    logoutReached.TrySetResult();
                }

                await next(context);
            });

        try
        {
            var userName = _host.NewUserName();
            await _host.EnrollAsync(userName);
            using var client = _host.CreateClient();
            using var login = await _host.LoginAsync(client, userName, LocalAuthenticationTestHost.ValidPassword);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            var meTask = client.GetAsync("/api/v1/auth/me");
            await meEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var logoutTask = _host.PostEmptyCsrfAsync(client, "/api/v1/auth/logout");
            await logoutReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await observedAudit.LogoutMutationStarted.Task.WaitAsync(TimeSpan.FromMilliseconds(200)));

            releaseMe.TrySetResult();
            using var me = await meTask;
            using var logout = await logoutTask;
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            await observedAudit.LogoutMutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseMe.TrySetResult();
        }
    }

    public async Task InitializeAsync() => _host = await LocalAuthenticationTestHost.StartAsync();

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    private sealed class ActivityUpdateBarrier : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        private int _seen;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) &&
                command.CommandText.Contains("last_activity_at_utc", StringComparison.Ordinal) && Interlocked.Increment(ref _seen) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class FaultAuditFactory : IIdentityAuditTransactionFactory
    {
        private readonly IdentityAccessAuditTransactionAdapter _inner = new();
        public bool LoseNextMutationAck { get; set; }
        public bool ThrowAfterCommit { get; set; }
        public string? FailNextSetup { get; set; }
        public int ProtocolCalls { get; set; }
        public bool SlowDenial { get; init; }
        public TimeSpan DenialWait { get; set; }
        public async ValueTask<IIdentityAuditTransaction> EnlistAsync(DbConnection connection, DbTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            var failure = FailNextSetup;
            FailNextSetup = null;
            if (failure == "cancel") await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (failure is not null) throw new InvalidOperationException("Synthetic audit setup failure.");
            return new FaultTransaction(this, await _inner.EnlistAsync(connection, transaction, cancellationToken));
        }

        private sealed class FaultTransaction(FaultAuditFactory factory, IIdentityAuditTransaction inner) : IIdentityAuditTransaction
        {
            public async Task<AuditAppendOutcome> CommitWithOwnerMutationAsync(AuditIntent intent,
                Func<CancellationToken, Task> saveOwnerMutationAsync, CancellationToken cancellationToken = default)
            {
                factory.ProtocolCalls++;
                if (factory.SlowDenial && intent.SafeReason == "login-denied")
                {
                    var watch = Stopwatch.StartNew();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                    factory.DenialWait = watch.Elapsed;
                    return AuditAppendOutcome.Unavailable();
                }
                var outcome = await inner.CommitWithOwnerMutationAsync(intent, saveOwnerMutationAsync, cancellationToken);
                if (factory.LoseNextMutationAck && intent.Stage == AuditIntentStage.Mutation && outcome.Status == AuditAppendStatus.Durable)
                {
                    factory.LoseNextMutationAck = false;
                    if (factory.ThrowAfterCommit) throw new IOException("Synthetic lost commit acknowledgement.");
                    return AuditAppendOutcome.Unknown();
                }
                return outcome;
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    private sealed class UnknownAuditFactory : IIdentityAuditTransactionFactory
    {
        public ValueTask<IIdentityAuditTransaction> EnlistAsync(
            DbConnection connection,
            DbTransaction transaction,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<IIdentityAuditTransaction>(new UnknownAuditTransaction());
    }

    private sealed class UnknownAuditTransaction : IIdentityAuditTransaction
    {
        public Task<AuditAppendOutcome> CommitWithOwnerMutationAsync(
            AuditIntent intent,
            Func<CancellationToken, Task> saveOwnerMutationAsync,
            CancellationToken cancellationToken = default) => Task.FromResult(AuditAppendOutcome.Unknown());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class LogoutObservingAuditFactory(IIdentityAuditTransactionFactory inner) : IIdentityAuditTransactionFactory
    {
        public TaskCompletionSource LogoutMutationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IIdentityAuditTransaction> EnlistAsync(
            DbConnection connection,
            DbTransaction transaction,
            CancellationToken cancellationToken = default) =>
            new ObservedTransaction(await inner.EnlistAsync(connection, transaction, cancellationToken), LogoutMutationStarted);

        private sealed class ObservedTransaction(
            IIdentityAuditTransaction inner,
            TaskCompletionSource logoutMutationStarted) : IIdentityAuditTransaction
        {
            public Task<AuditAppendOutcome> CommitWithOwnerMutationAsync(
                AuditIntent intent,
                Func<CancellationToken, Task> saveOwnerMutationAsync,
                CancellationToken cancellationToken = default)
            {
                if (intent.Permission == "iam.logout")
                {
                    logoutMutationStarted.TrySetResult();
                }

                return inner.CommitWithOwnerMutationAsync(intent, saveOwnerMutationAsync, cancellationToken);
            }

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
