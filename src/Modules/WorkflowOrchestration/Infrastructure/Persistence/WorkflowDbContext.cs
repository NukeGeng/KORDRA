using DisposableTestBinding = (System.Guid RunId, string SourceSha256, string OperationManifestSha256, System.DateTimeOffset ExpiresAtUtc);
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
    public const int WakeSchemaRevision = 2;
    public const int CancellationSchemaRevision = 3;
    public const int StartSchemaRevision = 4;
    public const int ReconciliationSchemaRevision = 5;
    public bool IncludesOperatorReconciliation { get; }
    public bool IncludesStartReceipts { get; }
    public string SchemaName { get; }
    public bool IncludesWakeOperations { get; }
    public bool IncludesCancellationIntents { get; }

    private WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, string schema, bool wakeOperations, bool cancellationIntents, bool startReceipts, bool operatorReconciliation) : base(options)
    { SchemaName = schema; IncludesWakeOperations = wakeOperations; IncludesCancellationIntents = cancellationIntents; IncludesStartReceipts = startReceipts; IncludesOperatorReconciliation = operatorReconciliation; }

    /// <summary>Offline artifact generation only. Does not connect or provision.</summary>
    public static WorkflowDbContext CreateOfflineModel() => Create(new NpgsqlConnection(
        "Host=127.0.0.1;Database=kordra_design_time;Username=kordra_design_time;Timeout=1"), "wfl");

    public static WorkflowDbContext CreateOfflineWakeModel() => Create(new NpgsqlConnection(
        "Host=127.0.0.1;Database=kordra_design_time;Username=kordra_design_time;Timeout=1"), "wfl", true);

    /// <summary>Does not authorize operations; fixture approval must name the exact case/schema.</summary>
    public static WorkflowDbContext CreateFixture(NpgsqlConnection connection, Guid caseId)
    {
        var target = WorkflowFixtureTarget.Validate(connection, caseId);
        return Create(connection, target.SchemaName);
    }

    public static WorkflowDbContext CreateWakeFixture(NpgsqlConnection connection, Guid caseId)
    {
        var target = WorkflowFixtureTarget.Validate(connection, caseId);
        return Create(connection, target.SchemaName, true);
    }

    public static WorkflowDbContext CreateOfflineCancellationModel() => Create(new NpgsqlConnection(
        "Host=127.0.0.1;Database=kordra_design_time;Username=kordra_design_time;Timeout=1"), "wfl", true, true);

    public static WorkflowDbContext CreateCancellationFixture(NpgsqlConnection connection, Guid caseId)
    {
        var target = WorkflowFixtureTarget.Validate(connection, caseId);
        return Create(connection, target.SchemaName, true, true);
    }

    public static WorkflowDbContext CreateOfflineStartModel() => Create(new NpgsqlConnection(
        "Host=127.0.0.1;Database=kordra_design_time;Username=kordra_design_time;Timeout=1"), "wfl", true, true, true);

    public static WorkflowDbContext CreateStartFixture(NpgsqlConnection connection, Guid caseId)
    {
        var target = WorkflowFixtureTarget.Validate(connection, caseId);
        return Create(connection, target.SchemaName, true, true, true);
    }

    public static WorkflowDbContext CreateOfflineReconciliationModel() => Create(new NpgsqlConnection(
        "Host=127.0.0.1;Database=kordra_design_time;Username=kordra_design_time;Timeout=1"), "wfl", true, true, true, true);

    public static WorkflowDbContext CreateReconciliationFixture(NpgsqlConnection connection, Guid caseId)
    {
        var target = WorkflowFixtureTarget.Validate(connection, caseId);
        return Create(connection, target.SchemaName, true, true, true, true);
    }

    /// <summary>Explicit disposable database/case/revision selection only; never provisions or opens a connection.</summary>
    public static WorkflowDbContext CreateDisposableTestFixture(NpgsqlConnection connection, Guid caseId,
        int schemaRevision, DisposableTestBinding profile)
    {
        if (schemaRevision is < SchemaRevision or > ReconciliationSchemaRevision)
            throw new InvalidOperationException("Unsupported disposable WFL fixture revision.");
        var target = WorkflowFixtureTarget.ValidateDisposableTest(connection, caseId, profile);
        return Create(connection, target.SchemaName, schemaRevision >= WakeSchemaRevision,
            schemaRevision >= CancellationSchemaRevision, schemaRevision >= StartSchemaRevision,
            schemaRevision >= ReconciliationSchemaRevision);
    }

    private static WorkflowDbContext Create(NpgsqlConnection connection, string schema, bool wakeOperations = false, bool cancellationIntents = false, bool startReceipts = false, bool operatorReconciliation = false)
    {
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseNpgsql(connection, p => p.CommandTimeout(2))
            .ReplaceService<IModelCacheKeyFactory, WorkflowModelCacheKeyFactory>().Options;
        return new(options, schema, wakeOperations, cancellationIntents, startReceipts, operatorReconciliation);
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

        if (IncludesWakeOperations)
        {
            var wake = Scoped<WorkflowWakeOperationRecord>(model, "wake_operations");
            wake.HasKey(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.StepId, x.TimerId, x.Generation });
            wake.HasOne<WorkflowTimerRecord>().WithMany().HasForeignKey(x =>
                new { x.InstallationId, x.TenantId, x.InstanceId, x.StepId, x.TimerId, x.Generation }).OnDelete(DeleteBehavior.Restrict);
            wake.HasIndex(x => new { x.InstallationId, x.TenantId, x.SchedulerOperationId }).IsUnique();
            wake.HasIndex(x => new { x.InstallationId, x.TenantId, x.WakeOperationId }).IsUnique();
            wake.HasIndex(x => new { x.InstallationId, x.TenantId, x.MessageId }).IsUnique();
            wake.Property(x => x.Descriptor).HasColumnType("bytea").IsRequired();
            wake.ToTable(t => {
                t.HasCheckConstraint("ck_wake_operations_descriptor", "octet_length(descriptor) BETWEEN 2 AND 16384");
                t.HasCheckConstraint("ck_wake_operations_budget", "scheduler_attempt BETWEEN 1 AND 10");
                t.HasCheckConstraint("ck_wake_operations_epoch", "custody_epoch > 0");
                t.HasCheckConstraint("ck_wake_operations_operations", "scheduler_operation_id <> wake_operation_id");
            });
        }

        if (IncludesCancellationIntents)
        {
            var cancellation = Scoped<WorkflowCancellationIntentRecord>(model, "cancellation_intents");
            cancellation.HasKey(x => new { x.InstallationId, x.TenantId, x.RequestId });
            cancellation.HasIndex(x => new { x.InstallationId, x.TenantId, x.InstanceId }).IsUnique();
            InstanceForeignKey(cancellation);
            cancellation.ToTable(t => t.HasCheckConstraint("ck_cancellation_versions",
                "expected_version > 0 AND expected_version < 9223372036854775807 AND resulting_version = expected_version + 1"));
        }

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

        if (IncludesStartReceipts)
        {
            var start = Scoped<WorkflowStartReceiptRecord>(model, "start_receipts");
            start.HasKey(x => new { x.InstallationId, x.TenantId, x.ActorId, x.ScopedKeyHash });
            start.HasIndex(x => new { x.InstallationId, x.TenantId, x.RequestId }).IsUnique();
            start.HasOne<WorkflowRequestReceiptRecord>().WithMany()
                .HasForeignKey(x => new { x.InstallationId, x.TenantId, x.RequestId }).OnDelete(DeleteBehavior.Restrict);
        }

        if (IncludesOperatorReconciliation)
        {
            var decision = Scoped<WorkflowOperatorDecisionRecord>(model, "operator_decisions");
            decision.HasKey(x => new { x.InstallationId, x.TenantId, x.RequestId });
            decision.HasIndex(x => new { x.InstallationId, x.TenantId, x.InstanceId, x.OperationId, x.Kind }).IsUnique();
            InstanceForeignKey(decision);
            decision.ToTable(t =>
            {
                t.HasCheckConstraint("ck_operator_decision_kind", "kind IN (1,2)");
                t.HasCheckConstraint("ck_operator_decision_versions",
                    "expected_version > 0 AND expected_version < 9223372036854775807 AND resulting_version = expected_version + 1");
            });
        }

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
            if (entry.State == EntityState.Added && entry.Entity is WorkflowOperatorDecisionRecord decision)
            {
                var command = new WorkflowReconciliationCommand(decision.InstallationId, decision.TenantId,
                    decision.InstanceId, decision.RequestId, decision.ActorId, decision.OperationId,
                    decision.ExpectedVersion, (WorkflowReconciliationKind)decision.Kind,
                    decision.EvidenceHash, decision.SpecificationHash);
                var owner = ChangeTracker.Entries<WorkflowInstanceRecord>().SingleOrDefault(x =>
                    x.Entity.InstallationId == command.InstallationId && x.Entity.TenantId == command.TenantId &&
                    x.Entity.InstanceId == command.InstanceId)?.Entity
                    ?? throw new InvalidOperationException("Operator decision requires its tracked owner.");
                var canonical = WorkflowSpecification.ReadCanonical(owner.Specification);
                if (decision.CommandHash != command.CommandHash || decision.RecordedAtUtc == default ||
                    decision.RecordedAtUtc.Offset != TimeSpan.Zero || decision.RecordedAtUtc == DateTimeOffset.MaxValue ||
                    decision.ResultingVersion != command.ExpectedVersion + 1 || owner.Version != decision.ResultingVersion ||
                    owner.SpecificationHash != command.SpecificationHash || canonical.SpecificationHash != command.SpecificationHash ||
                    canonical.InstallationId != command.InstallationId || canonical.TenantId != command.TenantId ||
                    canonical.InstanceId != command.InstanceId || decision.RecordedAtUtc < canonical.CreatedAtUtc ||
                    !Enum.IsDefined((WorkflowState)owner.State) || WorkflowStateRules.IsTerminal((WorkflowState)owner.State))
                    throw new InvalidOperationException("Operator decision must match its immutable command and current owner.");
            }
            if (entry.State == EntityState.Added && entry.Entity is WorkflowCancellationIntentRecord cancellation)
            {
                var intent = new WorkflowCancellationIntent(cancellation.InstallationId, cancellation.TenantId,
                    cancellation.InstanceId, cancellation.RequestId, cancellation.ActorId, cancellation.ExpectedVersion, cancellation.SpecificationHash);
                var owner = ChangeTracker.Entries<WorkflowInstanceRecord>().SingleOrDefault(x =>
                    x.Entity.InstallationId == intent.InstallationId && x.Entity.TenantId == intent.TenantId &&
                    x.Entity.InstanceId == intent.InstanceId)?.Entity
                    ?? throw new InvalidOperationException("Cancellation binding requires its tracked owner.");
                var canonical = WorkflowSpecification.ReadCanonical(owner.Specification);
                if (cancellation.CommandHash != intent.CommandHash || cancellation.RecordedAtUtc == default ||
                    cancellation.ResultingVersion != cancellation.ExpectedVersion + 1 ||
                    owner.Version != cancellation.ResultingVersion || owner.SpecificationHash != intent.SpecificationHash ||
                    canonical.InstallationId != intent.InstallationId || canonical.TenantId != intent.TenantId ||
                    canonical.InstanceId != intent.InstanceId || canonical.SpecificationHash != intent.SpecificationHash ||
                    cancellation.RecordedAtUtc.ToUniversalTime() < canonical.CreatedAtUtc ||
                    !Enum.IsDefined((WorkflowState)owner.State) || WorkflowStateRules.IsTerminal((WorkflowState)owner.State))
                    throw new InvalidOperationException("Cancellation row must match its exact immutable command binding.");
            }
            if (entry.State == EntityState.Added && entry.Entity is WorkflowWakeOperationRecord wake)
            {
                var instance = ChangeTracker.Entries<WorkflowInstanceRecord>().SingleOrDefault(x =>
                    x.Entity.InstallationId == wake.InstallationId && x.Entity.TenantId == wake.TenantId &&
                    x.Entity.InstanceId == wake.InstanceId)?.Entity
                    ?? throw new InvalidOperationException("Wake binding requires its tracked owner specification.");
                var operation = WorkflowWakeOperation.ReadCanonical(wake.Descriptor,
                    WorkflowSpecification.ReadCanonical(instance.Specification));
                if (wake.DescriptorHash != operation.DescriptorHash || wake.StepId != operation.StepId ||
                    wake.TimerId != operation.TimerId || wake.Generation != operation.Generation ||
                    wake.SchedulerAttempt != operation.SchedulerAttempt || wake.CustodyEpoch != operation.CustodyEpoch ||
                    wake.SchedulerOperationId != operation.SchedulerOperationId || wake.WakeOperationId != operation.WakeOperationId ||
                    wake.MessageId != operation.MessageId)
                    throw new InvalidOperationException("Wake row must match its exact canonical descriptor.");
            }
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
                WorkflowRequestReceiptRecord or WorkflowAuthorityBindingRecord or WorkflowWakeOperationRecord or WorkflowCancellationIntentRecord or WorkflowStartReceiptRecord or WorkflowOperatorDecisionRecord)
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
            if (entry.Entity is WorkflowTimerRecord timer)
            {
                foreach (var property in entry.Properties.Where(p => p.IsModified))
                    if (property.Metadata.Name is not ("Version" or "Status" or "SchedulerAttempts"))
                        throw new InvalidOperationException("Workflow timer identity and original due time are immutable.");
                var before = (int)entry.Property("SchedulerAttempts").OriginalValue!;
                if (entry.Property("SchedulerAttempts").IsModified &&
                    (before is < 0 or >= WorkflowTimingRules.MaximumSchedulerAttempts || timer.SchedulerAttempts != before + 1))
                    throw new InvalidOperationException("Workflow scheduler attempts cannot reset, skip or exceed their budget.");
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
        (context.GetType(), ((WorkflowDbContext)context).SchemaName, ((WorkflowDbContext)context).IncludesWakeOperations, ((WorkflowDbContext)context).IncludesCancellationIntents, ((WorkflowDbContext)context).IncludesStartReceipts, ((WorkflowDbContext)context).IncludesOperatorReconciliation, designTime);
}
