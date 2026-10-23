using System.Text.Json;
using Kordra.Modules.InfrastructureInventory.Contracts;
using Kordra.Modules.InfrastructureInventory.Infrastructure;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.InfrastructureInventory;

/// <summary>INV-owned preparation only; Host enlists PRJ association and AUD before the single commit.</summary>
public sealed class TargetRegistryStore(InventoryDbContext db, IProjectOwnershipReader projects)
{
    public async Task<PreparedTargetChange> PrepareAsync(TargetCommand command, string keyHash, string contentHash, CancellationToken ct = default)
    {
        if (command.TenantId == Guid.Empty || command.ProjectId == Guid.Empty || command.EnvironmentId == Guid.Empty ||
            command.Operation is not ("register" or "rename" or "disable" or "activate" or "delete")) return new("invalid");
        var replay = await db.Commands.AsNoTracking().SingleOrDefaultAsync(x => x.KeyHash == keyHash, ct);
        if (replay is not null) return replay.ContentHash == contentHash
            ? new("success", JsonSerializer.Deserialize<TargetMetadata>(replay.ResultJson), true) : new("conflict");
        var owner = await projects.ResolveOwnershipAsync(command.TenantId, command.ProjectId, command.EnvironmentId, ct);
        if (!Matches(owner, command.TenantId, command.ProjectId, command.EnvironmentId)) return new("denied");
        if (command.Operation is "register" or "rename")
            if (!ValidName(command.Name)) return new("invalid");
        TargetRecord target;
        if (command.Operation == "register")
        {
            if (command.TargetId != Guid.Empty || command.ExpectedRevision != 0 || string.IsNullOrWhiteSpace(command.Kind) || command.Kind.Length > 64 ||
                command.Kind.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-'))) return new("invalid");
            target = new()
            {
                Id = Guid.NewGuid(),
                TenantId = command.TenantId,
                ProjectId = command.ProjectId,
                EnvironmentId = command.EnvironmentId,
                Name = command.Name!,
                Kind = command.Kind
            };
            db.Targets.Add(target);
        }
        else
        {
            if (command.ExpectedRevision <= 0 || command.Kind is not null) return new("invalid");
            var found = await db.Targets.SingleOrDefaultAsync(x => x.Id == command.TargetId && x.TenantId == command.TenantId, ct);
            if (found is null || found.ProjectId != command.ProjectId || found.EnvironmentId != command.EnvironmentId || found.Status == "deleted") return new("denied");
            var association = await projects.ReadAssociationAsync(command.TenantId, found.Id, ct);
            if (!AssociationMatches(found, association)) return new("denied");
            if (found.Revision != command.ExpectedRevision) return new("conflict");
            target = found;
            if (command.Operation == "rename") target.Name = command.Name!;
            else target.Status = command.Operation switch { "disable" => "disabled", "activate" => "active", _ => "deleted" };
            target.Revision++;
        }
        var result = Fact(target);
        db.Commands.Add(new() { KeyHash = keyHash, ContentHash = contentHash, ResultJson = JsonSerializer.Serialize(result) });
        return new("success", result);
    }

    /// <summary>Authoritative owner facts, not disclosure authorization. Caller must hold common read admission.</summary>
    public async Task<TargetMetadata?> ReadCurrentAsync(Guid tenantId, Guid targetId, CancellationToken ct = default)
    {
        var target = await db.Targets.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == targetId && x.Status == "active", ct);
        if (target is null || !Matches(await projects.ResolveOwnershipAsync(tenantId, target.ProjectId, target.EnvironmentId, ct), tenantId, target.ProjectId, target.EnvironmentId) ||
            !AssociationMatches(target, await projects.ReadAssociationAsync(tenantId, targetId, ct))) return null;
        return Fact(target);
    }
    public Task PersistAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && !name.Any(char.IsControl);
    private static bool Matches(ProjectOwnership? p, Guid tenant, Guid project, Guid? environment) => p is not null &&
        p.TenantId == tenant && p.ProjectId == project && p.ProjectRevision > 0 && p.EnvironmentId == environment &&
        (environment is null ? p.Selection == EnvironmentSelection.ResolvedAbsent && p.EnvironmentRevision is null && p.Classification is null && p.PolicyRevision is null :
            p.Selection == EnvironmentSelection.ResolvedPresent && p.EnvironmentRevision > 0 && p.PolicyRevision > 0 && p.Classification is "Development" or "Test" or "Staging" or "Production");
    private static bool AssociationMatches(TargetRecord t, TargetAssociation? a) => a is not null && a.TargetId == t.Id &&
        a.TenantId == t.TenantId && a.ProjectId == t.ProjectId && a.EnvironmentId == t.EnvironmentId && a.Revision == t.AssociationRevision;
    private static TargetMetadata Fact(TargetRecord t) => new(t.Id, t.TenantId, t.ProjectId, t.EnvironmentId, t.Name, t.Kind, t.Status, t.Revision, t.AssociationRevision);
}
