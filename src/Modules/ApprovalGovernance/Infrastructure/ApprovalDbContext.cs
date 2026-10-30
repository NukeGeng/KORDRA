using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ApprovalGovernance.Infrastructure;

public sealed class ApprovalPolicyHead
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PolicyId { get; set; }
    public long CurrentRevision { get; set; }
}
public sealed class ApprovalPolicyRevision
{
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PolicyId { get; set; }
    public long Revision { get; set; }
    public byte[] CanonicalPayload { get; set; } = [];
    public byte[] PayloadHash { get; set; } = [];
    public DateTimeOffset PublishedAtUtc { get; set; }
}
public sealed class ApprovalPolicyBinding
{
    public Guid BindingId { get; set; }
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EnvironmentId { get; set; }
    public long BindingRevision { get; set; }
    public Guid PolicyId { get; set; }
    public long PolicyRevision { get; set; }
}
public sealed class ApprovalPolicyBindingHistory
{
    public Guid BindingId { get; set; }
    public long BindingRevision { get; set; }
    public Guid InstallationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EnvironmentId { get; set; }
    public Guid PolicyId { get; set; }
    public long PolicyRevision { get; set; }
    public Guid ManifestId { get; set; }
    public DateTimeOffset BoundAtUtc { get; set; }
}
public sealed class ApprovalPolicyCommandReceipt
{
    public Guid InstallationId { get; set; }
    public Guid ManifestId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PolicyId { get; set; }
    public long PolicyRevision { get; set; }
    public Guid CommandId { get; set; }
    public byte[] CommandHash { get; set; } = [];
    public byte[] PayloadHash { get; set; } = [];
    public Guid AuditIntentId { get; set; }
    public byte[] AuditIntentHash { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ApprovalDbContext(DbContextOptions<ApprovalDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "apr_ef_migrations_history";
    public DbSet<ApprovalPolicyHead> Heads => Set<ApprovalPolicyHead>();
    public DbSet<ApprovalPolicyRevision> Revisions => Set<ApprovalPolicyRevision>();
    public DbSet<ApprovalPolicyBinding> Bindings => Set<ApprovalPolicyBinding>();
    public DbSet<ApprovalPolicyBindingHistory> BindingHistory => Set<ApprovalPolicyBindingHistory>();
    public DbSet<ApprovalPolicyCommandReceipt> Commands => Set<ApprovalPolicyCommandReceipt>();
    public DbSet<ApprovalRequestRecord> Requests => Set<ApprovalRequestRecord>();
    public DbSet<ApprovalDecisionRecord> Decisions => Set<ApprovalDecisionRecord>();
    public DbSet<ApprovalRequestCommandReceipt> RequestCommands => Set<ApprovalRequestCommandReceipt>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("app");
        var head = model.Entity<ApprovalPolicyHead>();
        head.ToTable("apr_policy_heads", t => t.HasCheckConstraint("ck_apr_policy_head", Ids("installation_id", "tenant_id", "project_id", "policy_id") + " AND current_revision > 0"));
        head.HasKey(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.PolicyId });
        head.Property(x => x.CurrentRevision).IsConcurrencyToken();
        var rev = model.Entity<ApprovalPolicyRevision>();
        rev.ToTable("apr_policy_revisions", t => t.HasCheckConstraint("ck_apr_policy_revision", Ids("installation_id", "tenant_id", "project_id", "policy_id") + " AND revision > 0 AND octet_length(payload_hash) = 32 AND octet_length(canonical_payload) BETWEEN 1 AND 65536 AND isfinite(published_at_utc)"));
        rev.HasKey(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.PolicyId, x.Revision });
        rev.HasOne<ApprovalPolicyHead>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.PolicyId }).OnDelete(DeleteBehavior.Restrict);
        var binding = model.Entity<ApprovalPolicyBinding>();
        binding.ToTable("apr_policy_bindings", t => t.HasCheckConstraint("ck_apr_policy_binding", Ids("binding_id", "installation_id", "tenant_id", "project_id", "environment_id", "policy_id") + " AND binding_revision > 0 AND policy_revision > 0"));
        binding.HasKey(x => x.BindingId); binding.Property(x => x.BindingId).ValueGeneratedNever();
        binding.HasAlternateKey(x => new { x.BindingId, x.InstallationId, x.TenantId, x.ProjectId, x.EnvironmentId });
        binding.HasIndex(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.EnvironmentId }).IsUnique();
        binding.Property(x => x.BindingRevision).IsConcurrencyToken();
        binding.HasOne<ApprovalPolicyRevision>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.PolicyId, Revision = x.PolicyRevision }).OnDelete(DeleteBehavior.Restrict);
        var history = model.Entity<ApprovalPolicyBindingHistory>();
        history.ToTable("apr_policy_binding_history", t => t.HasCheckConstraint("ck_apr_binding_history", Ids("binding_id", "installation_id", "tenant_id", "project_id", "environment_id", "policy_id", "manifest_id") + " AND binding_revision > 0 AND policy_revision > 0 AND isfinite(bound_at_utc)"));
        history.HasKey(x => new { x.BindingId, x.BindingRevision });
        history.HasOne<ApprovalPolicyBinding>().WithMany().HasForeignKey(x => new { x.BindingId, x.InstallationId, x.TenantId, x.ProjectId, x.EnvironmentId }).HasPrincipalKey(x => new { x.BindingId, x.InstallationId, x.TenantId, x.ProjectId, x.EnvironmentId }).OnDelete(DeleteBehavior.Restrict);
        history.HasOne<ApprovalPolicyRevision>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.PolicyId, Revision = x.PolicyRevision }).OnDelete(DeleteBehavior.Restrict);
        var receipt = model.Entity<ApprovalPolicyCommandReceipt>();
        receipt.ToTable("apr_policy_commands", t => t.HasCheckConstraint("ck_apr_policy_command", Ids("installation_id", "manifest_id", "tenant_id", "project_id", "policy_id", "command_id", "audit_intent_id") + " AND policy_revision > 0 AND octet_length(command_hash) = 32 AND octet_length(payload_hash) = 32 AND octet_length(audit_intent_hash) = 32 AND isfinite(created_at_utc)"));
        receipt.HasKey(x => new { x.InstallationId, x.ManifestId });
        receipt.HasIndex(x => x.CommandId).IsUnique();
        receipt.HasOne<ApprovalPolicyRevision>().WithMany().HasForeignKey(x => new { x.InstallationId, x.TenantId, x.ProjectId, x.PolicyId, Revision = x.PolicyRevision }).OnDelete(DeleteBehavior.Restrict);
        var request = model.Entity<ApprovalRequestRecord>();
        request.ToTable("apr_requests", t => t.HasCheckConstraint("ck_apr_request", Ids("id", "installation_id", "tenant_id", "project_id", "environment_id", "release_revision_id", "requester_principal_id") +
            " AND version > 0 AND state IN ('Pending','Granted','Rejected','Revoked') AND octet_length(spec_hash) = 32 AND octet_length(requester_evidence) BETWEEN 1 AND 65536 AND isfinite(created_at_utc) AND isfinite(pending_expires_at_utc) AND pending_expires_at_utc > created_at_utc AND (grant_expires_at_utc IS NULL OR (isfinite(grant_expires_at_utc) AND grant_expires_at_utc <= pending_expires_at_utc))"));
        request.HasKey(x => x.Id); request.Property(x => x.Id).ValueGeneratedNever();
        request.HasAlternateKey(x => new { x.Id, x.InstallationId, x.TenantId });
        request.HasIndex(x => new { x.InstallationId, x.ReleaseRevisionId }).IsUnique();
        request.Property(x => x.Version).IsConcurrencyToken();
        var decision = model.Entity<ApprovalDecisionRecord>();
        decision.ToTable("apr_decisions", t => t.HasCheckConstraint("ck_apr_decision", Ids("id", "installation_id", "tenant_id", "request_id", "actor_principal_id") +
            " AND request_version > 1 AND isfinite(occurred_at_utc) AND ((action = 'grant' AND reason_code = 'approved') OR (action = 'reject' AND reason_code IN ('changes_required','incorrect_artifact','incorrect_target','policy_denied')) OR (action = 'revoke' AND reason_code IN ('obsolete','requester_cancelled','owner_cancelled')))"));
        decision.HasKey(x => x.Id); decision.Property(x => x.Id).ValueGeneratedNever();
        decision.HasIndex(x => new { x.RequestId, x.RequestVersion }).IsUnique();
        decision.HasOne<ApprovalRequestRecord>().WithMany().HasForeignKey(x => new { x.RequestId, x.InstallationId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.InstallationId, x.TenantId }).OnDelete(DeleteBehavior.Restrict);
        var command = model.Entity<ApprovalRequestCommandReceipt>();
        command.ToTable("apr_request_commands", t => t.HasCheckConstraint("ck_apr_request_command", Ids("id", "installation_id", "tenant_id", "principal_id", "request_id", "audit_intent_id", "boot_id", "invocation_id") +
            " AND action IN ('request','grant','reject','revoke') AND octet_length(scoped_key_hash) = 32 AND octet_length(business_hash) = 32 AND octet_length(audit_intent_hash) = 32 AND isfinite(created_at_utc)"));
        command.HasKey(x => x.Id); command.Property(x => x.Id).ValueGeneratedNever();
        command.HasIndex(x => new { x.InstallationId, x.TenantId, x.PrincipalId, x.Action, x.ScopedKeyHash }).IsUnique();
        command.HasOne<ApprovalRequestRecord>().WithMany().HasForeignKey(x => new { x.RequestId, x.InstallationId, x.TenantId })
            .HasPrincipalKey(x => new { x.Id, x.InstallationId, x.TenantId }).OnDelete(DeleteBehavior.Restrict);
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c)
                    ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString())));
    }
    private static string Ids(params string[] names) => string.Join(" AND ", names.Select(name => $"{name} <> '00000000-0000-0000-0000-000000000000'::uuid"));
}
