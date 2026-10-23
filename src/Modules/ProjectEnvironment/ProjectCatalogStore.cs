using System.Text.Json;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Modules.ProjectEnvironment;

/// <summary>Owner-only preparation in a caller-enlisted transaction under common writer admission. Never commits.</summary>
public sealed class ProjectCatalogStore(ProjectDbContext db, IEnvironmentPolicyReader policies) : IProjectOwnershipReader
{
    public async Task<PreparedCatalogChange> PrepareAsync(CatalogCommand command, string keyHash, string contentHash, CancellationToken ct = default)
    {
        if (command.TenantId == Guid.Empty || command.Kind is not ("project" or "application" or "environment") ||
            command.Operation is not ("create" or "rename" or "suspend" or "activate" or "delete")) return new("invalid");
        var replay = await db.Commands.AsNoTracking().SingleOrDefaultAsync(x => x.KeyHash == keyHash, ct);
        if (replay is not null) return replay.ContentHash == contentHash
            ? new("success", JsonSerializer.Deserialize<CatalogFact>(replay.ResultJson), true) : new("conflict");
        ProjectRecord? project = null;
        if (command.Kind != "project")
        {
            project = await db.Projects.SingleOrDefaultAsync(x => x.TenantId == command.TenantId && x.Id == command.ProjectId && x.Status == "active", ct);
            if (project is null) return new("denied");
        }
        else if (command.Operation != "create" && command.ProjectId != command.ResourceId) return new("denied");
        if (command.Operation is "create" or "rename")
            if (!ValidName(command.Name)) return new("invalid");
        object resource;
        if (command.Operation == "create")
        {
            if (command.ResourceId != Guid.Empty || command.ExpectedRevision != 0 || command.Kind == "project" && command.ProjectId != Guid.Empty) return new("invalid");
            if (command.Kind == "environment")
            {
                if (command.Classification is not ("Development" or "Test" or "Staging" or "Production") || command.PolicyRevision is not > 0 ||
                    !await policies.IsApplicableAsync(command.TenantId, command.ProjectId, command.Classification, command.PolicyRevision.Value, ct)) return new("denied");
                resource = new EnvironmentRecord { Id = Guid.NewGuid(), TenantId = command.TenantId, ProjectId = command.ProjectId,
                    Name = command.Name!, Classification = command.Classification, PolicyRevision = command.PolicyRevision.Value };
            }
            else if (command.Classification is not null || command.PolicyRevision is not null) return new("invalid");
            else resource = command.Kind == "project"
                ? new ProjectRecord { Id = Guid.NewGuid(), TenantId = command.TenantId, Name = command.Name! }
                : new ApplicationRecord { Id = Guid.NewGuid(), TenantId = command.TenantId, ProjectId = command.ProjectId, Name = command.Name! };
            db.Add(resource);
        }
        else
        {
            if (command.ResourceId == Guid.Empty || command.ExpectedRevision <= 0 || command.Classification is not null || command.PolicyRevision is not null) return new("invalid");
            object? found = command.Kind switch
            {
                "project" => await db.Projects.SingleOrDefaultAsync(x => x.TenantId == command.TenantId && x.Id == command.ResourceId, ct),
                "application" => await db.Applications.SingleOrDefaultAsync(x => x.TenantId == command.TenantId && x.ProjectId == command.ProjectId && x.Id == command.ResourceId, ct),
                _ => await db.Environments.SingleOrDefaultAsync(x => x.TenantId == command.TenantId && x.ProjectId == command.ProjectId && x.Id == command.ResourceId, ct)
            };
            if (found is null) return new("denied"); resource = found;
            var previous = Fact(resource);
            if (previous.Status == "deleted") return new("denied");
            if (previous.Revision != command.ExpectedRevision) return new("conflict");
            var entry = db.Entry(resource);
            if (command.Operation == "rename") entry.Property("Name").CurrentValue = command.Name!;
            else entry.Property("Status").CurrentValue = command.Operation switch { "suspend" => "suspended", "activate" => "active", _ => "deleted" };
            entry.Property("Revision").CurrentValue = previous.Revision + 1;
        }
        if (project is not null) project.Revision++;
        var result = Fact(resource);
        db.Commands.Add(new() { KeyHash = keyHash, ContentHash = contentHash, ResultJson = JsonSerializer.Serialize(result) });
        return new("success", result);
    }

    public async Task<ProjectOwnership?> ResolveOwnershipAsync(Guid tenantId, Guid projectId, Guid? environmentId, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == projectId && x.Status == "active", cancellationToken);
        if (project is null) return null;
        if (environmentId is null) return new(tenantId, projectId, project.Revision, EnvironmentSelection.ResolvedAbsent, null, null, null, null);
        var env = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProjectId == projectId && x.Id == environmentId && x.Status == "active", cancellationToken);
        if (env is null || !await policies.IsApplicableAsync(tenantId, projectId, env.Classification, env.PolicyRevision, cancellationToken)) return null;
        return new(tenantId, projectId, project.Revision, EnvironmentSelection.ResolvedPresent, env.Id, env.Revision, env.Classification, env.PolicyRevision);
    }

    public async Task<TargetAssociation?> ReadAssociationAsync(Guid tenantId, Guid targetId, CancellationToken cancellationToken = default)
    {
        var row = await db.Associations.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.TargetId == targetId, cancellationToken);
        return row is null ? null : new(row.TargetId, row.TenantId, row.ProjectId, row.EnvironmentId, row.Revision);
    }

    public async Task<bool> BindTargetAsync(Guid targetId, Guid tenantId, Guid projectId, Guid? environmentId, CancellationToken ct = default)
    {
        if (targetId == Guid.Empty || await ResolveOwnershipAsync(tenantId, projectId, environmentId, ct) is null ||
            await db.Associations.AnyAsync(x => x.TargetId == targetId, ct)) return false;
        db.Associations.Add(new() { TargetId = targetId, TenantId = tenantId, ProjectId = projectId, EnvironmentId = environmentId });
        var project = await db.Projects.SingleAsync(x => x.TenantId == tenantId && x.Id == projectId, ct); project.Revision++;
        return true;
    }
    public Task PersistAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && !name.Any(char.IsControl);
    private static CatalogFact Fact(object row) => row switch
    {
        ProjectRecord p => new(p.Id, p.TenantId, p.Id, "project", p.Name, p.Status, p.Revision),
        ApplicationRecord a => new(a.Id, a.TenantId, a.ProjectId, "application", a.Name, a.Status, a.Revision),
        EnvironmentRecord e => new(e.Id, e.TenantId, e.ProjectId, "environment", e.Name, e.Status, e.Revision, e.Classification, e.PolicyRevision),
        _ => throw new InvalidOperationException("Unsupported owner resource.")
    };
}
