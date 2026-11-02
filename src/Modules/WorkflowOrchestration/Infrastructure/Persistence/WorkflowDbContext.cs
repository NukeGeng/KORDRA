using Kordra.Modules.WorkflowOrchestration.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;

namespace Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;

/// <summary>WFL owner model. No runtime registration, automatic migration or independent commit coordinator.</summary>
public sealed class WorkflowDbContext : DbContext
{
    public const int SchemaRevision = 1;
    public string SchemaName { get; }

    private WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, string schema) : base(options) => SchemaName = schema;

    /// <summary>Offline artifact generation only. Does not connect or provision.</summary>
    public static WorkflowDbContext CreateOfflineModel() => Create(new NpgsqlConnection(
        "Host=127.0.0.1;Database=kordra_design_time;Username=kordra_design_time;Timeout=1"), "wfl");

    /// <summary>Does not authorize operations; fixture approval must name the exact case/schema.</summary>
    public static WorkflowDbContext CreateFixture(NpgsqlConnection connection, Guid caseId)
    {
        var target = WorkflowFixtureTarget.Validate(connection, caseId);
        return Create(connection, target.SchemaName);
    }

    private static WorkflowDbContext Create(NpgsqlConnection connection, string schema)
    {
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseNpgsql(connection, p => p.CommandTimeout(2))
            .ReplaceService<IModelCacheKeyFactory, WorkflowModelCacheKeyFactory>().Options;
        return new(options, schema);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(SchemaName);
        var instance = Scoped<WorkflowInstanceRecord>(model, "instances");
        instance.HasKey(x => new { x.InstallationId, x.TenantId, x.InstanceId });
        instance.HasIndex(x => new { x.InstallationId, x.TenantId, x.RequestId }).IsUnique();
        instance.HasAlternateKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.RequestId });
        instance.Property(x => x.Specification).HasColumnType("bytea").IsRequired();
        instance.ToTable(t => {
            t.HasCheckConstraint("ck_instances_spec_size", "octet_length(specification) BETWEEN 2 AND 65536");
            t.HasCheckConstraint("ck_instances_state", "state BETWEEN 1 AND 10");
            t.HasCheckConstraint("ck_instances_lifetime", "deadline_utc > created_at_utc AND deadline_utc <= created_at_utc + interval '24 hours'");
        });

        var step = Scoped<WorkflowStepRecord>(model, "steps");
        step.HasKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.StepId });
        step.HasIndex(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.Ordinal }).IsUnique();
        InstanceForeignKey(step);
        step.ToTable(t => {
            t.HasCheckConstraint("ck_steps_ordinal", "ordinal BETWEEN 0 AND 31");
            t.HasCheckConstraint("ck_steps_state", "state BETWEEN 1 AND 6");
        });

        var transition = Scoped<WorkflowTransitionRecord>(model, "transitions");
        transition.HasKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.OperationId });
        StepForeignKey(transition);
        transition.HasIndex(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.ResultingVersion }).IsUnique();
        transition.ToTable(t => t.HasCheckConstraint("ck_transitions_state", "resulting_state BETWEEN 1 AND 10"));

        var timer = Scoped<WorkflowTimerRecord>(model, "timers");
        timer.HasKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.StepId, x.TimerId, x.Generation });
        StepForeignKey(timer);
        timer.HasIndex(x => new { x.InstallationId, x.TenantId, x.Status, x.DueAtUtc, x.TimerId });
        timer.ToTable(t => {
            t.HasCheckConstraint("ck_timers_status", "status BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_timers_budget", "scheduler_attempts BETWEEN 0 AND 10");
        });

        var custody = Scoped<WorkflowCustodyRecord>(model, "operation_custody");
        custody.HasKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.StepId, x.OperationId });
        StepForeignKey(custody);
        custody.ToTable(t => {
            t.HasCheckConstraint("ck_custody_phase", "phase BETWEEN 1 AND 4");
            t.HasCheckConstraint("ck_custody_times", "heartbeat_at_utc >= started_at_utc");
        });

        var recovery = Scoped<WorkflowRecoveryDecisionRecord>(model, "recovery_decisions");
        recovery.HasKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.DecisionId });
        recovery.HasOne<WorkflowCustodyRecord>().WithMany()
            .HasForeignKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.StepId, x.OperationId }).OnDelete(DeleteBehavior.Restrict);
        recovery.ToTable(t => t.HasCheckConstraint("ck_recovery_kind", "kind BETWEEN 1 AND 2"));

        var receipt = Scoped<WorkflowRequestReceiptRecord>(model, "request_receipts");
        receipt.HasKey(x => new { x.InstallationId, x.TenantId, x.RequestId });
        receipt.HasOne<WorkflowInstanceRecord>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.RequestId })
            .HasPrincipalKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.RequestId }).OnDelete(DeleteBehavior.Restrict);
        receipt.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
        receipt.ToTable(t => t.HasCheckConstraint("ck_receipt_size", "octet_length(result_json::text) <= 16384"));

        var binding = Scoped<WorkflowAuthorityBindingRecord>(model, "authority_bindings");
        binding.HasKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.BindingId });
        binding.HasIndex(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.Generation }).IsUnique();
        InstanceForeignKey(binding);

        var floor = Scoped<WorkflowClockFloorRecord>(model, "clock_floor");
        floor.HasKey(x => new { x.InstallationId, x.TenantId });
        floor.Property(x => x.FloorUtc).IsConcurrencyToken();

        foreach (var entity in model.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateStagedChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateStagedChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateStagedChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Entity is WorkflowInstanceRecord added)
            {
                var spec = WorkflowSpecification.ReadCanonical(added.Specification);
                if (spec.InstallationId != added.InstallationId || spec.TenantId != added.TenantId ||
                    spec.InstanceId != added.InstanceId || spec.RequestId != added.RequestId ||
                    spec.SpecificationHash != added.SpecificationHash || spec.CreatedAtUtc != added.CreatedAtUtc ||
                    spec.DeadlineUtc != added.DeadlineUtc || added.Version != 1 ||
                    added.State != (int)WorkflowState.Pending || added.DeadlineExceeded)
                    throw new InvalidOperationException("Persisted workflow must match its canonical initial business binding.");
            }
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Workflow retention forbids deletion through the owner context.");
            if (entry.State != EntityState.Modified) continue;
            if (entry.Entity is WorkflowTransitionRecord or WorkflowRecoveryDecisionRecord or
                WorkflowRequestReceiptRecord or WorkflowAuthorityBindingRecord)
                throw new InvalidOperationException("Workflow history is append-only.");
            if (entry.Entity is WorkflowInstanceRecord)
            {
                foreach (var property in entry.Properties.Where(p => p.IsModified))
                    if (property.Metadata.Name is not ("Version" or "State" or "DeadlineExceeded"))
                        throw new InvalidOperationException("Workflow business binding is immutable.");
                var oldState = (WorkflowState)(int)entry.Property("State").OriginalValue!;
                var newState = (WorkflowState)(int)entry.Property("State").CurrentValue!;
                if (oldState != newState && !WorkflowStateRules.CanChangeState(oldState, newState))
                    throw new InvalidOperationException("Workflow transition requires the accepted state graph or a dedicated recovery path.");
            }
            if (entry.Entity is WorkflowStepRecord)
            {
                foreach (var property in entry.Properties.Where(p => p.IsModified))
                    if (property.Metadata.Name is not ("Version" or "State"))
                        throw new InvalidOperationException("Workflow step identity/ordinal is immutable.");
                var oldState = (WorkflowStepState)(int)entry.Property("State").OriginalValue!;
                var newState = (WorkflowStepState)(int)entry.Property("State").CurrentValue!;
                if (oldState != newState && !WorkflowStateRules.CanChangeStepState(oldState, newState))
                    throw new InvalidOperationException("Workflow step transition requires the accepted graph or recovery evidence.");
            }
            if (entry.Entity is WorkflowClockFloorRecord floor &&
                floor.FloorUtc < (DateTimeOffset)entry.Property("FloorUtc").OriginalValue!)
                throw new InvalidOperationException("Workflow clock floor cannot decrease.");
            var version = entry.Metadata.FindProperty("Version");
            if (version is not null)
            {
                var original = (long)entry.Property("Version").OriginalValue!;
                var current = (long)entry.Property("Version").CurrentValue!;
                if (original < 1 || original == long.MaxValue || current != original + 1)
                    throw new InvalidOperationException("Workflow mutation must advance the expected version exactly once.");
            }
        }
        if (ChangeTracker.HasChanges() && Database.CurrentTransaction is null)
            throw new InvalidOperationException("Workflow persistence requires the caller-owned root transaction.");
    }

    private static EntityTypeBuilder<T> Scoped<T>(ModelBuilder model, string table) where T : class
    {
        var entity = model.Entity<T>();
        entity.ToTable(table);
        foreach (var property in entity.Metadata.GetProperties())
        {
            var column = string.Concat(property.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
            if (property.ClrType == typeof(Guid))
                entity.ToTable(t => t.HasCheckConstraint("ck_" + table + "_" + column, column + " <> '00000000-0000-0000-0000-000000000000'::uuid"));
            if (property.Name is "Version" or "Epoch" or "Generation" or "ResultingVersion")
                entity.ToTable(t => t.HasCheckConstraint("ck_" + table + "_" + column, column + " > 0"));
            if (property.Name == "Version") entity.Property(property.Name).IsConcurrencyToken();
            if (property.Name.EndsWith("Hash", StringComparison.Ordinal))
            {
                entity.Property(property.Name).HasMaxLength(64).IsRequired();
                entity.ToTable(t => t.HasCheckConstraint("ck_" + table + "_" + column, column + " ~ '^[0-9a-f]{64}$'"));
            }
        }
        return entity;
    }

    private static void InstanceForeignKey<T>(EntityTypeBuilder<T> entity) where T : class =>
        entity.HasOne<WorkflowInstanceRecord>().WithMany().HasForeignKey("InstallationId", "TenantId", "InstanceId").OnDelete(DeleteBehavior.Restrict);

    private static void StepForeignKey<T>(EntityTypeBuilder<T> entity) where T : class =>
        entity.HasOne<WorkflowStepRecord>().WithMany().HasForeignKey("InstallationId", "TenantId", "InstanceId", "StepId").OnDelete(DeleteBehavior.Restrict);
}

internal sealed class WorkflowModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), ((WorkflowDbContext)context).SchemaName, designTime);
}
