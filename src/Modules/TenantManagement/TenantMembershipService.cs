using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.TenantManagement.Contracts;
using Kordra.Modules.TenantManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kordra.Modules.TenantManagement;

/// <summary>T04 owner handlers only. Trusted subject/resource/lease adapters are required; no HTTP registration.</summary>
public sealed class TenantMembershipService(
    IDbContextFactory<TenantDbContext> contexts, IRoleDefinitionReader definitions,
    ITenantSubjectReader subjects, ITenantScopeOwner resources, ITenantAuditTransactionFactory audits,
    FairAsyncReaderWriterAdmissionGate gate, IAuthorityClock clock)
{
    private sealed record Source(Guid Id, long Revision);

    public async Task<TenantCommandResult> InitializeOfflineAsync(IOfflineProvisioningLease offline,
        Guid manifestId, Guid principalId, string name, Guid definitionId, long definitionRevision,
        CancellationToken cancellationToken = default)
    {
        offline.RequireHeld();
        if (manifestId == Guid.Empty || principalId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl)) return new("invalid");
        await using var lease = await gate.AcquireWriteAsync(cancellationToken);
        offline.RequireHeld();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = Hash(manifestId.ToString("D"));
        var hash = Hash(JsonSerializer.Serialize(new { kind = "initialize", principalId, name, definitionId, definitionRevision }));
        var replay = await ReplayAsync(db, key, hash, cancellationToken);
        if (replay is not null) return replay;
        var definition = await definitions.GetCurrentAsync(definitionId, definitionRevision, cancellationToken);
        if (!await definitions.IsActivePrincipalAsync(principalId, cancellationToken) || definition is null ||
            definition.Rules.Any(x => !FoundationPermissions.IsValid(x) || x.ScopeKind != "organization") ||
            !definition.Rules.Any(x => x.Effect == "allow")) return new("denied");
        var org = new TenantOrganization { Id = Guid.NewGuid(), Name = name };
        var member = new TenantMembership { Id = Guid.NewGuid(), TenantId = org.Id, PrincipalId = principalId };
        db.Organizations.Add(org); db.Memberships.Add(member);
        db.Assignments.Add(new TenantAssignment { Id = Guid.NewGuid(), TenantId = org.Id, MembershipId = member.Id,
            DefinitionId = definitionId, DefinitionRevision = definitionRevision, ScopeKind = "organization", ScopeId = org.Id,
            IsFounding = true, GrantorPrincipalId = principalId });
        offline.RequireHeld();
        return await CommitAsync(db, tx, key, hash, org.Id, org.Id, 1, null, "tenant.initialize", cancellationToken, _ => { offline.RequireHeld(); return Task.FromResult(true); });
    }

    public async Task<TenantCommandResult> AddMemberOfflineAsync(IOfflineProvisioningLease offline,
        Guid manifestId, Guid tenantId, Guid principalId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        offline.RequireHeld();
        if (manifestId == Guid.Empty || tenantId == Guid.Empty || principalId == Guid.Empty || expectedRevision <= 0) return new("invalid");
        await using var lease = await gate.AcquireWriteAsync(cancellationToken);
        offline.RequireHeld();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = Hash(manifestId.ToString("D"));
        var hash = Hash(JsonSerializer.Serialize(new { kind = "member", tenantId, principalId, expectedRevision }));
        var replay = await ReplayAsync(db, key, hash, cancellationToken);
        if (replay is not null) return replay;
        var org = await db.Organizations.SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (org is null || org.Status != "active" || !await definitions.IsActivePrincipalAsync(principalId, cancellationToken)) return new("denied");
        if (org.Revision != expectedRevision || await db.Memberships.AnyAsync(x => x.TenantId == tenantId && x.PrincipalId == principalId, cancellationToken)) return new("conflict");
        var member = new TenantMembership { Id = Guid.NewGuid(), TenantId = tenantId, PrincipalId = principalId };
        db.Memberships.Add(member); org.Revision++;
        offline.RequireHeld();
        return await CommitAsync(db, tx, key, hash, tenantId, member.Id, member.Revision, null, "membership.provision", cancellationToken, _ => { offline.RequireHeld(); return Task.FromResult(true); });
    }

    public async Task<TenantCommandResult> GrantOfflineAsync(IOfflineProvisioningLease offline,
        Guid manifestId, Guid tenantId, Guid membershipId, long expectedRevision,
        Guid definitionId, long definitionRevision, TenantScope scope, DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        offline.RequireHeld();
        if (manifestId == Guid.Empty || tenantId == Guid.Empty || membershipId == Guid.Empty || expectedRevision <= 0 || scope is null || scope.Id == Guid.Empty)
            return new("invalid");
        await using var lease = await gate.AcquireWriteAsync(cancellationToken);
        offline.RequireHeld();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = Hash(manifestId.ToString("D"));
        var hash = Hash(JsonSerializer.Serialize(new { kind = "founding-grant", tenantId, membershipId, expectedRevision, definitionId, definitionRevision, scope, expiresAtUtc }));
        var replay = await ReplayAsync(db, key, hash, cancellationToken);
        if (replay is not null) return replay;
        var member = await db.Memberships.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == membershipId && x.Status == "active", cancellationToken);
        var definition = await definitions.GetCurrentAsync(definitionId, definitionRevision, cancellationToken);
        if (member is null || definition is null || definition.Rules.Any(x => !FoundationPermissions.IsValid(x) || x.ScopeKind != scope.Kind) ||
            !await definitions.IsActivePrincipalAsync(member.PrincipalId, cancellationToken) ||
            !await db.Organizations.AnyAsync(x => x.Id == tenantId && x.Status == "active", cancellationToken) ||
            !await ScopeOwnedAsync(tenantId, scope, cancellationToken) || expiresAtUtc <= clock.GetTrustedUtcNow() ||
            expiresAtUtc is { Offset: var offset } && offset != TimeSpan.Zero) return new("denied");
        if (member.Revision != expectedRevision) return new("conflict");
        var assignment = new TenantAssignment { Id = Guid.NewGuid(), TenantId = tenantId, MembershipId = membershipId,
            DefinitionId = definitionId, DefinitionRevision = definitionRevision, ScopeKind = scope.Kind, ScopeId = scope.Id,
            ExpiresAtUtc = expiresAtUtc, IsFounding = true, GrantorPrincipalId = member.PrincipalId };
        db.Assignments.Add(assignment); member.Revision++;
        offline.RequireHeld();
        return await CommitAsync(db, tx, key, hash, tenantId, assignment.Id, assignment.Revision, null, "membership.provision", cancellationToken, _ => { offline.RequireHeld(); return Task.FromResult(expiresAtUtc is null || expiresAtUtc > clock.GetTrustedUtcNow()); });
    }

    public async Task<IReadOnlyList<SelfMembership>?> MyMembershipsAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await gate.AcquireReadAsync(cancellationToken);
        var principal = await subjects.CurrentPrincipalAsync(cancellationToken);
        if (principal is null || !await definitions.IsActivePrincipalAsync(principal.Value, cancellationToken)) return null;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var result = await (from member in db.Memberships.AsNoTracking()
            join org in db.Organizations.AsNoTracking() on member.TenantId equals org.Id
            where member.PrincipalId == principal && member.Status == "active" && org.Status == "active"
            orderby member.TenantId
            select new SelfMembership(member.TenantId, member.Id, member.Revision)).Take(51).ToArrayAsync(cancellationToken);
        return result.Length <= 50 ? result : null;
    }

    /// <summary>Fact port for trusted composition already holding the shared admission. This is not a permit.</summary>
    public async Task<EffectiveAssignment?> ReadEffectiveUnderAdmissionAsync(Guid tenantId, Guid principalId,
        string permission, TenantScope scope, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await ResolveAsync(db, tenantId, principalId, permission, scope, clock.GetTrustedUtcNow(), cancellationToken);
    }

    public async Task<TenantCommandResult> ExecuteAsync(TenantCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null || command.TenantId == Guid.Empty || command.ExpectedRevision <= 0 ||
            command.Key is null || command.Key.Length is < 16 or > 128 ||
            command.Key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '~' or '-')) ||
            command.Kind is not ("assign" or "revoke-assignment" or "revoke-membership" or "suspend") ||
            command.ExpiresAtUtc is { Offset: var offset } && offset != TimeSpan.Zero) return new("invalid");
        await using var lease = await gate.AcquireWriteAsync(cancellationToken);
        var now = clock.GetTrustedUtcNow();
        var principal = await subjects.CurrentPrincipalAsync(cancellationToken);
        if (principal is null || !await definitions.IsActivePrincipalAsync(principal.Value, cancellationToken)) return new("denied");
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var permission = command.Kind is "assign" or "revoke-assignment" ? "membership.assign-role" : "org.manage";
        var administrative = await ResolveAsync(db, command.TenantId, principal.Value, permission,
            new TenantScope("organization", command.TenantId), now, cancellationToken);
        if (administrative is null) return new("denied");
        var hash = Hash(JsonSerializer.Serialize(new { principal, command }));
        var key = Hash(command.Key);
        var replay = await ReplayAsync(db, key, hash, cancellationToken);
        if (replay is not null) return replay;
        var org = await db.Organizations.SingleAsync(x => x.Id == command.TenantId, cancellationToken);
        Guid resultId; long revision;
        if (command.Kind == "suspend")
        {
            if (org.Revision != command.ExpectedRevision) return new("conflict");
            org.Status = "suspended"; org.Revision++; resultId = org.Id; revision = org.Revision;
        }
        else
        {
            var member = await db.Memberships.SingleOrDefaultAsync(x => x.TenantId == command.TenantId && x.Id == command.MembershipId, cancellationToken);
            if (member is null || member.Status != "active" || !await definitions.IsActivePrincipalAsync(member.PrincipalId, cancellationToken)) return new("denied");
            if (command.Kind == "assign")
            {
                if (member.Revision != command.ExpectedRevision) return new("conflict");
                if (command.Scope is null || command.Scope.Id == Guid.Empty || command.ExpiresAtUtc <= now ||
                    !await ScopeOwnedAsync(command.TenantId, command.Scope, cancellationToken)) return new("denied");
                var definition = await definitions.GetCurrentAsync(command.DefinitionId, command.DefinitionRevision, cancellationToken);
                if (definition is null || definition.Rules.Any(x => !FoundationPermissions.IsValid(x) || x.ScopeKind != command.Scope.Kind)) return new("denied");
                var allow = definition.Rules.Where(x => x.Effect == "allow").ToArray();
                if (allow.Length == 0) return new("denied");
                var provenance = new List<Source> { new(administrative.AssignmentId, administrative.Revision) };
                var expiry = new List<DateTimeOffset?> { administrative.ExpiresAtUtc };
                foreach (var rule in allow)
                {
                    if (!FoundationPermissions.Allows(definition, rule.Permission, command.Scope.Kind)) continue;
                    var ceiling = await ResolveAsync(db, command.TenantId, principal.Value, rule.Permission, command.Scope, now, cancellationToken);
                    if (ceiling is null) return new("denied");
                    provenance.Add(new(ceiling.AssignmentId, ceiling.Revision)); expiry.Add(ceiling.ExpiresAtUtc);
                }
                if (expiry.Any(x => x is not null && (command.ExpiresAtUtc is null || command.ExpiresAtUtc > x))) return new("denied");
                var assignment = new TenantAssignment { Id = Guid.NewGuid(), TenantId = command.TenantId, MembershipId = member.Id,
                    DefinitionId = definition.DefinitionId, DefinitionRevision = definition.Revision, ScopeKind = command.Scope.Kind,
                    ScopeId = command.Scope.Id, ExpiresAtUtc = command.ExpiresAtUtc, GrantorPrincipalId = principal.Value,
                    ProvenanceJson = JsonSerializer.Serialize(provenance.Distinct().ToArray()) };
                db.Assignments.Add(assignment); member.Revision++; resultId = assignment.Id; revision = 1;
            }
            else if (command.Kind == "revoke-membership")
            {
                if (member.Revision != command.ExpectedRevision) return new("conflict");
                member.Status = "revoked"; member.Revision++; resultId = member.Id; revision = member.Revision;
            }
            else
            {
                var assignment = await db.Assignments.SingleOrDefaultAsync(x => x.TenantId == command.TenantId &&
                    x.Id == command.AssignmentId && x.MembershipId == member.Id, cancellationToken);
                if (assignment is null) return new("denied");
                if (assignment.Revision != command.ExpectedRevision) return new("conflict");
                assignment.Status = "revoked"; assignment.Revision++; member.Revision++;
                resultId = assignment.Id; revision = assignment.Revision;
            }
        }
        return await CommitAsync(db, tx, key, hash, command.TenantId, resultId, revision, principal,
            permission, cancellationToken, async token =>
            {
                var atCommit = clock.GetTrustedUtcNow();
                if (await subjects.CurrentPrincipalAsync(token) != principal ||
                    await ResolveAsync(db, command.TenantId, principal.Value, permission,
                        new TenantScope("organization", command.TenantId), atCommit, token) is null) return false;
                if (command.Kind != "assign") return true;
                if (command.ExpiresAtUtc <= atCommit) return false;
                var currentDefinition = await definitions.GetCurrentAsync(command.DefinitionId, command.DefinitionRevision, token);
                if (currentDefinition is null) return false;
                foreach (var rule in currentDefinition.Rules.Where(x => x.Effect == "allow"))
                    if (FoundationPermissions.Allows(currentDefinition, rule.Permission, command.Scope!.Kind) &&
                        await ResolveAsync(db, command.TenantId, principal.Value, rule.Permission, command.Scope!, atCommit, token) is null) return false;
                return true;
            });
    }

    private async Task<bool> ScopeOwnedAsync(Guid tenant, TenantScope scope, CancellationToken ct) =>
        scope.Kind == "organization" ? scope.Id == tenant : scope.Kind is "project" or "environment" && await resources.BelongsToTenantAsync(tenant, scope, ct);

    private async Task<EffectiveAssignment?> ResolveAsync(TenantDbContext db, Guid tenant, Guid principal,
        string permission, TenantScope scope, DateTimeOffset now, CancellationToken ct)
    {
        if (!FoundationPermissions.IsValid(new PermissionRule(permission, scope.Kind)) ||
            !await ScopeOwnedAsync(tenant, scope, ct) || !await definitions.IsActivePrincipalAsync(principal, ct) ||
            !await db.Organizations.AnyAsync(x => x.Id == tenant && x.Status == "active", ct)) return null;
        var member = await db.Memberships.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.PrincipalId == principal && x.Status == "active", ct);
        if (member is null) return null;
        var assignments = await db.Assignments.AsNoTracking().Where(x => x.TenantId == tenant && x.MembershipId == member.Id &&
            x.ScopeKind == scope.Kind && x.ScopeId == scope.Id && x.Status == "active" && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now)).Take(33).ToArrayAsync(ct);
        if (assignments.Length > 32) return null;
        var matches = new List<TenantAssignment>();
        foreach (var assignment in assignments)
        {
            var definition = await definitions.GetCurrentAsync(assignment.DefinitionId, assignment.DefinitionRevision, ct);
            if (definition is null || !await ValidProvenanceAsync(db, assignment, now, [], ct)) return null;
            if (definition.Rules.Any(x => x.Permission == permission && x.ScopeKind == scope.Kind && x.Effect == "deny")) return null;
            if (FoundationPermissions.Allows(definition, permission, scope.Kind)) matches.Add(assignment);
        }
        if (matches.Count != 1) return null;
        var selected = matches[0];
        return new(selected.Id, selected.Revision, selected.DefinitionId, selected.DefinitionRevision, selected.ExpiresAtUtc);
    }

    private async Task<bool> ValidProvenanceAsync(TenantDbContext db, TenantAssignment assignment,
        DateTimeOffset now, HashSet<Guid> visited, CancellationToken ct)
    {
        if (visited.Count >= 32 || !visited.Add(assignment.Id)) return false;
        if (assignment.Status != "active" || assignment.ExpiresAtUtc <= now ||
            await definitions.GetCurrentAsync(assignment.DefinitionId, assignment.DefinitionRevision, ct) is null) return false;
        var member = await db.Memberships.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == assignment.TenantId && x.Id == assignment.MembershipId && x.Status == "active", ct);
        if (member is null || !await definitions.IsActivePrincipalAsync(member.PrincipalId, ct)) return false;
        Source[]? sources;
        try { sources = JsonSerializer.Deserialize<Source[]>(assignment.ProvenanceJson); }
        catch (JsonException) { return false; }
        if (sources is null || sources.Length > 32) return false;
        if (assignment.IsFounding) return sources.Length == 0 && assignment.GrantorPrincipalId == member.PrincipalId && await ScopeOwnedAsync(assignment.TenantId, new TenantScope(assignment.ScopeKind, assignment.ScopeId), ct);
        if (sources.Length == 0) return false;
        foreach (var source in sources)
        {
            var parent = await db.Assignments.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == assignment.TenantId && x.Id == source.Id, ct);
            if (parent is null || parent.Revision != source.Revision) return false;
            var grantor = await db.Memberships.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == assignment.TenantId && x.Id == parent.MembershipId, ct);
            if (grantor?.PrincipalId != assignment.GrantorPrincipalId ||
                !await ValidProvenanceAsync(db, parent, now, new HashSet<Guid>(visited), ct)) return false;
        }
        return true;
    }

    private async Task<TenantCommandResult> CommitAsync(TenantDbContext db, IDbContextTransaction tx,
        string key, string hash, Guid tenant, Guid resource, long revision, Guid? subject, string permission, CancellationToken ct, Func<CancellationToken, Task<bool>>? revalidate = null)
    {
        db.Commands.Add(new TenantCommandReceipt { KeyHash = key, ContentHash = hash, TenantId = tenant, ResourceId = resource, Revision = revision });
        var id = Guid.NewGuid();
        var intent = new AuditIntent(1, id, id, AuditIntentStage.Mutation, subject, tenant, resource,
            permission, "organization", tenant, null, revision, "tenant-owner-mutation", clock.GetTrustedUtcNow(), id);
        await using var audit = await audits.EnlistAsync(db.Database.GetDbConnection(), tx.GetDbTransaction(), ct);
        try
        {
            var result = await audit.CommitAsync(intent, async token =>
            {
                if (revalidate is not null && !await revalidate(token)) return false;
                await db.SaveChangesAsync(token);
                return true;
            }, ct);
            if (result.Status == AuditAppendStatus.Unknown) gate.Close();
            return result.Status == AuditAppendStatus.Durable ? new("success", tenant, resource, revision) : new("unavailable");
        }
        catch { gate.Close(); throw; }
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static async Task<TenantCommandResult?> ReplayAsync(TenantDbContext db, string key, string hash, CancellationToken ct)
    {
        var replay = await db.Commands.AsNoTracking().SingleOrDefaultAsync(x => x.KeyHash == key, ct);
        return replay is null ? null : replay.ContentHash == hash ? new("success", replay.TenantId, replay.ResourceId, replay.Revision) : new("conflict");
    }
}
