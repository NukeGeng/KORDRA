using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Kordra.Modules.ApprovalGovernance;
using Kordra.ApprovalGovernance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace Kordra.ApprovalGovernance.Tests;

public sealed class ApprovalRequestPersistenceTests : IAsyncLifetime
{
    private ApprovalDbContext _db = null!;
    private IDbContextTransaction _tx = null!;
    private readonly Guid _installation = Guid.NewGuid(), _tenant = Guid.NewGuid(), _project = Guid.NewGuid(), _environment = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    public async Task InitializeAsync()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Isolated test database required.");
        _db = new(ApprovalDbContextFactory.CreateTestOptions(locator)); await _db.Database.MigrateAsync();
        _tx = await _db.Database.BeginTransactionAsync();
    }
    public async Task DisposeAsync() { await _tx.RollbackAsync(); await _tx.DisposeAsync(); await _db.DisposeAsync(); }
    private ApprovalRequestRecord Request(Guid? revision = null) => new()
    {
        Id = Guid.NewGuid(), InstallationId = _installation, TenantId = _tenant, ProjectId = _project,
        EnvironmentId = _environment, ReleaseRevisionId = revision ?? Guid.NewGuid(), RequesterPrincipalId = Guid.NewGuid(),
        SpecHash = new byte[32], RequesterEvidence = [1], CreatedAtUtc = _now, PendingExpiresAtUtc = _now.AddHours(1)
    };
    [Fact]
    public async Task QueueAndDetailAreBoundedScopedDetachedAndUseScopedCursors()
    {
        var store = new ApprovalRequestStore(_db);
        var first = Request(); first.Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Request(); second.Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var foreign = Request(); foreign.EnvironmentId = Guid.NewGuid();
        store.StageRequest(first); store.StageRequest(second); store.StageRequest(foreign); await _db.SaveChangesAsync();
        await store.StageDecisionAsync(_installation, _tenant, _project, _environment, first.Id, 1, Guid.NewGuid(),
            Guid.NewGuid(), ApprovalDecisionAction.Reject, "changes_required", _now.AddMinutes(1));
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var query = new ApprovalRequestQueryStore(_db);
        var page = await query.ReadPageUnderAdmissionAsync(_installation, _tenant, _project, _environment, 1, null);
        Assert.NotNull(page); Assert.Equal(first.Id, Assert.Single(page.Requests).Id); Assert.Equal(first.Id, page.NextAfterId);
        var last = await query.ReadPageUnderAdmissionAsync(_installation, _tenant, _project, _environment, 1, page.NextAfterId);
        Assert.NotNull(last); Assert.Equal(second.Id, Assert.Single(last.Requests).Id); Assert.Null(last.NextAfterId);
        Assert.Null(await query.ReadPageUnderAdmissionAsync(_installation, _tenant, _project, _environment, 1, foreign.Id));
        Assert.Null(await query.ReadPageUnderAdmissionAsync(_installation, _tenant, _project, _environment, 51, null));
        Assert.Null(await query.ReadUnderAdmissionAsync(_installation, Guid.NewGuid(), _project, _environment, first.Id));
        Assert.Null(await query.ReadUnderAdmissionAsync(_installation, _tenant, _project, _environment, foreign.Id));
        var detail = await query.ReadUnderAdmissionAsync(_installation, _tenant, _project, _environment, first.Id);
        Assert.NotNull(detail); Assert.Equal("reject", Assert.Single(detail.Decisions).Action);
        detail.Request.RequesterEvidence[0] = 9;
        Assert.Empty(_db.ChangeTracker.Entries());
        Assert.Equal(1, (await query.ReadUnderAdmissionAsync(_installation, _tenant, _project, _environment, first.Id))!.Request.RequesterEvidence[0]);
    }
    [Fact]
    public async Task ReceiptReplayRequiresExactOwnerAndPayloadAndClonesEvidence()
    {
        var row = Request(); new ApprovalRequestStore(_db).StageRequest(row);
        var store = new ApprovalRequestReceiptStore(_db);
        var key = Enumerable.Repeat((byte)1, 32).ToArray(); var body = Enumerable.Repeat((byte)2, 32).ToArray();
        var hash = Enumerable.Repeat((byte)3, 32).ToArray(); var boot = Guid.NewGuid(); var invocation = Guid.NewGuid();
        await store.StageAsync(Guid.NewGuid(), _installation, _tenant, _project, _environment, row.RequesterPrincipalId,
            row.Id, boot, invocation, "request", key, body, Guid.NewGuid(), hash, _now);
        hash[0] = 7; await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var candidate = await store.FindUnderAdmissionAsync(_installation, _tenant, _project, _environment,
            row.RequesterPrincipalId, "request", key, body);
        Assert.Equal("candidate", candidate.Status); Assert.Equal(row.Id, candidate.RequestId);
        Assert.Equal(3, candidate.AuditIntentHash![0]); Assert.Equal(boot, candidate.BootId); Assert.Equal(invocation, candidate.InvocationId);
        candidate.AuditIntentHash[0] = 9;
        Assert.Equal(3, (await store.FindUnderAdmissionAsync(_installation, _tenant, _project, _environment,
            row.RequesterPrincipalId, "request", key, body)).AuditIntentHash![0]);
        Assert.Equal("conflict", (await store.FindUnderAdmissionAsync(_installation, _tenant, Guid.NewGuid(), _environment,
            row.RequesterPrincipalId, "request", key, body)).Status);
        Assert.Equal("conflict", (await store.FindUnderAdmissionAsync(_installation, _tenant, _project, _environment,
            row.RequesterPrincipalId, "request", key, new byte[32])).Status);
        Assert.Equal("absent", (await store.FindUnderAdmissionAsync(_installation, Guid.NewGuid(), _project, _environment,
            row.RequesterPrincipalId, "request", key, body)).Status);
        await Assert.ThrowsAsync<ApprovalRequestConflictException>(() => store.StageAsync(Guid.NewGuid(), _installation, _tenant,
            _project, Guid.NewGuid(), row.RequesterPrincipalId, row.Id, boot, invocation, "grant", key, body,
            Guid.NewGuid(), new byte[32], _now));
    }
    [Fact]
    public async Task EnlistedContextParticipatesInCallerRollback()
    {
        var row = Request();
        await using (var enlisted = new ApprovalDbContext(ApprovalDbContextFactory.CreateEnlistedOptions(_db.Database.GetDbConnection())))
        {
            await enlisted.Database.UseTransactionAsync(_tx.GetDbTransaction());
            new ApprovalRequestStore(enlisted).StageRequest(row);
            await enlisted.SaveChangesAsync();
            Assert.True(await _db.Requests.AsNoTracking().AnyAsync(x => x.Id == row.Id));
        }
        await _tx.RollbackAsync();
        await _tx.DisposeAsync();
        _tx = await _db.Database.BeginTransactionAsync();
        Assert.False(await _db.Requests.AsNoTracking().AnyAsync(x => x.Id == row.Id));
    }
    [Theory]
    [InlineData("Host=127.0.0.1;Port=5432;Database=kordra;Username=kordra_admin")]
    [InlineData("Host=localhost;Port=5432;Database=kordra;Username=kordra_app")]
    [InlineData("Host=127.0.0.1;Port=5432;Database=kordra_test;Username=kordra_app")]
    public void EnlistedContextRejectsOtherTargetsAndRoles(string locator)
    {
        using var connection = new NpgsqlConnection(locator);
        Assert.Throws<InvalidOperationException>(() => ApprovalDbContextFactory.CreateEnlistedOptions(connection));
    }
    [Fact]
    public async Task OneRequestPerRevisionAndNoDirectGrantedInsert()
    {
        var first = Request(); _db.Requests.Add(first); await _db.SaveChangesAsync();
        _db.Requests.Add(Request(first.ReleaseRevisionId));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
    [Fact]
    public async Task InitialGrantIsRejectedByDatabase()
    {
        var row = Request(); row.State = "Granted"; _db.Requests.Add(row);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.IsType<PostgresException>(error.InnerException);
    }
    [Fact]
    public async Task PendingGrantThenRevokePreservesOriginalEvidence()
    {
        var row = Request(); _db.Requests.Add(row); await _db.SaveChangesAsync();
        row.State = "Granted"; row.Version = 2; row.GrantedAtUtc = _now.AddMinutes(1);
        row.GrantExpiresAtUtc = _now.AddMinutes(10); row.ApproverPrincipalId = Guid.NewGuid(); row.ApproverEvidence = [2];
        await _db.SaveChangesAsync();
        row.State = "Revoked"; row.Version = 3; await _db.SaveChangesAsync();
        Assert.Equal(new byte[] { 1 }, row.RequesterEvidence); Assert.Equal(new byte[] { 2 }, row.ApproverEvidence);
        row.State = "Granted"; row.Version = 4;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.IsType<PostgresException>(error.InnerException);
    }
    [Fact]
    public async Task RequestOwnerFactsCannotBeRewrittenDuringTransition()
    {
        var row = Request(); _db.Requests.Add(row); await _db.SaveChangesAsync();
        row.State = "Rejected"; row.Version = 2; row.SpecHash = Enumerable.Repeat((byte)1, 32).ToArray();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        Assert.IsType<PostgresException>(error.InnerException);
    }
    [Fact]
    public async Task StoreStagesDecisionAndStateInSharedTransactionAndRejectsStaleVersion()
    {
        var store = new ApprovalRequestStore(_db); var row = Request(); store.StageRequest(row); await _db.SaveChangesAsync();
        await store.StageDecisionAsync(_installation, _tenant, _project, _environment, row.Id, 1, Guid.NewGuid(), Guid.NewGuid(),
            ApprovalDecisionAction.Reject, "changes_required", _now.AddMinutes(1));
        Assert.Equal("Rejected", row.State); Assert.Equal(2, row.Version);
        Assert.Single(_db.Decisions.Local); await _db.SaveChangesAsync();
        Assert.Equal(1, await _db.Decisions.CountAsync(x => x.RequestId == row.Id));
        await Assert.ThrowsAsync<ApprovalRequestConflictException>(() => store.StageDecisionAsync(_installation, _tenant, _project,
            _environment, row.Id, 1, Guid.NewGuid(), Guid.NewGuid(), ApprovalDecisionAction.Reject, "changes_required", _now.AddMinutes(2)));
    }
    [Fact]
    public async Task StoreRejectsCrossTenantAndUnboundedGrantBeforeStaging()
    {
        var store = new ApprovalRequestStore(_db); var row = Request(); store.StageRequest(row); await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ApprovalRequestConflictException>(() => store.StageDecisionAsync(_installation, Guid.NewGuid(), _project,
            _environment, row.Id, 1, Guid.NewGuid(), Guid.NewGuid(), ApprovalDecisionAction.Reject, "policy_denied", _now));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StageDecisionAsync(_installation, _tenant, _project,
            _environment, row.Id, 1, Guid.NewGuid(), Guid.NewGuid(), ApprovalDecisionAction.Grant, "approved", _now.AddMinutes(1),
            _now.AddHours(2), [1]));
        Assert.Equal("Pending", row.State); Assert.Empty(_db.Decisions.Local);
    }
    [Fact]
    public async Task StoreDoesNotStageWithoutTransaction()
    {
        await _tx.RollbackAsync(); await _tx.DisposeAsync();
        _tx = await _db.Database.BeginTransactionAsync(); // replace fixture transaction after testing a separate context
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")!;
        await using var outside = new ApprovalDbContext(ApprovalDbContextFactory.CreateTestOptions(locator));
        Assert.Throws<InvalidOperationException>(() => new ApprovalRequestStore(outside).StageRequest(Request()));
    }
    [Fact]
    public async Task CompetingDecisionsOnIndependentTransactionsCannotBothCommit()
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")!;
        var options = ApprovalDbContextFactory.CreateTestOptions(locator);
        var row = Request();
        await using (var seed = new ApprovalDbContext(options))
        await using (var seedTx = await seed.Database.BeginTransactionAsync())
        { new ApprovalRequestStore(seed).StageRequest(row); await seed.SaveChangesAsync(); await seedTx.CommitAsync(); }
        // Synthetic immutable test history is retained only in kordra_test.
        await using var first = new ApprovalDbContext(options); await using var second = new ApprovalDbContext(options);
        await using var firstTx = await first.Database.BeginTransactionAsync(); await using var secondTx = await second.Database.BeginTransactionAsync();
        await new ApprovalRequestStore(first).StageDecisionAsync(_installation, _tenant, _project, _environment, row.Id, 1,
            Guid.NewGuid(), Guid.NewGuid(), ApprovalDecisionAction.Reject, "changes_required", _now.AddMinutes(1));
        await new ApprovalRequestStore(second).StageDecisionAsync(_installation, _tenant, _project, _environment, row.Id, 1,
            Guid.NewGuid(), Guid.NewGuid(), ApprovalDecisionAction.Revoke, "obsolete", _now.AddMinutes(1));
        await first.SaveChangesAsync(); await firstTx.CommitAsync();
        var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(conflict.InnerException).SqlState);
        await secondTx.RollbackAsync();
        await using var verify = new ApprovalDbContext(options);
        Assert.Equal("Rejected", (await verify.Requests.AsNoTracking().SingleAsync(x => x.Id == row.Id)).State);
        Assert.Equal(1, await verify.Decisions.CountAsync(x => x.RequestId == row.Id));
    }
    [Fact]
    public async Task DatabaseRejectsDecisionWithoutMatchingRequestTransition()
    {
        var row = Request(); _db.Requests.Add(row); await _db.SaveChangesAsync();
        _db.Decisions.Add(new() { Id = Guid.NewGuid(), InstallationId = _installation, TenantId = _tenant,
            RequestId = row.Id, RequestVersion = 2, Action = "reject", ActorPrincipalId = Guid.NewGuid(),
            ReasonCode = "changes_required", OccurredAtUtc = _now.AddMinutes(1) });
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(() => _db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE"));
    }
    [Fact]
    public async Task DatabaseRejectsRequestTransitionWithoutMatchingDecision()
    {
        var row = Request(); _db.Requests.Add(row); await _db.SaveChangesAsync();
        row.State = "Rejected"; row.Version = 2; await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(() => _db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE"));
    }
    [Fact]
    public async Task ConsistentDecisionAndRequestPassDeferredConstraints()
    {
        var row = Request(); var store = new ApprovalRequestStore(_db); store.StageRequest(row); await _db.SaveChangesAsync();
        await store.StageDecisionAsync(_installation, _tenant, _project, _environment, row.Id, 1, Guid.NewGuid(), Guid.NewGuid(),
            ApprovalDecisionAction.Reject, "changes_required", _now.AddMinutes(1));
        await _db.SaveChangesAsync(); await _db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
    }
}
