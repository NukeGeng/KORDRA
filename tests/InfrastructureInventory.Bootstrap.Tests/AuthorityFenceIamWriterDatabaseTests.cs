using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

/// <summary>
/// 12.3c-1 (D4) on actual PostgreSQL (operation-gated; ADR-0025 disposable database + run-scoped roles): every IAM invalidating
/// writer, called through its REAL entry point with the enforced fence, (a) waits while the installation fence is held, (b)
/// advances the epoch by exactly one when it changes authority and then a permit issued before it is rejected (KD005), and
/// (c) leaves the epoch unchanged when nothing is invalidated. (d) A session that expires while its writer waits on the fence
/// is judged by trusted time read after the wait, so nothing commits (PR35-IAM-FENCE-TIME-001).
/// </summary>
public sealed class AuthorityFenceIamWriterDatabaseTests
{
    /// <summary>Registry scenarios this class proves; the completeness test pins it to <see cref="AuthorityWriterRegistry"/>.</summary>
    internal static readonly string[] Scenarios = ["iam.boot", "iam.login", "iam.logout", "iam.logout-all", "iam.password",
        "iam.enroll", "iam.recover", "iam.disable", "iam.definition"];
    private const string ManifestVariable = "KORDRA_FENCE_IAM_MANIFEST";
    private const string LocatorVariable = "KORDRA_FENCE_IAM_CONNECTION_STRING";

    [Fact]
    public void ExportFenceSqlWithoutDatabaseAccess()
    {
        var sql = Script("0123456789ab");
        Assert.Contains("CREATE SCHEMA iam_workflow AUTHORIZATION kordra_iam_owner_r0123456789ab", sql);
        if (Environment.GetEnvironmentVariable("KORDRA_FENCE_IAM_EXPORT") is not { Length: > 0 } directory) return;
        var suffix = Environment.GetEnvironmentVariable("KORDRA_FENCE_IAM_SUFFIX") ?? throw new InvalidOperationException("Run suffix required.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "iam-workflow-authority-v3.sql"), Script(suffix));
    }

    internal static string Script(string suffix) => OwnerFunctionPoc.ForRun(OwnerFunctionPoc.Sql("iam-workflow-authority-v3"), suffix);

    [AuthorityFenceIamDatabaseFact]
    public async Task IamWritersHoldTheFenceAndAdvanceTheEpochOnlyWhenAuthorityChanges()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var ct = budget.Token;
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable(ManifestVariable)!));
        Assert.Equal("approved_for_execution", approval.RootElement.GetProperty("status").GetString());
        var suffix = approval.RootElement.GetProperty("suffix").GetString()!;
        Assert.Equal(approval.RootElement.GetProperty("sha256").GetProperty("iam-workflow-authority-v3.sql").GetString(),
            OwnerFunctionPoc.Sha256(Script(suffix)));
        var profile = RegressionPostgreSqlSelection.LoadRunnerProfile() ?? throw new InvalidOperationException("Disposable runner profile required.");
        var locator = Environment.GetEnvironmentVariable(LocatorVariable) ?? throw new InvalidOperationException("Approved fixture locator required.");
        var binding = profile.OwnerBinding;
        var workerLogin = new NpgsqlConnectionStringBuilder(locator)
            { Username = OwnerFunctionPoc.Role("kordra_worker_runtime", suffix), Password = null }.ConnectionString;

        // Owner schemas through their own migrations, then the reviewed IAM v2 artifact (fence, services, permits).
        await using (var iam = new KordraIdentityDbContext(IdentityAccessDbContextFactory.CreateDisposableTestOptions(locator, binding)))
            await iam.Database.MigrateAsync(ct);
        await using (var aud = new AuditDbContext(AuditDbContextFactory.CreateDisposableTestOptions(locator, binding)))
            await aud.Database.MigrateAsync(ct);
        await using var setup = new NpgsqlConnection(locator);
        await setup.OpenAsync(ct);
        await Execute(setup, Script(suffix), ct);

        var installation = Guid.NewGuid(); var tenant = Guid.NewGuid(); var environment = Guid.NewGuid();
        var workerService = Guid.NewGuid();
        var workerPin = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)); // 12.3d D3: presented with every probe permit
        await Manifest(Guid.NewGuid(), "api", "kordra_test", "[]");
        await Manifest(workerService, "worker", OwnerFunctionPoc.Role("kordra_worker_runtime", suffix), JsonSerializer.Serialize(new[] {
            new Dictionary<string, object> { ["tenant_id"] = tenant, ["environment_id"] = environment, ["template_id"] = WorkflowSpecification.TemplateId,
                ["template_version"] = WorkflowSpecification.TemplateVersion, ["transition_kind"] = "admit" } }));
        var probeInstance = Guid.NewGuid(); var specHash = new string('a', 64);
        await InApiRoot(async (connection, root) => await new WorkflowAuthorityFunctionPort(connection, root).CreateDelegationAsync(new(Guid.NewGuid(),
            tenant, probeInstance, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), environment, Guid.NewGuid(), 1,
            new string('b', 64), WorkflowSpecification.TemplateId, WorkflowSpecification.TemplateVersion, specHash, ["admit"],
            Encoding.UTF8.GetBytes("{\"probe\":1}"), DateTimeOffset.UtcNow.AddMinutes(30)), ct));

        // Real writers composed with the enforced fence on the disposable owners (offline and online entry points alike).
        var directory = Path.Combine(Path.GetTempPath(), "kordra-fence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var denylist = Path.Combine(directory, "denylist.json");
            var denylistBytes = JsonSerializer.SerializeToUtf8Bytes(new[] { "synthetic-denied-password-value" });
            await File.WriteAllBytesAsync(denylist, denylistBytes, ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(denylist, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var policy = LocalPasswordPolicy.Load(denylist, "fence-fixture-v1", "12.3c fixture", Convert.ToHexStringLower(SHA256.HashData(denylistBytes)));
            var contexts = new IdentityContexts(locator, binding);
            var audits = IdentityAccessAuditTransactionAdapter.CreateDisposableTestAdapter(binding);
            var gate = new FairAsyncReaderWriterAdmissionGate();
            var configuration = new LocalAuthenticationConfiguration(installation, Guid.NewGuid());
            var fence = new PostgresAuthorityFence(installation);
            var hasher = new PasswordHasher<KordraIdentityUser>(
                Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { IterationCount = 220_000 }));
            var auth = new LocalAuthenticationService(contexts, audits, hasher, configuration, gate, null, policy, fence);
            var definitions = new RoleDefinitionStore(contexts, gate, audits, configuration, auth, fence);
            Assert.Equal(AuthorityFenceMode.Enforced, auth.FenceMode); Assert.Equal(AuthorityFenceMode.Enforced, definitions.FenceMode);
            string Password() => "Fence-Fixture-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
            string Key() => "fence-key-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            var proven = new HashSet<string>(StringComparer.Ordinal);

            await Race("iam.boot", true, async () => { await auth.InitializeBootAsync(ct); return true; });
            var passwordA = Password(); var passwordB = Password();
            Guid userA = default, userB = default;
            await Race("iam.enroll", false, async () =>
            {
                var enrolled = await auth.EnrollOfflineAsync("fence-user-a", passwordA, ct);
                userA = enrolled.PrincipalId ?? Guid.Empty; return enrolled.Status == LocalAuthenticationStatus.Success;
            });
            userB = (await auth.EnrollOfflineAsync("fence-user-b", passwordB, ct)).PrincipalId ?? Guid.Empty;
            Assert.NotEqual(Guid.Empty, userA); Assert.NotEqual(Guid.Empty, userB);
            LocalLoginResult session = null!;
            await Race("iam.login", false, async () =>
            {
                session = await auth.LoginAsync("fence-user-a", passwordA, ct); return session.Status == LocalAuthenticationStatus.Success;
            });
            // Failed attempts change only a counter; the attempt that starts the lockout is the invalidating one.
            var beforeFailures = await Epoch();
            for (var attempt = 0; attempt < 4; attempt++)
                Assert.Equal(LocalAuthenticationStatus.InvalidCredentials, (await auth.LoginAsync("fence-user-b", Password(), ct)).Status);
            Assert.Equal(beforeFailures, await Epoch());
            await Race("iam.login", true, async () =>
                (await auth.LoginAsync("fence-user-b", Password(), ct)).Status == LocalAuthenticationStatus.InvalidCredentials);
            await Race("iam.logout", true, async () =>
                (await auth.LogoutAsync(userA, session.SessionReference, configuration.BootGeneration, ct)).Status == LocalAuthenticationStatus.Success);
            var first = await auth.LoginAsync("fence-user-a", passwordA, ct);
            Assert.Equal(LocalAuthenticationStatus.Success, (await auth.LoginAsync("fence-user-a", passwordA, ct)).Status);
            await Race("iam.logout-all", true, async () => (await auth.LogoutAllAsync(userA, first.SessionReference,
                configuration.BootGeneration, Key(), ct)).Status == LocalAuthenticationStatus.Success);
            var current = await auth.LoginAsync("fence-user-a", passwordA, ct);
            var newPassword = Password();
            await Race("iam.password", true, async () => (await auth.ChangePasswordAsync(userA, current.SessionReference,
                configuration.BootGeneration, passwordA, newPassword, Key(), ct)).Status == LocalAuthenticationStatus.Success);
            await Race("iam.recover", true, async () =>
                (await auth.RecoverOfflineAsync(userA, Password(), ct)).Status == LocalAuthenticationStatus.Success);
            await Race("iam.disable", true, async () =>
                (await auth.DisableOfflineAsync(userA, ct)).Status == LocalAuthenticationStatus.Success);
            var lease = new HeldLease();
            DefinitionWriteResult published = null!;
            await Race("iam.definition", false, async () =>
            {
                published = await definitions.PublishOfflineAsync(lease, Guid.NewGuid(), Guid.Empty, 0, "Fence fixture",
                    [new PermissionRule("release.read", "project")], false, ct);
                return published.Status == "success";
            });
            await Race("iam.definition", true, async () => (await definitions.PublishOfflineAsync(lease, Guid.NewGuid(),
                published.DefinitionId, published.Revision, "Fence fixture", [new PermissionRule("release.read", "project")], true, ct)).Status == "success");
            Assert.Equal(Scenarios.Order(StringComparer.Ordinal), proven.Order(StringComparer.Ordinal));

            // (d) PR35-IAM-FENCE-TIME-001: each case logs in normally, then calls the session-checking entry point through a writer
            // composed like `auth` but on a clock the test moves past the idle lifetime only while that writer is observed waiting
            // on the held fence. Trusted time read after the wait refuses the expired session: nothing commits and the epoch stays.
            // A reading taken before the wait would accept the session, commit and advance.
            var passwordC = Password();
            var userC = (await auth.EnrollOfflineAsync("fence-user-c", passwordC, ct)).PrincipalId ?? Guid.Empty;
            Assert.NotEqual(Guid.Empty, userC);
            await ExpiresWhileWaiting("iam.logout", LocalAuthenticationStatus.SessionInvalid,
                (late, reference) => late.LogoutAsync(userC, reference, configuration.BootGeneration, ct));
            await ExpiresWhileWaiting("iam.logout-all", LocalAuthenticationStatus.SessionInvalid,
                (late, reference) => late.LogoutAllAsync(userC, reference, configuration.BootGeneration, Key(), ct));
            // The pre-wait recovery check still sees a valid session; the check under the fence reports the expiry as a conflict.
            await ExpiresWhileWaiting("iam.password", LocalAuthenticationStatus.Conflict,
                (late, reference) => late.ChangePasswordAsync(userC, reference, configuration.BootGeneration, passwordC, Password(), Key(), ct));

            async Task ExpiresWhileWaiting(string scenario, LocalAuthenticationStatus expected,
                Func<LocalAuthenticationService, string?, Task<LocalCommandResult>> write)
            {
                var login = await auth.LoginAsync("fence-user-c", passwordC, ct);
                Assert.Equal(LocalAuthenticationStatus.Success, login.Status);
                var clock = new ShiftedClock();
                var late = new LocalAuthenticationService(contexts, audits, hasher, configuration, gate, clock, policy, fence);
                // Control: before the wait this writer still admits the session, so only time read after the wait can refuse it.
                Assert.Equal(LocalAuthenticationStatus.Success,
                    (await late.ResolveCurrentSessionAsync(userC, login.SessionReference, configuration.BootGeneration, ct)).Status);
                await Race(scenario, false, async () => (await write(late, login.SessionReference)).Status == expected,
                    whileHeld: () => clock.Shift(configuration.IdleLifetime + TimeSpan.FromMinutes(1)));
            }

            async Task Race(string scenario, bool advances, Func<Task<bool>> writer, Action? whileHeld = null)
            {
                var label = whileHeld is null ? scenario : scenario + " (session expires while waiting)";
                var permit = await IssueProbe();
                var before = await Epoch();
                await using (var holder = new NpgsqlConnection(locator))
                {
                    await holder.OpenAsync(ct);
                    await using var held = await holder.BeginTransactionAsync(ct);
                    await new WorkflowAuthorityFunctionPort(holder, held).AcquireFenceAsync(installation, ct);
                    var running = writer();
                    // (a) The writer must be observed WAITING on the fence lock before the holder releases it (lock_timeout 1 s).
                    var waited = false;
                    for (var poll = 0; poll < 150 && !running.IsCompleted; poll++)
                    {
                        if (await Scalar<long>(setup, "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() " +
                            "AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()") > 0) { waited = true; break; }
                        await Task.Delay(20, ct);
                    }
                    Assert.True(waited, label + " did not wait for the held fence");
                    Assert.False(running.IsCompleted, label);
                    whileHeld?.Invoke();
                    await held.RollbackAsync(ct);
                    Assert.True(await running, label + " entry point did not return its expected result");
                }
                // (b)/(c) Exactly one advance for an invalidating change; none otherwise. A permit issued before is then stale.
                Assert.Equal(before + (advances ? 1 : 0), await Epoch());
                var consumed = await Consume(permit);
                // Epoch is checked before expiry in consume_permit, so an advance always yields KD005; without an advance the
                // permit is never rejected for its epoch (it may only have expired if the writer was slow).
                if (advances) Assert.Equal("KD005", consumed); else Assert.NotEqual("KD005", consumed);
                proven.Add(scenario);
            }
        }
        finally { Directory.Delete(directory, true); }

        async Task Manifest(Guid service, string kind, string login, string bindings)
        {
            await using var command = new NpgsqlCommand("SELECT * FROM iam_workflow.apply_service_manifest(@sha, 'bootstrap', @installation, " +
                "@service, @kind, @login::name, @pin, clock_timestamp() - interval '1 minute', clock_timestamp() + interval '1 hour', @bindings::jsonb, @audit)", setup);
            command.Parameters.AddWithValue("sha", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
            command.Parameters.AddWithValue("installation", installation); command.Parameters.AddWithValue("service", service);
            command.Parameters.AddWithValue("kind", kind); command.Parameters.AddWithValue("login", login);
            command.Parameters.AddWithValue("pin", service == workerService ? workerPin : Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
            command.Parameters.AddWithValue("bindings", bindings); command.Parameters.AddWithValue("audit", Guid.NewGuid());
            await command.ExecuteNonQueryAsync(ct);
        }

        async Task InApiRoot(Func<NpgsqlConnection, NpgsqlTransaction, Task> action)
        {
            await using var connection = new NpgsqlConnection(locator);
            await connection.OpenAsync(ct);
            await using var root = await connection.BeginTransactionAsync(ct);
            await action(connection, root);
            await root.CommitAsync(ct);
        }

        async Task<(Guid Permit, Guid Operation)> IssueProbe()
        {
            var operation = Guid.NewGuid(); var permit = Guid.NewGuid();
            await InApiRoot(async (connection, root) => await new WorkflowAuthorityFunctionPort(connection, root).IssuePermitAsync(new(permit,
                workerService, tenant, probeInstance, Guid.NewGuid(), operation, 1, specHash, WorkflowSpecification.TemplateHash, "admit",
                installation, 0, workerPin), ct));
            return (permit, operation);
        }

        async Task<string?> Consume((Guid Permit, Guid Operation) probe)
        {
            await using var worker = new NpgsqlConnection(workerLogin);
            await worker.OpenAsync(ct);
            await using var root = await worker.BeginTransactionAsync(ct);
            try
            {
                await new WorkflowAuthorityFunctionPort(worker, root).ConsumePermitAsync(new(probe.Permit, probe.Operation, probeInstance, 1,
                    specHash, installation, 0), ct);
                await root.RollbackAsync(ct);
                return null;
            }
            catch (PostgresException error) { return error.SqlState; }
        }

        Task<long> Epoch() => Scalar<long>(setup, $"SELECT epoch FROM iam_workflow.authority_fences WHERE installation_id = '{installation}'");
    }

    private sealed class IdentityContexts(string locator, (Guid RunId, string SourceSha256, string OperationManifestSha256, DateTimeOffset ExpiresAtUtc) binding)
        : IDbContextFactory<KordraIdentityDbContext>
    {
        public KordraIdentityDbContext CreateDbContext() => new(IdentityAccessDbContextFactory.CreateDisposableTestOptions(locator, binding));
    }

    private sealed class HeldLease : IOfflineProvisioningLease { public void RequireHeld() { } }

    /// <summary>
    /// Real time plus a shift applied to wall and monotonic time alike, so the writer's trusted clock sees elapsed time,
    /// not a jump it would reject (it tolerates at most 1 s between the two).
    /// </summary>
    private sealed class ShiftedClock : TimeProvider
    {
        private long shiftTicks;
        internal void Shift(TimeSpan by) => Interlocked.Add(ref shiftTicks, by.Ticks);
        private TimeSpan Current => TimeSpan.FromTicks(Interlocked.Read(ref shiftTicks));
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + Current;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp() + (long)(Current.TotalSeconds * TimestampFrequency);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}

public sealed class AuthorityFenceIamDatabaseFactAttribute : FactAttribute
{
    public AuthorityFenceIamDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_FENCE_IAM_MANIFEST")))
            Skip = "Requires the exact approved 12.3c-1 fence fixture manifest, disposable database and run roles.";
    }
}
