using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Xunit;

namespace Kordra.Messaging.Durability.Tests;

public sealed record FixtureMessage(Guid CaseId);

/// <summary>Real PostgreSQL transaction probe. Database owner is used for fixture setup, never as ACL proof.</summary>
public sealed class ExternalTransactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerTransactionOwnsBothContextsAndDurableEnvelope(bool commit)
    {
        var locator = Environment.GetEnvironmentVariable("KORDRA_IAM_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Exact private kordra_test locator required.");
        var fixture = new FixtureDatabaseScope(locator, Guid.NewGuid());
        var schema = fixture.Schema; var created = false;
        using var host = Host.CreateDefaultBuilder().UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
            Program.ConfigureStorage(options, locator, schema);
            options.UseEntityFrameworkCoreTransactions();
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            options.PublishMessage<FixtureMessage>().ToPostgresqlQueue("t12_fixture");
        }).Build();
        await using var setup = fixture.CreateConnection(); await setup.OpenAsync();
        try
        {
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", setup)) await create.ExecuteNonQueryAsync();
            created = true;
            // Setup-only DDL, confined to the fresh UUID schema. Runtime DDL denial is a separate unmet case.
            await using (var tables = new NpgsqlCommand($"""
                CREATE TABLE {schema}.fixture_owner(id uuid PRIMARY KEY, value text NOT NULL);
                CREATE TABLE {schema}.fixture_audit(id uuid PRIMARY KEY, value text NOT NULL);
                CREATE TABLE {schema}.fixture_floor(id uuid PRIMARY KEY, value text NOT NULL);
                """, setup)) await tables.ExecuteNonQueryAsync();
            await host.StartAsync();
            await using var connection = fixture.CreateConnection(); await connection.OpenAsync();
            await using var owner = new OwnerContext(new DbContextOptionsBuilder<OwnerContext>().UseNpgsql(connection)
                .ReplaceService<IModelCacheKeyFactory, FixtureModelKey>().Options, schema);
            await using var tx = await owner.Database.BeginTransactionAsync();
            await using var audit = new AuditContext(new DbContextOptionsBuilder<AuditContext>().UseNpgsql(connection)
                .ReplaceService<IModelCacheKeyFactory, FixtureModelKey>().Options, schema);
            await audit.Database.UseTransactionAsync(tx.GetDbTransaction());
            var outbox = host.Services.GetRequiredService<IDbContextOutbox>(); outbox.Enroll(owner);
            owner.Rows.Add(new FixtureRow { Id = fixture.CaseId, Value = "synthetic-owner" });
            owner.Floors.Add(new FixtureFloor { Id = fixture.CaseId, Value = "synthetic-floor" });
            audit.Rows.Add(new FixtureRow { Id = fixture.CaseId, Value = "synthetic-audit" });
            await owner.SaveChangesAsync(); await audit.SaveChangesAsync();
            await outbox.PublishAsync(new FixtureMessage(fixture.CaseId));
            Assert.Equal(0, await Count("fixture_owner")); Assert.Equal(0, await Count("fixture_audit"));
            Assert.Equal(0, await Count("fixture_floor"));
            Assert.Equal(0, await Count("wolverine_outgoing_envelopes") + await Count("wolverine_incoming_envelopes"));
            if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
            Assert.Equal(commit ? 1 : 0, await Count("fixture_owner"));
            Assert.Equal(commit ? 1 : 0, await Count("fixture_audit"));
            Assert.Equal(commit ? 1 : 0, await Count("fixture_floor"));
            Assert.Equal(commit ? 1 : 0, await Count("wolverine_outgoing_envelopes") + await Count("wolverine_incoming_envelopes"));
            // No flush before the caller commits. Consumer/ACK-loss tests are a subsequent PoC slice.
            async Task<long> Count(string table)
            {
                await using var observer = fixture.CreateConnection(); await observer.OpenAsync();
                await using var query = new NpgsqlCommand($"SELECT count(*) FROM {schema}.{table}", observer);
                return (long)(await query.ExecuteScalarAsync())!;
            }
        }
        finally
        {
            try { await host.StopAsync(); }
            finally
            {
                if (created)
                {
                    await using var cleanup = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", setup);
                    await cleanup.ExecuteNonQueryAsync();
                }
            }
        }
    }
}

public sealed class FixtureRow { public Guid Id { get; set; } public string Value { get; set; } = ""; }
public sealed class FixtureFloor { public Guid Id { get; set; } public string Value { get; set; } = ""; }
public interface IFixtureSchema { string Schema { get; } }
public sealed class OwnerContext(DbContextOptions<OwnerContext> options, string schema) : DbContext(options), IFixtureSchema
{
    public string Schema => schema;
    public DbSet<FixtureRow> Rows => Set<FixtureRow>();
    public DbSet<FixtureFloor> Floors => Set<FixtureFloor>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<FixtureRow>().ToTable("fixture_owner", schema);
        model.Entity<FixtureFloor>().ToTable("fixture_floor", schema);
        model.Entity<FixtureRow>().Property(x => x.Id).HasColumnName("id");
        model.Entity<FixtureRow>().Property(x => x.Value).HasColumnName("value");
        model.Entity<FixtureFloor>().Property(x => x.Id).HasColumnName("id");
        model.Entity<FixtureFloor>().Property(x => x.Value).HasColumnName("value");
    }
}
public sealed class AuditContext(DbContextOptions<AuditContext> options, string schema) : DbContext(options), IFixtureSchema
{
    public string Schema => schema;
    public DbSet<FixtureRow> Rows => Set<FixtureRow>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<FixtureRow>().ToTable("fixture_audit", schema);
        model.Entity<FixtureRow>().Property(x => x.Id).HasColumnName("id");
        model.Entity<FixtureRow>().Property(x => x.Value).HasColumnName("value");
    }
}
public sealed class FixtureModelKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((IFixtureSchema)context).Schema, designTime);
}
