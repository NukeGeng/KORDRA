using System.Data;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Binding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class DisposableArtifactOwnerTests
{
    [Theory]
    [InlineData("expired")][InlineData("wrong-database")][InlineData("default")]
    public async Task RejectedTargetCannotReachOwnerOrAuditAndDoesNotPoisonAdmission(string kind)
    {
        var run = Guid.NewGuid();
        Binding binding = (run, new string('a', 64), new string('b', 64), DateTimeOffset.UtcNow.AddMinutes(10));
        var locator = $"Host=127.0.0.1;Database=kordra_test_{run:N};Username=kordra_test;Pooling=false";
        if (kind == "wrong-database") locator = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Pooling=false";
        using var connection = new NpgsqlConnection(locator);
        var options = new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(connection).Options;
        var contexts = new Contexts(options); var gate = new FairAsyncReaderWriterAdmissionGate();
        if (kind == "expired") binding.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var coordinator = kind == "default"
            ? new ArtifactOwnerTransaction(Guid.NewGuid(), contexts, new NeverAudit(), gate, new NeverClock())
            : ArtifactOwnerTransaction.CreateDisposableTestTransaction(Guid.NewGuid(), contexts, new NeverAudit(), gate, new NeverClock(), binding);
        var callbacks = 0;
        var result = await coordinator.RunAsync(_ => { callbacks++; throw new InvalidOperationException("Unexpected audit intent."); },
            (_, _) => { callbacks++; throw new InvalidOperationException("Unexpected owner preparation."); },
            _ => { callbacks++; throw new InvalidOperationException("Unexpected authority callback."); }, default);
        Assert.Equal("unavailable", result.Status); Assert.Null(result.Prepared);
        Assert.Equal(1, contexts.Calls); Assert.Equal(0, callbacks);
        Assert.False(gate.IsClosed); Assert.Equal(ConnectionState.Closed, connection.State);
    }
    private sealed class Contexts(DbContextOptions<KordraIdentityDbContext> options) : IDbContextFactory<KordraIdentityDbContext>
    {
        public int Calls;
        public KordraIdentityDbContext CreateDbContext() { Calls++; return new(options); }
    }
    private sealed class NeverClock : IAuthorityClock
    { public DateTimeOffset GetTrustedUtcNow() => throw new InvalidOperationException("Unexpected clock access."); }
    private sealed class NeverAudit : IArtifactAuditTransactionFactory
    {
        public ValueTask<IArtifactAuditTransaction> EnlistAsync(System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction transaction, CancellationToken ct) => throw new InvalidOperationException("Unexpected AUD enlistment.");
    }
}
