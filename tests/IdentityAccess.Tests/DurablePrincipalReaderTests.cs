using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

[Collection("IdentityAccess PostgreSQL and local HTTPS")]
public sealed class DurablePrincipalReaderTests
{
    [Fact]
    public async Task PersistentEvidenceDoesNotDependOnSessionAndDetectsDisableAndRestore()
    {
        var locator = TestDatabaseConfiguration.TryGetConnectionString() ?? throw new InvalidOperationException("Isolated test database required.");
        var contexts = new PooledDbContextFactory<KordraIdentityDbContext>(new DbContextOptionsBuilder<KordraIdentityDbContext>()
            .UseNpgsql(locator, p => p.MigrationsHistoryTable(KordraIdentityDbContext.MigrationHistoryTableName, "app")).Options);
        await using var db = await contexts.CreateDbContextAsync(); await db.Database.MigrateAsync();
        var id = Guid.NewGuid(); var name = "synthetic-" + id.ToString("N");
        var user = new KordraIdentityUser { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(),
            PasswordHash = "synthetic", SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"), Revision = 1 };
        db.Principals.Add(user); await db.SaveChangesAsync();
        try
        {
            var reader = new DurablePrincipalReader(contexts);
            var first = await reader.ReadUnderAdmissionAsync(id); Assert.NotNull(first);
            Assert.True(first.Active); Assert.Equal(1, first.PrincipalRevision); Assert.Equal(64, first.SecurityRevisionHash.Length);
            Assert.DoesNotContain(user.SecurityStamp!, first.SecurityRevisionHash);
            Assert.False(await db.Sessions.AnyAsync(x => x.PrincipalId == id));
            user.AccountStatus = "disabled"; user.Revision++; user.SecurityStamp = Guid.NewGuid().ToString("N"); await db.SaveChangesAsync();
            var disabled = await reader.ReadUnderAdmissionAsync(id); Assert.NotNull(disabled);
            Assert.False(disabled.Active); Assert.NotEqual(first.SecurityRevisionHash, disabled.SecurityRevisionHash);
            user.AccountStatus = "active"; user.Revision++; user.SecurityStamp = Guid.NewGuid().ToString("N"); await db.SaveChangesAsync();
            var restored = await reader.ReadUnderAdmissionAsync(id); Assert.NotNull(restored);
            Assert.True(restored.Active); Assert.True(restored.PrincipalRevision > first.PrincipalRevision);
            Assert.NotEqual(first.SecurityRevisionHash, restored.SecurityRevisionHash);
            Assert.Null(await reader.ReadUnderAdmissionAsync(Guid.NewGuid()));
        }
        finally { db.Principals.Remove(user); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task MissingIdentityAndUnavailableStorageNeverBecomePositiveFacts()
    {
        var reader = new DurablePrincipalReader(new Unavailable());
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadUnderAdmissionAsync(Guid.Empty));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadUnderAdmissionAsync(Guid.NewGuid()));
    }
    private sealed class Unavailable : IDbContextFactory<KordraIdentityDbContext>
    {
        public KordraIdentityDbContext CreateDbContext() => throw new InvalidOperationException("Synthetic unavailable store.");
        public Task<KordraIdentityDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Synthetic unavailable store.");
    }
}
