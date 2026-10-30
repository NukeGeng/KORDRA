using System.Security.Cryptography;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Authentication;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Modules.IdentityAccess.Authorization;

public sealed record DefinitionWriteResult(string Status, Guid DefinitionId = default, long Revision = 0);

/// <summary>Exact owner reads and offline-only immutable definition publication. No HTTP registration.</summary>
public sealed class RoleDefinitionStore(
    IDbContextFactory<KordraIdentityDbContext> contexts,
    FairAsyncReaderWriterAdmissionGate gate,
    IIdentityAuditTransactionFactory audits, LocalAuthenticationConfiguration? configuration = null, IAuthorityClock? clock = null) : IRoleDefinitionReader, IDurableRoleEvidenceReader
{
    public async Task<DurableRoleEvidence> ReadExactUnderAdmissionAsync(Guid definitionId, long revision, CancellationToken cancellationToken = default)
    {
        if (definitionId == Guid.Empty || revision <= 0) return new(DurableRoleEvidenceStatus.Unavailable);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.RoleDefinitions.AsNoTracking().Where(x => x.DefinitionId == definitionId)
            .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(cancellationToken);
        if (row is null || row.IsRetired || row.Revision != revision) return new(DurableRoleEvidenceStatus.Invalidated);
        try
        {
            var rules = JsonSerializer.Deserialize<PermissionRule[]>(row.RulesJson, new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
            if (rules is not { Length: > 0 and <= 32 } || !rules.All(FoundationPermissions.IsValid)) return new(DurableRoleEvidenceStatus.Unavailable);
            return new(DurableRoleEvidenceStatus.Current, new(row.DefinitionId, row.Revision, row.Name, Array.AsReadOnly(rules)));
        }
        catch (JsonException) { return new(DurableRoleEvidenceStatus.Unavailable); }
    }
    public async Task<RoleDefinitionSnapshot?> GetCurrentAsync(Guid definitionId, long exactRevision, CancellationToken cancellationToken = default)
    {
        if (definitionId == Guid.Empty || exactRevision <= 0) return null;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.RoleDefinitions.AsNoTracking().Where(x => x.DefinitionId == definitionId)
            .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(cancellationToken);
        if (row is null || row.IsRetired || row.Revision != exactRevision) return null;
        try
        {
            var rules = JsonSerializer.Deserialize<PermissionRule[]>(row.RulesJson, new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
            return rules is { Length: > 0 and <= 32 } && rules.All(FoundationPermissions.IsValid)
                ? new RoleDefinitionSnapshot(row.DefinitionId, row.Revision, row.Name, Array.AsReadOnly(rules)) : null;
        }
        catch (JsonException) { return null; }
    }

    public async Task<bool> IsActivePrincipalAsync(Guid principalId, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return principalId != Guid.Empty && await db.Principals.AnyAsync(x => x.Id == principalId && x.AccountStatus == "active", cancellationToken);
    }

    public async Task<DefinitionWriteResult> PublishOfflineAsync(IOfflineProvisioningLease offline,
        Guid manifestId, Guid definitionId, long expectedRevision, string name,
        IReadOnlyList<PermissionRule> rules, bool retire = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offline);
        offline.RequireHeld();
        if (manifestId == Guid.Empty || expectedRevision < 0 || (definitionId == Guid.Empty && expectedRevision != 0) ||
            string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl) ||
            rules is null || rules.Count is < 1 or > 32 || !rules.All(FoundationPermissions.IsValid))
            return new("invalid");
        var ordered = rules.OrderBy(x => x.Permission, StringComparer.Ordinal).ThenBy(x => x.ScopeKind, StringComparer.Ordinal)
            .ThenBy(x => x.Effect, StringComparer.Ordinal).ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { definitionId, expectedRevision, name, ordered, retire })));
        await using var lease = await gate.AcquireWriteAsync(cancellationToken);
        offline.RequireHeld();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var replay = await db.RoleDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.ManifestId == manifestId, cancellationToken);
        if (replay is not null) return replay.ContentHash == hash ? new("success", replay.DefinitionId, replay.Revision) : new("conflict");
        var previous = definitionId == Guid.Empty ? null : await db.RoleDefinitions.AsNoTracking().Where(x => x.DefinitionId == definitionId)
            .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(cancellationToken);
        if ((previous?.Revision ?? 0) != expectedRevision || (definitionId != Guid.Empty && previous is null)) return new("conflict");
        var row = new KordraRoleDefinition { DefinitionId = definitionId == Guid.Empty ? Guid.NewGuid() : definitionId,
            Revision = expectedRevision + 1, ManifestId = manifestId, ContentHash = hash, Name = name,
            RulesJson = JsonSerializer.Serialize(ordered), IsRetired = retire };
        db.RoleDefinitions.Add(row);
        var intent = new AuditIntent(1, manifestId, manifestId, AuditIntentStage.Mutation, null, null, null,
            "iam.role-definition", "instance", row.DefinitionId, row.Revision, row.Revision,
            retire ? "definition-retired" : "definition-published", clock?.GetTrustedUtcNow() ?? DateTimeOffset.UtcNow, manifestId);
        // Setup-only enlistment failure cannot leave an attempted commit.
        await using var audit = await audits.EnlistAsync(db.Database.GetDbConnection(), tx.GetDbTransaction(), cancellationToken);
        try
        {
            offline.RequireHeld();
            var result = await audit.CommitWithOwnerMutationAsync(intent, async ct =>
            {
                offline.RequireHeld(); await db.SaveChangesAsync(ct);
                if (configuration is not null && clock is not null) await AuthorityClockFloorStore.StageAsync(db, configuration.InstallationId, clock.GetTrustedUtcNow(), ct);
            }, cancellationToken);
            if (result.Status == AuditAppendStatus.Unknown) gate.Close();
            return result.Status == AuditAppendStatus.Durable ? new("success", row.DefinitionId, row.Revision) : new("unavailable");
        }
        catch { gate.Close(); throw; }
    }
}
