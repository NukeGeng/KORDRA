using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.ApprovalGovernance.Domain;
using Kordra.Modules.ApprovalGovernance.Contracts;
using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ApprovalGovernance;

/// <summary>APR owner staging only. Host owns custody, audit, transaction commit and replay reconciliation.</summary>
public sealed class ApprovalPolicyStore(ApprovalDbContext db) : IReleaseApprovalPolicyReader
{
    public async Task<ReleaseApprovalPolicyBinding?> ReadCurrentUnderAdmissionAsync(Guid installationId, Guid tenantId,
        Guid projectId, Guid environmentId, CancellationToken cancellationToken = default)
    {
        if (installationId == Guid.Empty || tenantId == Guid.Empty || projectId == Guid.Empty || environmentId == Guid.Empty) return null;
        var binding = await db.Bindings.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
            x.TenantId == tenantId && x.ProjectId == projectId && x.EnvironmentId == environmentId, cancellationToken);
        if (binding is null) return null;
        var head = await db.Heads.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
            x.TenantId == tenantId && x.ProjectId == projectId && x.PolicyId == binding.PolicyId, cancellationToken);
        if (head is null || head.CurrentRevision != binding.PolicyRevision) return null;
        var revision = await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
            x.TenantId == tenantId && x.ProjectId == projectId && x.PolicyId == binding.PolicyId && x.Revision == binding.PolicyRevision, cancellationToken);
        if (revision is null) throw new InvalidDataException("Approval policy history is unavailable.");
        ReleaseApprovalPolicy policy;
        try { policy = ReleaseApprovalPolicy.Parse(revision.CanonicalPayload); }
        catch (ArgumentException exception) { throw new InvalidDataException("Approval policy integrity failed.", exception); }
        var bytes = policy.GetCanonicalBytes();
        if (revision.PayloadHash.Length != 32 || !CryptographicOperations.FixedTimeEquals(bytes, revision.CanonicalPayload) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(policy.PayloadHash), revision.PayloadHash))
            throw new InvalidDataException("Approval policy integrity failed.");
        var digest = BindingDigest(binding, policy.PayloadHash);
        return new(installationId, tenantId, projectId, environmentId, binding.BindingId, binding.BindingRevision,
            binding.PolicyId, binding.PolicyRevision, policy.PayloadHash, digest, bytes);
    }

    public async Task<bool> StagePublicationAsync(IOfflineApprovalCustody custody, ApprovalPolicyManifest manifest,
        IReadOnlyList<ApprovalEnvironmentEvidence> environments, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        custody.RequireHeld();
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Approval policy requires an enlisted transaction.");
        if (nowUtc.Offset != TimeSpan.Zero || nowUtc.Ticks % 10 != 0 || environments.Count != manifest.BindingChanges.Count) return false;
        var evidence = new Dictionary<Guid, ApprovalEnvironmentEvidence>();
        foreach (var item in environments)
            if (item.TenantId != manifest.TenantId || item.ProjectId != manifest.ProjectId || item.ProjectRevision <= 0 ||
                item.EnvironmentRevision <= 0 || !evidence.TryAdd(item.EnvironmentId, item)) return false;
        var head = await db.Heads.SingleOrDefaultAsync(x => x.InstallationId == manifest.InstallationId &&
            x.TenantId == manifest.TenantId && x.ProjectId == manifest.ProjectId && x.PolicyId == manifest.PolicyId, cancellationToken);
        if (head is null ? manifest.ExpectedPolicyRevision != 0 : head.CurrentRevision != manifest.ExpectedPolicyRevision) return false;
        var changes = new List<(ApprovalBindingChange Change, ApprovalPolicyBinding? Current)>();
        foreach (var change in manifest.BindingChanges)
        {
            if (!evidence.TryGetValue(change.EnvironmentId, out var currentEnv) ||
                currentEnv.EnvironmentRevision != change.ExpectedEnvironmentRevision ||
                !manifest.Policy.Rules.Any(x => x.Classification == currentEnv.Classification)) return false;
            var current = await db.Bindings.SingleOrDefaultAsync(x => x.InstallationId == manifest.InstallationId &&
                x.TenantId == manifest.TenantId && x.ProjectId == manifest.ProjectId && x.EnvironmentId == change.EnvironmentId, cancellationToken);
            if (current is null ? change.ExpectedBindingId is not null || change.ExpectedBindingRevision != 0 :
                current.BindingId != change.ExpectedBindingId || current.BindingRevision != change.ExpectedBindingRevision) return false;
            changes.Add((change, current));
        }
        custody.RequireHeld();
        var revision = checked(manifest.ExpectedPolicyRevision + 1);
        if (head is null)
            db.Heads.Add(new()
            {
                InstallationId = manifest.InstallationId,
                TenantId = manifest.TenantId,
                ProjectId = manifest.ProjectId,
                PolicyId = manifest.PolicyId,
                CurrentRevision = revision
            });
        else head.CurrentRevision = revision;
        db.Revisions.Add(new()
        {
            InstallationId = manifest.InstallationId,
            TenantId = manifest.TenantId,
            ProjectId = manifest.ProjectId,
            PolicyId = manifest.PolicyId,
            Revision = revision,
            CanonicalPayload = manifest.Policy.GetCanonicalBytes(),
            PayloadHash = Convert.FromHexString(manifest.Policy.PayloadHash),
            PublishedAtUtc = nowUtc
        });
        foreach (var (change, current) in changes)
        {
            var id = current?.BindingId ?? Guid.NewGuid();
            var next = checked(change.ExpectedBindingRevision + 1);
            if (current is null)
                db.Bindings.Add(new()
                {
                    BindingId = id,
                    InstallationId = manifest.InstallationId,
                    TenantId = manifest.TenantId,
                    ProjectId = manifest.ProjectId,
                    EnvironmentId = change.EnvironmentId,
                    BindingRevision = next,
                    PolicyId = manifest.PolicyId,
                    PolicyRevision = revision
                });
            else { current.BindingRevision = next; current.PolicyId = manifest.PolicyId; current.PolicyRevision = revision; }
            db.BindingHistory.Add(new()
            {
                BindingId = id,
                BindingRevision = next,
                InstallationId = manifest.InstallationId,
                TenantId = manifest.TenantId,
                ProjectId = manifest.ProjectId,
                EnvironmentId = change.EnvironmentId,
                PolicyId = manifest.PolicyId,
                PolicyRevision = revision,
                ManifestId = manifest.ManifestId,
                BoundAtUtc = nowUtc
            });
        }
        return true;
    }

    public async Task<ApprovalPolicyCommandReceipt?> ReadCommandUnderAdmissionAsync(Guid installationId, Guid manifestId,
        CancellationToken cancellationToken = default)
    {
        if (installationId == Guid.Empty || manifestId == Guid.Empty) return null;
        return await db.Commands.AsNoTracking().SingleOrDefaultAsync(x => x.InstallationId == installationId &&
            x.ManifestId == manifestId, cancellationToken);
    }

    public void StageCommand(IOfflineApprovalCustody custody, ApprovalPolicyManifest manifest, Guid commandId,
        Guid auditIntentId, string auditIntentHash, DateTimeOffset nowUtc)
    {
        custody.RequireHeld();
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Receipt requires an enlisted transaction.");
        if (commandId == Guid.Empty || auditIntentId == Guid.Empty || auditIntentHash.Length != 64 ||
            !auditIntentHash.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            nowUtc.Offset != TimeSpan.Zero || nowUtc.Ticks % 10 != 0)
            throw new ArgumentException("Invalid approval policy receipt.");
        db.Commands.Add(new()
        {
            InstallationId = manifest.InstallationId,
            ManifestId = manifest.ManifestId,
            TenantId = manifest.TenantId,
            ProjectId = manifest.ProjectId,
            PolicyId = manifest.PolicyId,
            PolicyRevision = checked(manifest.ExpectedPolicyRevision + 1),
            CommandId = commandId,
            CommandHash = Convert.FromHexString(manifest.CommandHash),
            PayloadHash = Convert.FromHexString(manifest.Policy.PayloadHash),
            AuditIntentId = auditIntentId,
            AuditIntentHash = Convert.FromHexString(auditIntentHash),
            CreatedAtUtc = nowUtc
        });
    }

    private static string BindingDigest(ApprovalPolicyBinding binding, string payloadHash)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("bindingId", binding.BindingId.ToString("D"));
            writer.WriteNumber("bindingRevision", binding.BindingRevision);
            writer.WriteString("environmentId", binding.EnvironmentId.ToString("D"));
            writer.WriteString("installationId", binding.InstallationId.ToString("D"));
            writer.WriteString("payloadHash", payloadHash);
            writer.WriteString("policyId", binding.PolicyId.ToString("D"));
            writer.WriteNumber("policyRevision", binding.PolicyRevision);
            writer.WriteString("projectId", binding.ProjectId.ToString("D"));
            writer.WriteString("purpose", "release-intent-v1");
            writer.WriteString("tenantId", binding.TenantId.ToString("D"));
            writer.WriteEndObject();
        }
        var prefix = Encoding.UTF8.GetBytes("release-approval-binding-v1\n");
        var bytes = stream.ToArray(); var input = new byte[prefix.Length + bytes.Length];
        prefix.CopyTo(input, 0); bytes.CopyTo(input, prefix.Length);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
}
